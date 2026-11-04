using System.Diagnostics;
using Fm27Editor.Core.Discovery;
using Fm27Editor.Core.Clubs;
using Fm27Editor.Core.Game;
using Fm27Editor.Core.Memory;
using Fm27Editor.Core.Offsets;
using Fm27Editor.Core.Players;
using Fm27Editor.Core.Staff;
using Fm27Editor.Core.Transfer;
using Fm27Editor.Core.Verification;

// Football Manager 27 Editor command-line tools.
//   players [name]                     list players (filtered by name)
//   verify <golden.json>               compare decoded players with values copied from the game UI
//   set <name> <field> <value>         write one attribute / CA / PA / position / personality value,
//                                      Height (cm) or Born (yyyy-MM-dd)
//   find-offsets <golden.json> <out>   rebuild the offset profile for the running build
//   transfer dump <name> [out.json]    snapshot player/contract/pending/club/finance bytes
//   transfer peek <name>               print the transfer-relevant fields of one player
//   transfer diff <before> <after>     compare two snapshots by object offset
//   captain <id> [vice] [--restore 0x<person>] [--write]   set/undo team captain or vice captain
//   clubvision <club> <type> <importance> [--from <old>] [--write]   set a club vision importance
//   maxattrs <id> [--write]            team preset: every performance attribute 20
//   keepplaying <id>|batch <ids...> [--write]   remove a planned retirement
string offsetsDir = Environment.GetEnvironmentVariable("FM27_OFFSETS") ?? Path.Combine(AppContext.BaseDirectory, "offsets");
Console.OutputEncoding = System.Text.Encoding.UTF8;
try
{
    return args.FirstOrDefault() switch
    {
        "status" => Status(),
        "players" => Players(args.Length > 1 ? string.Join(' ', args[1..]) : null),
        "healthplayers" => HealthPlayers(),
        "healthstructure" => HealthStructure(),
        "healthstaff" => HealthStaff(),
        "healthclubs" => HealthClubs(),
        "exportplayers" => ExportPlayers(args[1], args[2], args[3..].Select(int.Parse).ToArray()),
        "freezecheck" => FreezeCheck(int.Parse(args[1]), Enum.Parse<PlayerAttribute>(args[2], true), int.Parse(args[3])),
        "freezedecreasecheck" => FreezeDecreaseCheck(int.Parse(args[1]), Enum.Parse<PlayerAttribute>(args[2], true)),
        "freezedecreasereconnect" => FreezeDecreaseReconnect(args[1], int.Parse(args[2]), Enum.Parse<PlayerAttribute>(args[3], true)),
        "freezebalancecheck" => FreezeBalanceCheck(int.Parse(args[1]), long.Parse(args[2])),
        "freezestorecheck" => FreezeStoreCheck(args[1], int.Parse(args[2]), int.Parse(args[3])),
        "freezeruncheck" => FreezeRunCheck(args[1], int.Parse(args[2]), int.Parse(args[3])),
        "masscheck" => MassCheck(Enum.Parse<PlayerAttribute>(args[1], true), int.Parse(args[2]), args[3..].Select(int.Parse).ToArray()),
        "massmulticheck" => MassMultiCheck(int.Parse(args[1])),
        "presetbatchcheck" => PresetBatchCheck(Enum.Parse<PlayerPreset>(args[1], true), args[2..].Select(int.Parse).ToArray()),
        "injuryundocheck" => InjuryUndoCheck(int.Parse(args[1])),
        "squadhealcheck" => SquadHealCheck(int.Parse(args[1])),
        "massscalarcheck" => MassScalarCheck(Enum.Parse<PlayerField>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), long.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "massqualitycheck" => MassQualityCheck(Enum.Parse<PlayerQuality>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "massratingcheck" => MassRatingCheck(args[1], args[2], Enum.Parse<MassEditMode>(args[3], true), int.Parse(args[4]), args[5..].Select(int.Parse).ToArray()),
        "staffmasscheck" => StaffMassCheck(Enum.Parse<StaffMassField>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "staffattrmasscheck" => StaffAttributeMassCheck(Enum.Parse<StaffAttribute>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "staffpersonmasscheck" => StaffPersonalityMassCheck(Enum.Parse<StaffPersonality>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "clubmasscheck" => ClubMassCheck(Enum.Parse<ClubField>(args[1], true), Enum.Parse<MassEditMode>(args[2], true), long.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "retirements" => Retirements(args[1..].Select(int.Parse).ToArray()),
        "keepplaying" => KeepPlaying(int.Parse(args[1]), args.Contains("--write")),
        "keepplayingbatch" => KeepPlayingBatch(args[1..]),
        "captain" => CaptainCommand(args[1..]),
        "maxattrs" => MaxAttributesCommand(int.Parse(args[1]), args.Contains("--write")),
        "masswagecheck" => MassWageCheck(int.Parse(args[1]), args[2..].Select(int.Parse).ToArray()),
        "staffwagemass" => StaffWageMassCommand(int.Parse(args[1]), args[2..]),
        "unhundo" => UnhUndoCheck(int.Parse(args[1])),
        "unhids" => UnhappyIds(),
        "banids" => BannedIds(),
        "banundo" => BanUndoCheck(int.Parse(args[1])),
        "setattrid" => SetAttributeById(int.Parse(args[1]), Enum.Parse<PlayerAttribute>(args[2], true), int.Parse(args[3])),
        "setwageid" => SetWageById(int.Parse(args[1]), int.Parse(args[2])),
        "contractid" => ContractById(int.Parse(args[1])),
        "verify" => Verify(args[1]),
        "set" => Set(args[1], args[2], args[3]),
        "move" => Move(args[1], int.Parse(args[2]), int.Parse(args[3]), args.Contains("--write")),
        "releaseinspect" => ReleaseInspect(int.Parse(args[1])),
        "release" => ReleasePlayer(int.Parse(args[1]), args.Contains("--write")),
        "contract" => ContractCommand(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null),
        "find-offsets" => FindOffsets(args[1], args[2], args.SkipWhile(a => a != "--date").Skip(1).FirstOrDefault()),
        "transfer" => Transfer(args[1..]),
        "clauses" => args[1] == "fee" ? SellOnsWithFee() : Clauses(int.Parse(args[1])),
        "club" => ClubInfoCommand(int.Parse(args[1])),
        "clubfields" => ClubFieldsCommand(int.Parse(args[1])),
        "squad" => SquadCommand(int.Parse(args[1])),
        "heal" => HealCommand(string.Join(" ", args[1..])),
        "injuries" => InjuriesCommand(string.Join(" ", args[1..])),
        "bans" => BansCommand(string.Join(" ", args[1..])),
        "permitprobe" => PermitProbeCommand(),
        "nearptr" => NearPtrCommand(Convert.ToUInt64(args[1], 16), args[2..]),
        "reltype" => RelTypeCommand(Convert.ToInt32(args[1], 16)),
        "cities" => CitiesCommand(string.Join(" ", args[1..])),
        "careersurvey" => CareerSurvey(),
        "offerscan" => OfferScan(),
        "staffcontract" => StaffContractCommand(string.Join(" ", args[1..])),
        "callups" => CallUpsCommand(string.Join(" ", args[1..])),
        "ctypes" => ContractTypes(),
        "future" => FutureCommand(string.Join(" ", args[1..])),
        "proposalsurvey" => ProposalSurvey(),
        "offerlist" => OfferList(args.Length > 1 ? string.Join(" ", args[1..]) : null),
        "careers" => CareersCommand(string.Join(" ", args[1..])),
        "pfhist" => PlayerFieldHistogram(Enum.Parse<PlayerField>(args[1]), args.Length > 2 ? int.Parse(args[2]) : 0),
        "personidx" => PersonIndexCommand(Convert.ToUInt64(args[1], 16)),
        "near32" => Near32Command(int.Parse(args[1]), args[2..]),
        "neardate" => NearDateCommand(int.Parse(args[1]), int.Parse(args[2]), args[3..]),
        "vecscan" => VecScanCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "languages" => LanguagesCommand(string.Join(" ", args[1..])),
        "languagevalue" => LanguageValueCommand(int.Parse(args[1]), Convert.ToUInt64(args[2], 16), int.Parse(args[3])),
        "banmatches" => BanMatchesCommand(int.Parse(args[1]), Convert.ToUInt64(args[2], 16), int.Parse(args[3]), int.Parse(args[4])),
        "injuryduration" => InjuryDurationCommand(int.Parse(args[1]), Convert.ToUInt64(args[2], 16), int.Parse(args[3]), int.Parse(args[4])),
        "destroy" => DestroyCommand(string.Join(" ", args[1..])),
        "removebans" => RemoveBansCommand(string.Join(" ", args[1..])),
        "setplayer" => SetPlayerField(string.Join(" ", args[1..^2]), args[^2], long.Parse(args[^1])),
        "statuses" => StatusesCommand(string.Join(" ", args[1..])),
        "unh" => UnhCommand(string.Join(" ", args[1..])),
        "ptrvt" => PtrVtCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "nation" => args.Length >= 4 && long.TryParse(args[^1], out long nv) && !args[^2].All(char.IsDigit)
            ? NationCommand(string.Join(" ", args[1..^2]), args[^2], nv) : NationCommand(string.Join(" ", args[1..]), null, null),
        "stadium" => args.Length >= 4 && long.TryParse(args[^1], out long sv) && Enum.TryParse<Fm27Editor.Core.Stadiums.StadiumField>(args[^2], true, out _) ? StadiumCommand(string.Join(" ", args[1..^2]), args[^2], sv) : StadiumCommand(string.Join(" ", args[1..]), null, null),
        "poke" => PokeCommand(Convert.ToUInt64(args[1], 16), args[2], args[3]),
        "rootvecs" => RootVecsCommand(Convert.ToUInt64(args[1], 16)),
        "vecbounds" => VecBoundsCommand(Convert.ToUInt64(args[1], 16), Convert.ToUInt64(args[2], 16)),
        "near" => NearCommand(int.Parse(args[1]), int.Parse(args[2])),
        "clubprobe" => ClubProbeCommand(args[1], args[2], int.Parse(args[3]), args[4..]),
        "nationbytes" => NationBytesCommand(Convert.ToInt32(args[1], 16), int.Parse(args[2]), args[3..]),
        "nationprobe" =>NationProbeCommand(args[1], args[2], int.Parse(args[3]), args[4..]),
        "setstaff" =>SetStaffCommand(string.Join(" ", args[1..^2]), args[^2], int.Parse(args[^1])),
        "staff" => StaffCommand(string.Join(" ", args[1..])),
        "staffid" => StaffById(args[1..].Select(int.Parse).ToArray()),
        "staffmove" => StaffMove(int.Parse(args[1]), int.Parse(args[2]), args.Contains("--write")),
        "staffmovesurvey" => StaffMoveSurvey(int.Parse(args[1])),
        "setstaffabilityid" => SetStaffAbilityById(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3])),
        "staffcontractid" => StaffContractById(int.Parse(args[1]), null),
        "setstaffwageid" => StaffContractById(int.Parse(args[1]), int.Parse(args[2])),
        "staffcareers" => StaffCareers(),
        "staffknowledge" => StaffKnowledgeCommand(string.Join(" ", args[1..])),
        "tendencysurvey" => TendencySurvey(),
        "staffprobe" =>StaffProbeCommand(int.Parse(args[1])),
        "vthist" =>VtHistCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "findlist" =>FindListCommand(Convert.ToUInt64(args[1], 16)),
        "hex" =>HexCommand(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16)),
        "findperson" =>FindPersonCommand(args[1..].Select(int.Parse).ToArray()),
        "pokedate" =>PokeDateCommand(string.Join(" ", args[1..^3]), args[^3], Convert.ToInt32(args[^2], 16), DateOnly.Parse(args[^1])),
        "datediff" =>DateDiffCommand(args[1], Convert.ToInt32(args[2], 16), args[3], Convert.ToInt32(args[4], 16)),
        "match" =>MatchCommand(args[1], int.Parse(args[2]), args[3..]),
        "removeunh" =>RemoveUnhCommand(string.Join(" ", args[1..])),
        "byid" =>ById(args[1..].Select(int.Parse).ToArray()),
        "peek" => Peek(args[1], Convert.ToInt32(args[2], 16), int.Parse(args[3]), args[4..].Select(int.Parse).ToArray()),
        "setclub" => SetClubField(int.Parse(args[1]), args[2], long.Parse(args[3])),
        "clublists" => ClubListsCommand(int.Parse(args[1])),
        "clubvision" => ClubVisionCommand(args[1..]),
        "clubfinancelists" => ClubFinanceListsCommand(int.Parse(args[1])),
        "clubstaff" => ClubStaffCommand(int.Parse(args[1])),
        "comps" => CompsCommand(string.Join(" ", args[1..])),
        "awards" => AwardsCommand(string.Join(" ", args[1..])),
        "nationcap" => NationCapCommand(string.Join(" ", args[1..])),
        "nationteams" => NationTeamsCommand(string.Join(" ", args[1..])),
        "nationfind" => NationFindCommand(args[1], int.Parse(args[2]), args[3..]),
        "nationcolours" => NationColoursCommand(args[1], args[2]),
        "nationdeep" => NationDeepCommand(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), args[6..]),
        "compsurvey" => CompSurveyCommand(Convert.ToInt32(args[1], 16), int.Parse(args[2])),
        "compscan" => CompScanCommand(Convert.ToInt32(args[1], 16), int.Parse(args[2])),
        "compsnap" => CompSnapCommand(args[1]),
        "compdiff" => CompDiffCommand(args[1]),
        "clubdeep" => ClubDeepCommand(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), args[6..]),
        "clubrel" => ClubRelCommand(int.Parse(args[1]), null),
        "setclubrel" => ClubRelCommand(int.Parse(args[1]), args[2..]),
        "probe" => Probe(Convert.ToUInt64(args[1], 16), Convert.ToInt32(args[2], 16), int.Parse(args[3]), args[4..]),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is GameNotRunningException or UnsupportedBuildException or StaleObjectException or DiscoveryException or MemoryAccessException or ArgumentOutOfRangeException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

int Usage()
{
    Console.Error.WriteLine("usage: players [name] | verify <golden.json> | set <name> <field> <value> | find-offsets <golden.json> <out.json> [--date yyyy-MM-dd] | transfer dump|peek <name> [out.json] | transfer diff <before.json> <after.json>");
    return 1;
}

GameSession Attach(bool writable) => GameSession.Attach(GameSession.LoadProfiles(offsetsDir), writable);

int HealthPlayers()
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    int positionsInRange = sample.Count(p => p.Positions.All(v => v <= 20));
    int natural = sample.Count(p => p.Positions.Contains((byte)20));
    int attributesInRange = sample.Count(p => p.RawAttributes.All(v => v <= 100));
    int personalitiesInRange = sample.Count(p => p.Personality.All(v => v <= 20));
    PlayerAttribute[] goalkeeper = [PlayerAttribute.AerialReach, PlayerAttribute.CommandOfArea, PlayerAttribute.Communication,
        PlayerAttribute.Handling, PlayerAttribute.Kicking, PlayerAttribute.OneOnOnes, PlayerAttribute.TendencyToPunch,
        PlayerAttribute.Reflexes, PlayerAttribute.RushingOut];
    var keepers = sample.Where(p => p.Position(PlayerPosition.Goalkeeper) == 20).ToArray();
    var outfield = sample.Where(p => p.Position(PlayerPosition.Goalkeeper) <= 5).ToArray();
    var strikers = sample.Where(p => p.Position(PlayerPosition.Striker) == 20 && p.Position(PlayerPosition.DefenderCentral) <= 10).ToArray();
    var defenders = sample.Where(p => p.Position(PlayerPosition.DefenderCentral) == 20 && p.Position(PlayerPosition.Striker) <= 10).ToArray();
    static double? Mean(PlayerRecord[] group, Func<PlayerRecord, double> value) => group.Length == 0 ? null : group.Average(value);
    double? gkGap = Mean(keepers, p => goalkeeper.Average(a => (double)p.Raw(a))) - Mean(outfield, p => goalkeeper.Average(a => (double)p.Raw(a)));
    double? finishingGap = Mean(strikers, p => p.Raw(PlayerAttribute.Finishing)) - Mean(defenders, p => p.Raw(PlayerAttribute.Finishing));
    double? tacklingGap = Mean(defenders, p => p.Raw(PlayerAttribute.Tackling)) - Mean(strikers, p => p.Raw(PlayerAttribute.Tackling));
    bool enough = count >= 50;
    bool positionsOk = enough && positionsInRange >= count * .99 && natural >= count * .95;
    bool attributesOk = enough && attributesInRange >= count * .99 && gkGap >= 20 && finishingGap >= 20 && tacklingGap >= 20;
    bool personalityOk = enough && personalitiesInRange >= count * .99;
    Console.WriteLine($"build {session.Profile.Build}; sampled {count} of {all.Count}; read-only diagnostics");
    Console.WriteLine($"positions {(positionsOk ? "pass" : "check")}: range {positionsInRange}/{count}, natural {natural}/{count}");
    Console.WriteLine($"attributes {(attributesOk ? "pass" : "check")}: range {attributesInRange}/{count}; goalkeeper gap {gkGap:F1}, finishing gap {finishingGap:F1}, tackling gap {tacklingGap:F1}");
    Console.WriteLine($"groups: goalkeepers {keepers.Length}, outfield {outfield.Length}, strikers {strikers.Length}, defenders {defenders.Length}");
    Console.WriteLine($"personality {(personalityOk ? "pass" : "check")}: range {personalitiesInRange}/{count}");
    return 0;
}

int HealthStructure()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var contract = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    var team = profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    var club = profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");
    int ownerOffset = contract.Owner ?? throw new InvalidOperationException("Profile has no contract owner offset");
    int personOffset = profile.Relations?.PlayerPerson ?? throw new InvalidOperationException("Profile has no player person offset");
    var all = session.Players.LoadAll();
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    bool Pointer(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = session.Memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
    int contractCount = 0, contractFits = 0, teamCount = 0, teamFits = 0, unreadable = 0;
    var mismatches = new List<string>();
    foreach (var player in sample)
    {
        if (!Pointer(player.Address + (ulong)contract.PlayerContract, out ulong contractAddress)) { unreadable++; continue; }
        if (contractAddress != 0)
        {
            contractCount++;
            bool vtRead = Pointer(contractAddress, out ulong vt);
            bool ownerRead = Pointer(contractAddress + (ulong)ownerOffset, out ulong owner);
            if (vtRead && vt == session.ModuleBase + contract.ContractVtableRva && ownerRead && owner == player.Address + (ulong)personOffset) contractFits++;
            else if (mismatches.Count < 10)
                mismatches.Add($"  id {player.Id}: vtable {(vtRead ? $"0x{vt:X}" : "unreadable")}, owner {(ownerRead ? $"0x{owner:X}" : "unreadable")}, expected owner 0x{player.Address + (ulong)personOffset:X}");
        }
        if (!Pointer(player.Address + (ulong)team.PlayerTeam, out ulong teamAddress)) { unreadable++; continue; }
        if (teamAddress == 0) continue;
        teamCount++;
        if (!Pointer(teamAddress, out ulong teamVt) || teamVt != session.ModuleBase + team.VtableRva ||
            !Pointer(teamAddress + (ulong)contract.TeamClub, out ulong clubAddress) || clubAddress == 0 ||
            !Pointer(clubAddress, out ulong clubVt) || clubVt != session.ModuleBase + club.ClubVtableRva ||
            !Pointer(teamAddress + (ulong)team.Squad, out ulong begin) || !Pointer(teamAddress + (ulong)team.Squad + 8, out ulong end) ||
            begin == 0 || end < begin || (end - begin) % 8 != 0 || end - begin > 8 * 5000) continue;
        var bytes = new byte[(int)(end - begin)];
        if (!session.Memory.TryRead(begin, bytes)) continue;
        for (int i = 0; i < bytes.Length; i += 8)
            if (BitConverter.ToUInt64(bytes, i) == player.Address) { teamFits++; break; }
    }
    bool contractsOk = contractCount >= 50 && contractFits >= .95 * contractCount && unreadable == 0;
    bool teamsOk = teamCount >= 50 && teamFits >= .95 * teamCount && unreadable == 0;
    Console.WriteLine($"build {profile.Build}; sampled {count} of {all.Count}; read-only diagnostics");
    Console.WriteLine($"contracts {(contractsOk ? "pass" : "check")}: owner and vtable {contractFits}/{contractCount}");
    foreach (var mismatch in mismatches) Console.WriteLine(mismatch);
    Console.WriteLine($"teams {(teamsOk ? "pass" : "check")}: club and squad membership {teamFits}/{teamCount}; unreadable player pointers {unreadable}");
    return 0;
}

int HealthStaff()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var offsets = profile.Staff ?? throw new InvalidOperationException("Profile has no staff offsets");
    var contract = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    var all = session.Staff.LoadAll();
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    var keeper = new List<byte>(); var coachGk = new List<byte>(); var coachPhysio = new List<byte>();
    var physio = new List<byte>(); var physioJudge = new List<byte>(); var scout = new List<byte>();
    var otherTypes = new Dictionary<(ulong Vtable, short? Job), int>();
    int valuesFit = 0, contractCount = 0, contractFit = 0, ownerFit = 0, otherVtable = 0, unreadable = 0;
    bool Pointer(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = session.Memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
    foreach (var person in sample)
    {
        if (person.CurrentAbility is >= 0 and <= 200 && person.PotentialAbility is >= -200 and <= 200 &&
            person.HomeReputation is >= 0 and <= 10000 && person.CurrentReputation is >= 0 and <= 10000 &&
            person.WorldReputation is >= 0 and <= 10000 && person.Attributes.All(v => v <= 20) && person.Personality.All(v => v <= 20)) valuesFit++;
        if (!Pointer(person.Address + (ulong)offsets.Job, out ulong job)) { unreadable++; continue; }
        if (job == 0) continue;
        contractCount++;
        bool vtRead = Pointer(job, out ulong vt);
        bool ownerRead = Pointer(job + (ulong)offsets.JobPerson, out ulong owner);
        if (ownerRead && owner == person.Address) ownerFit++;
        if (vtRead && vt != session.ModuleBase + contract.ContractVtableRva && ownerRead && owner == person.Address)
        {
            otherVtable++;
            var key = (vt, person.JobCode);
            otherTypes[key] = otherTypes.GetValueOrDefault(key) + 1;
        }
        if (!vtRead || vt != session.ModuleBase + contract.ContractVtableRva || !ownerRead || owner != person.Address) continue;
        contractFit++;
        if (person.JobCode is not { } code) continue;
        int role = code > 1 && code % 2 == 1 ? code - 1 : code;
        var attributes = new byte[3];
        if (!session.Memory.TryRead(person.StaffAddress + (ulong)offsets.Attributes[StaffAttribute.CoachingGoalkeeping].Offset, attributes.AsSpan(0, 1)) ||
            !session.Memory.TryRead(person.StaffAddress + (ulong)offsets.Attributes[StaffAttribute.Physiotherapy].Offset, attributes.AsSpan(1, 1)) ||
            !session.Memory.TryRead(person.StaffAddress + (ulong)offsets.Attributes[StaffAttribute.JudgingPlayerPotential].Offset, attributes.AsSpan(2, 1))) { unreadable++; continue; }
        if (role == 34) keeper.Add(attributes[0]);
        if (role is 2 or 20) { coachGk.Add(attributes[0]); coachPhysio.Add(attributes[1]); }
        if (role is 12 or 50) { physio.Add(attributes[1]); physioJudge.Add(attributes[2]); }
        if (role is 14 or 44) scout.Add(attributes[2]);
    }
    static double? Gap(List<byte> high, List<byte> low) => high.Count == 0 || low.Count == 0 ? null : high.Average(v => (double)v) - low.Average(v => (double)v);
    double? gk = Gap(keeper, coachGk), medical = Gap(physio, coachPhysio), judging = Gap(scout, physioJudge);
    bool fits = count >= 50 && valuesFit >= .97 * count && contractCount >= 50 && contractFit >= .95 * contractCount &&
        gk >= 20 && medical >= 20 && judging >= 20 && unreadable == 0;
    Console.WriteLine($"build {profile.Build}; sampled {count} of {all.Count}; read-only diagnostics");
    Console.WriteLine($"staff {(fits ? "pass" : "check")}: values {valuesFit}/{count}, contracts {contractFit}/{contractCount}, owner {ownerFit}/{contractCount}, other vtable with matching owner {otherVtable}, unreadable {unreadable}");
    foreach (var entry in otherTypes.OrderByDescending(e => e.Value).Take(10))
        Console.WriteLine($"  other job vtable 0x{entry.Key.Vtable:X}, job {entry.Key.Job}: {entry.Value}");
    Console.WriteLine($"goalkeeping gap {gk:F1}, physiotherapy gap {medical:F1}, judging gap {judging:F1}");
    Console.WriteLine($"groups: goalkeeper coaches {keeper.Count}, coaches {coachGk.Count}, physios {physio.Count}, scouts {scout.Count}");
    return 0;
}

int HealthClubs()
{
    using var session = Attach(writable: false);
    var profile = session.Profile;
    var co = profile.Club ?? throw new InvalidOperationException("Profile has no club offsets");
    var team = profile.Team ?? throw new InvalidOperationException("Profile has no team offsets");
    var contract = profile.Contract ?? throw new InvalidOperationException("Profile has no contract offsets");
    int teamsOffset = co.Lists?.Teams ?? throw new InvalidOperationException("Profile has no club teams offset");
    var all = session.LoadClubs(session.Players.LoadAll());
    int count = Math.Min(2000, all.Count);
    var sample = Enumerable.Range(0, count).Select(i => all[(int)((long)i * all.Count / count)]).ToArray();
    bool Pointer(ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        bool ok = session.Memory.TryRead(address, bytes);
        value = ok ? BitConverter.ToUInt64(bytes) : 0;
        return ok;
    }
    int fit = 0, missing = 0;
    foreach (var club in sample)
    {
        if (!Pointer(club.Address + (ulong)teamsOffset, out ulong begin) ||
            !Pointer(club.Address + (ulong)teamsOffset + 8, out ulong end) || begin == 0 || end <= begin ||
            (end - begin) % 8 != 0 || end - begin > 8 * 128) { missing++; continue; }
        if (!Pointer(begin, out ulong senior) || senior == 0 || !Pointer(senior, out ulong vt) ||
            vt != session.ModuleBase + team.VtableRva || !Pointer(senior + (ulong)contract.TeamClub, out ulong owner) || owner != club.Address) continue;
        long? reputation = session.ClubFields.Read(senior, ClubField.TeamReputation);
        if (club.Id is > 0 && reputation is >= 0 and <= 10000) fit++;
    }
    bool ok = count >= 50 && fit >= .95 * count;
    Console.WriteLine($"build {profile.Build}; sampled {count} of {all.Count} clubs reachable from players; read-only diagnostics");
    Console.WriteLine($"clubs {(ok ? "pass" : "check")}: senior team/owner/id/reputation {fit}/{count}; invalid or empty team list {missing}");
    return 0;
}

int BannedIds()
{
    using var session = Attach(writable: false);
    int shown = 0;
    foreach (var p in session.Players.LoadAll())
    {
        int count = session.ReadBans(p).Count(BanWriter.IsMatchBan);
        if (count == 0) continue;
        Console.WriteLine($"{p.Id} {p.DisplayName}: {count} match bans");
        if (++shown == 10) break;
    }
    return 0;
}

int BanUndoCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var writer = new BanWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.PlanRemoveMatchBans(player) ?? throw new InvalidOperationException("No match bans");
    Console.WriteLine($"{id}: {plan.OldCount} records, {plan.OldCount - plan.NewCount} match bans");
    writer.ApplyRemoval(plan);
    Console.WriteLine($"after lift: {session.ReadBans(player).Count} records");
    writer.UndoRemoval(plan);
    int restored = session.ReadBans(player).Count;
    Console.WriteLine($"after undo: {restored} records");
    if (restored != plan.OldCount) throw new StaleObjectException("Ban list did not restore");
    return 0;
}

