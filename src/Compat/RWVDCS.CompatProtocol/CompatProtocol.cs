using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RWVDCS.CompatProtocol
{
    public static class CompatProtocolConstants
    {
        public const uint Magic = 0x43565752; // "RWVC" little-endian
        public const ushort MajorVersion = 1;
        public const ushort MinorVersion = 0;
        public const int HeaderSize = 36;
        public const int DefaultMaxPayloadBytes = 16 * 1024 * 1024;
        public const int DefaultMaxBatchItems = 50_000;
        public const int DefaultMaxStringBytes = 1024 * 1024;
        public const string DefaultRequestPipe = "RWVDCS.default.Realtime.Request.v1";
        public const string DefaultEventPipe = "RWVDCS.default.Realtime.Events.v1";
    }

    public enum CompatOperation : ushort
    {
        Hello = 1,
        Attach = 2,
        Detach = 3,
        Renew = 4,
        SubscribeBatch = 10,
        UnsubscribeBatch = 11,
        UnsubscribeAll = 12,
        ReadBatch = 20,
        ReadAll = 21,
        WriteBatch = 22,
        PollChanged = 23,
        SetDataInformType = 24,
        PauseSession = 25,
        Heartbeat = 30,

        DataChanged = 100,
        RuntimeChanging = 101,
        RuntimeRebound = 102,
        SubscriptionInvalidated = 103,
        Error = 255,
    }

    [Flags]
    public enum CompatFrameFlags : ushort
    {
        None = 0,
        Response = 1,
        Event = 2,
        Error = 4,
    }

    public enum CompatErrorCode : int
    {
        Ok = 0,
        InvalidRequest = 1,
        UnsupportedVersion = 2,
        MessageTooLarge = 3,
        SessionNotFound = 4,
        InvalidHandle = 5,
        NotFound = 6,
        NotWritable = 7,
        TypeMismatch = 8,
        ConversionFailed = 9,
        RuntimeUnavailable = 10,
        RuntimeChanging = 11,
        RuntimeGenerationMismatch = 12,
        Timeout = 13,
        Busy = 14,
        InternalError = 15,
    }

    public enum CompatValueKind : byte
    {
        Null = 0,
        Boolean = 1,
        Byte = 2,
        UInt16 = 3,
        UInt32 = 4,
        Int32 = 5,
        Int64 = 6,
        Single = 7,
        Double = 8,
        String = 9,
    }

    public sealed class CompatFrame
    {
        public CompatOperation Operation { get; set; }
        public CompatFrameFlags Flags { get; set; }
        public ulong RequestId { get; set; }
        public int SessionId { get; set; }
        public ulong RuntimeGeneration { get; set; }
        public byte[] Payload { get; set; } = new byte[0];
    }

    public struct CompatValue
    {
        public CompatValueKind Kind;
        public object Value;

        public CompatValue(CompatValueKind kind, object value)
        {
            Kind = kind;
            Value = value;
        }

        public static CompatValue FromObject(object value)
        {
            if (value == null)
                return new CompatValue(CompatValueKind.Null, null);
            if (value is bool) return new CompatValue(CompatValueKind.Boolean, value);
            if (value is byte) return new CompatValue(CompatValueKind.Byte, value);
            if (value is ushort) return new CompatValue(CompatValueKind.UInt16, value);
            if (value is uint) return new CompatValue(CompatValueKind.UInt32, value);
            if (value is int) return new CompatValue(CompatValueKind.Int32, value);
            if (value is long) return new CompatValue(CompatValueKind.Int64, value);
            if (value is float) return new CompatValue(CompatValueKind.Single, value);
            if (value is double) return new CompatValue(CompatValueKind.Double, value);
            if (value is string) return new CompatValue(CompatValueKind.String, value);
            if (value.GetType().IsEnum)
                return new CompatValue(CompatValueKind.Int32, Convert.ToInt32(value));
            return new CompatValue(CompatValueKind.String, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        public object ToObject()
        {
            return Kind == CompatValueKind.Null ? null : Value;
        }
    }

    public static class CompatFrameCodec
    {
        public static async Task WriteAsync(Stream stream, CompatFrame frame, CancellationToken cancellationToken)
        {
            byte[] payload = frame.Payload ?? new byte[0];
            if (payload.Length > CompatProtocolConstants.DefaultMaxPayloadBytes)
                throw new InvalidDataException("兼容协议负载超过上限：" + payload.Length);

            byte[] header;
            using (var ms = new MemoryStream(CompatProtocolConstants.HeaderSize))
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                writer.Write(CompatProtocolConstants.Magic);
                writer.Write(CompatProtocolConstants.MajorVersion);
                writer.Write(CompatProtocolConstants.MinorVersion);
                writer.Write((ushort)frame.Operation);
                writer.Write((ushort)frame.Flags);
                writer.Write(frame.RequestId);
                writer.Write(frame.SessionId);
                writer.Write(frame.RuntimeGeneration);
                writer.Write(payload.Length);
                writer.Flush();
                header = ms.ToArray();
            }

            await stream.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);
            if (payload.Length > 0)
                await stream.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public static async Task<CompatFrame> ReadAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] header = new byte[CompatProtocolConstants.HeaderSize];
            await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);

            uint magic;
            ushort major;
            ushort operation;
            ushort flags;
            ulong requestId;
            int sessionId;
            ulong generation;
            int payloadLength;
            using (var ms = new MemoryStream(header, false))
            using (var reader = new BinaryReader(ms, Encoding.UTF8, true))
            {
                magic = reader.ReadUInt32();
                major = reader.ReadUInt16();
                reader.ReadUInt16(); // minor
                operation = reader.ReadUInt16();
                flags = reader.ReadUInt16();
                requestId = reader.ReadUInt64();
                sessionId = reader.ReadInt32();
                generation = reader.ReadUInt64();
                payloadLength = reader.ReadInt32();
            }

            if (magic != CompatProtocolConstants.Magic)
                throw new InvalidDataException("兼容协议 Magic 不匹配");
            if (major != CompatProtocolConstants.MajorVersion)
                throw new InvalidDataException("不支持的兼容协议主版本：" + major);
            if (payloadLength < 0 || payloadLength > CompatProtocolConstants.DefaultMaxPayloadBytes)
                throw new InvalidDataException("兼容协议负载长度非法：" + payloadLength);

            var payload = new byte[payloadLength];
            if (payloadLength > 0)
                await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            return new CompatFrame
            {
                Operation = (CompatOperation)operation,
                Flags = (CompatFrameFlags)flags,
                RequestId = requestId,
                SessionId = sessionId,
                RuntimeGeneration = generation,
                Payload = payload,
            };
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    throw new EndOfStreamException("兼容协议连接已关闭");
                offset += read;
            }
        }
    }

    public static class CompatBinary
    {
        public static byte[] Build(Action<BinaryWriter> write)
            => Build(0, write);

        public static byte[] Build(int initialCapacity, Action<BinaryWriter> write)
        {
            if (initialCapacity < 0 || initialCapacity > CompatProtocolConstants.DefaultMaxPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            using (var ms = new MemoryStream(initialCapacity))
            using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                write(writer);
                writer.Flush();
                return ms.ToArray();
            }
        }

        public static BinaryReader Open(byte[] payload)
        {
            return new BinaryReader(new MemoryStream(payload ?? new byte[0], false), Encoding.UTF8, false);
        }

        public static void WriteString(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > CompatProtocolConstants.DefaultMaxStringBytes)
                throw new InvalidDataException("字符串超过兼容协议上限");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == -1)
                return null;
            if (length < 0 || length > CompatProtocolConstants.DefaultMaxStringBytes)
                throw new InvalidDataException("字符串长度非法：" + length);
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException("字符串负载不完整");
            return Encoding.UTF8.GetString(bytes);
        }

        public static void WriteStrings(BinaryWriter writer, string[] values)
        {
            values = values ?? new string[0];
            ValidateCount(values.Length);
            writer.Write(values.Length);
            byte[] buffer = new byte[256];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (value == null)
                {
                    writer.Write(-1);
                    continue;
                }
                int length = Encoding.UTF8.GetByteCount(value);
                if (length > CompatProtocolConstants.DefaultMaxStringBytes)
                    throw new InvalidDataException("字符串超过兼容协议上限");
                if (length > buffer.Length)
                    buffer = new byte[NextBufferSize(length)];
                writer.Write(length);
                if (length > 0)
                {
                    Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
                    writer.Write(buffer, 0, length);
                }
            }
        }

        public static string[] ReadStrings(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            ValidateCount(count);
            var result = new string[count];
            byte[] buffer = new byte[256];
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadInt32();
                if (length == -1)
                {
                    result[i] = null;
                    continue;
                }
                if (length < 0 || length > CompatProtocolConstants.DefaultMaxStringBytes)
                    throw new InvalidDataException("字符串长度非法：" + length);
                if (length > buffer.Length)
                    buffer = new byte[NextBufferSize(length)];
                int offset = 0;
                while (offset < length)
                {
                    int read = reader.BaseStream.Read(buffer, offset, length - offset);
                    if (read == 0)
                        throw new EndOfStreamException("字符串负载不完整");
                    offset += read;
                }
                result[i] = Encoding.UTF8.GetString(buffer, 0, length);
            }
            return result;
        }

        private static int NextBufferSize(int required)
        {
            int size = 256;
            while (size < required && size < CompatProtocolConstants.DefaultMaxStringBytes / 2)
                size *= 2;
            return Math.Max(size, required);
        }

        public static void WriteLongs(BinaryWriter writer, long[] values)
        {
            values = values ?? new long[0];
            ValidateCount(values.Length);
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
                writer.Write(values[i]);
        }

        public static long[] ReadLongs(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            ValidateCount(count);
            var result = new long[count];
            for (int i = 0; i < count; i++)
                result[i] = reader.ReadInt64();
            return result;
        }

        public static void WriteBooleans(BinaryWriter writer, bool[] values)
        {
            values = values ?? new bool[0];
            ValidateCount(values.Length);
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
                writer.Write(values[i]);
        }

        public static bool[] ReadBooleans(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            ValidateCount(count);
            var result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = reader.ReadBoolean();
            return result;
        }

        public static void WriteValues(BinaryWriter writer, CompatValue[] values)
        {
            values = values ?? new CompatValue[0];
            ValidateCount(values.Length);
            writer.Write(values.Length);
            for (int i = 0; i < values.Length; i++)
                WriteValue(writer, values[i]);
        }

        public static CompatValue[] ReadValues(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            ValidateCount(count);
            var result = new CompatValue[count];
            for (int i = 0; i < count; i++)
                result[i] = ReadValue(reader);
            return result;
        }

        public static void WriteValue(BinaryWriter writer, CompatValue value)
        {
            writer.Write((byte)value.Kind);
            switch (value.Kind)
            {
                case CompatValueKind.Null: return;
                case CompatValueKind.Boolean: writer.Write(Convert.ToBoolean(value.Value)); return;
                case CompatValueKind.Byte: writer.Write(Convert.ToByte(value.Value)); return;
                case CompatValueKind.UInt16: writer.Write(Convert.ToUInt16(value.Value)); return;
                case CompatValueKind.UInt32: writer.Write(Convert.ToUInt32(value.Value)); return;
                case CompatValueKind.Int32: writer.Write(Convert.ToInt32(value.Value)); return;
                case CompatValueKind.Int64: writer.Write(Convert.ToInt64(value.Value)); return;
                case CompatValueKind.Single: writer.Write(Convert.ToSingle(value.Value)); return;
                case CompatValueKind.Double: writer.Write(Convert.ToDouble(value.Value)); return;
                case CompatValueKind.String: WriteString(writer, Convert.ToString(value.Value)); return;
                default: throw new InvalidDataException("未知值类型：" + value.Kind);
            }
        }

        public static CompatValue ReadValue(BinaryReader reader)
        {
            var kind = (CompatValueKind)reader.ReadByte();
            switch (kind)
            {
                case CompatValueKind.Null: return new CompatValue(kind, null);
                case CompatValueKind.Boolean: return new CompatValue(kind, reader.ReadBoolean());
                case CompatValueKind.Byte: return new CompatValue(kind, reader.ReadByte());
                case CompatValueKind.UInt16: return new CompatValue(kind, reader.ReadUInt16());
                case CompatValueKind.UInt32: return new CompatValue(kind, reader.ReadUInt32());
                case CompatValueKind.Int32: return new CompatValue(kind, reader.ReadInt32());
                case CompatValueKind.Int64: return new CompatValue(kind, reader.ReadInt64());
                case CompatValueKind.Single: return new CompatValue(kind, reader.ReadSingle());
                case CompatValueKind.Double: return new CompatValue(kind, reader.ReadDouble());
                case CompatValueKind.String: return new CompatValue(kind, ReadString(reader));
                default: throw new InvalidDataException("未知值类型：" + kind);
            }
        }

        public static void ValidateCount(int count)
        {
            if (count < 0 || count > CompatProtocolConstants.DefaultMaxBatchItems)
                throw new InvalidDataException("批量项数量非法：" + count);
        }
    }
}
