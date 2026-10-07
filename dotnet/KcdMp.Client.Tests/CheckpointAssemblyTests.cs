using KcdMp.Wire;

public sealed class CheckpointAssemblyTests
{
    private static readonly Guid Epoch = Guid.NewGuid(), Id = Guid.NewGuid();
    private static CheckpointPacket Packet(ushort index, ushort count, byte[] data) => new(2, Epoch, Id, index, count, data);
    [Fact] public void ReorderedAndRepeatedChunksProduceExactlyOneUnchangedArtifact()
    {
        var assembly = new CheckpointAssembly(Epoch, Id);
        var first = Enumerable.Repeat((byte)17, CheckpointPacket.ChunkBytes).ToArray();
        var last = new byte[] { 1, 2, 3 };
        Assert.Null(assembly.Add(Packet(1, 2, last)));
        Assert.Null(assembly.Add(Packet(1, 2, last)));
        Assert.Equal(first.Concat(last), assembly.Add(Packet(0, 2, first))!);
        first[0] = 99;
        Assert.Equal((byte)17, assembly.Add(Packet(1, 2, last))![0]);
    }
    [Fact] public void ConflictingStaleAndUnboundedChunksAreRejected()
    {
        var assembly = new CheckpointAssembly(Epoch, Id);
        assembly.Add(Packet(1, 2, new byte[] { 1 }));
        Assert.Throws<InvalidDataException>(() => assembly.Add(Packet(1, 2, new byte[] { 2 })));
        Assert.Throws<InvalidDataException>(() => assembly.Add(Packet(1, 3, new byte[] { 1 })));
        Assert.Throws<InvalidDataException>(() => assembly.Add(Packet(0, 2, new byte[] { 1 })));
        Assert.Throws<InvalidDataException>(() => assembly.Add(Packet(1, 2, new byte[] { 1 }) with { Epoch = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => assembly.Add(Packet(1, 2, new byte[] { 1 }) with { Checkpoint = Guid.NewGuid() }));
        Assert.Throws<ArgumentException>(() => Packet(0, 2049, new byte[] { 1 }).Encode());
    }
    [Fact] public void WirePreservesScopesAndRejectsEmptyTruncatedOrOversizedPackets()
    {
        var packet = Packet(0, 1, new byte[] { 7, 8 });
        var decoded = CheckpointPacket.Decode(packet.Encode());
        Assert.Equal(Epoch, decoded.Epoch); Assert.Equal(Id, decoded.Checkpoint); Assert.Equal(packet.Data, decoded.Data);
        Assert.Throws<InvalidDataException>(() => CheckpointPacket.Decode(new byte[36]));
        Assert.Throws<ArgumentException>(() => Packet(0, 1, new byte[4097]).Encode());
        Assert.Throws<ArgumentException>(() => (Packet(0, 1, new byte[] { 1 }) with { Epoch = Guid.Empty }).Encode());
    }
}
