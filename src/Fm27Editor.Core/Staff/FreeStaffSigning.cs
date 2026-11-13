using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Transfer;

namespace Fm27Editor.Core.Staff;

public sealed record FreeStaffSigningPlan(StaffRecord Staff, ClubEntry To, ulong Team, byte Job, int Wage, DateOnly Today, DateOnly Expiry,
    ulong Root, int OfferId, int Serial, FutureTransferVectorSnapshot Department, FutureTransferVectorSnapshot? PersonIds,
    FutureTransferVectorSnapshot? PersonOffers, IReadOnlyList<PlannedWrite> Guards);

/// <summary>
/// Gives a free-agent staff member a club job the way FM27's own In-Game Editor does ("Move to my club" / "Move to other
/// club", measured on Steam 27.0.1+2503222, 14/11/2026: Duarte → Man Utd Coach, Rhodes → Man Utd Recruitment Analyst,
/// Fonsalas → Man City Physio). The game writes:
/// <list type="bullet">
/// <item>a 200-byte contract (person, first team, wage, job, start and signed dates today, expiry) on <c>person+0xA8</c>;</item>
/// <item>a finished transfer offer (status 0x14, no seller, no fee) in the first empty slot of the game's offer list, its ID
/// being that slot's index, and the offer serial counter raised by one;</item>
/// <item>the offer ID on the person's ID vector (<c>+0x98</c>) and a 72-byte link record on the person's offer vector
/// (<c>+0xA0</c>), as for players (<c>player+0x320/+0x328</c>);</item>
/// <item>the staff part appended to the club department of the role (Coaching, Medical or Scouting);</item>
/// <item>the date at <c>person+0x11C</c> set to today and the low dword at <c>person+0x18</c> cleared (all three samples).</item>
/// </list>
/// The game also chose the wage and expiry itself (1,067 / 739 / 1,058; 2028 / 2028 / 2030); here the player enters them.
/// Bytes the game set in only some samples (<c>person+0x120</c>, <c>+0xE0</c>, staff flags) are not written.
/// </summary>
public sealed class FreeStaffSigning(IProcessMemory memory, OffsetProfile profile, ulong moduleBase)
{
    public const int ContractSize = 200, OfferSize = 208, LinkSize = 72;
    public const int PersonIds = 0x98, PersonOffers = 0xA0, PersonJoined = 0x11C, PersonCleared = 0x18;
    const int OfferSerial = 0x7D4, ChangedFlag = 0x50, MaxOffers = 100_000, MaxDepartment = 1024, MaxPersonOffers = 100;
    const byte FinishedStatus = 0x14;

    public FreeStaffSigningPlan Plan(StaffRecord staff, ClubEntry to, byte job, int wage, DateOnly today, DateOnly expiry)
    {
        var S = profile.Staff ?? throw new InvalidOperationException("No staff offsets");
        var C = profile.Contract ?? throw new InvalidOperationException("No contract offsets");
        var K = profile.Club ?? throw new InvalidOperationException("No club offsets");
        var T = profile.Team ?? throw new InvalidOperationException("No team offsets");
        var po = profile.Proposals ?? throw new InvalidOperationException("No offer offsets");
        if (!StaffMoveService.SupportedJobs.Contains(job)) throw new InvalidOperationException("That role cannot be given");
        if (wage < 0) throw new ArgumentOutOfRangeException(nameof(wage), "Wage cannot be negative");
        if (expiry <= today) throw new ArgumentException("The contract must end after today", nameof(expiry));
        if (staff.Kind.Manager || staff.Kind.StaffPart != 0x100) throw new InvalidOperationException("Human managers and unusual staff objects cannot be moved");

        var guards = new List<PlannedWrite>();
        byte[] Guard(ulong at, int length, string what) { var b = memory.Read(at, length); guards.Add(new(at, b, [], what)); return b; }
        ulong Pointer(ulong at, string what) => BitConverter.ToUInt64(Guard(at, 8, what));

        if (staff.Id <= 0 || !S.Kinds.Contains(staff.Kind) ||
            Pointer(staff.Address, "person vtable") != moduleBase + staff.Kind.PersonVtableRva ||
            Pointer(staff.StaffAddress, "staff vtable") != moduleBase + staff.Kind.StaffVtableRva ||
            BitConverter.ToInt32(Guard(staff.Address + (ulong)S.Id, 4, "staff ID")) != staff.Id)
            throw new StaleObjectException("Staff identity changed");
        if (Pointer(staff.Address + (ulong)S.Job, "staff contract") != 0)
            throw new InvalidOperationException($"{staff.DisplayName} already has a job; use the normal move");

        if (to.Id is not > 0 || Pointer(to.Address, "club vtable") != moduleBase + K.ClubVtableRva) throw new StaleObjectException("Club changed");
        GenderGuard.EnsureMenClub(memory, profile, to.Address);
        var first = to.FirstTeam ?? throw new InvalidOperationException($"{to.DisplayName} has no first team");
        if (Pointer(first.Address, "team vtable") != moduleBase + T.VtableRva || Pointer(first.Address + (ulong)C.TeamClub, "team club") != to.Address)
            throw new StaleObjectException("Team changed");

        int at = StaffMoveService.DepartmentOf(job) switch { "Medical" => K.MedicalStaff, "Scouting" => K.ScoutingStaff, _ => K.CoachingStaff }
            ?? throw new InvalidOperationException("Missing staff department offset");
        var department = FutureTransferVectorSnapshot.Capture(memory, to.Address + (ulong)at, 8, MaxDepartment);
        if (department.IndexOf(x => BitConverter.ToUInt64(x) == staff.StaffAddress) >= 0) throw new InvalidOperationException("Already in that department");

        ulong root = Pointer(moduleBase + po.ListRootRva, "offer root");
        if (root == 0) throw new StaleObjectException("Offer root missing");
        int serial = BitConverter.ToInt32(Guard(root + OfferSerial, 4, "offer serial"));
        if (serial < 0 || serial == int.MaxValue) throw new StaleObjectException("Invalid offer serial");
        Guard(root + ChangedFlag, 1, "offer changed flag");
        var offers = FutureTransferVectorSnapshot.Capture(memory, root + (ulong)po.ListOffset, 8, MaxOffers);
        guards.AddRange(offers.Guards);
        // The game puts the new offer in the first empty slot; its ID is the slot index (IDs 132-134 in the traces).
        int slot = offers.IndexOf(x => BitConverter.ToUInt64(x) == 0);
        if (slot < 0) throw new InvalidOperationException("The game's offer list has no free slot");

        ulong ids = Pointer(staff.Address + PersonIds, "person offer IDs");
        ulong links = Pointer(staff.Address + PersonOffers, "person offers");
        var personIds = ids == 0 ? null : FutureTransferVectorSnapshot.Capture(memory, ids, 4, MaxPersonOffers);
        var personOffers = links == 0 ? null : FutureTransferVectorSnapshot.Capture(memory, links, 8, MaxPersonOffers);
        Guard(staff.Address + PersonJoined, 4, "joined date");
        Guard(staff.Address + PersonCleared, 4, "person field +0x18");
        return new(staff, to, first.Address, job, wage, today, expiry, root, slot, serial, department, personIds, personOffers, guards);
    }

