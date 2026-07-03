namespace MapExplorer.Rendering;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>
    /// Rounds and clamps a color-channel computation to [0,255] before narrowing
    /// to byte. Several color formulas here (surface shading, ore-overlay blend,
    /// the height gradient) can legitimately compute outside that range — e.g.
    /// snow's (240,245,255) times a >1.0 shade factor exceeds 255. The original
    /// TS app got this for free because values flowed into a Uint8ClampedArray,
    /// which clamps automatically; a bare `(byte)` cast in C# wraps instead
    /// (270 becomes 14), which is what made snow/ice render near-black.
    /// </summary>
    public static byte ClampByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
