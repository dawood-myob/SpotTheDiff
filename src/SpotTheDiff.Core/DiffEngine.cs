using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SpotTheDiff.Core;

public static class DiffEngine
{
    private const int SideBySideGap = 12;

    public static DiffResult Compare(Bitmap before, Bitmap after, DiffOptions options)
    {
        int w = before.Width, h = before.Height;
        bool resized = after.Width != w || after.Height != h;
        using Bitmap? scaled = resized ? Resize(after, w, h) : null;
        Bitmap afterFit = scaled ?? after;

        byte[] pa = ReadPixels(before);
        byte[] pb = ReadPixels(afterFit);

        bool[] mask = BuildMask(pa, pb, w, h, Math.Clamp(options.Tolerance, 0, 255));
        bool[] merged = Dilate(mask, w, h, Math.Max(0, options.MergeDistance));
        List<DiffRegion> regions = FindRegions(mask, merged, w, h, Math.Max(1, options.MinPixels));

        Bitmap annotated = Render(before, afterFit, mask, regions, options);
        return new DiffResult(regions, annotated, resized);
    }

    private static Bitmap Resize(Bitmap src, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, w, h));
        return bmp;
    }

    private static byte[] ReadPixels(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var buf = new byte[w * h * 4];
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), buf, y * w * 4, w * 4);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return buf;
    }

    private static bool[] BuildMask(byte[] a, byte[] b, int w, int h, int tolerance)
    {
        int n = w * h;
        var mask = new bool[n];
        for (int i = 0, o = 0; i < n; i++, o += 4)
        {
            int d = Math.Abs(a[o] - b[o]);
            int dg = Math.Abs(a[o + 1] - b[o + 1]);
            int dr = Math.Abs(a[o + 2] - b[o + 2]);
            if (dg > d) d = dg;
            if (dr > d) d = dr;
            mask[i] = d > tolerance;
        }
        return mask;
    }

    /// <summary>Square dilation (separable, O(n)) so nearby changed pixels merge into one region.</summary>
    private static bool[] Dilate(bool[] mask, int w, int h, int r)
    {
        if (r == 0) return mask;

        var horiz = new bool[mask.Length];
        var prefix = new int[w + 1];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
                prefix[x + 1] = prefix[x] + (mask[row + x] ? 1 : 0);
            for (int x = 0; x < w; x++)
            {
                int lo = Math.Max(0, x - r);
                int hi = Math.Min(w, x + r + 1);
                horiz[row + x] = prefix[hi] - prefix[lo] > 0;
            }
        }

        var result = new bool[mask.Length];
        var counts = new int[w];
        for (int y = 0; y <= Math.Min(h - 1, r); y++)
            for (int x = 0; x < w; x++)
                if (horiz[y * w + x]) counts[x]++;

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
                result[row + x] = counts[x] > 0;

            int add = y + r + 1;
            if (add < h)
                for (int x = 0; x < w; x++)
                    if (horiz[add * w + x]) counts[x]++;

            int remove = y - r;
            if (remove >= 0)
                for (int x = 0; x < w; x++)
                    if (horiz[remove * w + x]) counts[x]--;
        }
        return result;
    }

    private static List<DiffRegion> FindRegions(bool[] mask, bool[] merged, int w, int h, int minPixels)
    {
        var visited = new bool[merged.Length];
        var stack = new int[merged.Length];
        var found = new List<(Rectangle Bounds, int Count)>();

        for (int seed = 0; seed < merged.Length; seed++)
        {
            if (!merged[seed] || visited[seed]) continue;

            int sp = 0;
            stack[sp++] = seed;
            visited[seed] = true;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;

            while (sp > 0)
            {
                int idx = stack[--sp];
                int x = idx % w, y = idx / w;

                if (mask[idx])
                {
                    count++;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(h - 1, y + 1); ny++)
                {
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
                    {
                        int ni = ny * w + nx;
                        if (merged[ni] && !visited[ni])
                        {
                            visited[ni] = true;
                            stack[sp++] = ni;
                        }
                    }
                }
            }

            if (count >= minPixels && maxX >= 0)
                found.Add((Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1), count));
        }

        // Reading order: top to bottom, then left to right.
        return found
            .OrderBy(f => f.Bounds.Top)
            .ThenBy(f => f.Bounds.Left)
            .Select((f, i) => new DiffRegion(i + 1, f.Bounds, f.Count))
            .ToList();
    }

    private static Bitmap Render(Bitmap before, Bitmap after, bool[] mask, List<DiffRegion> regions, DiffOptions options)
    {
        int w = before.Width, h = before.Height;
        bool sideBySide = options.Layout == OutputLayout.SideBySide;
        int width = sideBySide ? w * 2 + SideBySideGap : w;

        var canvas = new Bitmap(width, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.FromArgb(90, 90, 90));
        g.PixelOffsetMode = PixelOffsetMode.Half;

        using Bitmap? tint = options.TintChangedPixels && regions.Count > 0 ? BuildTint(mask, w, h) : null;

        var offsets = sideBySide ? new[] { 0, w + SideBySideGap } : new[] { 0 };
        var sources = sideBySide ? new[] { before, after } : new[] { after };

        for (int i = 0; i < offsets.Length; i++)
        {
            g.DrawImage(sources[i], new Rectangle(offsets[i], 0, w, h));
            if (tint != null)
                g.DrawImage(tint, new Rectangle(offsets[i], 0, w, h));
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        float scale = Math.Max(1f, Math.Max(w, h) / 900f);
        float pen = Math.Max(2f, 2f * scale);
        float fontSize = Math.Max(17f, 15f * scale);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

        foreach (int offset in offsets)
            DrawRegions(g, regions, offset, w, h, options.Shape, pen, font);

        return canvas;
    }

    private static Bitmap BuildTint(bool[] mask, int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            int o = i * 4;
            px[o] = 40; px[o + 1] = 40; px[o + 2] = 255; px[o + 3] = 100; // BGRA: translucent red
        }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w * 4, IntPtr.Add(data.Scan0, y * data.Stride), w * 4);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    private static void DrawRegions(Graphics g, List<DiffRegion> regions, int offsetX, int w, int h,
        HighlightShape shape, float penWidth, Font font)
    {
        using var red = new Pen(Color.Red, penWidth) { LineJoin = LineJoin.Round };
        using var halo = new Pen(Color.White, penWidth + 2.5f) { LineJoin = LineJoin.Round };
        using var textBrush = new SolidBrush(Color.Red);
        using var haloBrush = new SolidBrush(Color.FromArgb(210, Color.White));

        float pad = Math.Max(5f, penWidth * 2.5f);

        foreach (DiffRegion region in regions)
        {
            Rectangle b = region.Bounds;
            RectangleF shapeRect;
            if (shape == HighlightShape.Circle)
            {
                // Ellipse scaled by sqrt(2) fully encloses the bounding rectangle.
                float rx = Math.Max(14f, b.Width / 2f * 1.4142f + pad);
                float ry = Math.Max(14f, b.Height / 2f * 1.4142f + pad);
                float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
                shapeRect = new RectangleF(cx - rx, cy - ry, rx * 2, ry * 2);
            }
            else
            {
                shapeRect = RectangleF.FromLTRB(b.Left - pad, b.Top - pad, b.Right + pad, b.Bottom + pad);
            }

            shapeRect.X += offsetX;

            if (shape == HighlightShape.Circle)
            {
                g.DrawEllipse(halo, shapeRect);
                g.DrawEllipse(red, shapeRect);
            }
            else
            {
                g.DrawRectangle(halo, shapeRect.X, shapeRect.Y, shapeRect.Width, shapeRect.Height);
                g.DrawRectangle(red, shapeRect.X, shapeRect.Y, shapeRect.Width, shapeRect.Height);
            }

            // Number label above the shape (or below it when there is no room at the top).
            string label = region.Number.ToString();
            SizeF size = g.MeasureString(label, font);
            float lx = Math.Clamp(shapeRect.Left, offsetX, offsetX + w - size.Width);
            float ly = shapeRect.Top - size.Height - 1;
            if (ly < 0) ly = Math.Min(h - size.Height, shapeRect.Bottom + 1);

            g.FillRectangle(haloBrush, lx, ly, size.Width, size.Height);
            g.DrawString(label, font, textBrush, lx, ly);
        }
    }
}
