// VrrCaps.cs — VRR frame rate cap presets computed the way ReLimiter does, so RHI's dropdowns
// and ReLimiter's own "VRR Cap" button pick the same number.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RenoDXCommander.Services;

public static class VrrCaps
{
    /// <summary>Refresh rates offered in the VRR preset lists.</summary>
    private static readonly int[] NominalHz = { 60, 75, 100, 120, 144, 165, 180, 200, 240, 280, 300, 360, 480, 500 };

    /// <summary>
    /// ReLimiter's Reflex VRR cap: hz - hz²/3600, rounded down (src/osd.cpp). ReLimiter uses the display's exact
    /// rate, so a "240 Hz" mode reported as 239.96 gives 223, not 224.
    /// </summary>
    public static int Cap(double hz) => hz > 1.0 ? (int)(hz - hz * hz / 3600.0) : 0;

    /// <summary>
    /// One preset per common refresh rate. Where an attached display runs at that rate (rounded), its exact rate is used,
    /// so the preset matches what ReLimiter calculates on that display; the lowest wins when several do.
    /// </summary>
    public static IReadOnlyList<(int Fps, int Hz)> Presets() => Presets(HdrToggleService.GetActiveRefreshRates());

    /// <summary>Testable core of <see cref="Presets()"/>.</summary>
    public static IReadOnlyList<(int Fps, int Hz)> Presets(IEnumerable<double> displayRates)
    {
        var rates = displayRates.ToList();
        var list = new List<(int Fps, int Hz)>(NominalHz.Length);
        foreach (var hz in NominalHz)
        {
            var exact = rates.Where(r => (int)Math.Round(r) == hz).ToList();
            int fps = exact.Count > 0 ? exact.Min(Cap) : Cap(hz);
            list.Add((fps, hz));
        }
        return list;
    }
}
