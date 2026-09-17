using Size = System.Drawing.Size;
using SizeF = System.Drawing.SizeF;
using System.IO.Compression;

namespace TuiCode.Workbench.About;

/// <summary>The About artwork as pixels, decoded from the resource written by scripts/update-about-image.cs.</summary>
internal static class AboutImage
{
    public static Size ReadSize()
    {
        using var reader = Open();
        return new Size(reader.ReadUInt16(), reader.ReadUInt16());
    }

    public static Color[,] Load()
    {
        using var reader = Open();
        int width = reader.ReadUInt16();
        int height = reader.ReadUInt16();
        var rgb = reader.ReadBytes(width * height * 3);

        var pixels = new Color[width, height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                pixels[x, y] = new Color(rgb[i], rgb[i + 1], rgb[i + 2]);
            }
        return pixels;
    }

    private static BinaryReader Open()
    {
        var stream = typeof(AboutImage).Assembly.GetManifestResourceStream("about.rgb.z")
            ?? throw new InvalidOperationException("about.rgb.z resource is missing");
        return new BinaryReader(new ZLibStream(stream, CompressionMode.Decompress));
    }

    public static Color[,] Scale(Color[,] source, int width, int height)
    {
        var sourceWidth = source.GetLength(0);
        var sourceHeight = source.GetLength(1);
        var result = new Color[width, height];

        for (var y = 0; y < height; y++)
        {
            var sy = Math.Clamp((y + 0.5) * sourceHeight / height - 0.5, 0, sourceHeight - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, sourceHeight - 1);
            var fy = sy - y0;

            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((x + 0.5) * sourceWidth / width - 0.5, 0, sourceWidth - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, sourceWidth - 1);
                var fx = sx - x0;

                result[x, y] = new Color(
                    Lerp(source[x0, y0].R, source[x1, y0].R, source[x0, y1].R, source[x1, y1].R, fx, fy),
                    Lerp(source[x0, y0].G, source[x1, y0].G, source[x0, y1].G, source[x1, y1].G, fx, fy),
                    Lerp(source[x0, y0].B, source[x1, y0].B, source[x0, y1].B, source[x1, y1].B, fx, fy));
            }
        }
        return result;
    }

    private static int Lerp(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, double fx, double fy)
    {
        var top = topLeft + (topRight - topLeft) * fx;
        var bottom = bottomLeft + (bottomRight - bottomLeft) * fx;
        return (int)Math.Round(top + (bottom - top) * fy);
    }

    /// <summary>Fills <paramref name="columns"/> across and stops on a row boundary: iTerm2 blanks the whole of a partly covered row.</summary>
    public static (Size Pixels, int Rows) Fit(Size imageSize, SizeF cellPixels, int columns)
    {
        var width = (int)Math.Round(columns * cellPixels.Width);
        var naturalHeight = (double)imageSize.Height * width / imageSize.Width;
        var rows = Math.Max(1, (int)(naturalHeight / cellPixels.Height));
        return (new Size(width, (int)Math.Round(rows * cellPixels.Height)), rows);
    }

    /// <summary>Scales to <paramref name="size"/>, trimming the top and bottom to keep the aspect ratio.</summary>
    public static Color[,] Cover(Color[,] source, Size size)
    {
        var sourceWidth = source.GetLength(0);
        var sourceHeight = source.GetLength(1);
        var keptRows = Math.Min(sourceHeight, (int)Math.Round((double)size.Height * sourceWidth / size.Width));
        var top = (sourceHeight - keptRows) / 2;

        var cropped = new Color[sourceWidth, keptRows];
        for (var x = 0; x < sourceWidth; x++)
            for (var y = 0; y < keptRows; y++)
                cropped[x, y] = source[x, top + y];

        return Scale(cropped, size.Width, size.Height);
    }
}
