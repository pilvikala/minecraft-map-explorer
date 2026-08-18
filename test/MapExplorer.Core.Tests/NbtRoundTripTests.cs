using System.IO.Compression;
using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Tests;

public class NbtRoundTripTests
{
    // The object model (NbtListTag/NbtIntArrayTag/...) can't itself represent a malformed
    // length or bad nesting, so the malicious-input tests below hand-assemble raw NBT bytes
    // instead of going through Write().
    private static void AppendUInt16(List<byte> buf, ushort v)
    {
        buf.Add((byte)(v >> 8));
        buf.Add((byte)v);
    }

    private static void AppendInt32(List<byte> buf, int v)
    {
        buf.Add((byte)(v >> 24));
        buf.Add((byte)(v >> 16));
        buf.Add((byte)(v >> 8));
        buf.Add((byte)v);
    }

    private static void AppendString(List<byte> buf, string s)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        AppendUInt16(buf, (ushort)bytes.Length);
        buf.AddRange(bytes);
    }


    [Fact]
    public void WriteThenParse_PreservesEveryTagType()
    {
        var fields = new Dictionary<string, NbtTag>
        {
            ["aByte"] = new NbtByteTag(-12),
            ["aShort"] = new NbtShortTag(1234),
            ["anInt"] = new NbtIntTag(-987654),
            ["aLong"] = new NbtLongTag(1234567890123L),
            ["aFloat"] = new NbtFloatTag(3.5f),
            ["aDouble"] = new NbtDoubleTag(12.375),
            ["aByteArray"] = new NbtByteArrayTag([1, 2, 255, 0]),
            ["aString"] = new NbtStringTag("hello, nbt"),
            ["aList"] = new NbtListTag(NbtTagType.Double, [new NbtDoubleTag(1.0), new NbtDoubleTag(2.0), new NbtDoubleTag(3.0)]),
            ["anIntArray"] = new NbtIntArrayTag([1, -2, 3]),
            ["aLongArray"] = new NbtLongArrayTag([100L, -200L]),
            ["nested"] = new NbtCompoundTag(new Dictionary<string, NbtTag>
            {
                ["inner"] = new NbtStringTag("value")
            })
        };
        var doc = new NbtDocument("", new NbtCompoundTag(fields));

        var bytes = NbtRoundTrip.Write(doc);
        var parsed = NbtRoundTrip.Parse(bytes);

        Assert.Equal("", parsed.RootName);
        Assert.Equal(-12, ((NbtByteTag)parsed.Root.Get("aByte")!).Value);
        Assert.Equal(1234, ((NbtShortTag)parsed.Root.Get("aShort")!).Value);
        Assert.Equal(-987654, ((NbtIntTag)parsed.Root.Get("anInt")!).Value);
        Assert.Equal(1234567890123L, ((NbtLongTag)parsed.Root.Get("aLong")!).Value);
        Assert.Equal(3.5f, ((NbtFloatTag)parsed.Root.Get("aFloat")!).Value);
        Assert.Equal(12.375, ((NbtDoubleTag)parsed.Root.Get("aDouble")!).Value);
        Assert.Equal(new byte[] { 1, 2, 255, 0 }, ((NbtByteArrayTag)parsed.Root.Get("aByteArray")!).Value);
        Assert.Equal("hello, nbt", ((NbtStringTag)parsed.Root.Get("aString")!).Value);

        var list = (NbtListTag)parsed.Root.Get("aList")!;
        Assert.Equal(NbtTagType.Double, list.ElementType);
        Assert.Equal([1.0, 2.0, 3.0], list.Items.Select(i => ((NbtDoubleTag)i).Value));

        Assert.Equal([1, -2, 3], ((NbtIntArrayTag)parsed.Root.Get("anIntArray")!).Value);
        Assert.Equal([100L, -200L], ((NbtLongArrayTag)parsed.Root.Get("aLongArray")!).Value);

        var nested = (NbtCompoundTag)parsed.Root.Get("nested")!;
        Assert.Equal("value", ((NbtStringTag)nested.Get("inner")!).Value);
    }

    [Fact]
    public void GZipFileRoundTrip_PreservesUnrelatedFieldsAfterAnEdit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nbt-roundtrip-{Guid.NewGuid():N}.dat");
        try
        {
            var root = new NbtCompoundTag(new Dictionary<string, NbtTag>
            {
                ["Health"] = new NbtFloatTag(20f),
                ["Untouched"] = new NbtStringTag("still here"),
                ["Pos"] = new NbtListTag(NbtTagType.Double, [new NbtDoubleTag(1.0), new NbtDoubleTag(2.0), new NbtDoubleTag(3.0)])
            });
            NbtRoundTrip.WriteGZipFile(path, new NbtDocument("", root));

            var doc = NbtRoundTrip.ReadGZipFile(path);
            doc.Root.Set("Health", new NbtFloatTag(10f));
            NbtRoundTrip.WriteGZipFile(path, doc);

            var reloaded = NbtRoundTrip.ReadGZipFile(path);
            Assert.Equal(10f, ((NbtFloatTag)reloaded.Root.Get("Health")!).Value);
            Assert.Equal("still here", ((NbtStringTag)reloaded.Root.Get("Untouched")!).Value);
            var pos = (NbtListTag)reloaded.Root.Get("Pos")!;
            Assert.Equal([1.0, 2.0, 3.0], pos.Items.Select(i => ((NbtDoubleTag)i).Value));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_RejectsListWithNegativeLength()
    {
        var buf = new List<byte>();
        buf.Add(0x0A); // root: TAG_Compound
        AppendString(buf, "");

        buf.Add(0x09); // field: TAG_List
        AppendString(buf, "list");
        buf.Add(0x03); // element type: TAG_Int
        AppendInt32(buf, -1); // length: -1 — must be rejected, not silently treated as empty

        buf.Add(0x00); // TAG_End for root compound

        Assert.Throws<InvalidOperationException>(() => NbtRoundTrip.Parse(buf.ToArray()));
    }

    [Fact]
    public void Parse_RejectsIntArrayLengthLargerThanRemainingData()
    {
        var buf = new List<byte>();
        buf.Add(0x0A);
        AppendString(buf, "");

        buf.Add(0x0B); // field: TAG_Int_Array
        AppendString(buf, "arr");
        AppendInt32(buf, 1_000_000); // claims 4 MB of ints but no data follows

        buf.Add(0x00);

        Assert.Throws<InvalidOperationException>(() => NbtRoundTrip.Parse(buf.ToArray()));
    }

    [Fact]
    public void Parse_RejectsExcessiveNestingDepth()
    {
        const int depth = 600; // past the 512 limit
        var buf = new List<byte>();
        buf.Add(0x0A); // root compound
        AppendString(buf, "");

        for (int i = 0; i < depth; i++)
        {
            buf.Add(0x0A); // nested TAG_Compound field
            AppendString(buf, "c");
        }
        for (int i = 0; i < depth + 1; i++) buf.Add(0x00); // close every nested compound + root

        Assert.Throws<InvalidOperationException>(() => NbtRoundTrip.Parse(buf.ToArray()));
    }

    [Fact]
    public void Write_RejectsStringLongerThanUshortMax()
    {
        var tooLong = new string('a', ushort.MaxValue + 1);
        var doc = new NbtDocument("", new NbtCompoundTag(new Dictionary<string, NbtTag>
        {
            ["s"] = new NbtStringTag(tooLong)
        }));

        Assert.Throws<InvalidOperationException>(() => NbtRoundTrip.Write(doc));
    }

    [Fact]
    public void ReadGZipFile_RejectsBombThatExceedsMaxDecompressedSize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nbt-bomb-{Guid.NewGuid():N}.dat");
        try
        {
            // All-zero payload past NbtRoundTrip's 64 MB decompression cap — compresses down
            // to a few KB, so the file on disk stays tiny while still exercising the guard
            // against expanding a small file into a huge one in memory.
            const int oversizedLength = 64 * 1024 * 1024 + 1024 * 1024;
            using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                var chunk = new byte[1024 * 1024];
                for (int written = 0; written < oversizedLength; written += chunk.Length)
                    gzip.Write(chunk, 0, chunk.Length);
            }

            Assert.Throws<InvalidOperationException>(() => NbtRoundTrip.ReadGZipFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
