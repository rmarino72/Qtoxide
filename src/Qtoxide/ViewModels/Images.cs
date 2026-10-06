using Avalonia;
using Avalonia.Media.Imaging;
using QRCoder;

namespace Qtoxide.ViewModels;

internal static class Images
{
    public static Bitmap? Load(byte[]? data)
    {
        if (data is null || data.Length == 0)
            return null;
        try
        {
            using var stream = new MemoryStream(data);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null; // not an image we can decode: show the initials instead
        }
    }

    /// <summary>Scales an image down until its PNG fits in <paramref name="maxBytes"/> (avatars are limited to 64 KiB).</summary>
    public static byte[]? ToAvatarPng(string path, int maxBytes)
    {
        using var source = new Bitmap(path);
        foreach (int side in new[] { 256, 192, 128, 96, 64, 48 })
        {
            double scale = Math.Min(1.0, side / Math.Max(source.PixelSize.Width, (double)source.PixelSize.Height));
            var size = new PixelSize(Math.Max(1, (int)(source.PixelSize.Width * scale)), Math.Max(1, (int)(source.PixelSize.Height * scale)));
            using var scaled = source.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
            using var output = new MemoryStream();
            scaled.Save(output);
            if (output.Length <= maxBytes)
                return output.ToArray();
        }
        return null;
    }

    public static Bitmap QrCode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return new Bitmap(new MemoryStream(png));
    }
}
