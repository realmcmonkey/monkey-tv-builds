namespace MonkeyTV;

using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class PortableAssetImporter
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".ico", ".webp", ".bmp"
    };

    public static string ImportIcon(string sourcePath, Guid serviceId) =>
        Import(sourcePath, PortablePaths.AssetsDirectory, serviceId);

    public static string ImportBanner(string sourcePath, Guid serviceId)
    {
        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension)) throw new InvalidOperationException("Choose a PNG, JPEG, WebP, or BMP image.");
        using var stream = File.OpenRead(sourcePath);
        return ImportBannerStream(stream);
    }

    public static string ImportBannerBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return ImportBannerStream(stream);
    }

    private static string Import(string sourcePath, string destinationDirectory, Guid serviceId)
    {
        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
            throw new InvalidOperationException("Choose a PNG, JPEG, ICO, WebP, or BMP image.");

        Directory.CreateDirectory(destinationDirectory);
        var target = Path.Combine(destinationDirectory, serviceId.ToString("N") + extension.ToLowerInvariant());
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, target, true);
        return Path.GetRelativePath(PortablePaths.AppRoot, target);
    }

    private static string ImportBannerStream(Stream source)
    {
        var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var scale = Math.Min(1d, Math.Min(1600d / frame.PixelWidth, 900d / frame.PixelHeight));
        BitmapSource bitmap = frame;
        if (scale < 1) bitmap = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        var bytes = output.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..20].ToLowerInvariant();
        Directory.CreateDirectory(PortablePaths.BannersDirectory);
        var target = Path.Combine(PortablePaths.BannersDirectory, hash + ".jpg");
        if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
        return Path.GetRelativePath(PortablePaths.AppRoot, target);
    }
}
