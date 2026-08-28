using RWVDCS.Blocks.RW;
using RWVDCS.Core.Blocks;

namespace RWVDCS.Core.Tests.Blocks;

public sealed class BlockStateCodecSafetyTests
{
    [Fact]
    public void Every_block_codec_stays_inside_its_declared_schema_range()
    {
        const int guardLength = 32;
        const byte guardValue = 0xA5;

        foreach (Type blockType in GetConcreteBlockTypes())
        {
            var block = (Function)Activator.CreateInstance(blockType)!;
            BlockStateCodec codec = BlockStateCodec.For(blockType);
            int stateLength = codec.Schema.ByteLength;
            var buffer = Enumerable.Repeat(guardValue, guardLength + stateLength + guardLength).ToArray();

            codec.Flush(block, buffer, guardLength);

            Assert.True(buffer.AsSpan(0, guardLength).SequenceEqual(
                Enumerable.Repeat(guardValue, guardLength).ToArray()),
                $"{blockType.FullName} Flush 改写了状态区前哨兵");
            Assert.True(buffer.AsSpan(guardLength + stateLength, guardLength).SequenceEqual(
                Enumerable.Repeat(guardValue, guardLength).ToArray()),
                $"{blockType.FullName} Flush 改写了状态区后哨兵");

            codec.Load(block, buffer, guardLength);
        }
    }

    [Fact]
    public void Every_block_codec_rejects_a_buffer_one_byte_short()
    {
        foreach (Type blockType in GetConcreteBlockTypes())
        {
            var block = (Function)Activator.CreateInstance(blockType)!;
            BlockStateCodec codec = BlockStateCodec.For(blockType);
            int stateLength = codec.Schema.ByteLength;
            if (stateLength == 0)
                continue;

            var shortBuffer = new byte[stateLength - 1];
            Assert.ThrowsAny<ArgumentException>(() => codec.Flush(block, shortBuffer, 0));
        }
    }

    private static Type[] GetConcreteBlockTypes()
        => typeof(DEVICE).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Function).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
}
