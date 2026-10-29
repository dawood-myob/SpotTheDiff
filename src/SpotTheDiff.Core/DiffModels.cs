using System.Drawing;

namespace SpotTheDiff.Core;

public enum HighlightShape
{
    Rectangle,
    Circle
}

public enum OutputLayout
{
    /// <summary>Annotated "before" and "after" next to each other.</summary>
    SideBySide,

    /// <summary>Annotated "after" image only.</summary>
    AfterOnly
}

public sealed class DiffOptions
{
    /// <summary>Max per-channel difference (0-255) that is still treated as "same". Higher = less sensitive.</summary>
    public int Tolerance { get; set; } = 25;

    /// <summary>Changed pixels closer than this (in px) are merged into one region.</summary>
    public int MergeDistance { get; set; } = 12;

    /// <summary>Regions with fewer changed pixels than this are ignored as noise.</summary>
    public int MinPixels { get; set; } = 12;

    public HighlightShape Shape { get; set; } = HighlightShape.Rectangle;

    public OutputLayout Layout { get; set; } = OutputLayout.SideBySide;

    /// <summary>Also tint the individual changed pixels.</summary>
    public bool TintChangedPixels { get; set; } = false;
}

public sealed record DiffRegion(int Number, Rectangle Bounds, int ChangedPixels);

public sealed class DiffResult : IDisposable
{
    public DiffResult(IReadOnlyList<DiffRegion> regions, Bitmap annotated, bool afterWasResized)
    {
        Regions = regions;
        Annotated = annotated;
        AfterWasResized = afterWasResized;
    }

    public IReadOnlyList<DiffRegion> Regions { get; }

    public Bitmap Annotated { get; }

    /// <summary>True when the "after" image had a different size and was scaled to match "before".</summary>
    public bool AfterWasResized { get; }

    public void Dispose() => Annotated.Dispose();
}