int UnhappyIds()
{
    using var session = Attach(writable: false);
    var date = session.ReadGameDate() ?? throw new InvalidOperationException("Game date unavailable");
    int shown = 0;
    foreach (var p in session.Players.LoadAll())
    {
        var active = session.ReadUnhappiness(p).Count(x => x.IsActive(date));
        if (active == 0) continue;
        Console.WriteLine($"{p.Id} {p.DisplayName}: {active} active");
        if (++shown == 10) break;
    }
    return 0;
}

// unhundo <player id>: remove active unhappiness and undo it on a test save.
int UnhUndoCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var date = session.ReadGameDate() ?? throw new InvalidOperationException("Game date unavailable");
    var writer = new UnhappinessWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.PlanRemoveActive(player, date) ?? throw new InvalidOperationException("No active unhappiness");
    Console.WriteLine($"{id}: {plan.OldCount} records, {plan.OldCount - plan.NewCount} active");
    writer.ApplyRemoval(plan);
    Console.WriteLine($"after removal: {session.ReadUnhappiness(player).Count} records");
    writer.UndoRemoval(plan);
    int restored = session.ReadUnhappiness(player).Count;
    Console.WriteLine($"after undo: {restored} records");
    if (restored != plan.OldCount) throw new StaleObjectException("Unhappiness did not restore");
    return 0;
}

// squadhealcheck <club id>: plan, apply and undo the first team's Heal preset.
int MassWageCheck(int change, int[] ids)
{
    using var session = Attach(writable: true);
    var all = session.Players.LoadAll();
    var players = ids.Select(id => all.Single(p => p.Id == id)).ToArray();
    var plan = PlayerWageMassEdit.Plan(players, MassEditMode.Change, change);
    foreach (var item in plan) Console.WriteLine($"{item.Player.Id}: {item.Before} -> {item.After}");
    foreach (var item in plan) PlayerWageMassEdit.Apply(item, session.Players, session.Contracts);
    foreach (var item in plan.Reverse()) PlayerWageMassEdit.Undo(item, session.Players, session.Contracts);
    foreach (var item in plan)
        if (session.Players.Reload(item.Player.Address)?.Contract?.Wage != item.Before) throw new StaleObjectException("Wage did not restore");
    Console.WriteLine($"{plan.Count} wages applied, undone, and read back");
    return 0;
}

int StaffWageMassCommand(int delta, string[] args)
{
    bool write = args.Contains("--write");
    var ids = args.Where(a => a != "--write").Select(int.Parse).ToArray();
    using var session = Attach(writable: write);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffWageMassEdit.Plan(targets, session.StaffContracts, MassEditMode.Change, delta);
    foreach (var change in plan.Changes) Console.WriteLine($"{change.Staff.Id}: {change.Before} -> {change.After}");
    Console.WriteLine($"{plan.Changes.Count} changes, {plan.Unavailable} unavailable");
    if (!write) return 0;
    foreach (var change in plan.Changes) StaffWageMassEdit.Apply(change, session.Staff, session.StaffContracts);
    foreach (var change in plan.Changes)
    {
        var person = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff no longer exists");
        var contract = session.StaffContracts.Read(person) ?? throw new StaleObjectException("Contract no longer exists");
        if (contract.Wage != change.After) throw new StaleObjectException("Wage readback differs");
        Console.WriteLine($"{person.Id}: read back {contract.Wage}");
    }
    return 0;
}

int Retirements(int[] ids)
{
    using var session = Attach(writable: false);
    var reader = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
    var plans = reader.ReadAll();
    var wanted = ids.ToHashSet();
    var players = session.Players.LoadAll().Where(p => p.Id is { } id && (wanted.Count == 0 || wanted.Contains(id))).ToArray();
    Console.WriteLine($"{plans.Count} planned retirements");
    foreach (var player in players)
    {
        uint person = reader.PersonId(player);
        if (plans.TryGetValue(person, out var plan))
            Console.WriteLine($"{player.Id} {player.DisplayName}: {plan.Date:yyyy-MM-dd} (person {person})");
        else if (wanted.Count > 0) Console.WriteLine($"{player.Id} {player.DisplayName}: none (person {person})");
    }
    return 0;
}

