using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SpotTheDiff;

internal static class ImageUtil
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads a file without keeping it locked, flattened onto white as 32bpp ARGB.</summary>
    public static Bitmap LoadFile(string path)
    {
        using var fs = File.OpenRead(path);
        using var img = Image.FromStream(fs, false, false);
        return ToOpaque(img);
    }

    /// <summary>Copies any image to a 32bpp bitmap, flattening transparency onto white.</summary>
    public static Bitmap ToOpaque(Image src)
    {
        var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
        return bmp;
    }

    /// <summary>Gets an image from the clipboard (Snipping Tool, Print Screen, copied image or copied file).</summary>
    public static Bitmap? TryGetClipboardImage()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                // Snipping Tool offers a PNG format that preserves exact pixels.
                if (Clipboard.GetData("PNG") is MemoryStream png)
                {
                    using var img = Image.FromStream(png);
                    return ToOpaque(img);
                }

                if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } clip)
                {
                    using (clip) return ToOpaque(clip);
                }

                if (Clipboard.ContainsFileDropList())
                {
                    string? file = Clipboard.GetFileDropList().Cast<string?>().FirstOrDefault(f => f != null && IsImageFile(f));
                    if (file != null) return LoadFile(file);
                }

                return null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                Thread.Sleep(50); // clipboard briefly locked by another app
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
            {
                return null;
            }
        }
        return null;
    }

    public static bool ClipboardHasImage()
    {
        try
        {
            return Clipboard.ContainsImage() || Clipboard.ContainsData("PNG") ||
                   (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string?>().Any(f => f != null && IsImageFile(f)));
        }
        catch
        {
            return false;
        }
    }
}
