using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ImageUniqueizer;

public static class ImageProcessor
{
    public static void ProcessAndSaveImage(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path is null or empty.", nameof(inputPath));

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is null or empty.", nameof(outputPath));

        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}", inputPath);

        try
        {
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            using var image = Image.Load(inputPath);

            image.Mutate(x => x.AutoOrient());

            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.CicpProfile = null;

            var random = new Random();

            double angle = random.NextDouble() * 3.0 - 1.5;
            image.Mutate(x => x.Rotate((float)angle));

            int origW = image.Width;
            int origH = image.Height;

            if (angle != 0)
            {
                double rad = Math.Abs(angle) * Math.PI / 180.0;
                double cosA = Math.Cos(rad);
                double sinA = Math.Sin(rad);

                double bbW = origW * cosA + origH * sinA;
                double bbH = origW * sinA + origH * cosA;

                double aspect = (double)origW / origH;
                double sW = bbW / aspect;
                double scale = Math.Min(sW, bbH) / origH;

                int cropW = (int)Math.Round(origW * scale);
                int cropH = (int)Math.Round(origH * scale);
                cropW = Math.Clamp(cropW, 1, image.Width);
                cropH = Math.Clamp(cropH, 1, image.Height);

                int cx = (image.Width - cropW) / 2;
                int cy = (image.Height - cropH) / 2;

                image.Mutate(x => x.Crop(new Rectangle(cx, cy, cropW, cropH)));
            }

            if (image.Width != origW || image.Height != origH)
                image.Mutate(x => x.Resize(origW, origH));

            float brightness = Clamp((float)(0.97 + random.NextDouble() * 0.06), 0.97f, 1.03f);
            float contrast = Clamp((float)(0.97 + random.NextDouble() * 0.06), 0.97f, 1.03f);

            image.Mutate(x => x.Brightness(brightness).Contrast(contrast));

            using var opaqueImage = image.CloneAs<Rgb24>();

            opaqueImage.SaveAsJpeg(outputPath, new JpegEncoder
            {
                Quality = 95,
                ColorType = JpegEncodingColor.YCbCrRatio420,
                SkipMetadata = true
            });
        }
        catch (FileNotFoundException) { throw; }
        catch (ArgumentException) { throw; }
        catch (InvalidImageContentException ex)
        {
            throw new InvalidOperationException(
                $"Corrupted or unsupported image: {inputPath}", ex);
        }
        catch (UnknownImageFormatException ex)
        {
            throw new InvalidOperationException(
                $"Unsupported image format: {inputPath}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Access denied for file: {inputPath}", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"I/O error processing file: {inputPath}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to process image: {inputPath}", ex);
        }
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
