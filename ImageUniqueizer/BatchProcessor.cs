using System.IO;

namespace ImageUniqueizer;

public record BatchProcessResult
{
    public int TotalImages { get; init; }
    public int SuccessCount { get; init; }
    public List<string> Errors { get; init; } = [];
}

public static class BatchProcessor
{
    public static async Task<BatchProcessResult> ProcessBatchAsync(
        IEnumerable<string> sourceImagePaths,
        string outputDirectory,
        int setsCount,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is null or empty.", nameof(outputDirectory));

        if (setsCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(setsCount), "Sets count must be positive.");

        var paths = sourceImagePaths.ToList();
        if (paths.Count == 0)
            return new BatchProcessResult { TotalImages = 0, SuccessCount = 0, Errors = [] };

        int totalImages = paths.Count * setsCount;
        int successCount = 0;
        var errors = new List<string>();
        var errorsLock = new object();
        int completed = 0;

        for (int i = 1; i <= setsCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string setDir = Path.Combine(outputDirectory, $"Set_{i}");
            Directory.CreateDirectory(setDir);

            await Parallel.ForEachAsync(paths, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (sourcePath, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(sourcePath);
                string outputPath = Path.Combine(setDir, fileName);

                try
                {
                    ImageProcessor.ProcessAndSaveImage(sourcePath, outputPath);
                    Interlocked.Increment(ref successCount);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (errorsLock)
                    {
                        errors.Add($"{sourcePath}: {ex.Message}");
                    }
                }
                finally
                {
                    int current = Interlocked.Increment(ref completed);
                    int percent = (int)((long)current * 100 / totalImages);
                    progress?.Report(percent);
                }
            });
        }

        return new BatchProcessResult
        {
            TotalImages = totalImages,
            SuccessCount = successCount,
            Errors = errors
        };
    }
}
