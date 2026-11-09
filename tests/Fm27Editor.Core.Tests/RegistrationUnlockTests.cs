using Fm27Editor.Core.Clubs;

namespace Fm27Editor.Core.Tests;

/// <summary>
/// The site and the stolen bytes come from the live 27.0.0+2493426 image (<c>mov r14,[rbx]</c> + <c>mov r15,[rbx+8]</c>);
/// the trampoline and the patch below are ours, so these tests pin their exact byte layout.
/// </summary>
public class RegistrationUnlockTests
{
    const ulong From = 0x7FFAEFDD1E36;
    const ulong Page = From + 0x10000 - 0x10;
    const ulong Stub = Page + 0x10;
    const ulong Resume = From + 7;

    [Fact]
    public void Stub_stores_the_rules_pointer_then_replays_the_stolen_loads()
    {
        var stub = RegistrationUnlock.BuildStub(Page, RegistrationUnlock.StolenBytes, Resume);

        Assert.Equal(0x49, stub[0]);                       // mov r11, imm64
        Assert.Equal(0xBB, stub[1]);
        Assert.Equal(Page, BitConverter.ToUInt64(stub, 2));
        Assert.Equal(new byte[] { 0x4D, 0x89, 0x1B }, stub[10..13]);   // mov [r11], rbx
        Assert.Equal(RegistrationUnlock.StolenBytes, stub[13..20]);
        Assert.Equal(new byte[] { 0xFF, 0x25, 0, 0, 0, 0 }, stub[20..26]); // jmp qword ptr [rip+0]
        Assert.Equal(Resume, BitConverter.ToUInt64(stub, 26));
        Assert.Equal(34, stub.Length);
    }

    [Fact]
    public void Patch_is_a_relative_jump_padded_to_the_stolen_length()
    {
        var patch = RegistrationUnlock.BuildPatch(From, Stub);

        Assert.Equal(RegistrationUnlock.StolenBytes.Length, patch.Length);
        Assert.Equal(0xE9, patch[0]);
        int expected = unchecked((int)(long)(Stub - (From + 5)));
        Assert.Equal(expected, BitConverter.ToInt32(patch, 1));
        Assert.Equal(new byte[] { 0x90, 0x90 }, patch[5..7]);
    }

    [Fact]
    public void Patch_refuses_a_trampoline_that_a_rel32_jump_cannot_reach()
    {
        ulong from = From;
        ulong far = from + 0x9000_0000UL;
        Assert.Throws<InvalidOperationException>(() => RegistrationUnlock.BuildPatch(from, far));
        Assert.Throws<InvalidOperationException>(() => RegistrationUnlock.BuildPatch(from, 0x10));
    }
}
