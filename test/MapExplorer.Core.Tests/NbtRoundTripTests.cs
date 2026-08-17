using MapExplorer.Core.Nbt.RoundTrip;

namespace MapExplorer.Core.Tests;

public class NbtRoundTripTests
{
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
}