int ClubMassCheck(ClubField field, MassEditMode mode, long value, int[] ids)
{
    using var session = Attach(writable: true);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var targets = ids.Select(id => clubs.Single(c => c.Id == id)).ToArray();
    var plan = ClubMassEdit.Plan(targets, field, mode, value, int.MinValue, int.MaxValue, session.Memory, session.Profile, session.ModuleBase);
    foreach (var change in plan) Console.WriteLine($"{change.Club.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) ClubMassEdit.Apply(change, session.Memory, session.Profile, session.ModuleBase);
    foreach (var change in plan.Reverse()) ClubMassEdit.Undo(change, session.Memory, session.Profile, session.ModuleBase);
    foreach (var change in plan)
        if (session.ClubFields.Read(change.Club.Address, field) != change.Before) throw new StaleObjectException("Club field did not restore");
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int StaffPersonalityMassCheck(StaffPersonality field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffPersonalityMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Staff.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) StaffPersonalityMassEdit.Apply(change, session.Staff, session.StaffWriter);
    foreach (var change in plan.Reverse()) StaffPersonalityMassEdit.Undo(change, session.Staff, session.StaffWriter);
    foreach (var change in plan)
    {
        var current = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff member disappeared");
        if (current.Personality[(int)field] != change.Before) throw new StaleObjectException("Personality did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int StaffAttributeMassCheck(StaffAttribute field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffAttributeMassEdit.Plan(targets, field, mode, value, session.StaffWriter);
    foreach (var change in plan) Console.WriteLine($"{change.Staff.Id} {change.Field}: {change.Before} ({change.BeforeRaw}) -> {change.After} ({change.AfterRaw})");
    foreach (var change in plan) StaffAttributeMassEdit.Apply(change, session.Staff, session.StaffWriter);
    foreach (var change in plan.Reverse()) StaffAttributeMassEdit.Undo(change, session.Staff, session.StaffWriter);
    foreach (var change in plan)
    {
        var current = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff member disappeared");
        if (session.StaffWriter.ReadAttributeRaw(current, change.Field) != change.BeforeRaw) throw new StaleObjectException("Attribute did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and exact bytes read back");
    return 0;
}

int StaffMassCheck(StaffMassField field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll();
    var targets = ids.Select(id => staff.Single(s => s.Id == id)).ToArray();
    var plan = StaffMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Staff.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) StaffMassEdit.Apply(change, session.Staff, session.StaffWriter);
    foreach (var change in plan.Reverse()) StaffMassEdit.Undo(change, session.Staff, session.StaffWriter);
    foreach (var change in plan)
    {
        var current = session.Staff.Reload(change.Staff.Address) ?? throw new StaleObjectException("Staff member disappeared");
        if (StaffMassEdit.Plan([current], change.Field, MassEditMode.Set, change.Before).Count != 0)
            throw new StaleObjectException("Field did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int MassRatingCheck(string kind, string fieldName, MassEditMode mode, int value, int[] ids)
{
    var field = kind switch
    {
        "position" => new PlayerRatingField(Position: Enum.Parse<PlayerPosition>(fieldName, true)),
        "personality" => new PlayerRatingField(Personality: Enum.Parse<PersonalityAttribute>(fieldName, true)),
        _ => throw new ArgumentException("Expected position or personality")
    };
    using var session = Attach(writable: true);
    var players = session.Players.LoadAll();
    var targets = ids.Select(id => players.Single(p => p.Id == id)).ToArray();
    var plan = PlayerRatingMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerRatingMassEdit.Apply(change, session.Players, session.Writer);
    foreach (var change in plan.Reverse()) PlayerRatingMassEdit.Undo(change, session.Players, session.Writer);
    foreach (var change in plan)
    {
        var current = session.Players.Reload(change.Player.Address) ?? throw new StaleObjectException("Player disappeared");
        if (PlayerRatingMassEdit.Plan([current], change.Field, MassEditMode.Set, change.Before).Count != 0)
            throw new StaleObjectException("Rating did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int MassQualityCheck(PlayerQuality field, MassEditMode mode, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var players = session.Players.LoadAll();
    var targets = ids.Select(id => players.Single(p => p.Id == id)).ToArray();
    var plan = PlayerQualityMassEdit.Plan(targets, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerQualityMassEdit.Apply(change, session.Players, session.Writer);
    foreach (var change in plan.Reverse()) PlayerQualityMassEdit.Undo(change, session.Players, session.Writer);
    foreach (var change in plan)
    {
        var current = session.Players.Reload(change.Player.Address) ?? throw new StaleObjectException("Player disappeared");
        if (PlayerQualityMassEdit.Plan([current], change.Field, MassEditMode.Set, change.Before).Count != 0)
            throw new StaleObjectException("Field did not restore");
    }
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int MassScalarCheck(PlayerField field, MassEditMode mode, long value, int[] ids)
{
    using var session = Attach(writable: true);
    var players = session.Players.LoadAll();
    var targets = ids.Select(id => players.Single(p => p.Id == id)).ToArray();
    var plan = PlayerScalarMassEdit.Plan(targets, session.PlayerFields, field, mode, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Field}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerPresetBatch.Apply(change, session.Players, session.PlayerFields);
    foreach (var change in plan.Reverse()) PlayerPresetBatch.Undo(change, session.Players, session.PlayerFields);
    foreach (var change in plan)
        if (session.PlayerFields.Read(change.Player, change.Field) != change.Before) throw new StaleObjectException("Scalar did not restore");
    Console.WriteLine($"{plan.Count} changes applied, undone, and read back");
    return 0;
}

int SquadHealCheck(int clubId)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).Single(c => c.Id == clubId);
    var team = club.FirstTeam ?? throw new InvalidOperationException("No first team");
    var squad = new SquadReader(session.Memory, session.Profile, session.ModuleBase).Read(team.Address);
    var writer = new InjuryWriter(session.Memory, session.Profile, session.ModuleBase);
    var injuries = squad.Select(writer.PlanRemoveAll).Where(p => p is not null).Cast<InjuryRemovalPlan>().ToArray();
    var fitness = PlayerPresetBatch.Plan(squad, session.PlayerFields, PlayerPreset.Fitness);
    Console.WriteLine($"{club.DisplayName}: {squad.Count} players, {injuries.Length} injury lists, {fitness.Count} fitness fields");
    foreach (var injury in injuries) writer.ApplyRemoval(injury);
    foreach (var c in fitness) PlayerPresetBatch.Apply(c, session.Players, session.PlayerFields);
    foreach (var c in fitness.Reverse()) PlayerPresetBatch.Undo(c, session.Players, session.PlayerFields);
    foreach (var injury in injuries.Reverse()) writer.UndoRemoval(injury);
    foreach (var c in fitness)
        if (session.PlayerFields.Read(c.Player, c.Field) != c.Before) throw new StaleObjectException("Fitness did not restore");
    foreach (var injury in injuries)
        if (session.ReadInjuries(injury.Player).Count != injury.Injuries.Count) throw new StaleObjectException("Injuries did not restore");
    Console.WriteLine("Heal applied and undone; all changed values read back");
    return 0;
}

// injuryundocheck <player id>: remove injuries and restore them for a test save.
int InjuryUndoCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var writer = new InjuryWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.PlanRemoveAll(player) ?? throw new InvalidOperationException("Player has no injuries");
    Console.WriteLine($"{id}: {plan.Injuries.Count} injuries before");
    writer.ApplyRemoval(plan);
    Console.WriteLine($"after heal: {session.ReadInjuries(player).Count}");
    writer.UndoRemoval(plan);
    int restored = session.ReadInjuries(player).Count;
    Console.WriteLine($"after undo: {restored}");
    if (restored != plan.Injuries.Count) throw new StaleObjectException("Injuries did not restore");
    return 0;
}

// presetbatchcheck <fitness|morale> <player ids...>
int PresetBatchCheck(PlayerPreset preset, int[] ids)
{
    using var session = Attach(writable: true);
    var all = session.Players.LoadAll();
    var players = ids.Select(id => all.Single(p => p.Id == id)).ToArray();
    var plan = PlayerPresetBatch.Plan(players, session.PlayerFields, preset);
    foreach (var c in plan) Console.WriteLine($"{c.Player.Id} {c.Field}: {c.Before} -> {c.After}");
    foreach (var c in plan) PlayerPresetBatch.Apply(c, session.Players, session.PlayerFields);
    foreach (var c in plan.Reverse()) PlayerPresetBatch.Undo(c, session.Players, session.PlayerFields);
    foreach (var c in plan)
    {
        var actual = session.PlayerFields.Read(c.Player, c.Field);
        if (actual != c.Before) throw new StaleObjectException($"Undo failed for {c.Player.Id} {c.Field}");
    }
    Console.WriteLine($"Applied and undone {plan.Count} changes; all values read back");
    return 0;
}

// massmulticheck <player id>: exercise a two-field batch and undo on a test save.
int MassMultiCheck(int id)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var plan = PlayerMassEdit.Plan([player], [new(PlayerAttribute.Dribbling, MassEditMode.Change, -1), new(PlayerAttribute.Pace, MassEditMode.Change, -1)]);
    foreach (var c in plan) Console.WriteLine($"{id} {c.Attribute}: {c.Before} -> {c.After}");
    foreach (var c in plan) PlayerMassEdit.Apply(c, session.Players, session.Writer);
    foreach (var c in plan.Reverse()) PlayerMassEdit.Undo(c, session.Players, session.Writer);
    var after = session.Players.Reload(player.Address)!;
    foreach (var c in plan)
    {
        Console.WriteLine($"{c.Attribute}: raw {after.Raw(c.Attribute)}, expected {c.BeforeRaw}");
        if (after.Raw(c.Attribute) != c.BeforeRaw) throw new StaleObjectException("Batch undo did not restore original value");
    }
    return 0;
}

// masscheck <attribute> <value> <player ids...> previews, applies and undoes one batch.
int MassCheck(PlayerAttribute attribute, int value, int[] ids)
{
    using var session = Attach(writable: true);
    var all = session.Players.LoadAll();
    var selected = ids.Select(id => all.Single(p => p.Id == id)).ToArray();
    var plan = PlayerMassEdit.Plan(selected, attribute, MassEditMode.Set, value);
    foreach (var change in plan) Console.WriteLine($"{change.Player.Id} {change.Player.DisplayName}: {change.Before} -> {change.After}");
    foreach (var change in plan) PlayerMassEdit.Apply(change, session.Players, session.Writer);
    foreach (var change in plan.Reverse()) PlayerMassEdit.Undo(change, session.Players, session.Writer);
    foreach (var change in plan)
    {
        var after = session.Players.Reload(change.Player.Address)!;
        Console.WriteLine($"{after.Id} readback raw {after.Raw(attribute)} expected {change.BeforeRaw}");
        if (after.Raw(attribute) != change.BeforeRaw) throw new StaleObjectException("Batch undo did not restore original value");
    }
    return 0;
}

// freezecheck <player id> <attribute> <temporary displayed value>: exercise one freeze cycle on a test save.
int FreezeDecreaseReconnect(string path, int id, PlayerAttribute attribute)
{
    byte improved;
    using (var session = Attach(writable: true))
    {
        var player = session.Players.LoadAll().Single(p => p.Id == id);
        byte initial = player.Raw(attribute);
        if (initial is < 6 or > 95) throw new InvalidOperationException("Sample must permit both a decrease and an increase");
        improved = (byte)(initial - 5);
        var runner = new FreezeRunner(session, path);
        runner.SetPlayer(player, [(attribute, FreezeMode.AllowDecrease)]);
        session.Writer.SetAttributeRaw(player, attribute, improved);
        if (runner.Tick() != 0) throw new StaleObjectException("Decrease was not accepted");
    }
    using var reconnected = Attach(writable: true);
    var fresh = reconnected.Players.LoadAll().Single(p => p.Id == id);
    var restored = new FreezeRunner(reconnected, path);
    var field = restored.Players.Single(p => p.Id == id).Fields.Single(f => f.Attribute == attribute);
    if (field.Mode != FreezeMode.AllowDecrease || field.Value != improved) throw new StaleObjectException("Freeze mode or value was not restored");
    reconnected.Writer.SetAttributeRaw(fresh, attribute, (byte)(improved + 5));
    if (restored.Tick() != 1 || reconnected.Players.Reload(fresh.Address)?.Raw(attribute) != improved)
        throw new StaleObjectException("Reconnected freezer did not correct the value");
    Console.WriteLine($"{id} {attribute}: reconnected AllowDecrease at {improved}, corrected increase");
    return 0;
}

int FreezeDecreaseCheck(int id, PlayerAttribute attribute)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    byte initial = player.Raw(attribute);
    if (initial is < 6 or > 95) throw new InvalidOperationException("Sample must permit both a decrease and an increase");
    var freezer = FrozenPlayer.Capture(player, [(attribute, FreezeMode.AllowDecrease)]);
    session.Writer.SetAttributeRaw(player, attribute, (byte)(initial - 5));
    if (freezer.Reconcile(session.Players, session.Writer) != 0) throw new StaleObjectException("Decrease was not accepted");
    var improved = session.Players.Reload(player.Address) ?? throw new StaleObjectException("Player disappeared");
    session.Writer.SetAttributeRaw(improved, attribute, initial);
    if (freezer.Reconcile(session.Players, session.Writer) != 1) throw new StaleObjectException("Increase was not corrected");
    byte after = session.Players.Reload(player.Address)!.Raw(attribute);
    if (after != initial - 5) throw new StaleObjectException("Freezer value did not persist");
    Console.WriteLine($"{id} {attribute}: {initial} -> {initial - 5} -> {initial} -> {after}");
    return 0;
}

int FreezeCheck(int id, PlayerAttribute attribute, int temporary)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var freeze = FrozenPlayer.Capture(player, [(attribute, FreezeMode.Fixed)]);
    int before = player.Attribute(attribute);
    session.Writer.SetAttribute(player, attribute, temporary);
    int changed = session.Players.Reload(player.Address)!.Attribute(attribute);
    int corrections = freeze.Reconcile(session.Players, session.Writer);
    int after = session.Players.Reload(player.Address)!.Attribute(attribute);
    Console.WriteLine($"{id} {player.DisplayName} {attribute}: {before} -> {changed} -> {after}, corrections {corrections}");
    return 0;
}

// freezebalancecheck <club id> <temporary balance>
int FreezeBalanceCheck(int id, long temporary)
{
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).Single(c => c.Id == id);
    var access = session.ClubFields;
    var frozen = Fm27Editor.Core.Clubs.ClubBalanceFreezer.Capture(club, access, FreezeMode.AllowIncrease);
    long before = frozen.Value;
    access.Set(club.Address, Fm27Editor.Core.Clubs.ClubField.Balance, before, temporary);
    bool corrected = frozen.Reconcile(session.Memory, session.Profile, access);
    long? after = access.Read(club.Address, Fm27Editor.Core.Clubs.ClubField.Balance);
    Console.WriteLine($"{id} {club.DisplayName} balance: {before} -> {temporary} -> {after}, corrected {corrected}");
    return 0;
}

// freezestorecheck <temporary file> <player id> <club id>
int FreezeStoreCheck(string path, int playerId, int clubId)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var clubs = session.LoadClubs(players);
    var player = players.Single(p => p.Id == playerId);
    var club = clubs.Single(c => c.Id == clubId);
    var frozenPlayer = FrozenPlayer.Capture(player, [(PlayerAttribute.Dribbling, FreezeMode.AllowIncrease)]);
    var frozenClub = Fm27Editor.Core.Clubs.ClubBalanceFreezer.Capture(club, session.ClubFields, FreezeMode.AllowIncrease);
    string key = FreezeStore.SaveKey(session);
    FreezeStore.Save(path, key, FreezeStore.Snapshot([frozenPlayer], [frozenClub]));
    var resolved = FreezeStore.Resolve(FreezeStore.Load(path, key), players, clubs);
    if (resolved.Players.Count != 1 || resolved.Clubs.Count != 1) throw new InvalidOperationException("Freeze entries did not resolve");
    Console.WriteLine($"key {key}; player {resolved.Players[0].Id} raw {resolved.Players[0].Fields.Single().Value}; club {resolved.Clubs[0].Id} balance {resolved.Clubs[0].Value}");
    return 0;
}

// freezeruncheck <temporary file> <player id> <temporary displayed dribbling>
int FreezeRunCheck(string path, int id, int temporary)
{
    int before;
    using (var firstSession = Attach(writable: true))
    {
        var firstPlayer = firstSession.Players.LoadAll().Single(p => p.Id == id);
        before = firstPlayer.Attribute(PlayerAttribute.Dribbling);
        new FreezeRunner(firstSession, path).SetPlayer(firstPlayer, [(PlayerAttribute.Dribbling, FreezeMode.Fixed)]);
    }
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var reconnected = new FreezeRunner(session, path);
    session.Writer.SetAttribute(player, PlayerAttribute.Dribbling, temporary);
    int corrected = reconnected.Tick();
    int after = session.Players.Reload(player.Address)!.Attribute(PlayerAttribute.Dribbling);
    Console.WriteLine($"{id}: stored {before}, changed {temporary}, restored {after}, corrections {corrected}");
    return 0;
}

// exportplayers <csv|json> <path> [player id ...]
int ExportPlayers(string format, string path, int[] ids)
{
    if (format is not ("csv" or "json")) throw new ArgumentOutOfRangeException(nameof(format), "Expected csv or json");
    using var session = Attach(writable: false);
    var wanted = ids.ToHashSet();
    var players = session.Players.LoadAll().Where(p => ids.Length == 0 || p.Id is { } id && wanted.Contains(id)).ToArray();
    var date = session.ReadGameDate();
    foreach (var player in players) player.GameDate = date;
    using var output = File.Create(path);
    if (format == "csv") PlayerExport.WriteCsv(output, players); else PlayerExport.WriteJson(output, players);
    Console.WriteLine($"{players.Length} players exported to {path}");
    return 0;
}

int KeepPlayingBatch(string[] args)
{
    bool write = args.Contains("--write");
    var ids = args.Where(a => a != "--write").Select(int.Parse).ToArray();
    if (ids.Length == 0 || ids.Distinct().Count() != ids.Length) throw new ArgumentException("Provide unique player IDs");
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var writer = new KeepPlayingWriter(session.Memory, session.Profile, session.ModuleBase);
    var plans = ids.Select(id => writer.Plan(all.Single(p => p.Id == id))).ToArray();
    Console.WriteLine($"{plans.Length} players planned");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    foreach (var preview in plans)
    {
        var current = writer.Replan(preview);
        writer.Apply(current);
        Console.WriteLine($"{current.Player.Id}: applied");
    }
    var reader = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
    var remaining = reader.ReadAll();
    foreach (var preview in plans)
    {
        byte traits = session.Memory.Read(preview.Player.Address + (ulong)session.Profile.PreferredMoves!.Value + 6, 1)[0];
        if (remaining.ContainsKey(preview.PersonId) || (traits & 3) != 2) throw new StaleObjectException("Retirement verification failed");
        Console.WriteLine($"{preview.Player.Id}: no retirement, trait {traits:X2}");
    }
    return 0;
}

int CaptainCommand(string[] args)
{
    bool write = args.Contains("--write");
    int restoreIndex = Array.IndexOf(args, "--restore");
    ulong? restore = restoreIndex >= 0
        ? Convert.ToUInt64(args[restoreIndex + 1].Replace("0x", ""), 16)
        : null;
    var words = args.Where((_, i) => i != restoreIndex && (restoreIndex < 0 || i != restoreIndex + 1) && args[i] != "--write").ToArray();
    int id = int.Parse(words[0]);
    var role = words.Length > 1 && words[1].Equals("vice", StringComparison.OrdinalIgnoreCase) ? CaptainRole.ViceCaptain : CaptainRole.Captain;
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var service = new TeamCaptainService(session.Memory, session.Profile, session.ModuleBase);
    var plan = service.Plan(player, role, restore);
    Console.WriteLine($"{id} {player.DisplayName}: {TeamCaptainService.Label(role)} team 0x{plan.Team:X} club 0x{plan.Club:X} person 0x{plan.Person:X} (was 0x{plan.Previous:X})");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    service.Apply(plan);
    ulong after = BitConverter.ToUInt64(session.Memory.Read(plan.Address, 8), 0);
    if (after != plan.Person) throw new StaleObjectException("Captain readback failed");
    Console.WriteLine($"applied; {TeamCaptainService.Label(role)} is 0x{after:X}");
    return 0;
}

int KeepPlaying(int id, bool write)
{
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var writer = new KeepPlayingWriter(session.Memory, session.Profile, session.ModuleBase);
    var plan = writer.Plan(player);
    Console.WriteLine($"{id} {player.DisplayName}: retirement {plan.RetirementDate?.ToString("yyyy-MM-dd") ?? "none"}, {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    writer.Apply(plan);
    var reader = new PlannedRetirements(session.Memory, session.Profile, session.ModuleBase);
    Console.WriteLine($"applied; retirement {(reader.ReadAll().ContainsKey(plan.PersonId) ? "present" : "none")}; trait byte {session.Memory.Read(player.Address + (ulong)session.Profile.PreferredMoves!.Value + 6, 1)[0]:X2}");
    return 0;
}

int MaxAttributesCommand(int id, bool write)
{
    using var session = Attach(writable: write);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var plan = MaxAttributes.Plan([player]);
    Console.WriteLine($"{id} {player.DisplayName}: {plan.Changes.Count} changes, {plan.Unavailable} unavailable");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    foreach (var change in plan.Changes) PlayerMassEdit.Apply(change, session.Players, session.Writer);
    var after = session.Players.Reload(player.Address) ?? throw new StaleObjectException("Player no longer readable");
    int correct = plan.Changes.Count(c => after.Raw(c.Attribute) == c.AfterRaw);
    Console.WriteLine($"verified {correct}/{plan.Changes.Count}; Crossing {after.Attribute(PlayerAttribute.Crossing)}, Dirtiness {after.Attribute(PlayerAttribute.Dirtiness)}, InjuryProneness {after.Attribute(PlayerAttribute.InjuryProneness)}");
    if (correct != plan.Changes.Count) throw new StaleObjectException("Some values changed after application");
    return 0;
}

int StaffMove(int id, int buyerId, bool write)
{
    using var session = Attach(writable: write);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var from = clubs.Single(c => c.Address == staff.Club?.Address);
    var to = clubs.Single(c => c.Id == buyerId);
    var service = new Fm27Editor.Core.Staff.StaffMoveService(session.Memory, session.Profile, session.ModuleBase);
    var plan = service.Plan(staff, from, to, session.ReadGameDate());
    Console.WriteLine($"{staff.DisplayName}: {from.DisplayName} -> {to.DisplayName}, {string.Join(", ", plan.Departments)}, {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    service.Apply(plan);
    var after = session.Staff.Reload(staff.Address) ?? throw new StaleObjectException("Staff no longer readable");
    Console.WriteLine($"applied; club now {after.Club?.DisplayName ?? "-"}");
    return 0;
}

int StaffMoveSurvey(int id)
{
    using var session = Attach(writable: false);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var from = clubs.Single(c => c.Address == staff.Club?.Address);
    var service = new Fm27Editor.Core.Staff.StaffMoveService(session.Memory, session.Profile, session.ModuleBase);
    int found = 0;
    var failures = new Dictionary<string, int>();
    foreach (var club in clubs.Where(c => c.Address != from.Address && c.Id is > 0))
    {
        try
        {
            var plan = service.Plan(staff, from, club);
            Console.WriteLine($"{club.Id} {club.DisplayName}: {string.Join(", ", plan.Departments)}; {plan.Writes.Count} writes");
            if (++found == 5) break;
        }
        catch (Exception e) when (e is InvalidOperationException or MemoryAccessException or StaleObjectException)
        { failures[e.Message] = failures.GetValueOrDefault(e.Message) + 1; }
    }
    Console.WriteLine($"{found} candidates; read only");
    if (found == 0) foreach (var failure in failures.OrderByDescending(x => x.Value).Take(5)) Console.WriteLine($"  {failure.Value}: {failure.Key}");
    return 0;
}

int StaffById(int[] ids)
{
    using var session = Attach(writable: false);
    var wanted = ids.ToHashSet();
    var staff = session.Staff.LoadAll();
    Console.WriteLine($"{staff.Count} staff read");
    foreach (var person in staff.Where(p => wanted.Contains(p.Id)))
        Console.WriteLine($"{person.Id} {person.DisplayName}: CA {person.CurrentAbility}, PA {person.PotentialAbility}, club {person.Club?.DisplayName ?? "-"}, team {person.TeamName ?? "-"}, person 0x{person.Address:X}, staff 0x{person.StaffAddress:X}");
    return 0;
}

int SetStaffAbilityById(int id, int ca, int pa)
{
    using var session = Attach(writable: true);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    session.StaffWriter.SetAbility(staff, ca, pa);
    var after = session.Staff.Reload(staff.Address) ?? throw new StaleObjectException("Staff no longer readable");
    Console.WriteLine($"{id} {staff.DisplayName}: CA {staff.CurrentAbility} -> {after.CurrentAbility}, PA {staff.PotentialAbility} -> {after.PotentialAbility}");
    return 0;
}

int StaffContractById(int id, int? wage)
{
    using var session = Attach(writable: wage is not null);
    var staff = session.Staff.LoadAll().Single(s => s.Id == id);
    var contract = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is not readable");
    Console.WriteLine($"{id} {staff.DisplayName}: wage {contract.Wage}, type {contract.Type}, rolling {contract.Rolling}, started {contract.Started:yyyy-MM-dd}, expires {contract.Expires:yyyy-MM-dd}");
    if (wage is not { } newWage) return 0;
    if (contract.Started is not { } started || contract.Expires is not { } expires)
        throw new InvalidOperationException("Staff contract dates are not readable");
    session.StaffContracts.Set(staff, contract, new OfferContractFields(newWage, (ContractType)contract.Type, contract.Rolling, started, expires));
    var after = session.StaffContracts.Read(staff) ?? throw new StaleObjectException("Staff contract is no longer readable");
    Console.WriteLine($"{id} {staff.DisplayName}: wage {contract.Wage} -> {after.Wage}");
    return 0;
}

int Status()
{
    using var session = Attach(writable: true);
    Console.WriteLine($"build {session.Profile.Build}, access {(session.CanWrite ? "read/write" : "read-only")}");
    return 0;
}

int Players(string? filter)
{
    using var session = Attach(writable: false);
    var sw = Stopwatch.StartNew();
    var players = session.Players.LoadAll();
    var today = session.ReadGameDate();
    Console.WriteLine($"build {session.Profile.Build}{(session.Profile.ReadOnly ? " (limited writes)" : "")}: {players.Count} players in {sw.ElapsedMilliseconds} ms, game date {today?.ToString("yyyy-MM-dd") ?? "unknown"}");
    var shown = filter is null ? players.Take(20) : players.Where(p => Matches(p, filter));
    foreach (var p in shown)
        Console.WriteLine($"{p.DisplayName,-30} age {(today is { } t ? p.AgeOn(t) : null),-3} {p.Club?.DisplayName,-22} [{p.NationalityName,-12} | {p.DivisionName,-18}] £{p.Contract?.Wage,-7} to {p.Contract?.Expires} joined {p.Contract?.Joined}  " +
                          $"CA {p.CurrentAbility,3} PA {p.PotentialAbility,3}  {p.Height} cm  {p.DateOfBirth}  " +
                          $"Cro {p.Attribute(PlayerAttribute.Crossing)} Dri {p.Attribute(PlayerAttribute.Dribbling)} Pac {p.Attribute(PlayerAttribute.Pace)}  0x{p.Address:X}");
    return 0;
}

static bool Matches(PlayerRecord p, string filter) =>
    new[] { p.DisplayName, p.FullName }.Any(n => n?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);

int SetAttributeById(int id, PlayerAttribute attribute, int value)
{
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.Id == id).ToList();
    if (matches.Count != 1) throw new ArgumentOutOfRangeException(nameof(id), id, $"{matches.Count} players with ID {id}");
    var player = matches[0];
    int before = player.Attribute(attribute);
    session.Writer.SetAttribute(player, attribute, value);
    var after = session.Players.Reload(player.Address) ?? throw new StaleObjectException("Player no longer exists");
    Console.WriteLine($"{id} {player.DisplayName}: {attribute} {before} -> {after.Attribute(attribute)}");
    return 0;
}

int ContractById(int id)
{
    using var session = Attach(writable: false);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    var contract = player.Contract ?? throw new StaleObjectException("Player has no readable contract");
    Console.WriteLine($"{id} {player.DisplayName}: wage {contract.Wage}, expires {contract.Expires:yyyy-MM-dd}");
    return 0;
}

int SetWageById(int id, int value)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == id);
    int before = player.Contract?.Wage ?? throw new StaleObjectException("Player has no readable contract");
    session.Contracts.SetWage(player, value);
    var after = session.Players.Reload(player.Address)?.Contract ?? throw new StaleObjectException("Player contract no longer readable");
    Console.WriteLine($"{id} {player.DisplayName}: wage {before} -> {after.Wage}");
    return 0;
}

int Verify(string goldenPath)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    int failed = 0;
    foreach (var g in GoldenPlayers.Load(goldenPath))
    {
        var candidates = players.Where(p => p.DisplayName == g.Name && (g.DateOfBirth is null || p.DateOfBirth == g.DateOfBirth)).ToList();
        if (candidates.Count != 1)
        {
            Console.WriteLine($"FAIL {g.Name}: {candidates.Count} players with this name and birth date");
            failed++;
            continue;
        }
        var diffs = GoldenPlayers.Compare(g, candidates[0]);
        Console.WriteLine(diffs.Count == 0
            ? $"ok   {g.Name} ({g.Attributes.Count} attributes, CA {candidates[0].CurrentAbility} PA {candidates[0].PotentialAbility})"
            : $"FAIL {g.Name}: {string.Join("; ", diffs)}");
        if (diffs.Count > 0) failed++;
    }
    Console.WriteLine(failed == 0 ? "all golden players match" : $"{failed} golden players differ");
    return failed == 0 ? 0 : 3;
}

// release <player id> [--write]   release preview; --write applies it
int ReleasePlayer(int id, bool write)
{
    using var session = Attach(writable: write);
    var players = session.Players.LoadAll().Where(p => p.Id == id).ToArray();
    if (players.Length != 1) { Console.Error.WriteLine($"{players.Length} players with id {id}"); return 2; }
    var service = new Fm27Editor.Core.Transfer.ReleaseService(session.Memory, session.Profile, session.ModuleBase,
        () => session.Staff.LoadAll());
    var plan = service.Plan(players[0]);
    Console.WriteLine($"{id} {players[0].DisplayName}: {players[0].ClubName} -> free agent, {plan.Squads} squads, {plan.Captaincies} captaincies, {plan.Writes.Count} writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    service.Apply(plan);
    var after = session.Players.Reload(players[0].Address) ?? throw new StaleObjectException("Player no longer readable");
    Console.WriteLine($"applied; club {after.ClubName}, contract {(after.Contract is null ? "none" : "present")}");
    return 0;
}

// releaseinspect <player id>   read-only examination of the player's team and captaincy links
int ReleaseInspect(int id)
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll().Where(p => p.Id == id).ToArray();
    if (players.Length != 1) { Console.Error.WriteLine($"{players.Length} players with id {id}"); return 2; }
    var inspection = new Fm27Editor.Core.Transfer.ReleaseInspector(session.Memory, session.Profile, session.ModuleBase).Inspect(players[0]);
    Console.WriteLine($"{id} {players[0].DisplayName}: {players[0].ClubName}; contract 0x{inspection.Contract:X}; playing team 0x{inspection.PlayingTeam:X}");
    foreach (var team in inspection.Teams)
        Console.WriteLine($"  team 0x{team.Address:X}: squad {team.SquadCount}, player {team.PlayerOccurrences}, captain {team.Captain}, vice {team.ViceCaptain}");
    Console.WriteLine("read only; managed-club and other release conditions not checked");
    return 0;
}

