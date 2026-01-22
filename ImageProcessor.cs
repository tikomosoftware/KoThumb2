using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace KoThumb2
{
    public enum ConversionMode
    {
        Resize,
        FormatOnly
    }

    public enum ResizeBase
    {
        Width,
        Height,
        LongSide
    }

    public enum ResizeAlgorithm
    {
        Lanczos,
        Bicubic,
        Bilinear
    }

    public enum OutputFormat
    {
        JPG,
        PNG,
        WebP
    }

    public enum FilenameRule
    {
        Keep,
        Prefix,
        Suffix,
        Serial
    }

    public enum ConflictResolution
    {
        Ask,
        AutoRename,
        Skip
    }

    public class ProcessSettings
    {
        public string InputFolder { get; set; } = string.Empty;
        public bool IncludeSubfolders { get; set; } = false;
        public string FileExtensions { get; set; } = "*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tiff";

        public string OutputFolder { get; set; } = string.Empty;
        public bool ReplicateFolderStructure { get; set; } = true;

        public ConversionMode Mode { get; set; } = ConversionMode.Resize;

        // Resize settings
        public ResizeBase ResizeBase { get; set; } = ResizeBase.LongSide;
        public int TargetSize { get; set; } = 1920;
        public ResizeAlgorithm Algorithm { get; set; } = ResizeAlgorithm.Lanczos;

        // Format settings
        public OutputFormat Format { get; set; } = OutputFormat.JPG;
        public int JpegQuality { get; set; } = 95;
        public bool ProgressiveJpeg { get; set; } = true;
        public int PngCompressionLevel { get; set; } = 9;
        public int WebpQuality { get; set; } = 95;
        public bool WebpLossless { get; set; } = false;

        // Filename settings
        public FilenameRule FilenameRule { get; set; } = FilenameRule.Keep;
        public string Prefix { get; set; } = "resized_";
        public string Suffix { get; set; } = "_processed";
        public string SerialPattern { get; set; } = "image_{0:D3}";
        public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.AutoRename;

        // Metadata
        public bool KeepExif { get; set; } = true;
        public bool KeepColorProfile { get; set; } = true;
        public bool AlwaysOnTop { get; set; } = false;
    }

    public class ImageProcessor
    {
        public delegate void ProgressHandler(int current, int total, string fileName, string fullPath);
        public delegate void CompletionHandler(int success, int failed, List<string> errors);

        public async Task<byte[]> ProcessPreviewAsync(string file, ProcessSettings settings)
        {
            return await Task.Run(() =>
            {
                using var image = SixLabors.ImageSharp.Image.Load(file);
                
                // Resizing
                if (settings.Mode == ConversionMode.Resize)
                {
                    int width = image.Width;
                    int height = image.Height;
                    int targetW = width;
                    int targetH = height;

                    switch (settings.ResizeBase)
                    {
                        case ResizeBase.Width:
                            targetW = settings.TargetSize;
                            targetH = (int)((double)height * targetW / width);
                            break;
                        case ResizeBase.Height:
                            targetH = settings.TargetSize;
                            targetW = (int)((double)width * targetH / height);
                            break;
                        case ResizeBase.LongSide:
                            if (width >= height)
                            {
                                targetW = settings.TargetSize;
                                targetH = (int)((double)height * targetW / width);
                            }
                            else
                            {
                                targetH = settings.TargetSize;
                                targetW = (int)((double)width * targetH / height);
                            }
                            break;
                    }

                    var resampler = settings.Algorithm switch
                    {
                        ResizeAlgorithm.Bicubic => KnownResamplers.Bicubic,
                        ResizeAlgorithm.Bilinear => KnownResamplers.Triangle,
                        _ => KnownResamplers.Lanczos3
                    };

                    image.Mutate(x => x.Resize(targetW, targetH, resampler));
                }

                using var ms = new MemoryStream();
                // For preview, always use PNG as it's lossless and easy for WPF to decode
                image.Save(ms, new PngEncoder());
                return ms.ToArray();
            });
        }

        public async Task ProcessImagesAsync(ProcessSettings settings, ProgressHandler onProgress, CompletionHandler onComplete, CancellationToken ct)
        {
            var files = GetFiles(settings.InputFolder, settings.IncludeSubfolders, settings.FileExtensions);
            int total = files.Count;
            int success = 0;
            int failed = 0;
            var errors = new List<string>();

            // Determine output folder and structure
            if (!Directory.Exists(settings.OutputFolder))
            {
                Directory.CreateDirectory(settings.OutputFolder);
            }

            await Task.Run(() =>
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                    CancellationToken = ct
                };

                int processedCount = 0;
                int sharedSerialIndex = 1;

                // For serial numbering, we might need a lock if processing in parallel
                // But serial numbering is usually sequential. 
                // Let's decide if we want to support parallel with serial.
                // If serial, we might need to sort files first.
                var sortedFiles = files.OrderBy(f => f).ToList();

                Parallel.ForEach(sortedFiles, parallelOptions, (file) =>
                {
                    if (ct.IsCancellationRequested) return;

                    try
                    {
                        ProcessFile(file, settings, ref sharedSerialIndex);
                        Interlocked.Increment(ref success);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        lock (errors)
                        {
                            errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                        }
                    }

                    int current = Interlocked.Increment(ref processedCount);
                    onProgress?.Invoke(current, total, Path.GetFileName(file), file);
                });
            }, ct);

            onComplete?.Invoke(success, failed, errors);
        }

        private List<string> GetFiles(string folder, bool subfolders, string extensions)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return new List<string>();

            var filters = extensions.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            var allFiles = new List<string>();

            foreach (var filter in filters)
            {
                allFiles.AddRange(Directory.GetFiles(folder, filter, subfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            }

            return allFiles.Distinct().ToList();
        }

        private void ProcessFile(string file, ProcessSettings settings, ref int serialIndex)
        {
            using var image = SixLabors.ImageSharp.Image.Load(file);

            // Metadata handling
            if (!settings.KeepExif)
            {
                image.Metadata.ExifProfile = null;
            }
            if (!settings.KeepColorProfile)
            {
                // Note: ImageSharp doesn't have a direct "remove color profile" in the same way, 
                // but we can strip ICC if needed.
                // image.Metadata.IccProfile = null;
            }

            // Resizing
            if (settings.Mode == ConversionMode.Resize)
            {
                int width = image.Width;
                int height = image.Height;
                int targetW = width;
                int targetH = height;

                switch (settings.ResizeBase)
                {
                    case ResizeBase.Width:
                        targetW = settings.TargetSize;
                        targetH = (int)((double)height * targetW / width);
                        break;
                    case ResizeBase.Height:
                        targetH = settings.TargetSize;
                        targetW = (int)((double)width * targetH / height);
                        break;
                    case ResizeBase.LongSide:
                        if (width >= height)
                        {
                            targetW = settings.TargetSize;
                            targetH = (int)((double)height * targetW / width);
                        }
                        else
                        {
                            targetH = settings.TargetSize;
                            targetW = (int)((double)width * targetH / height);
                        }
                        break;
                }

                var resampler = settings.Algorithm switch
                {
                    ResizeAlgorithm.Bicubic => KnownResamplers.Bicubic,
                    ResizeAlgorithm.Bilinear => KnownResamplers.Triangle,
                    _ => KnownResamplers.Lanczos3
                };

                image.Mutate(x => x.Resize(targetW, targetH, resampler));
            }

            // Determine output path
            string outputDir = settings.OutputFolder;
            if (settings.ReplicateFolderStructure && settings.IncludeSubfolders)
            {
                string relativePath = Path.GetRelativePath(settings.InputFolder, Path.GetDirectoryName(file)!);
                outputDir = Path.Combine(outputDir, relativePath);
            }

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string baseName = Path.GetFileNameWithoutExtension(file);
            string outputFileName = settings.FilenameRule switch
            {
                FilenameRule.Prefix => settings.Prefix + baseName,
                FilenameRule.Suffix => baseName + settings.Suffix,
                FilenameRule.Serial => string.Format(settings.SerialPattern, Interlocked.Increment(ref serialIndex) - 1),
                _ => baseName
            };

            string extension = settings.Format switch
            {
                OutputFormat.PNG => ".png",
                OutputFormat.WebP => ".webp",
                _ => ".jpg"
            };

            string fullOutputPath = Path.Combine(outputDir, outputFileName + extension);

            // Handle conflict
            if (File.Exists(fullOutputPath))
            {
                if (settings.ConflictResolution == ConflictResolution.Skip) return;
                if (settings.ConflictResolution == ConflictResolution.AutoRename)
                {
                    int i = 1;
                    while (File.Exists(fullOutputPath))
                    {
                        fullOutputPath = Path.Combine(outputDir, $"{outputFileName}({i}){extension}");
                        i++;
                    }
                }
                // 'Ask' should be handled by the caller before starting, or we'd need a UI callback.
                // For batch, AutoRename is safer default if not Ask.
            }

            // Encode and save
            IImageEncoder encoder = settings.Format switch
            {
                OutputFormat.PNG => new PngEncoder { CompressionLevel = (PngCompressionLevel)settings.PngCompressionLevel },
                OutputFormat.WebP => new WebpEncoder { Quality = settings.WebpQuality },
                _ => new JpegEncoder { Quality = settings.JpegQuality }
            };

            image.Save(fullOutputPath, encoder);
        }
    }
}
