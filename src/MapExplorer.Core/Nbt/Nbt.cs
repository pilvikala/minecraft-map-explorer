using System.Buffers.Binary;
using System.Text;

namespace MapExplorer.Core.Nbt;

// Minimal NBT parser for Java Edition 1.18+ chunk data. Ported from the
// Electron app's TS decoder (src/main/nbt.ts) — same tag set, same big-endian
// layout. Unlike the JS version, C# has real 64-bit integers, so packed-long
// arrays are just `long[]` with ordinary shifts — no need for the
// BigInt-avoidance split into hi/lo 32-bit halves the JS port needed.
public abstract class NbtValue
{
}

public sealed class NbtInt(int value) : NbtValue
{
    public int Value { get; } = value;
}

public sealed class NbtLong(long value) : NbtValue
{
    public long Value { get; } = value;
}

public sealed class NbtString(string value) : NbtValue
{
    public string Value { get; } = value;
}

public sealed class NbtLongArray(long[] value) : NbtValue
{
    public long[] Value { get; } = value;
}

public sealed class NbtList(List<NbtValue> items) : NbtValue
{
    public List<NbtValue> Items { get; } = items;
}

public sealed class NbtCompound(Dictionary<string, NbtValue> fields) : NbtValue
{
    public Dictionary<string, NbtValue> Fields { get; } = fields;

    public NbtValue? Get(string key) => Fields.TryGetValue(key, out var v) ? v : null;
}

// Marker for tag types the chunk decoder never needs (FLOAT/DOUBLE/BYTE_ARRAY/
// INT_ARRAY) — bytes are skipped to keep the reader position correct, but the
// value itself is deliberately not materialized. A distinct type (rather than
// e.g. NbtInt(0)) means a future consumer that tries to read one of these
// fields gets a clear invalid-cast failure instead of a silently wrong zero.
public sealed class NbtSkipped : NbtValue
{
    public static readonly NbtSkipped Instance = new();
}

public static class Nbt
{
    private const byte TagEnd = 0;
    private const byte TagByte = 1;
    private const byte TagShort = 2;
    private const byte TagInt = 3;
    private const byte TagLong = 4;
    private const byte TagFloat = 5;
    private const byte TagDouble = 6;
    private const byte TagByteArray = 7;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagIntArray = 11;
    private const byte TagLongArray = 12;

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        public byte Byte() => _data[_offset++];

        public sbyte SByte() => unchecked((sbyte)_data[_offset++]);

        public short Int16()
        {
            var v = BinaryPrimitives.ReadInt16BigEndian(_data.Slice(_offset, 2));
            _offset += 2;
            return v;
        }

        public int Int32()
        {
            var v = BinaryPrimitives.ReadInt32BigEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public long Int64()
        {
            var v = BinaryPrimitives.ReadInt64BigEndian(_data.Slice(_offset, 8));
            _offset += 8;
            return v;
        }

        public string ReadString()
        {
            int len = (ushort)Int16();
            var s = Encoding.UTF8.GetString(_data.Slice(_offset, len));
            _offset += len;
            return s;
        }

        public void Skip(int n) => _offset += n;
    }

    private static NbtValue ReadPayload(ref Reader r, byte type)
    {
        switch (type)
        {
            case TagByte: return new NbtInt(r.SByte());
            case TagShort: return new NbtInt(r.Int16());
            case TagInt: return new NbtInt(r.Int32());
            case TagLong: return new NbtLong(r.Int64());
            case TagFloat: r.Skip(4); return NbtSkipped.Instance;
            case TagDouble: r.Skip(8); return NbtSkipped.Instance;
            case TagByteArray:
            {
                int len = r.Int32();
                r.Skip(len);
                return NbtSkipped.Instance;
            }
            case TagString: return new NbtString(r.ReadString());
            case TagList:
            {
                byte elemType = r.Byte();
                int len = r.Int32();
                var items = new List<NbtValue>(Math.Max(0, len));
                for (int i = 0; i < len; i++) items.Add(ReadPayload(ref r, elemType));
                return new NbtList(items);
            }
            case TagCompound:
            {
                var fields = new Dictionary<string, NbtValue>();
                while (true)
                {
                    byte tagType = r.Byte();
                    if (tagType == TagEnd) break;
                    string name = r.ReadString();
                    fields[name] = ReadPayload(ref r, tagType);
                }
                return new NbtCompound(fields);
            }
            case TagIntArray:
            {
                int len = r.Int32();
                r.Skip(len * 4);
                return NbtSkipped.Instance;
            }
            case TagLongArray:
            {
                int len = r.Int32();
                var arr = new long[len];
                for (int i = 0; i < len; i++) arr[i] = r.Int64();
                return new NbtLongArray(arr);
            }
            default:
                throw new InvalidOperationException($"Unknown NBT tag type: {type}");
        }
    }

    public static NbtCompound Parse(ReadOnlySpan<byte> data)
    {
        var r = new Reader(data);
        byte rootType = r.Byte();
        r.ReadString(); // root name, usually empty
        return (NbtCompound)ReadPayload(ref r, rootType);
    }
}