// move <name> <buyer club id> <fee> [--write]   permanent transfer; without --write only prints the plan
int Move(string name, int buyerId, int fee, bool write)
{
    using var session = Attach(writable: write);
    var all = session.Players.LoadAll();
    var matches = all.Where(p => int.TryParse(name, out int id) ? p.Id == id : p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var sw = Stopwatch.StartNew();
    var clubs = session.LoadClubs(all);
    Console.WriteLine($"{clubs.Count} clubs, {clubs.Sum(c => c.Teams.Count)} teams in {sw.ElapsedMilliseconds} ms");
    var buyer = clubs.SingleOrDefault(c => c.Id == buyerId);
    if (buyer is null) { Console.Error.WriteLine($"no club with id {buyerId}"); return 2; }
    var plan = session.Transfers.Plan(matches[0], clubs, buyer, fee);
    Console.WriteLine($"{matches[0].DisplayName}: {plan.Seller.DisplayName} -> {plan.Buyer.DisplayName} for {fee}, wage {plan.Wage}");
    Console.WriteLine($"into team 0x{plan.ToTeam.Address:X} ({plan.ToTeam.SquadCount}/{plan.ToTeam.SquadCapacity}{(plan.ToTeam.IsFirstTeam ? ", first team" : "")})"
                      + (plan.NeedsAllocation ? ", squad full: a new buffer will be allocated" : ""));
    foreach (var w in plan.Writes.Where(w => w.Value.Length == 4))
        Console.WriteLine($"  {w.What}: {BitConverter.ToInt32(w.Expected)} -> {BitConverter.ToInt32(w.Value)}");
    Console.WriteLine($"  {plan.Writes.Count(w => w.Value.Length == 8)} pointer writes");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.ApplyTransfer(plan);
    var after = session.Players.Reload(matches[0].Address)!;
    Console.WriteLine($"applied; contract club now {after.Club?.DisplayName}");
    return 0;
}

// clauses <club id>   sell-on / appearance fee / competition bonus clauses on the club's players
int Clauses(int clubId)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var byId = all.Where(p => p.Id is not null).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    var clauses = session.ReadTransferClauses(club.Address, session.LoadCompetitions(all));
    Console.WriteLine($"{club.DisplayName}: {clauses.Count} clauses");
    foreach (var c in clauses)
        Console.WriteLine($"  {c.Kind,-16} {(byId.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString()),-24} {c.Beneficiary?.DisplayName,-24} " +
                          $"fee {c.Amount?.ToString() ?? "-"} pct {c.Percentage?.ToString() ?? "-"} signed for {c.SignedFor?.ToString() ?? "-"} apps {c.AppearancesSoFar}/{c.Appearances} competition {c.Competition?.DisplayName ?? c.CompetitionIndex?.ToString()} ends {c.Ends:yyyy-MM-dd}");
    return 0;
}

// clubfinancelists <id>   Finances sponsors and debts read from the live game
int ClubFinanceListsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var sponsors = session.ReadSponsors(club.Address);
    Console.WriteLine($"{club.Name}: {sponsors.Count} sponsors");
    foreach (var s in sponsors) Console.WriteLine($"  type {s.IncomeTypeCode,3} {s.TotalValue,12:N0} {s.StartDate:yyyy-MM-dd} {s.EndDate:yyyy-MM-dd}");
    var debts = session.ReadDebts(club.Address);
    Console.WriteLine($"{debts.Count} debts");
    foreach (var d in debts) Console.WriteLine($"  source {d.SourceCode,3} {d.OriginalDebt,12:N0} {d.StartDate:yyyy-MM-dd} {d.EndDate:yyyy-MM-dd}");
    return 0;
}

// setclub <id> <ClubField> <value>   guarded write of one mapped club field (team fields go to the first team)
int SetClubField(int clubId, string fieldName, long value)
{
    var field = Enum.Parse<Fm27Editor.Core.Clubs.ClubField>(fieldName, ignoreCase: true);
    using var session = Attach(writable: true);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var access = session.ClubFields;
    var root = session.Profile.Club!.Fields![field].Root == Fm27Editor.Core.Clubs.ClubFieldRoot.Club ? club.Address : club.FirstTeam?.Address ?? 0;
    var before = access.Read(root, field) ?? throw new InvalidOperationException($"{field} is not readable for {club.Name}");
    access.Set(root, field, before, value);
    Console.WriteLine($"{club.Name} {field}: {before} -> {access.Read(root, field)}");
    return 0;
}

// byid <id>...   name, club and jadedness/condition of players by unique ID
// peek <player|contract> <hexoffset> <size> [ids...]   value histogram over every player, plus the listed players (read only)
int Peek(string root, int offset, int size, int[] ids)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var counts = new Dictionary<long, int>();
    var buf = new byte[8];
    foreach (var p in session.Players.LoadAll())
    {
        ulong at = p.Address;
        if (root == "contract")
        {
            if (!mem.TryRead(p.Address + 0x330, buf) || BitConverter.ToUInt64(buf) is var c && c == 0) continue;
            at = c;
        }
        if (!mem.TryRead(at + (ulong)offset, buf.AsSpan(0, size))) continue;
        long v = size switch { 1 => (sbyte)buf[0], 2 => BitConverter.ToInt16(buf), 4 => BitConverter.ToInt32(buf), _ => BitConverter.ToInt64(buf) };
        counts[v] = counts.GetValueOrDefault(v) + 1;
        if (p.Id is { } id && ids.Contains(id)) Console.WriteLine($"  {id,-11} {p.DisplayName,-26} {p.Club?.DisplayName,-24} {v}");
    }
    Console.WriteLine($"{root}+0x{offset:X} ({size}): " + string.Join("  ", counts.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key}×{k.Value}")));
    return 0;
}

// unh <club name>   unhappiness motives of each first-team player (read only); unh all = how many players have active ones
int UnhCommand(string clubName)
{
    using var session = Attach(writable: false);
    var today = session.ReadGameDate() ?? throw new InvalidOperationException("The in-game date could not be read");
    Console.WriteLine($"game date {today:yyyy-MM-dd}");
    var players = session.Players.LoadAll();
    if (clubName == "all")
    {
        int withList = 0, active = 0;
        foreach (var pl in players)
        {
            var m = session.ReadUnhappiness(pl);
            if (m.Count > 0) withList++;
            if (m.Any(x => x.IsActive(today))) active++;
        }
        Console.WriteLine($"{withList} players with motives, {active} with active ones");
        return 0;
    }
    var club = session.LoadClubs(players).FirstOrDefault(c => c.Name.Equals(clubName, StringComparison.OrdinalIgnoreCase));
    if (club?.FirstTeam is not { } team) { Console.Error.WriteLine($"no club or first team named {clubName}"); return 2; }
    foreach (var p in session.ReadSquad(team.Address))
        foreach (var m in session.ReadUnhappiness(p))
            Console.WriteLine($"  {p.Id,-11} {p.DisplayName,-24} motive {m.Motive} type {m.Type} from {m.Start:yyyy-MM-dd} to {m.End:yyyy-MM-dd} items {m.Item1}/{m.Item2} {(m.IsActive(today) ? "ACTIVE" : "ended")}");
    return 0;
}

// removeunh <name>   remove the active unhappiness motives (writes to the game)
int RemoveUnhCommand(string name)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    var today = session.ReadGameDate()!.Value;
    void Show(string when) => Console.WriteLine($"{when}: " + string.Join("; ", session.ReadUnhappiness(p).Select(m => $"motive {m.Motive} end {m.End:yyyy-MM-dd} {(m.IsActive(today) ? "ACTIVE" : "ended")}")));
    Show("before");
    Console.WriteLine($"removed {session.RemoveUnhappiness(p)}");
    Show("after ");
    return 0;
}

// match <player|contract> <size> <id>=<value>...   offsets where every listed player holds the given value (read only);
//   size 0 = game date (int16 day of year in the low 9 bits + int16 year), value yyyy-MM-dd
int MatchCommand(string root, int size, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var want = pairs.Select(s => s.Split('=')).ToDictionary(a => int.Parse(a[0]),
        a => size == 0 && DateOnly.Parse(a[1]) is var d ? d.Year * 1000L + d.DayOfYear : long.Parse(a[1]));
    if (size == 0) size = -4;
    int span = root == "contract" ? 0x100 : 0x3C8;
    var buf = new byte[8];
    var blocks = new Dictionary<int, byte[]>();
    foreach (var p in session.Players.LoadAll().Where(p => p.Id is { } id && want.ContainsKey(id)))
    {
        ulong at = p.Address;
        if (root == "contract" && (!mem.TryRead(p.Address + 0x330, buf) || (at = BitConverter.ToUInt64(buf)) == 0)) continue;
        var b = new byte[span];
        if (mem.TryRead(at, b)) blocks[p.Id!.Value] = b;
    }
    Console.WriteLine($"{blocks.Count}/{want.Count} players read");
    for (int o = 0; o + Math.Abs(size) <= span; o++)
        if (blocks.All(kv => (size switch
            {
                1 => (sbyte)kv.Value[o], 2 => BitConverter.ToInt16(kv.Value, o),
                -4 => BitConverter.ToInt16(kv.Value, o + 2) * 1000L + (BitConverter.ToUInt16(kv.Value, o) & 0x1FF),
                _ => BitConverter.ToInt32(kv.Value, o)
            }) == want[kv.Key]))
            Console.WriteLine($"  {root}+0x{o:X}");
    return 0;
}

// datediff <player|contract> <offset> <player|contract> <offset>   first players whose two dates differ (read only)
int DateDiffCommand(string rootA, int offA, string rootB, int offB)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var buf = new byte[8];
    DateOnly? At(PlayerRecord p, string root, int off)
    {
        ulong at = p.Address;
        if (root == "contract" && (!mem.TryRead(p.Address + 0x330, buf) || (at = BitConverter.ToUInt64(buf)) == 0)) return null;
        if (!mem.TryRead(at + (ulong)off, buf.AsSpan(0, 4))) return null;
        int day = BitConverter.ToUInt16(buf) & 0x1FF, year = BitConverter.ToInt16(buf, 2);
        return year > 1900 && year < 2300 && day is >= 1 and <= 365 ? new DateOnly(year, 1, 1).AddDays(day - 1) : null;
    }
    int shown = 0, differ = 0, total = 0;
    foreach (var p in session.Players.LoadAll().Where(p => p.Club?.DisplayName is "Man Utd" or "Arsenal" or "Chelsea" or "Liverpool" or "Real Madrid"))
    {
        var (a, b) = (At(p, rootA, offA), At(p, rootB, offB));
        total++;
        if (a == b) continue;
        differ++;
        if (shown++ < 12) Console.WriteLine($"  {p.Id,-11} {p.DisplayName,-24} {p.Club?.DisplayName,-14} {rootA}+0x{offA:X} {a:yyyy-MM-dd}  {rootB}+0x{offB:X} {b:yyyy-MM-dd}");
    }
    Console.WriteLine($"{differ}/{total} differ");
    return 0;
}

// pokedate <name> <player|contract> <offset> <yyyy-MM-dd>   RE probe: write one date, keeping the flag bits (writes to the game)
int PokeDateCommand(string name, string root, int off, DateOnly date)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    var mem = session.Memory;
    if (!session.Profile.PlayerVtableRvas.Any(r => mem.ReadUInt64(p.Address) == session.Game.ModuleBase + r)) throw new InvalidOperationException("not a player object");
    ulong at = root == "contract" ? mem.ReadUInt64(p.Address + 0x330) : p.Address;
    if (root == "contract" && mem.ReadUInt64(at) != session.Game.ModuleBase + session.Profile.Contract!.ContractVtableRva) throw new InvalidOperationException("not a full contract");
    ulong addr = at + (ulong)off;
    var old = mem.Read(addr, 4);
    short raw = BitConverter.ToInt16(old);
    byte[] next = [.. BitConverter.GetBytes((short)((raw & ~0x1FF) | date.DayOfYear)), .. BitConverter.GetBytes((short)date.Year)];
    if (!mem.TryWrite(addr, next) || !mem.Read(addr, 4).AsSpan().SequenceEqual(next)) throw new InvalidOperationException("write failed");
    Console.WriteLine($"{p.DisplayName} {root}+0x{off:X}: {Convert.ToHexString(old)} -> {Convert.ToHexString(next)}");
    return 0;
}

// findperson <ca> <pa> [<ca> <pa>...]   RE: heap hits of adjacent int16 CA/PA, with the (distance, vtable) of module
//   pointers before each hit; prints the (distance, vtable) pairs every person shares (read only)
int FindPersonCommand(int[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    var sets = new List<HashSet<(int, ulong)>>();
    var hitsPer = new List<List<ulong>>();
    for (int i = 0; i < pairs.Length; i += 2) { sets.Add([]); hitsPer.Add([]); }
    var sw = Stopwatch.StartNew();
    var chunk = new byte[1 << 20];
    foreach (var r in ((WindowsProcessMemory)mem).EnumerateWritableRegions(mb, me).Where(r => !r.IsModuleData))
        for (ulong off = 0; off < r.Size; off += (ulong)chunk.Length)
        {
            int len = (int)Math.Min((ulong)chunk.Length, r.Size - off);
            var span = chunk.AsSpan(0, len);
            if (!mem.TryRead(r.Start + off, span)) continue;
            for (int k = 0; k + 4 <= len; k += 2)
            {
                short ca = BitConverter.ToInt16(chunk, k), pa = BitConverter.ToInt16(chunk, k + 2);
                for (int j = 0; j < pairs.Length; j += 2)
                {
                    if (ca != pairs[j] || pa != pairs[j + 1]) continue;
                    ulong hit = r.Start + off + (ulong)k;
                    hitsPer[j / 2].Add(hit);
                    var before = new byte[0x800];
                    if (!mem.TryRead(hit - 0x800, before)) continue;
                    for (int d = 8; d <= 0x800; d += 8)
                    {
                        ulong q = BitConverter.ToUInt64(before, 0x800 - d);
                        if (q >= mb && q < me && (q - mb) > 0x4000000 && (q - mb) < 0x5100000) sets[j / 2].Add((d, q - mb));
                    }
                }
            }
        }
    for (int j = 0; j < sets.Count; j++) Console.WriteLine($"CA {pairs[j * 2]} PA {pairs[j * 2 + 1]}: {hitsPer[j].Count} hits");
    foreach (var hit in hitsPer[0].Take(8))
    {
        var before = new byte[0x800];
        if (!mem.TryRead(hit - 0x800, before)) continue;
        var ptrs = Enumerable.Range(1, 0x100).Select(i => (d: i * 8, q: BitConverter.ToUInt64(before, 0x800 - i * 8)))
            .Where(x => x.q >= mb && x.q < me).Take(4).Select(x => $"-0x{x.d:X}:+0x{x.q - mb:X}");
        Console.WriteLine($"  hit 0x{hit:X}  {string.Join("  ", ptrs)}");
    }
    var common = sets.Skip(1).Aggregate(new HashSet<(int, ulong)>(sets[0]), (a, s) => { a.IntersectWith(s); return a; });
    foreach (var (d, vt) in common.OrderBy(x => x.Item1).Take(30)) Console.WriteLine($"  object = hit - 0x{d:X}, vtable +0x{vt:X}");
    Console.WriteLine($"{sw.Elapsed.TotalSeconds:F1} s");
    return 0;
}

// hex <addr> <len>   raw dump, 16 bytes per row, module pointers marked (read only)
int HexCommand(ulong addr, int len)
{
    using var session = Attach(writable: false);
    var b = session.Memory.Read(addr, len);
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    for (int i = 0; i < len; i += 16)
    {
        var row = b[i..Math.Min(len, i + 16)];
        string marks = string.Join(" ", Enumerable.Range(0, row.Length / 8).Select(k => BitConverter.ToUInt64(row, k * 8))
            .Select(q => q >= mb && q < me ? $"[+0x{q - mb:X}]" : q is > 0x10000000000 and < 0x7FF000000000 ? "[ptr]" : ""));
        Console.WriteLine($"{addr + (ulong)i:X}  {Convert.ToHexString(row)}  {marks}");
    }
    return 0;
}

// findlist <vtable rva>   RE: module statics (or objects they point to) holding a begin/end vector of pointers whose
//   elements carry the vtable (read only)
int FindListCommand(ulong vtRva)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd, vt = mb + vtRva;
    var q8 = new byte[8];
    bool IsTarget(ulong p) => p != 0 && mem.TryRead(p, q8) && BitConverter.ToUInt64(q8) == vt;
    int Check(byte[] blk, int o)
    {
        ulong begin = BitConverter.ToUInt64(blk, o), end = BitConverter.ToUInt64(blk, o + 8);
        if (begin < 0x10000000000 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 200000) return 0;
        var first = new byte[64];
        if (!mem.TryRead(begin, first)) return 0;
        int ok = Enumerable.Range(0, 8).Count(i => IsTarget(BitConverter.ToUInt64(first, i * 8)));
        return ok >= 6 ? (int)((end - begin) / 8) : 0;
    }
    foreach (var r in ((WindowsProcessMemory)mem).EnumerateWritableRegions(mb, me).Where(r => r.IsModuleData))
    {
        var data = new byte[r.Size];
        if (!mem.TryRead(r.Start, data)) continue;
        for (int o = 0; o + 16 <= data.Length; o += 8)
        {
            if (Check(data, o) is var n && n > 0) Console.WriteLine($"  direct: module+0x{r.Start + (ulong)o - mb:X} begin/end, {n} entries");
            ulong p = BitConverter.ToUInt64(data, o);
            if (p < 0x10000000000 || p > 0x7FF000000000) continue;
            var blk = new byte[Environment.GetEnvironmentVariable("FINDLIST_SPAN") is { } sp ? Convert.ToInt32(sp, 16) : 0x100];
            if (!mem.TryRead(p, blk)) continue;
            for (int k = 0; k + 16 <= blk.Length; k += 8)
                if (Check(blk, k) is var m && m > 0) Console.WriteLine($"  [module+0x{r.Start + (ulong)o - mb:X}] + 0x{k:X} begin/end, {m} entries");
        }
    }
    return 0;
}

// vthist <root rva> <offset>   RE: vtable histogram of the pointer vector at [module + root] + offset (read only)
int VtHistCommand(ulong rootRva, int off)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    ulong obj = mem.ReadUInt64(mb + rootRva);
    ulong begin = mem.ReadUInt64(obj + (ulong)off), end = mem.ReadUInt64(obj + (ulong)off + 8);
    var ptrs = mem.Read(begin, (int)(end - begin));
    var hist = new Dictionary<ulong, int>();
    var q = new byte[8];
    for (int i = 0; i < ptrs.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(ptrs, i);
        ulong vt = p != 0 && mem.TryRead(p, q) ? BitConverter.ToUInt64(q) - mb : 0;
        hist[vt] = hist.GetValueOrDefault(vt) + 1;
    }
    Console.WriteLine($"object 0x{obj:X}, {ptrs.Length / 8} entries");
    foreach (var (vt, n) in hist.OrderByDescending(k => k.Value).Take(10)) Console.WriteLine($"  vtable +0x{vt:X}: {n}");
    return 0;
}

// ptrvt <addr> <len>   RE: for each qword pointer in [addr, addr+len), the module vtable of the object it points to (read only)
int PtrVtCommand(ulong addr, int len)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    var b = mem.Read(addr, len);
    var q = new byte[8];
    for (int o = 0; o + 8 <= len; o += 8)
    {
        ulong p = BitConverter.ToUInt64(b, o);
        if (p < 0x10000000000 || p > 0x7FF000000000 || !mem.TryRead(p, q)) continue;
        ulong vt = BitConverter.ToUInt64(q);
        Console.WriteLine($"  +0x{o:X3} -> 0x{p:X}  {(vt >= mb && vt < me ? $"vtable +0x{vt - mb:X}" : "")}");
    }
    return 0;
}

// staff <club name|staff name>   staff of a club (or one named person with every attribute) (read only)
int StaffCommand(string name)
{
    using var session = Attach(writable: false);
    var sw = Stopwatch.StartNew();
    var all = session.Staff.LoadAll();
    Console.WriteLine($"{all.Count} staff in {sw.ElapsedMilliseconds} ms");
    var atClub = all.Where(s => s.Club?.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) == true || s.Club?.Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true).ToList();
    bool raw = Environment.GetEnvironmentVariable("STAFF_JOBRAW") == "1";
    foreach (var s in atClub)
    {
        string jr = "";
        if (raw && session.Memory.ReadUInt64(s.Address + 0xA8) is var job && job != 0) jr = Convert.ToHexString(session.Memory.Read(job + 0x18, 0x10));
        Console.WriteLine($"  {s.Id,-9} job {s.JobCode,3}  {s.DisplayName,-26} {s.DateOfBirth:yyyy-MM-dd} {s.Nationality?.DisplayName,-16} CA {s.CurrentAbility} PA {s.PotentialAbility} {jr}");
    }
    if (atClub.Count > 0) { Console.WriteLine($"{atClub.Count} at {name}"); return 0; }
    if (name == "humans")
        foreach (var h in all.Where(s => s.Kind.Manager))
            Console.WriteLine($"  0x{h.Address:X} id {h.Id} '{h.FirstName}' '{h.LastName}' '{h.FullName}' club {h.Club?.DisplayName} job {h.JobCode} CA {h.CurrentAbility} PA {h.PotentialAbility} manager [{string.Join(",", h.ManagerAttributes!)}]");
    foreach (var s in all.Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) || s.FullName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true))
    {
        Console.WriteLine($"{s.DisplayName} ({s.FullName}) id {s.Id} born {s.DateOfBirth:yyyy-MM-dd} {s.Nationality?.DisplayName} {s.Club?.DisplayName} job {s.JobCode}");
        Console.WriteLine($"  person 0x{s.Address:X} staff 0x{s.StaffAddress:X}");
        Console.WriteLine($"  rep {s.HomeReputation}/{s.CurrentReputation}/{s.WorldReputation} CA {s.CurrentAbility} PA {s.PotentialAbility}");
        Console.WriteLine("  " + string.Join("  ", Enum.GetValues<Fm27Editor.Core.Staff.StaffAttribute>().Select(a => $"{a} {s.Attribute(a)}")));
        Console.WriteLine("  " + string.Join("  ", Enum.GetValues<Fm27Editor.Core.Staff.StaffPersonality>().Select(t => $"{t} {s.Personality[(int)t]}")));
    }
    return 0;
}

