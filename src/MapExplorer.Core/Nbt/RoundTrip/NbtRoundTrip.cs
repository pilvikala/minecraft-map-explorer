using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MapExplorer.Core.Nbt.RoundTrip;

/// <summary>A parsed NBT file: the (usually empty) root tag name plus its compound payload.</summary>
public sealed record NbtDocument(string RootName, NbtCompoundTag Root);

// Full read+write NBT codec for gzip'd single-file NBT documents (level.dat,
// playerdata/*.dat) — as opposed to MapExplorer.Core.Nbt.Nbt, which only
// reads (and only partially materializes) the anonymous-compound NBT embedded
// in region-file chunks.
public static class NbtRoundTrip
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

    // Generous headroom over real player/level.dat sizes (a few KB, rarely more than a
    // couple MB for a bloated inventory) — guards against a crafted gzip bomb expanding to
    // gigabytes in memory.
    private const int MaxDecompressedBytes = 64 * 1024 * 1024;

    // NBT itself has no nesting limit, but a real level.dat/playerdata file never nests
    // anywhere near this deep — this exists purely to fail fast on a maliciously crafted
    // file instead of blowing the call stack.
    private const int MaxNestingDepth = 512;

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;

        public int Remaining => _data.Length - _offset;

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

        public float Single()
        {
            var v = BinaryPrimitives.ReadSingleBigEndian(_data.Slice(_offset, 4));
            _offset += 4;
            return v;
        }

        public double Double()
        {
            var v = BinaryPrimitives.ReadDoubleBigEndian(_data.Slice(_offset, 8));
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

        public byte[] ReadBytes(int n)
        {
            var b = _data.Slice(_offset, n).ToArray();
            _offset += n;
            return b;
        }
    }

    private static NbtTag ReadPayload(ref Reader r, byte type, int depth = 0)
    {
        if (depth > MaxNestingDepth)
            throw new InvalidOperationException($"NBT nesting exceeds maximum depth of {MaxNestingDepth}");

        switch (type)
        {
            case TagByte: return new NbtByteTag(r.SByte());
            case TagShort: return new NbtShortTag(r.Int16());
            case TagInt: return new NbtIntTag(r.Int32());
            case TagLong: return new NbtLongTag(r.Int64());
            case TagFloat: return new NbtFloatTag(r.Single());
            case TagDouble: return new NbtDoubleTag(r.Double());
            case TagByteArray:
            {
                int len = r.Int32();
                if (len < 0 || len > r.Remaining)
                    throw new InvalidOperationException("NBT byte array length exceeds available data");
                return new NbtByteArrayTag(r.ReadBytes(len));
            }
            case TagString: return new NbtStringTag(r.ReadString());
            case TagList:
            {
                byte elemType = r.Byte();
                int len = r.Int32();
                // Every element takes at least one byte, so a length claiming more elements
                // than there are bytes left can only be a corrupt/malicious file — reject it
                // before pre-sizing the list rather than attempting a huge allocation.
                if (len < 0 || len > r.Remaining)
                    throw new InvalidOperationException("NBT list length exceeds available data");
                var items = new List<NbtTag>(len);
                for (int i = 0; i < len; i++) items.Add(ReadPayload(ref r, elemType, depth + 1));
                return new NbtListTag((NbtTagType)elemType, items);
            }
            case TagCompound:
            {
                var fields = new Dictionary<string, NbtTag>();
                while (true)
                {
                    byte tagType = r.Byte();
                    if (tagType == TagEnd) break;
                    string name = r.ReadString();
                    fields[name] = ReadPayload(ref r, tagType, depth + 1);
                }
                return new NbtCompoundTag(fields);
            }
            case TagIntArray:
            {
                int len = r.Int32();
                if (len < 0 || len > r.Remaining / sizeof(int))
                    throw new InvalidOperationException("NBT int array length exceeds available data");
                var arr = new int[len];
                for (int i = 0; i < len; i++) arr[i] = r.Int32();
                return new NbtIntArrayTag(arr);
            }
            case TagLongArray:
            {
                int len = r.Int32();
                if (len < 0 || len > r.Remaining / sizeof(long))
                    throw new InvalidOperationException("NBT long array length exceeds available data");
                var arr = new long[len];
                for (int i = 0; i < len; i++) arr[i] = r.Int64();
                return new NbtLongArrayTag(arr);
            }
            default:
                throw new InvalidOperationException($"Unknown NBT tag type: {type}");
        }
    }

    public static NbtDocument Parse(ReadOnlySpan<byte> data)
    {
        var r = new Reader(data);
        byte rootType = r.Byte();
        string rootName = r.ReadString();
        var root = (NbtCompoundTag)ReadPayload(ref r, rootType);
        return new NbtDocument(rootName, root);
    }

    private sealed class Writer
    {
        private readonly MemoryStream _stream = new();
        private readonly byte[] _buf = new byte[8];

        public void WriteByte(byte b) => _stream.WriteByte(b);
        public void WriteSByte(sbyte b) => _stream.WriteByte(unchecked((byte)b));

        public void WriteInt16(short v)
        {
            BinaryPrimitives.WriteInt16BigEndian(_buf, v);
            _stream.Write(_buf, 0, 2);
        }

        public void WriteInt32(int v)
        {
            BinaryPrimitives.WriteInt32BigEndian(_buf, v);
            _stream.Write(_buf, 0, 4);
        }

        public void WriteInt64(long v)
        {
            BinaryPrimitives.WriteInt64BigEndian(_buf, v);
            _stream.Write(_buf, 0, 8);
        }

        public void WriteSingle(float v)
        {
            BinaryPrimitives.WriteSingleBigEndian(_buf, v);
            _stream.Write(_buf, 0, 4);
        }

        public void WriteDouble(double v)
        {
            BinaryPrimitives.WriteDoubleBigEndian(_buf, v);
            _stream.Write(_buf, 0, 8);
        }

        public void WriteString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            // The NBT string length prefix is an unsigned 16-bit field — anything longer
            // can't round-trip and must fail loudly rather than silently wrap.
            if (bytes.Length > ushort.MaxValue)
                throw new InvalidOperationException($"NBT string is too long to encode ({bytes.Length} bytes, max {ushort.MaxValue})");
            WriteInt16(unchecked((short)(ushort)bytes.Length));
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void WriteRawBytes(byte[] bytes) => _stream.Write(bytes, 0, bytes.Length);

        public byte[] ToArray() => _stream.ToArray();
    }

    private static void WritePayload(Writer w, NbtTag tag)
    {
        switch (tag)
        {
            case NbtByteTag t: w.WriteSByte(t.Value); return;
            case NbtShortTag t: w.WriteInt16(t.Value); return;
            case NbtIntTag t: w.WriteInt32(t.Value); return;
            case NbtLongTag t: w.WriteInt64(t.Value); return;
            case NbtFloatTag t: w.WriteSingle(t.Value); return;
            case NbtDoubleTag t: w.WriteDouble(t.Value); return;
            case NbtByteArrayTag t:
                w.WriteInt32(t.Value.Length);
                w.WriteRawBytes(t.Value);
                return;
            case NbtStringTag t: w.WriteString(t.Value); return;
            case NbtListTag t:
                w.WriteByte((byte)t.ElementType);
                w.WriteInt32(t.Items.Count);
                foreach (var item in t.Items) WritePayload(w, item);
                return;
            case NbtCompoundTag t:
                foreach (var (key, value) in t.Fields)
                {
                    w.WriteByte((byte)value.TagType);
                    w.WriteString(key);
                    WritePayload(w, value);
                }
                w.WriteByte(TagEnd);
                return;
            case NbtIntArrayTag t:
                w.WriteInt32(t.Value.Length);
                foreach (var v in t.Value) w.WriteInt32(v);
                return;
            case NbtLongArrayTag t:
                w.WriteInt32(t.Value.Length);
                foreach (var v in t.Value) w.WriteInt64(v);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NBT tag for writing: {tag.GetType()}");
        }
    }

    public static byte[] Write(NbtDocument doc)
    {
        var w = new Writer();
        w.WriteByte((byte)NbtTagType.Compound);
        w.WriteString(doc.RootName);
        WritePayload(w, doc.Root);
        return w.ToArray();
    }

    public static NbtDocument ReadGZipFile(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        var buffer = new byte[81920];
        int read;
        long total = 0;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxDecompressedBytes)
                throw new InvalidOperationException($"NBT file exceeds maximum allowed decompressed size of {MaxDecompressedBytes} bytes");
            output.Write(buffer, 0, read);
        }

        return Parse(output.ToArray());
    }

    /// <summary>
    /// Writes via a temp file + atomic rename so a crash or power loss mid-write can never
    /// leave a half-written save file behind — these are irreplaceable player saves, not
    /// disposable build output.
    /// </summary>
    public static void WriteGZipFile(string path, NbtDocument doc)
    {
        var raw = Write(doc);
        var tmpPath = path + ".tmp";
        using (var output = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
        {
            using var gzip = new GZipStream(output, CompressionLevel.Optimal);
            gzip.Write(raw, 0, raw.Length);
        }
        File.Move(tmpPath, path, overwrite: true);
    }
}
