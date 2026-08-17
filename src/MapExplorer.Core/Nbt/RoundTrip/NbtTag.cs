namespace MapExplorer.Core.Nbt.RoundTrip;

// Full-fidelity NBT tag model — every tag type is materialized (unlike
// MapExplorer.Core.Nbt.Nbt, which deliberately skips FLOAT/DOUBLE/BYTE_ARRAY/
// INT_ARRAY for chunk-decoding throughput). Player data files (level.dat,
// playerdata/*.dat) are tiny and need to be read, partially edited, and
// written back byte-for-byte-equivalent, so every field — including ones
// this app never looks at — has to round-trip losslessly.
public enum NbtTagType : byte
{
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12
}

public abstract class NbtTag
{
    public abstract NbtTagType TagType { get; }
}

public sealed class NbtByteTag(sbyte value) : NbtTag
{
    public sbyte Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.Byte;
}

public sealed class NbtShortTag(short value) : NbtTag
{
    public short Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.Short;
}

public sealed class NbtIntTag(int value) : NbtTag
{
    public int Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.Int;
}

public sealed class NbtLongTag(long value) : NbtTag
{
    public long Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.Long;
}

public sealed class NbtFloatTag(float value) : NbtTag
{
    public float Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.Float;
}

public sealed class NbtDoubleTag(double value) : NbtTag
{
    public double Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.Double;
}

public sealed class NbtByteArrayTag(byte[] value) : NbtTag
{
    public byte[] Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.ByteArray;
}

public sealed class NbtStringTag(string value) : NbtTag
{
    public string Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.String;
}

public sealed class NbtListTag(NbtTagType elementType, List<NbtTag> items) : NbtTag
{
    public NbtTagType ElementType { get; set; } = elementType;
    public List<NbtTag> Items { get; } = items;
    public override NbtTagType TagType => NbtTagType.List;
}

public sealed class NbtCompoundTag(Dictionary<string, NbtTag> fields) : NbtTag
{
    public Dictionary<string, NbtTag> Fields { get; } = fields;
    public override NbtTagType TagType => NbtTagType.Compound;

    public NbtTag? Get(string key) => Fields.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, NbtTag value) => Fields[key] = value;
}

public sealed class NbtIntArrayTag(int[] value) : NbtTag
{
    public int[] Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.IntArray;
}

public sealed class NbtLongArrayTag(long[] value) : NbtTag
{
    public long[] Value { get; set; } = value;
    public override NbtTagType TagType => NbtTagType.LongArray;
}