// setstaff <full or display name> <StaffAttribute|CA|PA> <value>   guarded staff write (writes to the game)
int SetStaffCommand(string name, string field, int value)
{
    using var session = Attach(writable: true);
    var matches = session.Staff.LoadAll().Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} staff named '{name}'"); return 2; }
    var st = matches[0];
    var w = session.StaffWriter;
    if (field.Equals("CA", StringComparison.OrdinalIgnoreCase)) w.SetAbility(st, value, st.PotentialAbility);
    else if (field.Equals("PA", StringComparison.OrdinalIgnoreCase)) w.SetAbility(st, st.CurrentAbility, value);
    else if (Enum.TryParse<Fm27Editor.Core.Staff.ManagerAttribute>(field, true, out var ma))
    {
        w.SetManagerAttribute(st, ma, value);
        Console.WriteLine($"{st.DisplayName} {ma}: {st.ManagerAttributes![(int)ma]} -> {session.Staff.Reload(st.Address)!.ManagerAttributes![(int)ma]}");
        return 0;
    }
    else w.SetAttribute(st, Enum.Parse<Fm27Editor.Core.Staff.StaffAttribute>(field, true), value);
    var after = session.Staff.Reload(st.Address)!;
    Console.WriteLine($"{st.DisplayName}: CA {st.CurrentAbility}->{after.CurrentAbility} PA {st.PotentialAbility}->{after.PotentialAbility}"
        + (Enum.TryParse<Fm27Editor.Core.Staff.StaffAttribute>(field, true, out var a) ? $" {a} {st.Attribute(a)}->{after.Attribute(a)}" : ""));
    return 0;
}

// nation <name> [<NationField> <value>]   every mapped field of one nation; with a field, a guarded write (writes to the game)
int NationCommand(string name, string? field, long? value)
{
    using var session = Attach(writable: field is not null);
    var all = session.Nations.LoadAll();
    var n = all.FirstOrDefault(x => x.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (n is null) { Console.Error.WriteLine($"no nation '{name}' ({all.Count} nations)"); return 2; }
    var acc = session.NationFields;
    if (field is not null)
    {
        var f = Enum.Parse<Fm27Editor.Core.Nations.NationField>(field, true);
        long before = acc.Read(n.Address, f) ?? throw new InvalidOperationException($"{f} is not readable");
        acc.Set(n.Address, f, before, value!.Value);
        Console.WriteLine($"{n.DisplayName} {f}: {before} -> {acc.Read(n.Address, f)}");
        return 0;
    }
    Console.WriteLine($"{n.DisplayName} id {n.Id} 0x{n.Address:X} ({all.Count} nations)");
    foreach (var (f, v) in acc.ReadAll(n.Address)) Console.WriteLine($"  {f,-28} {v}");
    return 0;
}

// stadium <name> [<StadiumField> <value>]   one stadium with every mapped value; with a field, a guarded write (writes to the game)
int StadiumCommand(string name, string? field, long? value)
{
    using var session = Attach(writable: field is not null);
    var sw = Stopwatch.StartNew();
    var all = session.Stadiums.LoadAll();
    var st = all.Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    Console.WriteLine($"{all.Count} stadiums in {sw.ElapsedMilliseconds} ms, {st.Count} named '{name}'");
    var w = session.StadiumWriter;
    foreach (var s in st)
    {
        if (field is not null)
        {
            var f = Enum.Parse<Fm27Editor.Core.Stadiums.StadiumField>(field, true);
            long before = w.ReadFields(s.Address)[f];
            w.SetField(s.Address, f, before, value!.Value);
            Console.WriteLine($"{s.DisplayName} {f}: {before} -> {w.ReadFields(s.Address)[f]}");
            continue;
        }
        Console.WriteLine($"{s.DisplayName} id {s.Id} owner {s.Owner?.DisplayName} nearby {s.NearbyStadium} lat {s.Latitude} lon {s.Longitude} flags 0x{s.Flags:X2} addr 0x{s.Address:X}");
        foreach (var (f, v) in w.ReadFields(s.Address)) Console.WriteLine($"  {f,-24} {v}");
        foreach (var t in s.Stands) Console.WriteLine($"  stand {t.Index}: {t.Capacity,6} roof {t.HasRoof} seats {t.HasSeats} tunnel {t.HasTunnel}");
        Console.WriteLine($"  stands total {s.StandsTotal}");
    }
    return 0;
}

// poke <addr> <old hex> <new hex>   RE probe: write bytes only when memory still holds the old bytes (writes to the game)
int PokeCommand(ulong addr, string oldHex, string newHex)
{
    using var session = Attach(writable: true);
    byte[] old = Convert.FromHexString(oldHex), now = Convert.FromHexString(newHex);
    if (old.Length != now.Length) throw new ArgumentException("old and new must have the same length");
    if (!session.Memory.Read(addr, old.Length).AsSpan().SequenceEqual(old)) throw new InvalidOperationException($"0x{addr:X} no longer holds {oldHex}");
    if (!session.Memory.TryWrite(addr, now) || !session.Memory.Read(addr, now.Length).AsSpan().SequenceEqual(now)) throw new InvalidOperationException("write failed");
    Console.WriteLine($"0x{addr:X}: {oldHex} -> {newHex}");
    return 0;
}

// rootvecs <root rva>   RE: every begin/end pointer vector in the first 0x800 bytes of [module + rva], with the first element's vtable (read only)
int RootVecsCommand(ulong rva)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    ulong obj = mem.ReadUInt64(mb + rva);
    var b = mem.Read(obj, 0x800);
    for (int o = 0; o + 16 <= b.Length; o += 8)
    {
        ulong begin = BitConverter.ToUInt64(b, o), end = BitConverter.ToUInt64(b, o + 8);
        if (begin < 0x10000000000 || end <= begin || (end - begin) % 8 != 0 || (end - begin) / 8 > 2_000_000) continue;
        if (!mem.TryRead(begin, new byte[8]) || mem.ReadUInt64(begin) is var first && first == 0 || !mem.TryRead(first, new byte[8])) continue;
        ulong vt = mem.ReadUInt64(first);
        if (vt < mb || vt >= session.Game.ModuleEnd) continue;
        Console.WriteLine($"  +0x{o:X3}: {(end - begin) / 8,8} entries, first vtable +0x{vt - mb:X}");
    }
    return 0;
}

// vecbounds <addr inside a pointer vector> <vtable rva>   RE: walk both ways while entries carry the vtable (read only)
int VecBoundsCommand(ulong inside, ulong vtRva)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong vt = session.Game.ModuleBase + vtRva;
    bool Ok(ulong slot) => mem.TryRead(slot, new byte[8]) && mem.ReadUInt64(slot) is var p && p != 0 && mem.TryRead(p, new byte[8]) && mem.ReadUInt64(p) == vt;
    ulong lo = inside, hi = inside;
    while (Ok(lo - 8)) lo -= 8;
    while (Ok(hi + 8)) hi += 8;
    Console.WriteLine($"begin 0x{lo:X} end 0x{hi + 8:X}: {(hi + 8 - lo) / 8} entries");
    return 0;
}

// near <a> <b>   RE: heap int32 a with int32 b within ±0x40, plus the closest module pointer (vtable) below (read only)
int NearCommand(int a, int b)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, me = session.Game.ModuleEnd;
    var chunk = new byte[1 << 20];
    int hits = 0;
    foreach (var r in ((WindowsProcessMemory)mem).EnumerateWritableRegions(mb, me).Where(r => !r.IsModuleData))
        for (ulong off = 0; off < r.Size; off += (ulong)chunk.Length)
        {
            int len = (int)Math.Min((ulong)chunk.Length, r.Size - off);
            if (!mem.TryRead(r.Start + off, chunk.AsSpan(0, len))) continue;
            for (int k = 0; k + 4 <= len; k += 4)
            {
                if (BitConverter.ToInt32(chunk, k) != a) continue;
                int lo = Math.Max(0, k - 0x40), hi = Math.Min(len - 4, k + 0x40);
                int j = -1;
                for (int t = lo; t <= hi; t += 4) if (BitConverter.ToInt32(chunk, t) == b) { j = t; break; }
                if (j < 0) continue;
                ulong hit = r.Start + off + (ulong)k;
                var before = new byte[0x400];
                string vt = "";
                if (mem.TryRead(hit - 0x400, before))
                    for (int d = 8; d <= 0x400; d += 8)
                        if (BitConverter.ToUInt64(before, 0x400 - d) is var q && q >= mb && q < me) { vt = $"vtable +0x{q - mb:X} at -0x{d:X}"; break; }
                Console.WriteLine($"  0x{hit:X} ({b} at {(j - k):+0;-0}) {vt}");
                if (++hits > 30) return 0;
            }
        }
    return 0;
}

// clubprobe <clubA> <clubB> <size> <a>:<b>...   RE: like nationprobe, starting from two club objects (read only)
int ClubProbeCommand(string nameA, string nameB, int size, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var a = clubs.FirstOrDefault(c => c.DisplayName == nameA); var b = clubs.FirstOrDefault(c => c.DisplayName == nameB);
    if (a is null || b is null) { Console.Error.WriteLine("club not found"); return 2; }
    Console.WriteLine($"{nameA} 0x{a.Address:X}  {nameB} 0x{b.Address:X}");
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: double.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), B: double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
    double V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), -4 => BitConverter.ToSingle(d, o), -8 => BitConverter.ToDouble(d, o), _ => 0 };
    void Scan(string where, byte[] da, byte[] db)
    {
        for (int o = 0; o + Math.Abs(size) <= Math.Min(da.Length, db.Length); o++)
            foreach (var (x, y) in want)
                if (Math.Abs(V(da, o) - x) < 0.0001 * Math.Max(1, Math.Abs(x)) && Math.Abs(V(db, o) - y) < 0.0001 * Math.Max(1, Math.Abs(y))) Console.WriteLine($"  {where}+0x{o:X}: {x} / {y}");
    }
    var oa = mem.Read(a.Address, 0x400); var ob = mem.Read(b.Address, 0x400);
    for (int k = 0; k + 8 <= 0x400; k += 8)
    {
        ulong pa = BitConverter.ToUInt64(oa, k), pb = BitConverter.ToUInt64(ob, k);
        if (pa < 0x10000000000 || pb < 0x10000000000 || pa > 0x7FF000000000 || pb > 0x7FF000000000 || pa == pb) continue;
        var da = new byte[0x400]; var db = new byte[0x400];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        Scan($"[club+0x{k:X}] (vt +0x{BitConverter.ToUInt64(da) - session.Game.ModuleBase:X})", da, db);
    }
    return 0;
}

// nationbytes <offset hex> <len> [names...]   RE: bytes at [nation+0x108]+offset for every nation (or the named ones) (read only)
int NationBytesCommand(int offset, int len, string[] names)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var hist = new Dictionary<string, int>();
    foreach (var n in session.Nations.LoadAll())
    {
        ulong team = mem.ReadUInt64(n.Address + 0x108);
        if (team == 0 || !mem.TryRead(team + (ulong)offset, new byte[len])) continue;
        string hex = Convert.ToHexString(mem.Read(team + (ulong)offset, len));
        hist[hex] = hist.GetValueOrDefault(hex) + 1;
        if (names.Length == 0 || names.Contains(n.DisplayName, StringComparer.OrdinalIgnoreCase)) if (names.Length > 0) Console.WriteLine($"  {n.DisplayName,-24} {hex}");
    }
    Console.WriteLine("histogram: " + string.Join("  ", hist.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key}×{k.Value}")));
    return 0;
}

// nationprobe <nameA> <nameB> <size> <a>:<b>...   RE: offsets where nation A holds a and nation B holds b, in the nation
//   object and one pointer hop away (same pointer offset in both) (read only)
int NationProbeCommand(string nameA, string nameB, int size, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Players.LoadAll().Select(p => p.Nationality).Where(n => n is not null).DistinctBy(n => n!.Address).ToList();
    var a = nations.FirstOrDefault(n => n!.DisplayName == nameA); var b = nations.FirstOrDefault(n => n!.DisplayName == nameB);
    if (a is null || b is null) { Console.Error.WriteLine("nation not found"); return 2; }
    Console.WriteLine($"{nameA} 0x{a.Address:X}  {nameB} 0x{b.Address:X}  ({nations.Count} nations)");
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: double.Parse(x[0], System.Globalization.CultureInfo.InvariantCulture), B: double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
    double V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), -4 => BitConverter.ToSingle(d, o), _ => 0 };
    void Scan(string where, byte[] da, byte[] db)
    {
        for (int o = 0; o + Math.Abs(size) <= Math.Min(da.Length, db.Length); o++)
            foreach (var (x, y) in want)
                if (Math.Abs(V(da, o) - x) < 0.01 && Math.Abs(V(db, o) - y) < 0.01) Console.WriteLine($"  {where}+0x{o:X}: {x} / {y}");
    }
    var oa = mem.Read(a.Address, 0x400); var ob = mem.Read(b.Address, 0x400);
    Scan("nation", oa, ob);
    for (int k = 0; k + 8 <= 0x400; k += 8)
    {
        ulong pa = BitConverter.ToUInt64(oa, k), pb = BitConverter.ToUInt64(ob, k);
        if (pa < 0x10000000000 || pb < 0x10000000000 || pa > 0x7FF000000000 || pb > 0x7FF000000000 || pa == pb) continue;
        var da = new byte[0x900]; var db = new byte[0x900];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        Scan($"[nation+0x{k:X}]", da, db);
        if (k != 0x108) continue;
        for (int j = 0; j + 8 <= da.Length; j += 8)
        {
            ulong qa = BitConverter.ToUInt64(da, j), qb = BitConverter.ToUInt64(db, j);
            if (qa < 0x10000000000 || qb < 0x10000000000 || qa > 0x7FF000000000 || qb > 0x7FF000000000 || qa == qb) continue;
            var ea = new byte[0x200]; var eb = new byte[0x200];
            if (mem.TryRead(qa, ea) && mem.TryRead(qb, eb)) Scan($"[[nation+0x108]+0x{j:X}]", ea, eb);
        }
    }
    return 0;
}

// staffprobe [club id]   RE prototype: walk the person vector, pick staff (part = person - 0x100, human + 0x140) (read only)
int StaffProbeCommand(int club)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    ulong obj = mem.ReadUInt64(mb + 0x5703738);
    ulong begin = mem.ReadUInt64(obj + 0x88), end = mem.ReadUInt64(obj + 0x90);
    var ptrs = mem.Read(begin, (int)(end - begin));
    var q = new byte[8];
    string? Str(ulong p) { if (p == 0 || !mem.TryRead(p, q.AsSpan(0, 4))) return null; int n = BitConverter.ToInt32(q); return n is > 0 and < 200 ? System.Text.Encoding.UTF8.GetString(mem.Read(p + 4, n)) : null; }
    string? Entry(ulong e) => e != 0 && mem.TryRead(e, q) ? Str(BitConverter.ToUInt64(q)) : null;
    int pooled = 0, human = 0, atClub = 0;
    var clubHist = new Dictionary<int, int>();
    for (int i = 0; i < ptrs.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(ptrs, i);
        if (p == 0 || !mem.TryRead(p, q)) continue;
        ulong vt = BitConverter.ToUInt64(q) - mb;
        ulong s;
        if (vt == 0x4D8BE78) { s = p - 0x100; pooled++; }
        else if (vt == 0x4CC08D8) { s = p + 0x140; human++; }
        else continue;
        var pb = mem.Read(p, 0x90);
        var sb = mem.Read(s, 0xE8);
        int cid = BitConverter.ToInt32(sb, 0xB8);
        clubHist[cid] = clubHist.GetValueOrDefault(cid) + 1;
        if (cid != club && vt != 0x4CC08D8) continue;
        atClub++;
        int day = BitConverter.ToUInt16(pb, 0x88) & 0x1FF, year = BitConverter.ToInt16(pb, 0x8A);
        if (vt == 0x4CC08D8)
        {
            var below = mem.Read(p - 0x100, 0xE0);
            Console.WriteLine($"  person 0x{p:X}: at -0x100 vt +0x{BitConverter.ToUInt64(below) - mb:X} rep {BitConverter.ToInt16(below, 0xD4)}/{BitConverter.ToInt16(below, 0xD6)}/{BitConverter.ToInt16(below, 0xD8)} CA {BitConverter.ToInt16(below, 0xDA)} PA {BitConverter.ToInt16(below, 0xDC)}; job 0x{BitConverter.ToUInt64(pb, 0x8):X}");
        }
        Console.WriteLine($"  {BitConverter.ToInt32(pb, 0xC),-10} {Entry(BitConverter.ToUInt64(pb, 0x50))} {Entry(BitConverter.ToUInt64(pb, 0x58)),-16} full '{Str(BitConverter.ToUInt64(pb, 0x40))}' born {year}/{day} rep {BitConverter.ToInt16(sb, 0xD4)}/{BitConverter.ToInt16(sb, 0xD6)}/{BitConverter.ToInt16(sb, 0xD8)} CA {BitConverter.ToInt16(sb, 0xDA)} PA {BitConverter.ToInt16(sb, 0xDC)} club {cid} vt +0x{vt:X} staffvt +0x{BitConverter.ToUInt64(sb) - mb:X}");
    }
    Console.WriteLine($"pooled {pooled}, human {human}, listed {atClub}; top clubs: " + string.Join("  ", clubHist.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key}×{k.Value}")));
    return 0;
}

int ById(int[] ids)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => p.Id is { } id && ids.Contains(id)))
    {
        var v = session.PlayerFields.ReadAll(p);
        Console.WriteLine($"{p.Id,-11} {p.DisplayName,-26} {p.Club?.DisplayName,-24} 0x{p.Address:X} jaded {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Jadedness),5} cond {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Condition),5} injuries {session.ReadInjuries(p).Count}");
    }
    return 0;
}

// statuses [name]   registrations, formed-at, likes/dislikes and bans; with no name, game-wide likes/dislikes histograms
int StatusesCommand(string name)
{
    using var session = Attach(writable: false);
    if (name.Length == 0)
    {
        var values = new SortedDictionary<int, int>(); var reasons = new SortedDictionary<string, int>(); var flags = new SortedDictionary<string, int>();
        foreach (var r in session.Players.LoadAll().SelectMany(p => session.ReadStatuses(p).Relationships))
        {
            values[r.Value] = values.GetValueOrDefault(r.Value) + 1;
            reasons[$"{r.Kind}:{r.Reason}"] = reasons.GetValueOrDefault($"{r.Kind}:{r.Reason}") + 1;
            flags[$"{r.Kind}:{r.Flags:X2}"] = flags.GetValueOrDefault($"{r.Kind}:{r.Flags:X2}") + 1;
        }
        Console.WriteLine("values: " + string.Join(" ", values.Select(v => $"{v.Key}×{v.Value}")));
        Console.WriteLine("reasons: " + string.Join(" ", reasons.Select(v => $"{v.Key}×{v.Value}")));
        Console.WriteLine("flags: " + string.Join(" ", flags.Select(v => $"{v.Key}×{v.Value}")));
        return 0;
    }
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
    {
        var s = session.ReadStatuses(p);
        Console.WriteLine($"{p.DisplayName} ({p.Club?.DisplayName}):");
        foreach (var r in s.Registrations) Console.WriteLine($"  registered {r.Club?.DisplayName} kind {r.Kind}");
        foreach (var c in s.TrainedAtClubs) Console.WriteLine($"  trained at club {c.Club?.DisplayName} period {c.Period}");
        foreach (var n in s.TrainedAtNations) Console.WriteLine($"  trained at nation {n.Nation?.DisplayName} period {n.Period}");
        foreach (var r in s.Relationships) Console.WriteLine($"  {r.Kind} {r.Name} value {r.Value} reason {r.Reason} flags 0x{r.Flags:X2} record 0x{r.Address:X}");
        foreach (var b in session.ReadBans(p)) Console.WriteLine($"  ban scope {b.Scope} item {b.ItemId} from {b.Start:yyyy-MM-dd} matches {b.MatchesFulfilled}/{b.Matches} days {b.Days} reason {b.Reason}");
    }
    return 0;
}

// removebans <name>   remove match bans only; day and global bans stay (writes to the game)
int RemoveBansCommand(string name)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    void Show(string when) => Console.WriteLine($"{when}: " + string.Join("; ", session.ReadBans(p).Select(b => $"scope {b.Scope} matches {b.MatchesFulfilled}/{b.Matches} days {b.Days} reason {b.Reason}")));
    Show("before");
    Console.WriteLine($"removed {session.RemoveMatchBans(p)}");
    Show("after ");
    return 0;
}

// destroy <name>   morale 1, condition and sharpness 5000, jadedness 1000 (writes to the game)
int DestroyCommand(string name)
{
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    ShowFitness(session, p, "before");
    session.Destroy(p);
    ShowFitness(session, p, "after ");
    return 0;
}

// setplayer <name> <PlayerField> <value>   guarded write of one mapped player field
int SetPlayerField(string name, string fieldName, long value)
{
    var field = Enum.Parse<Fm27Editor.Core.Players.PlayerField>(fieldName, ignoreCase: true);
    using var session = Attach(writable: true);
    if (OnePlayer(session, name) is not { } p) return 2;
    long before = session.PlayerFields.Read(p, field) ?? throw new InvalidOperationException($"{field} is not readable");
    session.PlayerFields.Set(p, field, before, value);
    Console.WriteLine($"{p.DisplayName} {field}: {before} -> {session.PlayerFields.Read(p, field)}");
    return 0;
}

static PlayerRecord? OnePlayer(GameSession session, string name)
{
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count == 1) return matches[0];
    Console.Error.WriteLine($"{matches.Count} players named '{name}'");
    return null;
}

