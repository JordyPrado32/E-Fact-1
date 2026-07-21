using SkiaSharp;

namespace Simetric.Services;

public static class LogoImageProcessor
{
    public const int MinWidth = 200;
    public const int MinHeight = 100;
    public const int MaxWidth = 1200;
    public const int MaxHeight = 600;
    public const long MaxFileSize = 2 * 1024 * 1024;

    public static LogoImageResult Normalize(byte[] bytes, string contentType)
    {
        using var original = SKBitmap.Decode(bytes)
            ?? throw new InvalidOperationException("El archivo seleccionado no contiene una imagen válida.");

        if (original.Width <= 0 || original.Height <= 0)
            throw new InvalidOperationException("No se pudieron determinar las dimensiones del logo.");

        var cropWidth = Math.Min(original.Width, MaxWidth);
        var cropHeight = Math.Min(original.Height, MaxHeight);
        var finalWidth = Math.Max(cropWidth, MinWidth);
        var finalHeight = Math.Max(cropHeight, MinHeight);
        var requiresAdjustment = cropWidth != original.Width ||
                                 cropHeight != original.Height ||
                                 finalWidth != cropWidth ||
                                 finalHeight != cropHeight;

        if (!requiresAdjustment)
            return new LogoImageResult(bytes, contentType, original.Width, original.Height, false);

        using var normalized = new SKBitmap(finalWidth, finalHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.Transparent);

            var sourceX = Math.Max((original.Width - cropWidth) / 2, 0);
            var sourceY = Math.Max((original.Height - cropHeight) / 2, 0);
            var destinationX = (finalWidth - cropWidth) / 2;
            var destinationY = (finalHeight - cropHeight) / 2;

            canvas.DrawBitmap(
                original,
                new SKRect(sourceX, sourceY, sourceX + cropWidth, sourceY + cropHeight),
                new SKRect(
                    destinationX,
                    destinationY,
                    destinationX + cropWidth,
                    destinationY + cropHeight));
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(normalized);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("No se pudo guardar el logo ajustado.");
        var processedBytes = encoded.ToArray();

        if (processedBytes.LongLength > MaxFileSize)
            throw new InvalidOperationException("El logo ajustado excede el tamaño máximo de 2 MB.");

        return new LogoImageResult(processedBytes, "image/png", finalWidth, finalHeight, true);
    }
}

public sealed record LogoImageResult(
    byte[] Bytes,
    string ContentType,
    int Width,
    int Height,
    bool Adjusted);
