using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace SpotTheDiff.Core.Tests;

public class DiffEngineTests
{
    private static Bitmap Blank(int w = 300, int h = 200)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        return bmp;
    }

    private static void Fill(Bitmap bmp, Rectangle r, Color c)
    {
        using var g = Graphics.FromImage(bmp);
        using var b = new SolidBrush(c);
        g.FillRectangle(b, r);
    }

    [Fact]
    public void IdenticalImages_HaveNoDifferences()
    {
        using var a = Blank();
        using var b = Blank();
        using var result = DiffEngine.Compare(a, b, new DiffOptions());
        Assert.Empty(result.Regions);
    }

    [Fact]
    public void SingleChange_IsFoundWithMatchingBounds()
    {
        using var a = Blank();
        using var b = Blank();
        var changed = new Rectangle(100, 60, 40, 20);
        Fill(b, changed, Color.Black);

        using var result = DiffEngine.Compare(a, b, new DiffOptions());

        var region = Assert.Single(result.Regions);
        Assert.Equal(changed, region.Bounds);
        Assert.Equal(1, region.Number);
    }

    [Fact]
    public void FarApartChanges_AreSeparateAndNumberedInReadingOrder()
    {
        using var a = Blank();
        using var b = Blank();
        Fill(b, new Rectangle(200, 150, 20, 20), Color.Black);
        Fill(b, new Rectangle(10, 10, 20, 20), Color.Black);

        using var result = DiffEngine.Compare(a, b, new DiffOptions());

        Assert.Equal(2, result.Regions.Count);
        Assert.Equal(10, result.Regions[0].Bounds.Left);
        Assert.Equal(200, result.Regions[1].Bounds.Left);
    }

    [Fact]
    public void NearbyChanges_AreMergedIntoOneRegion()
    {
        using var a = Blank();
        using var b = Blank();
        Fill(b, new Rectangle(50, 50, 10, 10), Color.Black);
        Fill(b, new Rectangle(66, 50, 10, 10), Color.Black);

        using var result = DiffEngine.Compare(a, b, new DiffOptions { MergeDistance = 12 });

        Assert.Single(result.Regions);
    }

    [Fact]
    public void SubtleColourChange_IsIgnoredUntilToleranceIsLowered()
    {
        using var a = Blank();
        using var b = Blank();
        Fill(b, new Rectangle(20, 20, 50, 50), Color.FromArgb(245, 245, 245));

        using var tolerant = DiffEngine.Compare(a, b, new DiffOptions { Tolerance = 25 });
        using var strict = DiffEngine.Compare(a, b, new DiffOptions { Tolerance = 5 });

        Assert.Empty(tolerant.Regions);
        Assert.Single(strict.Regions);
    }

    [Fact]
    public void TinyNoise_IsIgnored()
    {
        using var a = Blank();
        using var b = Blank();
        b.SetPixel(5, 5, Color.Black);

        using var result = DiffEngine.Compare(a, b, new DiffOptions { MinPixels = 12 });

        Assert.Empty(result.Regions);
    }

    [Fact]
    public void DifferentSizes_AreHandled()
    {
        using var a = Blank(300, 200);
        using var b = Blank(150, 100);

        using var result = DiffEngine.Compare(a, b, new DiffOptions());

        Assert.True(result.AfterWasResized);
    }

    [Theory]
    [InlineData(OutputLayout.SideBySide, true)]
    [InlineData(OutputLayout.AfterOnly, false)]
    public void Output_HasExpectedWidth(OutputLayout layout, bool wider)
    {
        using var a = Blank();
        using var b = Blank();
        Fill(b, new Rectangle(100, 60, 40, 20), Color.Black);

        foreach (var shape in Enum.GetValues<HighlightShape>())
        {
            using var result = DiffEngine.Compare(a, b, new DiffOptions { Layout = layout, Shape = shape, TintChangedPixels = true });
            Assert.Equal(wider, result.Annotated.Width > a.Width);
            Assert.Equal(a.Height, result.Annotated.Height);
        }
    }
}