static void ShowFitness(GameSession session, PlayerRecord p, string when)
{
    var v = session.PlayerFields.ReadAll(p);
    var inj = session.ReadInjuries(p);
    Console.WriteLine($"{when}: injuries {inj.Count} morale {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Morale)} condition {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Condition)} sharpness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.MatchSharpness)} jadedness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Jadedness)}");
}

// heal <name>   remove all injuries, condition and sharpness 10000, jadedness -500 (writes to the game)
int HealCommand(string name)
{
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var p = matches[0];
    void Show(string when)
    {
        var v = session.PlayerFields.ReadAll(p);
        var inj = session.ReadInjuries(p);
        Console.WriteLine($"{when}: injuries {inj.Count} ({string.Join(", ", inj.Select(i => i.Name))}) condition {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Condition)} sharpness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.MatchSharpness)} jadedness {v.GetValueOrDefault(Fm27Editor.Core.Players.PlayerField.Jadedness)}");
    }
    Show("before");
    session.Heal(p);
    Show("after ");
    return 0;
}

// squad <id>   first-team squad with player fields (condition, sharpness, morale, asking price...)
int InjuriesCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => query.Length == 0 || p.Id.ToString() == query || p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        foreach (var i in session.ReadInjuries(p))
            Console.WriteLine($"{p.Id} | {p.DisplayName} | player 0x{p.Address:X} | injury 0x{i.Address:X} | {i.TypeId} {i.Name} | {i.Start:yyyy-MM-dd} | {i.MinimumDays}+{i.ExtraDays} | tail {i.Snapshot[0x58..]}");
    return 0;
}

int BansCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => query.Length == 0 || p.Id.ToString() == query || p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
        foreach (var b in session.ReadBans(p))
            Console.WriteLine($"{p.Id} | {p.DisplayName} | player 0x{p.Address:X} | ban 0x{b.Address:X} | scope {b.Scope} item {b.ItemId} | {b.Start:yyyy-MM-dd} | days {b.Days} matches {b.MatchesFulfilled}/{b.Matches} points {b.Points} reason {b.Reason}");
    return 0;
}

// personidx <object>   index of the person-list entry pointing within ±0x400 of an object (read only)
int PersonIndexCommand(ulong address)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong obj = mem.ReadUInt64(session.Game.ModuleBase + 0x5703738);
    ulong begin = mem.ReadUInt64(obj + 0x88), end = mem.ReadUInt64(obj + 0x90);
    var ptrs = mem.Read(begin, (int)(end - begin));
    for (int i = 0; i < ptrs.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(ptrs, i);
        if (p + 0x400 >= address && p <= address + 0x400) Console.WriteLine($"index {i / 8} -> 0x{p:X} (object {(long)(p - address):+#;-#;0})");
    }
    return 0;
}

// near32 <value> <hex>...   every 4-aligned heap int32 equal to value with one of the byte patterns within ±0x20 (read only)
int Near32Command(int value, string[] patterns)
{
    using var session = Attach(writable: false);
    var needles = patterns.Select(Convert.FromHexString).ToArray();
    var chunk = new byte[1 << 20]; int hits = 0, refs = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x40)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0x20; k + 0x24 <= length; k += 4)
            {
                if (BitConverter.ToInt32(chunk, k) != value) continue;
                refs++;
                var window = chunk.AsSpan(k - 0x20, 0x44).ToArray();
                if (needles.Length > 0 && !needles.Any(n => window.AsSpan().IndexOf(n) >= 0)) continue;
                if (++hits <= 200) Console.WriteLine($"0x{region.Start + offset + (ulong)k:X}  {Convert.ToHexString(window, 0, 0x20)} [{Convert.ToHexString(window, 0x20, 4)}] {Convert.ToHexString(window, 0x24, 0x20)}");
            }
        }
    Console.WriteLine($"{refs} refs, {hits} with pattern");
    return 0;
}

// neardate <day> <year> <hex>...   every 2-aligned game date (low 9 bits = day, any flag bits) with a byte pattern within ±0x40 (read only)
int NearDateCommand(int day, int year, string[] patterns)
{
    using var session = Attach(writable: false);
    var needles = patterns.Select(Convert.FromHexString).ToArray();
    var chunk = new byte[1 << 20]; int hits = 0, refs = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x80)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0x40; k + 0x44 <= length; k += 2)
            {
                if (BitConverter.ToInt16(chunk, k + 2) != year || (BitConverter.ToUInt16(chunk, k) & 0x1FF) != day || chunk[k + 1] == 0) continue;
                refs++;
                var window = chunk.AsSpan(k - 0x40, 0x84).ToArray();
                if (needles.Length > 0 && !needles.Any(n => window.AsSpan().IndexOf(n) >= 0)) continue;
                if (++hits <= 200) Console.WriteLine($"0x{region.Start + offset + (ulong)k:X}  {Convert.ToHexString(window, 0, 0x40)} [{Convert.ToHexString(window, 0x40, 4)}] {Convert.ToHexString(window, 0x44, 0x40)}");
            }
        }
    Console.WriteLine($"{refs} flagged dates, {hits} with pattern");
    return 0;
}

// languages [id or name]   every language record; with no query, also the value and record-byte histograms (read only)
int LanguagesCommand(string query)
{
    using var session = Attach(writable: false);
    var values = new SortedDictionary<int, int>(); var tails = new Dictionary<string, int>(); var counts = new SortedDictionary<int, int>();
    foreach (var p in session.Players.LoadAll().Where(p => query.Length == 0 || p.Id.ToString() == query || p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
    {
        var languages = session.ReadLanguages(p);
        counts[languages.Count] = counts.GetValueOrDefault(languages.Count) + 1;
        foreach (var l in languages)
        {
            values[l.Value] = values.GetValueOrDefault(l.Value) + 1;
            tails[l.Snapshot[0x12..]] = tails.GetValueOrDefault(l.Snapshot[0x12..]) + 1;
            if (query.Length > 0) Console.WriteLine($"{p.Id} | {p.DisplayName} | record 0x{l.Address:X} | {l.LanguageId} {l.Name} | {l.Value}");
        }
    }
    if (query.Length == 0)
    {
        Console.WriteLine("languages per player: " + string.Join(" ", counts.Select(c => $"{c.Key}×{c.Value}")));
        Console.WriteLine("values: " + string.Join(" ", values.Select(c => $"{c.Key}×{c.Value}")));
        Console.WriteLine("bytes +0x9..+0xF: " + string.Join(" ", tails.OrderByDescending(t => t.Value).Take(8).Select(t => $"{t.Key}×{t.Value}")));
    }
    return 0;
}

int LanguageValueCommand(int playerId, ulong record, int value)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == playerId);
    var language = session.ReadLanguages(player).Single(l => l.Address == record);
    new LanguageWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetValue(player, language, value);
    Console.WriteLine($"{player.Id}: {language.Name} {language.Value} -> {value}");
    return 0;
}

// vecscan <object> <size>   every begin/end pointer pair in the object (and one hop through a holder): element count and the first bytes of up to 4 pointed-to elements (read only)
int VecScanCommand(ulong address, int size)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var q = new byte[16];
    void Scan(ulong at, int length, string prefix, bool recurse)
    {
        var o = new byte[length];
        if (!mem.TryRead(at, o)) return;
        for (int k = 0; k + 16 <= length; k += 8)
        {
            ulong begin = BitConverter.ToUInt64(o, k), end = BitConverter.ToUInt64(o, k + 8);
            if (begin == 0 || end <= begin || end - begin > 0x800 || begin >> 40 == 0 || !mem.TryRead(begin, q.AsSpan(0, 8))) { if (recurse && begin >> 40 != 0 && begin != 0) Scan(begin, 0x40, $"{prefix}+0x{k:X}->", false); continue; }
            var items = mem.Read(begin, (int)Math.Min(end - begin, 0x40));
            var preview = new List<string>();
            for (int i = 0; i + 8 <= items.Length && preview.Count < 4; i += 8)
            {
                ulong p = BitConverter.ToUInt64(items, i);
                preview.Add(p >> 40 != 0 && mem.TryRead(p, q) ? Convert.ToHexString(q) : $"inline {Convert.ToHexString(items, i, 8)}");
            }
            Console.WriteLine($"{prefix}+0x{k:X}: {(end - begin) / 8} slots  {string.Join(" | ", preview)}");
        }
    }
    Scan(address, size, "", true);
    return 0;
}

// reltype <hex type>   every player relation record with that type byte, raw (read only)
// cities <name>   cities whose name contains the text, with id and nation pointer (read only)
int CitiesCommand(string query)
{
    using var session = Attach(writable: false);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var all = session.Cities.LoadAll();
    Console.WriteLine($"{all.Count} cities in {sw.ElapsedMilliseconds} ms");
    foreach (var c in all.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20)) Console.WriteLine($"{c.Id,10} {c.Name} 0x{c.Address:X} nation 0x{c.Nation:X}");
    return 0;
}

// staffcontract <name>   staff job record [person+0xA8]: vtable and the contract fields the offered-contract reader sees (read only)
int StaffContractCommand(string query)
{
    using var session = Attach(writable: false);
    ulong mb = session.Game.ModuleBase;
    foreach (var s in session.Staff.LoadAll().Where(s => s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5))
    {
        ulong job = session.Memory.ReadUInt64(s.Address + 0xA8);
        ulong vt = job == 0 ? 0 : session.Memory.ReadUInt64(job) - mb;
        var c = job == 0 ? null : session.FutureTransfers.ReadContract(job, 0);
        Console.WriteLine($"{s.DisplayName} person 0x{s.Address:X} job 0x{job:X} vt +0x{vt:X}: " + (c is null ? "not a contract" : $"wage {c.Wage} type {c.Type} rolling {c.Rolling} {c.Started} - {c.Expires}"));
    }
    return 0;
}

// callups <name>   national squad of matching players, and how many players are called up overall (read only)
int CallUpsCommand(string query)
{
    using var session = Attach(writable: false);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var all = session.CallUps.All();
    Console.WriteLine($"{all.Count} called-up players, {all.Values.Select(c => c.Team).Distinct().Count()} squads in {sw.ElapsedMilliseconds} ms");
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(10))
        Console.WriteLine(all.TryGetValue(p.Address, out var c) ? $"{p.DisplayName}: {session.CareerPlans.Item(c.NationFootball)?.Name} team 0x{c.Team:X} slot 0x{c.Slot:X}" : $"{p.DisplayName}: none");
    return 0;
}

// ctypes   histogram of contract +0x26 (int16) and +0xC3 (byte) over every player's current contract, with age samples (read only)
int ContractTypes()
{
    using var session = Attach(writable: false);
    var m = session.Memory; var date = session.ReadGameDate();
    var hist = new Dictionary<string, List<string>>();
    foreach (var p in session.Players.LoadAll())
    {
        ulong c = m.ReadUInt64(p.Address + 0x330);
        var b = new byte[0xC8];
        if (c == 0 || !m.TryRead(c, b)) continue;
        p.GameDate = date;
        string key = $"+0x26 {BitConverter.ToInt16(b, 0x26),3} +0xC3 {b[0xC3],3}";
        if (!hist.TryGetValue(key, out var l)) hist[key] = l = [];
        l.Add($"{p.DisplayName} {p.Id} age {p.Age}");
    }
    foreach (var (k, l) in hist.OrderByDescending(kv => kv.Value.Count).Take(25)) Console.WriteLine($"{l.Count,6} {k}  e.g. {string.Join("; ", l.Take(3))}");
    return 0;
}

// future <name>   proposals with offered contracts and future transfer dates (read only)
int FutureCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(5))
    {
        var proposals = session.Proposals.Read(p);
        var contracts = session.FutureTransfers.OfferContracts(p);
        Console.WriteLine($"{p.DisplayName}: {proposals.Count} proposal(s), {contracts.Count} offered contract(s)");
        foreach (var x in proposals) Console.WriteLine($"  {x.Type} {x.From} -> {x.To} {x.Value} status {x.Status} id {x.Id}" + (contracts.TryGetValue(x.Id, out var c) ? $" | contract 0x{c.Address:X} wage {c.Wage} type {c.Type} rolling {c.Rolling} {c.Started} - {c.Expires} loan {c.Loan}" : ""));
        foreach (var f in session.FutureTransfers.Read(p, proposals)) Console.WriteLine($"  future transfer id {f.Offer.Id} on {f.Date}");
    }
    return 0;
}

// proposalsurvey   offers listed by each player (ids at [player+0x320]): one sample per vtable / status (read only)
int ProposalSurvey()
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    ulong root = m.ReadUInt64(mb + 0x56F2EF0);
    var byId = new Dictionary<int, ulong>();
    for (ulong a = m.ReadUInt64(root + 8), e = m.ReadUInt64(root + 0x10); a < e; a += 8) { ulong o = m.ReadUInt64(a); byId.TryAdd(BitConverter.ToInt32(m.Read(o + 0x5C, 4)), o); }
    var seen = new Dictionary<string, int>(); int listed = 0, missing = 0;
    foreach (var p in session.Players.LoadAll())
    {
        var b = new byte[16];
        ulong h = m.ReadUInt64(p.Address + 0x320);
        if (h == 0 || !m.TryRead(h, b)) continue;
        ulong begin = BitConverter.ToUInt64(b, 0), end = BitConverter.ToUInt64(b, 8);
        if (begin == 0 || end < begin || (end - begin) % 4 != 0 || end - begin > 400) continue;
        foreach (var id in Enumerable.Range(0, (int)(end - begin) / 4).Select(i => BitConverter.ToInt32(m.Read(begin + (ulong)i * 4, 4))))
        {
            listed++;
            if (!byId.TryGetValue(id, out var o)) { missing++; continue; }
            var r = m.Read(o, 0x90);
            string key = $"vt +0x{BitConverter.ToUInt64(r, 0) - mb:X} status {r[0x8D]}";
            seen[key] = seen.GetValueOrDefault(key) + 1;
            if (seen[key] <= 2) Console.WriteLine($"{key}: {p.DisplayName} {p.Id} id {id} value {BitConverter.ToInt32(r, 0x58)}");
        }
    }
    Console.WriteLine($"{listed} listed ids, {missing} not in the offer list, {byId.Count} offers");
    foreach (var (k, n) in seen.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{n,6} {k}");
    return 0;
}

// offerlist [name]   walks [module+0x56F2EF0]+0x8 (every transfer offer): vtable / status histogram, or one player's offers (read only)
int OfferList(string? name)
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    var persons = session.Players.LoadAll().ToDictionary(p => p.Address + 0x288, p => p);
    var clubs = new Dictionary<ulong, string>();
    string Team(ulong t) { if (t == 0) return "-"; ulong c = m.ReadUInt64(t + 0x30); var b = new byte[8]; return c != 0 && m.TryRead(c + 0xC8, b) && BitConverter.ToUInt64(b) is var s && s != 0 ? System.Text.Encoding.UTF8.GetString(m.Read(s + 4, m.Read(s, 1)[0])) : $"team 0x{t:X}"; }
    ulong root = m.ReadUInt64(mb + 0x56F2EF0);
    ulong b0 = m.ReadUInt64(root + 8), e0 = m.ReadUInt64(root + 0x10);
    var hist = new Dictionary<string, int>();
    for (ulong a = b0; a < e0; a += 8)
    {
        ulong o = m.ReadUInt64(a); var r = m.Read(o, 0x98);
        ulong vt = BitConverter.ToUInt64(r, 0) - mb;
        persons.TryGetValue(BitConverter.ToUInt64(r, 0x38), out var p);
        string key = $"vt +0x{vt:X} status {r[0x8D]} b8 {r[8]}";
        hist[key] = hist.GetValueOrDefault(key) + 1;
        if (name == "samples" && p is not null && hist[key] <= 2) Console.WriteLine($"{key}: {p.DisplayName} {p.Id} {Team(BitConverter.ToUInt64(r, 0x50))} -> {Team(BitConverter.ToUInt64(r, 0x40))} {BitConverter.ToInt32(r, 0x58)}");
        if (name is not null && p is not null && p.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"0x{o:X} {key} {p.DisplayName}: {Team(BitConverter.ToUInt64(r, 0x50))} -> {Team(BitConverter.ToUInt64(r, 0x40))} value {BitConverter.ToInt32(r, 0x58)} id {BitConverter.ToInt32(r, 0x5C)}{Environment.NewLine}   {Convert.ToHexString(r)}");
    }
    if (name is null) foreach (var (k, n) in hist.OrderByDescending(kv => kv.Value).Take(40)) Console.WriteLine($"{n,6} {k}");
    return 0;
}

// offerscan   heap objects whose +0x38 is a player's person and +0x40 and +0x50 are teams (FM26 TransferOffer layout); vtable histogram and samples (read only)
int OfferScan()
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    var persons = session.Players.LoadAll().ToDictionary(p => p.Address + 0x288, p => p);
    ulong teamVt = mb + session.Profile.Team!.VtableRva;
    bool IsTeam(ulong a) => a != 0 && m.TryRead(a, new byte[8]) && m.ReadUInt64(a) == teamVt;
    var hist = new Dictionary<ulong, int>();
    var chunk = new byte[1 << 20];
    foreach (var region in session.Process.EnumerateWritableRegions(mb, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x100)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!m.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0; k + 0x98 <= length; k += 8)
            {
                if (!persons.TryGetValue(BitConverter.ToUInt64(chunk, k + 0x38), out var player)) continue;
                ulong buyer = BitConverter.ToUInt64(chunk, k + 0x40), seller = BitConverter.ToUInt64(chunk, k + 0x50);
                if (!IsTeam(buyer) && !IsTeam(seller)) continue;
                ulong vt = BitConverter.ToUInt64(chunk, k);
                hist[vt] = hist.GetValueOrDefault(vt) + 1;
                if (hist[vt] <= 3) Console.WriteLine($"0x{region.Start + offset + (ulong)k:X} vt +0x{vt - mb:X} {player.DisplayName} {player.Id}: {Convert.ToHexString(chunk, k, 0x98)}");
            }
        }
    foreach (var (vt, n) in hist.OrderByDescending(kv => kv.Value)) Console.WriteLine($"{n,6} vt 0x{vt:X} (+0x{vt - mb:X})");
    return 0;
}

// careers <name>   career plans of players matching the name (read only)
int CareersCommand(string query)
{
    using var session = Attach(writable: false);
    var reader = session.CareerPlans;
    foreach (var p in session.Players.LoadAll().Where(p => p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(10))
    {
        var plans = reader.Read(p);
        Console.WriteLine($"{p.DisplayName} {p.Id}: {(plans is null ? "no container" : plans.Count + " plan(s)")}");
        foreach (var c in plans ?? []) Console.WriteLine($"  cond {c.Condition} scope {c.Scope} job {c.Job} type {c.ItemType} item {c.ItemName} 0x{c.Address:X}");
    }
    return 0;
}

// careersurvey   every player career plan (player+0x350 -> vector<plan*> at +0), histogram by field and item vtable (read only)
int CareerSurvey()
{
    using var session = Attach(writable: false);
    var m = session.Memory; ulong mb = session.Game.ModuleBase;
    var hist = new Dictionary<string, int>();
    int players = 0, plans = 0;
    foreach (var p in session.Players.LoadAll())
    {
        try {
        ulong c = m.ReadUInt64(p.Address + 0x350);
        if (c == 0) continue;
        ulong b = m.ReadUInt64(c), e = m.ReadUInt64(c + 8);
        if (b == 0 || e < b || (e - b) / 8 > 50) continue;
        players++;
        for (ulong a = b; a < e; a += 8)
        {
            var r = m.Read(m.ReadUInt64(a), 16); plans++;
            ulong item = BitConverter.ToUInt64(r, 0);
            string vt = item == 0 ? "null" : $"+0x{m.ReadUInt64(item) - mb:X}";
            string key = $"cond {r[8]} scope {r[9]} job {r[10]} type {r[11]} c {BitConverter.ToInt32(r, 12)} item {vt}";
            hist[key] = hist.GetValueOrDefault(key) + 1;
            if (hist[key] == 1) Console.WriteLine($"  e.g. {p.DisplayName} {p.Id}: {key} item 0x{item:X}");
        }
        } catch (MemoryAccessException) { hist["unreadable"] = hist.GetValueOrDefault("unreadable") + 1; }
    }
    Console.WriteLine($"{players} players with a container, {plans} plans");
    foreach (var (k, n) in hist.OrderByDescending(kv => kv.Value).Take(40)) Console.WriteLine($"{n,7} {k}");
    return 0;
}

// pfhist <PlayerField> [player id]   histogram of a mapped player field over every loaded player, or one player's value (read only)
int PlayerFieldHistogram(PlayerField field, int id)
{
    using var session = Attach(writable: false);
    var access = new PlayerFieldAccess(session.Memory, session.Profile, session.Game.ModuleBase);
    var players = session.Players.LoadAll();
    if (id != 0) { foreach (var p in players.Where(p => p.Id == id)) Console.WriteLine($"{p.Id} {p.DisplayName}: {access.Read(p, field)}"); return 0; }
    foreach (var g in players.GroupBy(p => access.Read(p, field)).OrderByDescending(g => g.Count()).Take(40))
        Console.WriteLine($"{g.Key,12} {g.Count(),7}  e.g. {string.Join(", ", g.Take(3).Select(p => $"{p.DisplayName} {p.Id}"))}");
    return 0;
}

int RelTypeCommand(int type)
{
    using var session = Attach(writable: false);
    var o = session.Profile.Relations!;
    int count = 0;
    var q = new byte[8];
    bool Q(ulong at, out ulong v) { bool ok = at != 0 && session.Memory.TryRead(at, q); v = ok ? BitConverter.ToUInt64(q) : 0; return ok; }
    foreach (var p in session.Players.LoadAll())
    {
        if (!Q(p.Address + (ulong)o.Pointer, out ulong holder) || holder == 0) continue;
        if (!Q(holder, out ulong begin) || !Q(holder + 8, out ulong end) || begin == 0 || end < begin || end - begin > 500 * 16) continue;
        var bytes = session.Memory.Read(begin, (int)(end - begin));
        for (int k = 0; k + 16 <= bytes.Length; k += 16)
            if (bytes[k + o.Type] == type) { count++; Console.WriteLine($"{p.Id} | {p.DisplayName} | {p.Club?.DisplayName} | {Convert.ToHexString(bytes, k, 16)}"); }
    }
    Console.WriteLine($"{count} records of type 0x{type:X}");
    return 0;
}

// nearptr <target> <hex>...   every heap qword equal to target with one of the hex byte patterns within ±0x40 (read only)
int NearPtrCommand(ulong target, string[] patterns)
{
    using var session = Attach(writable: false);
    var needles = patterns.Select(Convert.FromHexString).ToArray();
    var chunk = new byte[1 << 20]; int hits = 0, refs = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x80)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!session.Memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            for (int k = 0x40; k + 0x48 <= length; k += 8)
            {
                if (BitConverter.ToUInt64(chunk, k) != target) continue;
                refs++;
                var window = chunk.AsSpan(k - 0x40, 0x88).ToArray();
                if (needles.Length > 0 && !needles.Any(n => window.AsSpan().IndexOf(n) >= 0)) continue;
                hits++;
                Console.WriteLine($"0x{region.Start + offset + (ulong)k:X}  -0x40: {Convert.ToHexString(window[..0x40])}\n    ptr+: {Convert.ToHexString(window[0x40..])}");
            }
        }
    Console.WriteLine($"{refs} refs, {hits} with pattern");
    return 0;
}