    public void Apply(FreeStaffSigningPlan plan, Func<int, ulong> allocate)
    {
        var C = profile.Contract!; var S = profile.Staff!; var po = profile.Proposals!;
        var guards = new List<PlannedWrite>(plan.Guards);
        foreach (var v in new[] { plan.Department, plan.PersonIds, plan.PersonOffers })
            if (v is not null) { v.Validate(memory); guards.AddRange(v.Guards); }
        var scratch = new List<PlannedWrite>(); var publish = new List<PlannedWrite>();
        ulong Fresh(byte[] data, string name)
        {
            ulong at = allocate(data.Length);
            if (at == 0) throw new MemoryAccessException($"Allocation failed: {name}");
            var before = memory.Read(at, data.Length);
            if (before.Any(x => x != 0)) throw new MemoryAccessException($"Allocation was not zeroed: {name}");
            scratch.Add(new(at, before, data, name)); return at;
        }
        void Append(FutureTransferVectorSnapshot v, byte[] item) { var w = v.Append(memory, item, allocate); scratch.Add(w[0]); publish.Add(w[1]); }
        void PersonVector(ulong slot, FutureTransferVectorSnapshot? snapshot, byte[] item)
        {
            if (snapshot is not null) { Append(snapshot, item); return; }
            ulong data = Fresh(item, "new person vector data");
            var h = new byte[24]; BitConverter.GetBytes(data).CopyTo(h, 0);
            BitConverter.GetBytes(data + (ulong)item.Length).CopyTo(h, 8); BitConverter.GetBytes(data + (ulong)item.Length).CopyTo(h, 16);
            publish.Add(new(slot, memory.Read(slot, 8), BitConverter.GetBytes(Fresh(h, "new person vector holder")), "person vector holder"));
        }

        ulong contract = Fresh(Contract(moduleBase + C.ContractVtableRva, plan.Staff.Address, plan.Team, plan.Wage, plan.Job, plan.Today, plan.Expiry), "staff contract");
        ulong offer = Fresh(Offer(moduleBase + po.TransferVtable, plan.Staff.Address, plan.Team, plan.OfferId, plan.Serial, plan.Today), "offer");
        ulong link = Fresh(Link(plan.OfferId), "offer link");
        Append(plan.Department, BitConverter.GetBytes(plan.Staff.StaffAddress));
        PersonVector(plan.Staff.Address + PersonIds, plan.PersonIds, BitConverter.GetBytes(plan.OfferId));
        PersonVector(plan.Staff.Address + PersonOffers, plan.PersonOffers, BitConverter.GetBytes(link));
        ulong slotAddress = memory.ReadUInt64(plan.Root + (ulong)po.ListOffset) + (ulong)plan.OfferId * 8;
        publish.Add(new(slotAddress, new byte[8], BitConverter.GetBytes(offer), "offer slot"));
        publish.Add(new(plan.Root + OfferSerial, BitConverter.GetBytes(plan.Serial), BitConverter.GetBytes(plan.Serial + 1), "offer serial"));
        publish.Add(new(plan.Staff.Address + PersonJoined, memory.Read(plan.Staff.Address + PersonJoined, 4), FlaggedDate(plan.Today), "joined date"));
        var cleared = memory.Read(plan.Staff.Address + PersonCleared, 4);
        if (cleared.Any(b => b != 0)) publish.Add(new(plan.Staff.Address + PersonCleared, cleared, new byte[4], "person field +0x18"));
        // The contract pointer goes last: until then the staff member is still a free agent to the game.
        publish.Add(new(plan.Staff.Address + (ulong)S.Job, new byte[8], BitConverter.GetBytes(contract), "staff contract"));
        FutureTransferTransaction.Apply(memory, guards, [.. scratch, .. publish]);
    }

