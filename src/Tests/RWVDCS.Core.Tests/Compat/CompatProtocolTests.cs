using RWVDCS.CompatProtocol;

namespace RWVDCS.Core.Tests.Compat;

public class CompatProtocolTests
{
    [Fact]
    public async Task Frame_roundtrip_preserves_header_and_payload()
    {
        var source = new CompatFrame
        {
            Operation = CompatOperation.WriteBatch,
            Flags = CompatFrameFlags.Response,
            RequestId = 123456789,
            SessionId = 42,
            RuntimeGeneration = 7,
            Payload = [1, 2, 3, 4, 5],
        };
        using var stream = new MemoryStream();

        await CompatFrameCodec.WriteAsync(stream, source, CancellationToken.None);
        stream.Position = 0;
        CompatFrame actual = await CompatFrameCodec.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(source.Operation, actual.Operation);
        Assert.Equal(source.Flags, actual.Flags);
        Assert.Equal(source.RequestId, actual.RequestId);
        Assert.Equal(source.SessionId, actual.SessionId);
        Assert.Equal(source.RuntimeGeneration, actual.RuntimeGeneration);
        Assert.Equal(source.Payload, actual.Payload);
    }

    [Fact]
    public void Values_roundtrip_preserves_legacy_boxing_types_and_float_bits()
    {
        var source = new[]
        {
            CompatValue.FromObject(null),
            CompatValue.FromObject(true),
            CompatValue.FromObject((byte)7),
            CompatValue.FromObject((ushort)65530),
            CompatValue.FromObject(4_000_000_000u),
            CompatValue.FromObject(-123),
            CompatValue.FromObject(1234567890123L),
            CompatValue.FromObject(-0.0f),
            CompatValue.FromObject(double.NaN),
            CompatValue.FromObject("中文/兼容"),
        };
        byte[] payload = CompatBinary.Build(w => CompatBinary.WriteValues(w, source));

        CompatValue[] actual;
        using (var reader = CompatBinary.Open(payload))
            actual = CompatBinary.ReadValues(reader);

        Assert.Null(actual[0].ToObject());
        Assert.IsType<bool>(actual[1].ToObject());
        Assert.IsType<byte>(actual[2].ToObject());
        Assert.IsType<ushort>(actual[3].ToObject());
        Assert.IsType<uint>(actual[4].ToObject());
        Assert.IsType<int>(actual[5].ToObject());
        Assert.IsType<long>(actual[6].ToObject());
        Assert.IsType<float>(actual[7].ToObject());
        Assert.Equal(BitConverter.SingleToInt32Bits(-0.0f),
            BitConverter.SingleToInt32Bits((float)actual[7].ToObject()!));
        Assert.True(double.IsNaN((double)actual[8].ToObject()!));
        Assert.Equal("中文/兼容", actual[9].ToObject());
    }

    [Fact]
    public void Reader_rejects_batch_count_over_limit_before_allocating()
    {
        byte[] payload = CompatBinary.Build(w => w.Write(CompatProtocolConstants.DefaultMaxBatchItems + 1));
        using var reader = CompatBinary.Open(payload);

        Assert.Throws<InvalidDataException>(() => CompatBinary.ReadLongs(reader));
    }

    [Fact]
    public void String_array_roundtrip_preserves_null_empty_unicode_and_long_values()
    {
        string[] source = [null!, "", "DPU1001_STATE@LCL", "中文测点", new string('x', 4097)];
        byte[] payload = CompatBinary.Build(8192, w => CompatBinary.WriteStrings(w, source));

        using var reader = CompatBinary.Open(payload);
        string[] actual = CompatBinary.ReadStrings(reader);

        Assert.Equal(source, actual);
    }

    [Fact]
    public async Task Frame_reader_rejects_invalid_magic()
    {
        byte[] header = new byte[CompatProtocolConstants.HeaderSize];
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CompatFrameCodec.ReadAsync(stream, CancellationToken.None));
    }
}
