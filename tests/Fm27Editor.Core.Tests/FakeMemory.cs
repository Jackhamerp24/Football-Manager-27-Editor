using System.Text;
using Fm27Editor.Core.Memory;

namespace Fm27Editor.Core.Tests;

/// <summary>Sparse in-memory address space: separate segments, reads may not cross a segment.</summary>
sealed class FakeMemory : IProcessMemory
{
    readonly List<(ulong Start, byte[] Data)> _segments = new();
    public int Writes { get; private set; }

    public byte[] Map(ulong start, int length)
    {
        var data = new byte[length];
        _segments.Add((start, data));
        return data;
    }

    /// <summary>Length-prefixed UTF-8 string as the game stores names.</summary>
    public void MapString(ulong address, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var d = Map(address, 4 + bytes.Length);
        BitConverter.GetBytes(bytes.Length).CopyTo(d, 0);
        bytes.CopyTo(d, 4);
    }

    /// <summary>Name entry whose first qword points at a string.</summary>
    public void MapNameEntry(ulong entry, ulong stringAddress, string text)
    {
        BitConverter.GetBytes(stringAddress).CopyTo(Map(entry, 8), 0);
        MapString(stringAddress, text);
    }

    public bool TryRead(ulong address, Span<byte> buffer)
    {
        foreach (var (start, data) in _segments)
            if (address >= start && address + (ulong)buffer.Length <= start + (ulong)data.Length)
            {
                data.AsSpan((int)(address - start), buffer.Length).CopyTo(buffer);
                return true;
            }
        return false;
    }

    public bool TryWrite(ulong address, ReadOnlySpan<byte> bytes)
    {
        foreach (var (start, data) in _segments)
            if (address >= start && address + (ulong)bytes.Length <= start + (ulong)data.Length)
            {
                bytes.CopyTo(data.AsSpan((int)(address - start)));
                Writes++;
                return true;
            }
        return false;
    }
}
