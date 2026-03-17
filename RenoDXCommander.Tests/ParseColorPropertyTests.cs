using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for UIFactory.ParseColor hex round-trip.
/// Feature: codebase-optimization, Property 2: ParseColor round-trip for valid hex strings
/// </summary>
public class ParseColorPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random (R, G, B) tuple as bytes.
    /// </summary>
    private static readonly Gen<(byte R, byte G, byte B)> GenRgb =
        from r in Gen.Choose(0, 255)
        from g in Gen.Choose(0, 255)
        from b in Gen.Choose(0, 255)
        select ((byte)r, (byte)g, (byte)b);

    /// <summary>
    /// Generates a random (A, R, G, B) tuple as bytes.
    /// </summary>
    private static readonly Gen<(byte A, byte R, byte G, byte B)> GenArgb =
        from a in Gen.Choose(0, 255)
        from r in Gen.Choose(0, 255)
        from g in Gen.Choose(0, 255)
        from b in Gen.Choose(0, 255)
        select ((byte)a, (byte)r, (byte)g, (byte)b);

    // ── Property 2a: 6-digit hex round-trip ───────────────────────────────────────
    // Feature: codebase-optimization, Property 2: ParseColor round-trip for valid hex strings
    // **Validates: Requirements 3.5**
    [Property(MaxTest = 100)]
    public Property ParseColor_RoundTrips_For6DigitHex()
    {
        return Prop.ForAll(
            Arb.From(GenRgb),
            ((byte R, byte G, byte B) rgb) =>
            {
                var hex = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
                var color = UIFactory.ParseColor(hex);

                return (color.A == 255 && color.R == rgb.R && color.G == rgb.G && color.B == rgb.B)
                    .Label($"Input: {hex} → A={color.A}, R={color.R}, G={color.G}, B={color.B} (expected A=255, R={rgb.R}, G={rgb.G}, B={rgb.B})");
            });
    }

    // ── Property 2b: 8-digit hex round-trip ───────────────────────────────────────
    // Feature: codebase-optimization, Property 2: ParseColor round-trip for valid hex strings
    // **Validates: Requirements 3.5**
    [Property(MaxTest = 100)]
    public Property ParseColor_RoundTrips_For8DigitHex()
    {
        return Prop.ForAll(
            Arb.From(GenArgb),
            ((byte A, byte R, byte G, byte B) argb) =>
            {
                var hex = $"#{argb.A:X2}{argb.R:X2}{argb.G:X2}{argb.B:X2}";
                var color = UIFactory.ParseColor(hex);

                return (color.A == argb.A && color.R == argb.R && color.G == argb.G && color.B == argb.B)
                    .Label($"Input: {hex} → A={color.A}, R={color.R}, G={color.G}, B={color.B} (expected A={argb.A}, R={argb.R}, G={argb.G}, B={argb.B})");
            });
    }
}
