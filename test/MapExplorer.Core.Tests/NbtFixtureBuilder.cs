using System.Buffers.Binary;
using System.Text;

namespace MapExplorer.Core.Tests;

// Independent test-only NBT encoder + packed-long bit-packer — deliberately
// NOT sharing any code with MapExplorer.Core.Nbt.Nbt or ChunkDecoder's
// ReadPackedEntry, so a bug in one is unlikely to be masked by a matching bug
// in the other. Mirrors the Electron app's src/test/nbt-fixtures.ts, which
// used the same independence strategy in TypeScript.
internal static class NbtFixtureBuilder
{
    private const byte TagInt = 3;
    private const byte TagString = 8;
    private const byte TagList = 9;
    private const byte TagCompound = 10;
    private const byte TagLongArray = 12;

    private abstract record NbtWriteValue;
    private sealed record NInt(int Value) : NbtWriteValue;
    private sealed record NString(string Value) : NbtWriteValue;
    private sealed record NLongArray(long[] Value) : NbtWriteValue;
    private sealed record NList(byte ElemType, List<NbtWriteValue> Items) : NbtWriteValue;
    private sealed record NCompound(Dictionary<string, NbtWriteValue> Fields) : NbtWriteValue;

    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public void Byte(byte b) => _bytes.Add(b);

        public void Int16(short v)
        {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buf, v);
            _bytes.AddRange(buf.ToArray());
        }

        public void Int32(int v)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, v);
            _bytes.AddRange(buf.ToArray());
        }

        public void Int64(long v)
        {
            Span<byte> buf = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buf, v);
            _bytes.AddRange(buf.ToArray());
        }

        public void Str(string s)
        {
            var utf8 = Encoding.UTF8.GetBytes(s);
            Int16((short)utf8.Length);
            _bytes.AddRange(utf8);
        }

        public byte[] ToArray() => [.. _bytes];
    }

    private static byte TagIdOf(NbtWriteValue val) => val switch
    {
        NInt => TagInt,
        NString => TagString,
        NLongArray => TagLongArray,
        NList => TagList,
        NCompound => TagCompound,
        _ => throw new InvalidOperationException()
    };

    private static void WritePayload(Writer w, NbtWriteValue val)
    {
        switch (val)
        {
            case NInt v: w.Int32(v.Value); return;
            case NString v: w.Str(v.Value); return;
            case NLongArray v:
                w.Int32(v.Value.Length);
                foreach (var l in v.Value) w.Int64(l);
                return;
            case NList v:
                w.Byte(v.ElemType);
                w.Int32(v.Items.Count);
                foreach (var item in v.Items) WritePayload(w, item);
                return;
            case NCompound v:
                foreach (var (key, item) in v.Fields)
                {
                    w.Byte(TagIdOf(item));
                    w.Str(key);
                    WritePayload(w, item);
                }
                w.Byte(0); // TAG_End
                return;
        }
    }

    private static byte[] EncodeRoot(string rootName, Dictionary<string, NbtWriteValue> compound)
    {
        var w = new Writer();
        w.Byte(TagCompound);
        w.Str(rootName);
        WritePayload(w, new NCompound(compound));
        return w.ToArray();
    }

    /// <summary>Independent reference implementation of Minecraft's packed-long bit layout, using plain loops + shifts (no BigInt equivalent needed in C#, but still kept separate from ChunkDecoder's ReadPackedEntry).</summary>
    public static long[] PackLongArray(int[] values, int bitsPerEntry)
    {
        int valuesPerLong = 64 / bitsPerEntry;
        int longCount = (int)Math.Ceiling(values.Length / (double)valuesPerLong);
        var longs = new long[longCount];
        for (int i = 0; i < values.Length; i++)
        {
            int longIdx = i / valuesPerLong;
            int bitIdx = (i % valuesPerLong) * bitsPerEntry;
            longs[longIdx] |= (long)values[i] << bitIdx;
        }
        return longs;
    }

    public static int BitsFor(int paletteLength, int minBits) =>
        Math.Max(minBits, (int)Math.Ceiling(Math.Log2(paletteLength)));

    public sealed record SectionFixture(
        int Y,
        List<string> BlockPalette,
        int[]? BlockIndices = null, // 4096 entries, i = by*256 + bz*16 + bx
        List<string>? BiomePalette = null,
        int[]? BiomeIndices = null); // 64 entries, i = by*16 + bz*4 + bx

    private static Dictionary<string, NbtWriteValue> BuildSectionCompound(SectionFixture section)
    {
        var result = new Dictionary<string, NbtWriteValue> { ["Y"] = new NInt(section.Y) };

        var blockStates = new Dictionary<string, NbtWriteValue>
        {
            ["palette"] = new NList(TagCompound, section.BlockPalette
                .Select(name => (NbtWriteValue)new NCompound(new Dictionary<string, NbtWriteValue> { ["Name"] = new NString(name) }))
                .ToList())
        };
        if (section.BlockPalette.Count > 1)
        {
            if (section.BlockIndices is not { Length: 4096 })
                throw new ArgumentException("BlockIndices (4096 entries) required when BlockPalette.Count > 1");
            int bits = BitsFor(section.BlockPalette.Count, 4);
            blockStates["data"] = new NLongArray(PackLongArray(section.BlockIndices, bits));
        }
        result["block_states"] = new NCompound(blockStates);

        if (section.BiomePalette is not null)
        {
            var biomes = new Dictionary<string, NbtWriteValue>
            {
                ["palette"] = new NList(TagString, section.BiomePalette.Select(name => (NbtWriteValue)new NString(name)).ToList())
            };
            if (section.BiomePalette.Count > 1)
            {
                if (section.BiomeIndices is not { Length: 64 })
                    throw new ArgumentException("BiomeIndices (64 entries) required when BiomePalette.Count > 1");
                int bits = BitsFor(section.BiomePalette.Count, 1);
                biomes["data"] = new NLongArray(PackLongArray(section.BiomeIndices, bits));
            }
            result["biomes"] = new NCompound(biomes);
        }

        return result;
    }

    public static byte[] BuildChunkNbt(List<SectionFixture> sections) =>
        EncodeRoot("", new Dictionary<string, NbtWriteValue>
        {
            ["sections"] = new NList(TagCompound, sections.Select(s => (NbtWriteValue)new NCompound(BuildSectionCompound(s))).ToList())
        });
}
