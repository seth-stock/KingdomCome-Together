// SPDX-License-Identifier: GPL-3.0-only
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>One participant artifact, bounded and tied to one authority incarnation/checkpoint.
/// Exact duplicate chunks are harmless. Conflicting metadata or data aborts.</summary>
public sealed class CheckpointAssembly(Guid epoch, Guid checkpoint)
{
    private readonly Dictionary<ushort, byte[]> _parts = new();
    private ushort _count;
    public byte[]? Add(CheckpointPacket packet)
    {
        packet.Encode();
        if (packet.Epoch != epoch || packet.Checkpoint != checkpoint) throw new InvalidDataException("Stale checkpoint chunk.");
        if (_count != 0 && packet.Count != _count) throw new InvalidDataException("Chunk count changed.");
        _count = packet.Count;
        if (_parts.TryGetValue(packet.Index, out var old) && !old.AsSpan().SequenceEqual(packet.Data))
            throw new InvalidDataException("Conflicting checkpoint chunk.");
        if (packet.Index + 1 < packet.Count && packet.Data.Length != CheckpointPacket.ChunkBytes)
            throw new InvalidDataException("Short non-final chunk.");
        _parts[packet.Index] = packet.Data.ToArray();
        if (_parts.Count != _count) return null;
        using var stream = new MemoryStream();
        for (ushort i = 0; i < _count; ++i) stream.Write(_parts[i]);
        return stream.ToArray();
    }
}