    /// <summary>
    /// The contract bytes, as in all three traces apart from the wage and expiry the game picked. <c>+0x5A</c> differed
    /// (0x46 / 0x49 / 0x44, meaning unknown); the first trace's 0x46 is used.
    /// </summary>
    public static byte[] Contract(ulong vtable, ulong person, ulong team, int wage, byte job, DateOnly today, DateOnly expiry)
    {
        var b = new byte[ContractSize];
        Put64(b, 0x00, vtable); Put64(b, 0x08, person); Put64(b, 0x10, team); Put64(b, 0x18, team);
        BitConverter.GetBytes(wage).CopyTo(b, 0x20);
        b[0x26] = job; b[0x2D] = 0x20;
        b.AsSpan(0x3C, 8).Fill(0xFF);
        FlaggedDate(today).CopyTo(b, 0x44);
        PlainDate(expiry).CopyTo(b, 0x48);
        FlaggedDate(today).CopyTo(b, 0x4C);
        PlainDate(new DateOnly(1900, 1, 1)).CopyTo(b, 0x50);
        b[0x57] = 0x40;
        new byte[] { 0, 0, 0x46, 0xE2, 0x14, 0xFF, 0, 0 }.CopyTo(b, 0x58);
        PlainDate(new DateOnly(1900, 1, 1)).CopyTo(b, 0xB4);
        BitConverter.GetBytes(1900).CopyTo(b, 0xBC);
        new byte[] { 0, 0, 0, 1, 0xFF, 0, 0, 0 }.CopyTo(b, 0xC0);
        return b;
    }

    /// <summary>The finished offer the game wrote: +0x6C ten days on (25/12 from 15/12), the other dates today or 1/1/1900.</summary>
    public static byte[] Offer(ulong vtable, ulong person, ulong team, int id, int serial, DateOnly today)
    {
        var b = new byte[OfferSize];
        Put64(b, 0x00, vtable); b[0x08] = 1; b[0x31] = 0x10;
        Put64(b, 0x38, person); Put64(b, 0x40, team); Put64(b, 0x48, team);
        BitConverter.GetBytes(id).CopyTo(b, 0x5C); BitConverter.GetBytes(serial).CopyTo(b, 0x60);
        b.AsSpan(0x64, 8).Fill(0xFF);
        FlaggedDate(today.AddDays(10)).CopyTo(b, 0x6C);
        FlaggedDate(today).CopyTo(b, 0x70);
        FlaggedDate(today).CopyTo(b, 0x74);
        PlainDate(new DateOnly(1900, 1, 1)).CopyTo(b, 0x78);
        FlaggedDate(today).CopyTo(b, 0x7C);
        PlainDate(new DateOnly(1900, 1, 1)).CopyTo(b, 0x80);
        b[0x86] = 1; b[0x89] = 0xFF; b[0x8D] = FinishedStatus; b[0x8E] = 1;
        return b;
    }

    public static byte[] Link(int offerId)
    {
        var b = new byte[LinkSize]; b[0] = 1;
        BitConverter.GetBytes(offerId).CopyTo(b, 0x28);
        new byte[] { 0x81, 0x69, 0x67, 0xFF }.CopyTo(b, 0x2C);
        PlainDate(new DateOnly(1900, 1, 1)).CopyTo(b, 0x38); b[0x3E] = 2; b[0x3F] = 1; b[0x40] = 1;
        return b;
    }

    static void Put64(byte[] b, int at, ulong v) => BitConverter.GetBytes(v).CopyTo(b, at);
    static byte[] PlainDate(DateOnly d) => [.. BitConverter.GetBytes((ushort)d.DayOfYear), .. BitConverter.GetBytes((short)d.Year)];
    /// <summary>Day of year with the flag bits 0x7E00 the game set on every "today" date in these traces.</summary>
    static byte[] FlaggedDate(DateOnly d) => [.. BitConverter.GetBytes((ushort)(d.DayOfYear | 0x7E00)), .. BitConverter.GetBytes((short)d.Year)];
}