// Read-only candidate scan based on the historical FM26 WorkPermit layout; results are hypotheses, not mappings.
int PermitProbeCommand()
{
    using var session = Attach(writable: false);
    var memory = session.Memory;
    var nations = session.Nations.LoadAll();
    var nationKeys = nations.SelectMany(n => new[] { n.Id ?? -1, BitConverter.ToInt32(memory.Read(n.Address + 8, 4)) }).Where(x => x >= 0).ToHashSet();
    var players = session.Players.LoadAll();
    var personKeys = players.SelectMany((p, index) => new[] { index, p.Id ?? -1, BitConverter.ToInt32(memory.Read(p.Address + 8, 4)), BitConverter.ToInt32(memory.Read(p.Address + 0x290, 4)) }).Where(x => x > 0).ToHashSet();
    var chunk = new byte[1 << 20]; long scanned = 0; int candidates = 0;
    foreach (var region in session.Process.EnumerateWritableRegions(session.Game.ModuleBase, session.Game.ModuleEnd).Where(r => !r.IsModuleData))
        for (ulong offset = 0; offset < region.Size; offset += (ulong)chunk.Length - 0x20)
        {
            int length = (int)Math.Min((ulong)chunk.Length, region.Size - offset);
            if (!memory.TryRead(region.Start + offset, chunk.AsSpan(0, length))) continue;
            scanned += length;
            for (int k = 0; k + 0x20 <= length; k += 8)
            {
                int sy = BitConverter.ToUInt16(chunk, k + 0x12), ey = BitConverter.ToUInt16(chunk, k + 0x16);
                int sd = BitConverter.ToUInt16(chunk, k + 0x10) & 511, ed = BitConverter.ToUInt16(chunk, k + 0x14) & 511;
                if (sy is < 2020 or > 2026 || ey < 2026 || ey > 2040 || sd is < 1 or > 366 || ed is < 1 or > 366 || sy > ey) continue;
                int nation = BitConverter.ToInt32(chunk, k), person = BitConverter.ToInt32(chunk, k + 8);
                if (!nationKeys.Contains(nation) || !personKeys.Contains(person)) continue;
                candidates++;
                Console.WriteLine($"candidate 0x{region.Start + offset + (ulong)k:X} nation-key {nation} person-key {person} dates {sy}/{sd}..{ey}/{ed} raw {Convert.ToHexString(chunk.AsSpan(k, 0x20))}");
            }
        }
    Console.WriteLine($"Hypothesis scan: {scanned:N0} bytes, {players.Count} players, {nations.Count} nations; {candidates} candidates. Not proof of presence or absence of work permits.");
    return 0;
}

int BanMatchesCommand(int playerId, ulong address, int matches, int fulfilled)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == playerId);
    var ban = session.ReadBans(player).Single(b => b.Address == address);
    new BanWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetMatches(player, ban, matches, fulfilled);
    Console.WriteLine($"{player.Id}: {ban.MatchesFulfilled}/{ban.Matches} -> {fulfilled}/{matches}");
    return 0;
}

int InjuryDurationCommand(int playerId, ulong address, int minimum, int extra)
{
    using var session = Attach(writable: true);
    var player = session.Players.LoadAll().Single(p => p.Id == playerId);
    var injury = session.ReadInjuries(player).Single(i => i.Address == address);
    new InjuryWriter(session.Memory, session.Profile, session.Game.ModuleBase).SetDuration(player, injury, minimum, extra);
    Console.WriteLine($"{player.Id}: {injury.MinimumDays}+{injury.ExtraDays} -> {minimum}+{extra}");
    return 0;
}

int SquadCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId);
    if (club?.FirstTeam is not { } team) { Console.Error.WriteLine($"no club or first team for id {clubId}"); return 2; }
    var squad = session.ReadSquad(team.Address);
    Console.WriteLine($"{club.Name}: {squad.Count} players");
    foreach (var p in squad)
    {
        var v = session.PlayerFields.ReadAll(p);
        string F(Fm27Editor.Core.Players.PlayerField f) => v.TryGetValue(f, out var x) ? x.ToString() : "-";
        var injuries = session.ReadInjuries(p);
        string inj = injuries.Count == 0 ? "" : "  injured: " + string.Join(", ", injuries.Select(i => $"{i.Name} {i.MinimumDays}+{i.ExtraDays}d"));
        Console.WriteLine($"  {p.DisplayName,-24} CA {p.CurrentAbility,3} cond {F(Fm27Editor.Core.Players.PlayerField.Condition),5} sharp {F(Fm27Editor.Core.Players.PlayerField.MatchSharpness),5} jaded {F(Fm27Editor.Core.Players.PlayerField.Jadedness),5} morale {F(Fm27Editor.Core.Players.PlayerField.Morale),2} price {F(Fm27Editor.Core.Players.PlayerField.AskingPrice),9} caps {F(Fm27Editor.Core.Players.PlayerField.Caps)}{inj}");
    }
    return 0;
}

// clubfields <id>   every club.fields mapping read from the live game (club fields + first team)
int ClubListsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var vision = session.ReadClubVision(club.FirstTeam?.Address ?? 0, clubs);
    var affiliations = session.ReadAffiliations(club.Address);
    var regional = session.ReadRegionalDivisions(club.Address);
    Console.WriteLine($"{club.Name} id {clubId}: confirmed culture {vision.Count}, affiliations {affiliations.Count}, regional {regional.Count}");
    foreach (var r in vision)
        Console.WriteLine($"  vision type {r.TypeCode} importance {r.Importance} start {r.StartDate:yyyy-MM-dd} end {r.EndDate:yyyy-MM-dd} item {r.ItemIndex} {r.ClubItem?.DisplayName}");
    foreach (var r in affiliations)
        Console.WriteLine($"  affiliation {r.ParentClub?.DisplayName} -> {r.FeederClub?.DisplayName} type {r.TypeCode} start {r.StartDate:yyyy-MM-dd}");
    foreach (var r in regional) Console.WriteLine($"  regional level {r.Level}");
    return 0;
}

int ClubVisionCommand(string[] args)
{
    bool write = args.Contains("--write");
    int fromIndex = Array.IndexOf(args, "--from");
    int? from = fromIndex >= 0 ? int.Parse(args[fromIndex + 1]) : null;
    var words = args.Where((_, i) => args[i] != "--write" && i != fromIndex && (fromIndex < 0 || i != fromIndex + 1)).ToArray();
    if (words.Length < 3) throw new ArgumentException("usage: clubvision <club-id> <type-code> <importance> [--from <current importance>] [--write]");
    int clubId = int.Parse(words[0]), type = int.Parse(words[1]), importance = int.Parse(words[2]);
    using var session = Attach(writable: write);
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var club = clubs.SingleOrDefault(c => c.Id == clubId) ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    var team = club?.FirstTeam ?? throw new InvalidOperationException($"No first team for club {clubId}");
    var matches = session.ReadClubVision(team.Address, clubs).Where(r => r.TypeCode == type).ToList();
    if (from is { } expected) matches = matches.Where(r => r.Importance == expected).ToList();
    if (matches.Count != 1)
        throw new InvalidOperationException($"Club {clubId} has {matches.Count} vision rows of type {type}" +
            (from is null ? "; add --from <current importance>" : ""));
    var entry = matches[0];
    Console.WriteLine($"{club!.Name} id {clubId} team 0x{team.Address:X}: type {entry.TypeCode} importance {entry.Importance} -> {importance}");
    if (!write) { Console.WriteLine("plan only; add --write to apply"); return 0; }
    session.SetClubVisionImportance(team.Address, entry, importance);
    var after = session.ReadClubVision(team.Address, clubs).Single(r => r.Address == entry.Address);
    if (after.Importance != importance) throw new StaleObjectException("Club vision readback failed");
    Console.WriteLine($"applied; type {after.TypeCode} importance {after.Importance}");
    return 0;
}

