using PdfiumViewer;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShowWrite;

public static class PdfService
{
    private const int HighDpi = 300;
    private const int UltraHighDpi = 600;

    public static List<string> ConvertPdfToImages(string pdfPath, string? outputDirectory = null, bool ultraHighQuality = false, int? customDpi = null)
    {
        var imagePaths = new List<string>();

        if (!File.Exists(pdfPath))
        {
            return imagePaths;
        }

        outputDirectory ??= Path.Combine(Path.GetTempPath(), "ShowWrite_PdfImages", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        try
        {
            using var doc = PdfDocument.Load(pdfPath);
            int pageCount = doc.PageCount;
            var results = new string[pageCount];

            int maxDegree = Math.Max(1, Environment.ProcessorCount - 1);
            var semaphore = new SemaphoreSlim(maxDegree, maxDegree);
            var tasks = new List<Task>();

            int dpi = customDpi ?? (ultraHighQuality ? UltraHighDpi : HighDpi);

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                int idx = pageIndex;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var fileName = $"Page_{idx + 1:D04}.png";
                        var outputPath = Path.Combine(outputDirectory, fileName);

                        using var image = doc.Render(idx, dpi, dpi, PdfRenderFlags.CorrectFromDpi | PdfRenderFlags.Annotations | PdfRenderFlags.ForPrinting);

                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Compression, (long)EncoderValue.CompressionNone);

                        image.Save(outputPath, ImageFormat.Png);
                        results[idx] = outputPath;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            imagePaths.AddRange(results.Where(p => !string.IsNullOrEmpty(p)));
        }
        catch (Exception ex)
        {

        }

        return imagePaths;
    }

    public static List<string> ConvertPdfToImagesHighQuality(string pdfPath, string? outputDirectory = null)
    {
        return ConvertPdfToImages(pdfPath, outputDirectory, ultraHighQuality: true);
    }
}