// clubdeep <idA> <idB> <size> <depth> <span> <a>:<b>...   RE (read only): walk both clubs' pointer graphs in lockstep (same slot offsets),
// report every offset inside the first <span> bytes of a node pair where the integers equal a (club A) and b (club B).
int ClubDeepCommand(int idA, int idB, int size, int depth, int span, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var clubs = session.LoadClubs(session.Players.LoadAll());
    var a = clubs.First(c => c.Id == idA); var b = clubs.First(c => c.Id == idB);
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: long.Parse(x[0]), B: long.Parse(x[1]))).ToList();
    long V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), _ => 0 };
    var seen = new HashSet<ulong>();
    var queue = new Queue<(ulong A, ulong B, string Path, int Depth)>();
    queue.Enqueue((a.Address, b.Address, "club", 0));
    int nodes = 0, hits = 0;
    while (queue.Count > 0 && nodes < 300000 && hits < 200)
    {
        var (pa, pb, path, d) = queue.Dequeue();
        var da = new byte[span]; var db = new byte[span];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        nodes++;
        for (int o = 0; o + size <= span; o++)
            foreach (var (x, y) in want)
                if (V(da, o) == x && V(db, o) == y && BitConverter.ToUInt64(da) - session.Game.ModuleBase < 0x8000000UL && hits++ < 200) Console.WriteLine($"  {path}+0x{o:X}  A=0x{pa:X} B=0x{pb:X}  vt +0x{BitConverter.ToUInt64(da) - session.Game.ModuleBase:X}");
        if (d >= depth) continue;
        for (int k = 0; k + 8 <= span; k += 8)
        {
            ulong ca = BitConverter.ToUInt64(da, k), cb = BitConverter.ToUInt64(db, k);
            if (ca < 0x10000000000 || cb < 0x10000000000 || ca > 0x7FF000000000 || cb > 0x7FF000000000 || ca == cb || !seen.Add(ca)) continue;
            queue.Enqueue((ca, cb, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} node pairs, {hits} hits");
    return 0;
}

// nationdeep <nationA> <nationB> <size> <depth> <span> <a>:<b>...   RE (read only): like clubdeep, starting from two nation objects.
int NationDeepCommand(string nameA, string nameB, int size, int depth, int span, string[] pairs)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Nations.LoadAll();
    var a = nations.First(n => string.Equals(n.Name, nameA, StringComparison.OrdinalIgnoreCase));
    var b = nations.First(n => string.Equals(n.Name, nameB, StringComparison.OrdinalIgnoreCase));
    var want = pairs.Select(s => s.Split(':')).Select(x => (A: long.Parse(x[0]), B: long.Parse(x[1]))).ToList();
    long V(byte[] d, int o) => size switch { 1 => d[o], 2 => BitConverter.ToInt16(d, o), 4 => BitConverter.ToInt32(d, o), _ => 0 };
    var seen = new HashSet<ulong>();
    var queue = new Queue<(ulong A, ulong B, string Path, int Depth)>();
    queue.Enqueue((a.Address, b.Address, "nation", 0));
    int nodes = 0, hits = 0;
    while (queue.Count > 0 && nodes < 300000 && hits < 100)
    {
        var (pa, pb, path, d) = queue.Dequeue();
        var da = new byte[span]; var db = new byte[span];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        nodes++;
        for (int o = 0; o + size <= span; o++)
            foreach (var (x, y) in want)
                if (V(da, o) == x && V(db, o) == y && BitConverter.ToUInt64(da) - session.Game.ModuleBase < 0x8000000UL && hits++ < 100)
                    Console.WriteLine($"  {path}+0x{o:X}  A=0x{pa:X} B=0x{pb:X}  vt +0x{BitConverter.ToUInt64(da) - session.Game.ModuleBase:X}");
        if (d >= depth) continue;
        for (int k = 0; k + 8 <= span; k += 8)
        {
            ulong ca = BitConverter.ToUInt64(da, k), cb = BitConverter.ToUInt64(db, k);
            if (ca < 0x10000000000 || cb < 0x10000000000 || ca > 0x7FF000000000 || cb > 0x7FF000000000 || ca == cb || !seen.Add(ca)) continue;
            queue.Enqueue((ca, cb, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} node pairs, {hits} hits");
    return 0;
}

// nationcolours <nationA> <nationB>: RE (read only), lockstep walk of two nations looking for a BGRA dword that is red-ish in A and yellow-ish in B.
int NationColoursCommand(string nameA, string nameB)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Nations.LoadAll();
    var a = nations.First(n => string.Equals(n.Name, nameA, StringComparison.OrdinalIgnoreCase));
    var b = nations.First(n => string.Equals(n.Name, nameB, StringComparison.OrdinalIgnoreCase));
    static bool Red(uint v) => (v >> 24) == 0xFF && (v >> 16 & 0xFF) > 0xB0 && (v >> 8 & 0xFF) < 0x60 && (v & 0xFF) < 0x60;
    static bool Yellow(uint v) => (v >> 24) == 0xFF && (v >> 16 & 0xFF) > 0xE0 && (v >> 8 & 0xFF) > 0xB0 && (v & 0xFF) < 0xA0;
    var seen = new HashSet<ulong>();
    var queue = new Queue<(ulong A, ulong B, string Path, int Depth)>();
    queue.Enqueue((a.Address, b.Address, "nation", 0));
    int nodes = 0, hits = 0;
    while (queue.Count > 0 && nodes < 200000 && hits < 40)
    {
        var (pa, pb, path, d) = queue.Dequeue();
        var da = new byte[0x400]; var db = new byte[0x400];
        if (!mem.TryRead(pa, da) || !mem.TryRead(pb, db)) continue;
        nodes++;
        for (int o = 0; o + 4 <= 0x400; o += 4)
            if (Red(BitConverter.ToUInt32(da, o)) && Yellow(BitConverter.ToUInt32(db, o)))
            { Console.WriteLine($"  {path}+0x{o:X}  A={BitConverter.ToUInt32(da, o):X8} B={BitConverter.ToUInt32(db, o):X8}  node A=0x{pa:X}"); hits++; }
        if (d >= 3) continue;
        for (int k = 0; k + 8 <= 0x400; k += 8)
        {
            ulong ca = BitConverter.ToUInt64(da, k), cb = BitConverter.ToUInt64(db, k);
            if (ca < 0x10000000000 || cb < 0x10000000000 || ca > 0x7FF000000000 || cb > 0x7FF000000000 || ca == cb || !seen.Add(ca)) continue;
            queue.Enqueue((ca, cb, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} node pairs, {hits} hits");
    return 0;
}

// nationfind <nation> <depth> <target nation>...: RE (read only), walks the pointer graph from the nation (first 0x400 bytes of each
// node) and prints where an 8-byte value equals one of the target nations' addresses.
int NationFindCommand(string name, int depth, string[] targets)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var nations = session.Nations.LoadAll();
    var start = nations.First(n => string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));
    var want = targets.ToDictionary(t => nations.First(n => string.Equals(n.Name, t, StringComparison.OrdinalIgnoreCase)).Address, t => t);
    var seen = new HashSet<ulong> { start.Address };
    var queue = new Queue<(ulong Addr, string Path, int Depth)>();
    queue.Enqueue((start.Address, "nation", 0));
    int hits = 0, nodes = 0;
    while (queue.Count > 0 && hits < 60 && nodes < 100000)
    {
        var (addr, path, d) = queue.Dequeue();
        var b = new byte[0x400];
        if (!mem.TryRead(addr, b)) continue;
        nodes++;
        for (int k = 0; k + 8 <= b.Length; k += 8)
        {
            ulong p = BitConverter.ToUInt64(b, k);
            if (want.TryGetValue(p, out var t)) { Console.WriteLine($"  {path}+0x{k:X} (node 0x{addr:X}) -> {t}"); hits++; }
            if (d < depth && p is > 0x10000000000 and < 0x7FF000000000 && seen.Add(p)) queue.Enqueue((p, $"{path}[+0x{k:X}]", d + 1));
        }
    }
    Console.WriteLine($"{nodes} nodes");
    return 0;
}

// nationteams <name>: read only, the men's national teams of the matching nations with their reputation.
int NationTeamsCommand(string query)
{
    using var session = Attach(writable: false);
    foreach (var n in session.Nations.LoadAll().Where(n => query.Length == 0 || (n.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20))
        Console.WriteLine($"{n.Name,-24} " + string.Join(" ", session.NationTeams.Read(n.Address).Select(t => $"[{t.Index}] {t.Reputation}")));
    return 0;
}

// nationcap <name>: RE (read only), the City at nation+0xE8 and the Continent at nation+0xF0 for the nations whose name matches.
int NationCapCommand(string query)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    string? Str(ulong field) { ulong sp = BitConverter.ToUInt64(mem.Read(field, 8)); if (sp == 0) return null; var l = new byte[4]; if (!mem.TryRead(sp, l)) return null; int n = BitConverter.ToInt32(l); return n is >= 0 and <= 300 ? System.Text.Encoding.UTF8.GetString(mem.Read(sp + 4, n)) : null; }
    foreach (var n in session.Nations.LoadAll().Where(n => query.Length == 0 || (n.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).Take(30))
    {
        var city = session.Cities.Read(mem.ReadUInt64(n.Address + 0xE8));
        ulong cont = mem.ReadUInt64(n.Address + 0xF0);
        var stadium = session.Stadiums.Reload(mem.ReadUInt64(n.Address + 0x118));
        Console.WriteLine($"{n.Name,-24} id {n.Id,5} capital {city?.Name,-18} continent 0x{cont:X} {(cont == 0 ? null : Str(cont + 0x18))} stadium {stadium?.DisplayName}");
    }
    Console.WriteLine("continents: " + string.Join(", ", session.NationLinks.Continents(session.Nations.LoadAll()).Select(c => c.Name)));
    return 0;
}

// awards <name>: read only, awards from the game-wide list.
int AwardsCommand(string query)
{
    using var session = Attach(writable: false);
    var all = session.Awards.LoadAll();
    foreach (var a in all.Where(a => query.Length == 0 || a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(40))
        Console.WriteLine($"0x{a.Address:X} index {a.Index} id {a.Id} rep {a.Reputation} home {a.WinnerHomeReputation} world {a.WinnerWorldReputation}  {a.Name} ({a.ShortName})");
    Console.WriteLine($"{all.Count} awards");
    return 0;
}

// comps <name>: competitions from the active build's game-wide list.
int CompsCommand(string query)
{
    using var session = Attach(writable: false);
    var all = session.Competitions.LoadAll();
    foreach (var competition in all.Where(c => c.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(25))
        Console.WriteLine($"0x{competition.Address:X} index {competition.Index} id {competition.Id} {competition.Name} ({competition.ShortName})");
    Console.WriteLine($"{all.Count} competitions");
    return 0;
}

// compsnap <path>: RE (read only). Save every competition object (0x1B0 bytes) keyed by competition ID, so a later
// compdiff can locate what FMRTE changed even when the save reload moves every address.
int CompSnapCommand(string path)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var lines = new List<string>();
    foreach (var competition in session.Competitions.LoadAll())
        lines.Add($"{competition.Id}\t{Convert.ToHexString(mem.Read(competition.Address, 0x1B0))}");
    File.WriteAllLines(path, lines);
    Console.WriteLine($"{lines.Count} competition objects -> {path}");
    return 0;
}

// compdiff <path>: RE (read only). Compare the saved snapshot with the live objects by competition ID.
int CompDiffCommand(string path)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var before = new Dictionary<int, byte[]>();
    foreach (var line in File.ReadAllLines(path))
    {
        var parts = line.Split('\t');
        if (parts.Length == 2 && int.TryParse(parts[0], out int id)) before[id] = Convert.FromHexString(parts[1]);
    }
    int changed = 0;
    foreach (var competition in session.Competitions.LoadAll())
    {
        if (!before.TryGetValue(competition.Id, out var old)) continue;
        var now = mem.Read(competition.Address, old.Length);
        var diffs = Enumerable.Range(0, old.Length).Where(i => old[i] != now[i]).ToArray();
        if (diffs.Length == 0) continue;
        changed++;
        if (changed > 40) continue;
        Console.WriteLine($"id {competition.Id} {competition.DisplayName}: {diffs.Length} bytes differ");
        int at = 0;
        while (at < diffs.Length)
        {
            int start = diffs[at], end = start;
            while (at + 1 < diffs.Length && diffs[at + 1] == end + 1) { at++; end++; }
            at++;
            int count = end - start + 1;
            Console.WriteLine($"  +0x{start:X3} ({count}) {Convert.ToHexString(old, start, count)} -> {Convert.ToHexString(now, start, count)}");
        }
    }
    Console.WriteLine($"{changed} competitions changed");
    return 0;
}

// compscan <start hex> <len>: RE (read only). For every byte offset in the range, list how many distinct values the
// whole competition list has and the most common ones, so enum-like fields stand out from per-object pointers.
int CompScanCommand(int start, int len)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase;
    var offsets = session.Profile.Competition ?? throw new InvalidOperationException("Profile has no competition offsets");
    var bodies = new List<(ulong Address, byte[] Bytes, int Id)>();
    foreach (var competition in session.Competitions.LoadAll())
    {
        var b = mem.Read(competition.Address, 0x1B0);
        if (BitConverter.ToUInt64(b) != mb + offsets.VtableRva) continue;
        bodies.Add((competition.Address, b, competition.Id));
    }
    Console.WriteLine($"{bodies.Count} competition objects");
    for (int o = start; o < start + len && o < 0x1B0; o++)
    {
        var counts = new Dictionary<byte, int>();
        foreach (var (_, b, _) in bodies)
        {
            counts.TryGetValue(b[o], out int n);
            counts[b[o]] = n + 1;
        }
        if (counts.Count > 12) continue; // pointers / high-entropy data
        var parts = counts.OrderByDescending(k => k.Value).Take(8)
            .Select(k => $"{(char.IsControl((char)k.Key) ? "?" : ((char)k.Key).ToString())}/{k.Key:X2}x{k.Value}");
        Console.WriteLine($"+0x{o:X3} {counts.Count,2} values: {string.Join(" ", parts)}");
    }
    return 0;
}

// compsurvey <offset hex> <len>: RE (read only), histogram of the bytes at competition+offset over the whole list, with example names.
int CompSurveyCommand(int offset, int len)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    ulong mb = session.Game.ModuleBase, root = mem.ReadUInt64(mb + 0x56F5070);
    ulong begin = mem.ReadUInt64(root + 0x40), end = mem.ReadUInt64(root + 0x48);
    var raw = mem.Read(begin, (int)(end - begin));
    string? Str(ulong field) { ulong sp = BitConverter.ToUInt64(mem.Read(field, 8)); if (sp == 0) return null; var l = new byte[4]; if (!mem.TryRead(sp, l)) return null; int n = BitConverter.ToInt32(l); return n is >= 0 and <= 300 ? System.Text.Encoding.UTF8.GetString(mem.Read(sp + 4, n)) : null; }
    var hist = new Dictionary<string, (int Count, List<string> Names)>();
    for (int i = 0; i < raw.Length; i += 8)
    {
        ulong p = BitConverter.ToUInt64(raw, i);
        var b = new byte[offset + len];
        if (!mem.TryRead(p, b) || BitConverter.ToUInt64(b) != mb + 0x4CDFAD8) continue;
        string hex = Convert.ToHexString(b, offset, len);
        var e = hist.TryGetValue(hex, out var x) ? x : (0, []);
        if (e.Names.Count < 3) e.Names.Add($"{Str(p + 0x40)} (id {BitConverter.ToInt32(b, 0xC)})");
        hist[hex] = (e.Item1 + 1, e.Names);
    }
    Console.WriteLine($"{hist.Count} distinct values");
    foreach (var (hex, (count, names)) in hist.OrderByDescending(k => k.Value.Count).Take(80))
        Console.WriteLine($"  {hex}  x{count}  {string.Join("; ", names)}");
    return 0;
}

int ClubStaffCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var today = session.ReadGameDate();
    var staff = session.Staff.LoadAll().Where(s => s.Club?.Address == club.Address).ToArray();
    Console.WriteLine($"{club.Name}: {staff.Length} staff");
    foreach (var s in staff)
    {
        int? age = today is { } t && s.DateOfBirth is { } b ? t.Year - b.Year - (t < b.AddYears(t.Year - b.Year) ? 1 : 0) : null;
        Console.WriteLine($"  job {s.JobCode,3} {s.DisplayName,-26} age {age,3} {s.Nationality?.DisplayName,-22} CA {s.CurrentAbility,3} PA {s.PotentialAbility,3}");
    }
    return 0;
}

// clubrel <id>: list. setclubrel <id> <row> <reason|-> <value|-> [permanent|temporary]: guarded write of one row.
int ClubRelCommand(int clubId, string[]? set)
{
    using var session = Attach(writable: set is not null);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var rows = session.ReadClubRelationships(club.Address);
    if (set is not null)
    {
        var row = rows[int.Parse(set[0])];
        session.SetClubRelationship(club.Address, row, set[1] == "-" ? null : int.Parse(set[1]),
            set.Length > 3 ? set[3] == "permanent" : null, set[2] == "-" ? null : int.Parse(set[2]));
        rows = session.ReadClubRelationships(club.Address);
    }
    Console.WriteLine($"{club.Name}: {rows.Count} relationship rows");
    for (int i = 0; i < rows.Count; i++)
    {
        var r = rows[i];
        string type = r.Kind == Fm27Editor.Core.Clubs.ClubRelationshipKind.Other ? $"Unknown ({r.TypeCode})" : r.Kind.ToString();
        Console.WriteLine($"  {i,2} 0x{r.Address:X} {type,-15} {r.Name,-28} value {r.Value,3} reason {r.Reason} permanent {r.Permanent}  {r.Snapshot}");
    }
    return 0;
}

int ClubFieldsCommand(int clubId)
{
    using var session = Attach(writable: false);
    var club = session.LoadClubs(session.Players.LoadAll()).SingleOrDefault(c => c.Id == clubId)
        ?? session.ScanAllClubs().SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var values = session.ClubFields.ReadAll(club.Address, club.FirstTeam?.Address ?? 0);
    Console.WriteLine($"{club.Name}: {values.Count} mapped fields");
    foreach (var (f, v) in values.OrderBy(kv => kv.Key)) Console.WriteLine($"  {f,-24} {v}");
    return 0;
}

// club <id>   addresses of the club object, its finance object, clause holder and teams (read only)
int ClubInfoCommand(int clubId)
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var club = session.LoadClubs(all).SingleOrDefault(c => c.Id == clubId);
    if (club is null) { Console.Error.WriteLine($"no club with id {clubId}"); return 2; }
    var k = session.Profile.Club!;
    Console.WriteLine($"{club.Name} ({club.ShortName}) id {club.Id}");
    Console.WriteLine($"  club    0x{club.Address:X}  vtable +0x{k.ClubVtableRva:X}");
    Console.WriteLine($"  finance 0x{club.Finance:X}  (club+0x{k.Finance?.Pointer:X})");
    if (k.TransferClauses is { } tc) Console.WriteLine($"  clauses 0x{session.Memory.ReadUInt64(club.Address + (ulong)tc.Pointer):X}  (club+0x{tc.Pointer:X})");
    foreach (var t in club.Teams)
        Console.WriteLine($"  team    0x{t.Address:X}  {(t.IsFirstTeam ? "first team" : "")} squad {t.SquadCount}/{t.SquadCapacity}");
    Console.WriteLine($"  module  0x{session.ModuleBase:X}");
    return 0;
}

// probe <hexAddr> <hexSize> <depth> <value>...
//   Read-only search for values inside [addr, addr+size) and, when depth > 0, inside the first 0x1000 bytes of every
//   object a qword in that range points to (depth levels). Each value is matched as int32, int16 and (when 0-255) byte.
//   Values may be decimal or 0x-hex. Prints path as +off[->+off...] so the field can be read back as a chain.
int Probe(ulong address, int size, int depth, string[] values)
{
    using var session = Attach(writable: false);
    var mem = session.Memory;
    var wants = values.Select(v => v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(v[2..], 16) : long.Parse(v)).ToArray();
    var seen = new HashSet<ulong>();
    void Scan(ulong at, int length, string path, int level)
    {
        if (!seen.Add(at)) return;
        var b = new byte[length];
        if (!mem.TryRead(at, b))
        {
            // Fall back to the largest readable prefix in 0x100 steps.
            int ok = 0;
            for (int n = 0x100; n <= length; n += 0x100) { if (mem.TryRead(at, b.AsSpan(0, n))) ok = n; else break; }
            if (ok == 0) return;
            length = ok;
        }
        for (int o = 0; o + 1 < length; o++)
            foreach (var w in wants)
            {
                if (o % 4 == 0 && o + 4 <= length && BitConverter.ToInt32(b, o) == w) Console.WriteLine($"{path}+0x{o:X} int32 = {w}");
                else if (o % 2 == 0 && w is >= short.MinValue and <= short.MaxValue && BitConverter.ToInt16(b, o) == w) Console.WriteLine($"{path}+0x{o:X} int16 = {w}");
                // Single bytes match almost everywhere, so they are reported only when one value is asked for.
                else if (wants.Length == 1 && w is >= 0 and <= 255 && b[o] == w) Console.WriteLine($"{path}+0x{o:X} byte = {w}");
            }
        if (level <= 0) return;
        for (int o = 0; o + 8 <= length; o += 8)
        {
            ulong q = BitConverter.ToUInt64(b, o);
            if (q < 0x10000 || q > 0x7FFF_FFFF_FFFF || q >= session.ModuleBase && q < session.ModuleBase + 0x1000_0000) continue;
            Scan(q, 0x1000, $"{path}+0x{o:X}->", level - 1);
        }
    }
    Scan(address, size, "", depth);
    return 0;
}

// clauses fee   every sell-on that stores the signed-for fee (not -1), with player and both clubs
int SellOnsWithFee()
{
    using var session = Attach(writable: false);
    var all = session.Players.LoadAll();
    var byId = all.Where(p => p.Id is not null).GroupBy(p => p.Id!.Value).ToDictionary(g => g.Key, g => g.First());
    foreach (var club in session.LoadClubs(all))
        foreach (var c in session.ReadTransferClauses(club.Address).Where(c => c.Kind == Fm27Editor.Core.Clubs.TransferClauseKind.SellOn && c.SignedFor is not null))
            Console.WriteLine($"{(byId.TryGetValue(c.PlayerId, out var p) ? p.DisplayName : c.PlayerId.ToString()),-26} at {club.DisplayName,-24} {c.Percentage}% to {c.Beneficiary?.DisplayName,-24} signed for {c.SignedFor:N0}");
    return 0;
}

// contract <name>                      show wage, expiry, playing time
// contract <name> Wage|Expires|PlayingTime <value>
int ContractCommand(string name, string? field, string? text)
{
    using var session = Attach(writable: field is not null);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) { Console.Error.WriteLine($"{matches.Count} players named '{name}'"); return 2; }
    var p = matches[0];
    var w = new ContractWriter(session.Memory, session.Profile, session.ModuleBase);
    if (p.Contract is not { } c) { Console.Error.WriteLine("no contract"); return 2; }
    var d = w.Read(p)!;
    Console.WriteLine($"{p.DisplayName}: {d.Type}, wage {d.Wage}, loyalty {d.LoyaltyBonus}, number {d.SquadNumber}");
    Console.WriteLine($"  started {d.Started:yyyy-MM-dd} signed {d.Signed:yyyy-MM-dd} expires {d.Expires:yyyy-MM-dd} joined club {d.JoinedClub:yyyy-MM-dd}");
    Console.WriteLine($"  playing time {d.PlayingTime}, future {d.FuturePlayingTime} from {d.FuturePlayingTimeYear}, happiness {d.Happiness}, playing time happiness {d.PlayingTimeHappiness}");
    foreach (var t in d.Terms) Console.WriteLine($"  term {t.Index}: type {t.Type} value {t.Value} parameter {t.Parameter}");
    if (field is null) return 0;
    switch (field.ToLowerInvariant())
    {
        case "wage": w.SetWage(p, int.Parse(text!)); break;
        case "expires": w.SetExpiry(p, DateOnly.ParseExact(text!, "yyyy-MM-dd")); break;
        case "playingtime": w.SetPlayingTime(p, Enum.Parse<PlayingTime>(text!, true)); break;
        case "number": w.SetSquadNumber(p, d, int.Parse(text!)); break;
        case "joinedclub": w.SetJoinedClub(p, d, DateOnly.ParseExact(text!, "yyyy-MM-dd")); break;
        default: Console.Error.WriteLine($"unknown contract field '{field}'"); return 2;
    }
    var after = session.Players.Reload(p.Address)!;
    var ad = w.Read(after)!;
    Console.WriteLine($"after: wage {after.Contract?.Wage}, expires {after.Contract?.Expires:yyyy-MM-dd}, playing time {w.ReadPlayingTime(after)}, number {ad.SquadNumber}, joined club {ad.JoinedClub:yyyy-MM-dd}");
    return 0;
}

int Set(string name, string field, string text)
{
    bool born = field.Equals("Born", StringComparison.OrdinalIgnoreCase);
    DateOnly date = default;
    int value = 0;
    if (born ? !DateOnly.TryParseExact(text, "yyyy-MM-dd", out date) : !int.TryParse(text, out value))
    {
        Console.Error.WriteLine($"bad value '{text}' for {field}");
        return 2;
    }
    using var session = Attach(writable: true);
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
    {
        Console.Error.WriteLine($"{matches.Count} players named '{name}'");
        return 2;
    }
    var p = matches[0];
    var w = session.Writer;
    string before;
    if (Enum.TryParse<PlayerAttribute>(field, true, out var a)) { before = p.Attribute(a).ToString(); w.SetAttribute(p, a, value); }
    else if (Enum.TryParse<PlayerPosition>(field, true, out var pos)) { before = p.Position(pos).ToString(); w.SetPosition(p, pos, value); }
    else if (Enum.TryParse<PersonalityAttribute>(field, true, out var per)) { before = p.PersonalityValue(per).ToString(); w.SetPersonality(p, per, value); }
    else if (field.Equals("CA", StringComparison.OrdinalIgnoreCase)) { before = p.CurrentAbility.ToString(); w.SetCurrentAbility(p, value); }
    else if (field.Equals("PA", StringComparison.OrdinalIgnoreCase)) { before = p.PotentialAbility.ToString(); w.SetPotentialAbility(p, value); }
    else if (field.Equals("Height", StringComparison.OrdinalIgnoreCase)) { before = p.Height.ToString(); w.SetHeight(p, value); }
    else if (born) { before = $"{p.DateOfBirth:yyyy-MM-dd}"; w.SetDateOfBirth(p, date); }
    else
    {
        Console.Error.WriteLine($"unknown field '{field}'");
        return 2;
    }
    Console.WriteLine($"{p.DisplayName} {field}: {before} -> {text}");
    return 0;
}

int FindOffsets(string goldenPath, string outPath, string? gameDate)
{
    var game = GameProcess.Locate();
    using var memory = new WindowsProcessMemory(game.ProcessId, writable: false);
    var sw = Stopwatch.StartNew();
    var date = gameDate is null ? (DateOnly?)null : DateOnly.Parse(gameDate, System.Globalization.CultureInfo.InvariantCulture);
    var profile = new OffsetFinder(memory, game, Console.WriteLine).Find(GoldenPlayers.Load(goldenPath), date);
    File.WriteAllText(outPath, profile.ToJson());
    Console.WriteLine($"wrote {outPath} in {sw.Elapsed.TotalSeconds:F0} s");
    return 0;
}

int Transfer(string[] args)
{
    string sub = args.FirstOrDefault() ?? "";
    try
    {
        return sub switch
        {
            "dump" => TransferDump(args[1], args.ElementAtOrDefault(2)),
            "peek" => TransferPeek(args[1]),
            "offers" => TransferOffers(),
            "diff" => TransferShowDiff(args[1], args[2]),
            _ => Usage(),
        };
    }
    catch (IndexOutOfRangeException) { return Usage(); }
}

PlayerRecord FindOne(GameSession session, string name)
{
    var matches = session.Players.LoadAll().Where(p => p.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1) throw new ArgumentOutOfRangeException(nameof(name), name, $"{matches.Count} players named '{name}'");
    return matches[0];
}

int TransferPeek(string name)
{
    using var session = Attach(writable: false);
    var player = FindOne(session, name);
    Console.WriteLine($"{player.DisplayName}  player 0x{player.Address:X}  game date {session.ReadGameDate()?.ToString("yyyy-MM-dd") ?? "unknown"}");
    Console.WriteLine($"club {player.Club?.DisplayName ?? "-"}  wage {player.Contract?.Wage.ToString() ?? "-"}  expires {player.Contract?.Expires?.ToString("yyyy-MM-dd") ?? "-"}  joined {player.Contract?.Joined?.ToString("yyyy-MM-dd") ?? "-"}");
    Console.WriteLine($"contract +0x330 -> 0x{player.Contract?.Address ?? 0:X}   pending +0x328 -> 0x{player.PendingContractAddress:X}");
    foreach (var clause in session.ReadClauses(player))
        Console.WriteLine($"clause type {clause.Type,3}  value {clause.Value}");
    if (player.Club is { } club) PrintFinance(session.ReadFinance(club.Address));
    return 0;
}

void PrintFinance(ClubFinance? finance)
{
    if (finance is null) { Console.WriteLine("finance unknown"); return; }
    Console.WriteLine($"finance 0x{finance.Address:X}  balance {finance.Balance}  budget {finance.TransferBudget}  spent {finance.SpentThisSeason}  wages {finance.WageSpend}");
}

int TransferOffers()
{
    using var session = Attach(writable: false);
    var players = session.Players.LoadAll();
    var withOffer = players.Where(p => p.PendingContractAddress != 0).ToList();
    Console.WriteLine($"{withOffer.Count} players have a pending contract pointer (player+0x328)");
    foreach (var player in withOffer.Take(40))
        Console.WriteLine($"{player.DisplayName,-28} club {player.Club?.DisplayName ?? "-",-24} contract 0x{player.Contract?.Address ?? 0:X} pending 0x{player.PendingContractAddress:X} clauses {session.ReadClauses(player).Count}");
    return 0;
}

int TransferDump(string name, string? outPath)
{
    using var session = Attach(writable: false);
    var player = FindOne(session, name);
    var snapshot = TransferSnapshot.Capture(session, player);
    string path = outPath ?? $"transfer-{string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c == '-'))}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    snapshot.Save(path);
    Console.WriteLine($"{player.DisplayName}: saved {path}  ({snapshot.Objects.Count} objects, {snapshot.Clauses.Count} clauses)");
    Console.WriteLine($"club {snapshot.Club ?? "-"}  wage {snapshot.Wage?.ToString() ?? "-"}  expires {snapshot.Expires ?? "-"}  joined {snapshot.Joined ?? "-"}");
    return 0;
}

int TransferShowDiff(string beforePath, string afterPath)
{
    var before = TransferSnapshot.Load(beforePath);
    var after = TransferSnapshot.Load(afterPath);
    Console.WriteLine($"{before.PlayerName}: {before.CapturedAt} -> {after.CapturedAt}");
    var changes = TransferDiff.Compare(before, after);
    foreach (var c in changes)
    {
        string where = c.Name is null ? $"{c.Section,-8} +0x{c.Offset:X3}" : $"{c.Section,-8} {c.Name}";
        string values = c.PointerBefore is { } pb
            ? $"ptr 0x{pb:X} -> 0x{c.PointerAfter ?? 0:X}"
            : $"{c.IntBefore?.ToString() ?? c.Before} -> {c.IntAfter?.ToString() ?? c.After}";
        Console.WriteLine($"{where}  {values}");
    }
    if (changes.Count == 0) Console.WriteLine("no differences");
    return 0;
}

int StaffCareers()
{
    using var session = Attach(writable: false);
    var all = session.Staff.LoadAll();
    var reader = new CareerPlanReader(session.Memory, session.Profile, session.Game.ModuleBase);
    int containers = 0;
    var with = new List<(Fm27Editor.Core.Staff.StaffRecord S, IReadOnlyList<CareerPlan> P)>();
    foreach (var s in all)
        if (reader.Read(session.Owner(s)) is { } plans) { containers++; if (plans.Count > 0) with.Add((s, plans)); }
    Console.WriteLine($"{all.Count} staff, {containers} with a container, {with.Count} with plans");
    foreach (var (s, p) in with.OrderByDescending(x => x.S.CurrentReputation).Take(15))
        Console.WriteLine($"  {s.DisplayName} ({s.Club?.DisplayName}) job {s.JobCode}: " + string.Join("; ", p.Select(x => $"{x.ItemName} c{x.Condition} s{x.Scope} j{x.Job} t{x.ItemType}")));
    return 0;
}

int StaffKnowledgeCommand(string name)
{
    using var session = Attach(writable: false);
    var nations = session.Nations.LoadAll();
    foreach (var s in session.Staff.LoadAll().Where(s => s.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine($"{s.DisplayName} ({s.Club?.DisplayName}): " + string.Join(", ", session.StaffKnowledge.Read(s, nations).Select(k => $"{k.Nation} {k.Value}")));
    return 0;
}

// tendencysurvey   RE: every tendency type code over all staff, with how many staff have it (read only)
int TendencySurvey()
{
    using var session = Attach(writable: false);
    var counts = new SortedDictionary<int, int>();
    int bad = 0;
    foreach (var s in session.Staff.LoadAll())
    {
        try { foreach (var t in session.StaffTendencies.Read(s)) counts[t.Type] = counts.GetValueOrDefault(t.Type) + 1; }
        catch (Exception) { bad++; }
    }
    Console.WriteLine($"{counts.Count} codes, {bad} unreadable");
    Console.WriteLine(string.Join("  ", counts.Select(kv => $"{kv.Key}:{kv.Value}")));
    return 0;
}
