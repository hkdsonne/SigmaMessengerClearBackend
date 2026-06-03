using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;

namespace FileService.Services;

public class YandexStorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly ILogger<YandexStorageService> _logger;

    // Типы файлов, которые НЕ нужно обрабатывать
    private static readonly HashSet<string> _skipProcessingTypes = new()
    {
        "video/mp4", "video/webm", "video/mpeg",
        "audio/mpeg", "audio/mp4", "audio/ogg",
        "application/zip", "application/gzip", "application/x-rar-compressed",
        "image/webp"
    };

    public YandexStorageService(ILogger<YandexStorageService> logger)
    {
        _logger = logger;

        var accessKey = Environment.GetEnvironmentVariable("S3_ACCESS_KEY")?.Trim();
        var secretKey = Environment.GetEnvironmentVariable("S3_SECRET_KEY")?.Trim();
        _bucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME")?.Trim() ?? "sigmadatastorage";

        _logger.LogInformation($"S3 Configuration: Bucket={_bucketName}");

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
        {
            _logger.LogError("S3_ACCESS_KEY or S3_SECRET_KEY is not configured!");
            throw new Exception("S3 credentials are not configured");
        }

        var config = new AmazonS3Config
        {
            ServiceURL = "https://storage.yandexcloud.net",
            ForcePathStyle = true,
            UseHttp = false
        };

        _s3Client = new AmazonS3Client(accessKey, secretKey, config);
    }

    private static bool ShouldCompressWithGZip(string contentType, long fileSize)
    {
        var textTypes = new[] { "text/", "application/json", "application/xml", "application/javascript", "application/x-www-form-urlencoded" };

        if (textTypes.Any(t => contentType.StartsWith(t)))
            return fileSize > 5 * 1024;

        return false;
    }

    private static async Task<byte[]> OptimizeImageAsync(byte[] imageData, string contentType)
    {
        try
        {
            using var inputStream = new MemoryStream(imageData);
            using var image = await Image.LoadAsync(inputStream);
            using var outputStream = new MemoryStream();

            IImageEncoder? encoder = contentType switch
            {
                "image/jpeg" => new JpegEncoder { Quality = 85 },
                "image/png" => new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
                _ => null
            };

            if (encoder is not null)
            {
                await image.SaveAsync(outputStream, encoder);

                if (outputStream.Length < imageData.Length)
                {
                    var savedPercent = (double)(imageData.Length - outputStream.Length) / imageData.Length;
                    return outputStream.ToArray();
                }
            }

            return imageData;
        }
        catch (Exception)
        {
            return imageData;
        }
    }

    /// <summary>
    /// Создает миниатюру изображения указанного размера (в формате PNG)
    /// </summary>
    private static async Task<byte[]?> CreateThumbnailAsync(byte[] imageData, int size = 200)
    {
        try
        {
            using var inputStream = new MemoryStream(imageData);
            using var image = await Image.LoadAsync(inputStream);

            // Изменяем размер, сохраняя пропорции и обрезая до квадрата
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop
            }));

            using var outputStream = new MemoryStream();
            await image.SaveAsync(outputStream, new PngEncoder());

            return outputStream.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<byte[]> CompressWithGZipAsync(byte[] input)
    {
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.SmallestSize))
        {
            await gzipStream.WriteAsync(input);
        }
        return outputStream.ToArray();
    }

    private static async Task<byte[]> DecompressWithGZipAsync(byte[] input)
    {
        using var inputStream = new MemoryStream(input);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        await gzipStream.CopyToAsync(outputStream);
        return outputStream.ToArray();
    }

    private async Task<(byte[] data, bool wasOptimized, bool wasCompressed, byte[]? thumbnailData)> ProcessFileAsync(byte[] fileData, string contentType)
    {
        var result = fileData;
        var wasOptimized = false;
        var wasCompressed = false;
        byte[]? thumbnailData = null;

        // 1. Оптимизируем изображения
        if (contentType.StartsWith("image/") && !_skipProcessingTypes.Contains(contentType))
        {
            var optimized = await OptimizeImageAsync(result, contentType);
            if (optimized.Length < result.Length)
            {
                result = optimized;
                wasOptimized = true;
            }

            // 2. Создаем миниатюру для изображений
            thumbnailData = await CreateThumbnailAsync(result);
        }

        // 3. Сжимаем текстовые файлы GZip
        if (ShouldCompressWithGZip(contentType, result.Length))
        {
            var compressed = await CompressWithGZipAsync(result);
            if (compressed.Length < result.Length)
            {
                result = compressed;
                wasCompressed = true;
            }
        }

        return (result, wasOptimized, wasCompressed, thumbnailData);
    }

    public async Task<string> UploadFileAsync(IFormFile file, string fileName)
    {
        try
        {
            _logger.LogInformation($"Uploading file: {fileName}, size: {file.Length} bytes, type: {file.ContentType}");

            byte[] fileData;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileData = memoryStream.ToArray();
            }

            var (processedData, wasOptimized, wasCompressed, thumbnailData) = await ProcessFileAsync(fileData, file.ContentType);

            string fileKey;
            if (wasCompressed)
            {
                fileKey = $"uploads/{DateTime.UtcNow:yyyy-MM-dd}/{fileName}.gz";
            }
            else
            {
                fileKey = $"uploads/{DateTime.UtcNow:yyyy-MM-dd}/{fileName}";
            }

            using var uploadStream = new MemoryStream(processedData);
            var uploadRequest = new TransferUtilityUploadRequest
            {
                InputStream = uploadStream,
                Key = fileKey,
                BucketName = _bucketName,
                Headers = { CacheControl = "max-age=86400" }
            };

            uploadRequest.Headers.ContentType = file.ContentType;

            uploadRequest.Metadata.Add("x-amz-meta-original-size", fileData.Length.ToString());
            uploadRequest.Metadata.Add("x-amz-meta-was-compressed", wasCompressed.ToString());
            uploadRequest.Metadata.Add("x-amz-meta-was-optimized", wasOptimized.ToString());

            var transferUtility = new TransferUtility(_s3Client);
            await transferUtility.UploadAsync(uploadRequest);

            // Если есть миниатюра - загружаем ее отдельно
            if (thumbnailData is not null)
            {
                var thumbKey = $"thumbnails/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid()}.png";
                using var thumbStream = new MemoryStream(thumbnailData);
                var thumbRequest = new TransferUtilityUploadRequest
                {
                    InputStream = thumbStream,
                    Key = thumbKey,
                    BucketName = _bucketName,
                    Headers = { CacheControl = "max-age=86400" }
                };
                thumbRequest.Headers.ContentType = "image/png";
                await transferUtility.UploadAsync(thumbRequest);
            }

            var fileUrl = $"https://{_bucketName}.storage.yandexcloud.net/{fileKey}";

            var savedPercent = (double)(fileData.Length - processedData.Length) / fileData.Length;
            _logger.LogInformation($"File uploaded. Original: {fileData.Length} bytes, Stored: {processedData.Length} bytes, Saved: {savedPercent:P0}");

            return fileUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file");
            throw;
        }
    }

    /// <summary>
    /// Загрузка файла с возвратом миниатюры (для аватарок и фото в чатах)
    /// </summary>
    public async Task<(string fileUrl, string thumbnailUrl)> UploadImageWithThumbnailAsync(IFormFile file, string fileName)
    {
        try
        {
            _logger.LogInformation($"Uploading image with thumbnail: {fileName}");

            byte[] fileData;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileData = memoryStream.ToArray();
            }

            var optimizedData = await OptimizeImageAsync(fileData, file.ContentType);
            var thumbnailData = await CreateThumbnailAsync(optimizedData, 200);

            var fileKey = $"images/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid()}.png";
            var thumbKey = $"thumbnails/{DateTime.UtcNow:yyyy-MM-dd}/{Guid.NewGuid()}.png";

            var transferUtility = new TransferUtility(_s3Client);

            // Загружаем оптимизированное изображение
            using var imageStream = new MemoryStream(optimizedData);
            await transferUtility.UploadAsync(imageStream, _bucketName, fileKey);

            string thumbnailUrl = string.Empty;
            // Загружаем миниатюру, если она есть
            if (thumbnailData is not null)
            {
                using var thumbStream = new MemoryStream(thumbnailData);
                await transferUtility.UploadAsync(thumbStream, _bucketName, thumbKey);
                thumbnailUrl = $"https://{_bucketName}.storage.yandexcloud.net/{thumbKey}";
            }

            var fileUrl = $"https://{_bucketName}.storage.yandexcloud.net/{fileKey}";

            return (fileUrl, thumbnailUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload image with thumbnail");
            throw;
        }
    }

    public async Task<Stream?> DownloadFileAsync(string fileUrl)
    {
        try
        {
            var fileKey = ExtractFileKeyFromUrl(fileUrl);
            if (string.IsNullOrEmpty(fileKey)) return null;

            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = fileKey
            };

            var response = await _s3Client.GetObjectAsync(request);

            await using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            var fileData = memoryStream.ToArray();

            if (fileKey.EndsWith(".gz"))
            {
                var decompressed = await DecompressWithGZipAsync(fileData);
                return new MemoryStream(decompressed);
            }

            return new MemoryStream(fileData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to download file: {fileUrl}");
            return null;
        }
    }

    /// <summary>
    /// Скачивание миниатюры
    /// </summary>
    public async Task<Stream?> DownloadThumbnailAsync(string thumbnailKey)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = thumbnailKey
            };

            var response = await _s3Client.GetObjectAsync(request);
            await using var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            return new MemoryStream(memoryStream.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to download thumbnail: {thumbnailKey}");
            return null;
        }
    }

    public async Task<bool> DeleteFileAsync(string fileUrl)
    {
        try
        {
            var fileKey = ExtractFileKeyFromUrl(fileUrl);
            if (string.IsNullOrEmpty(fileKey)) return false;

            await _s3Client.DeleteObjectAsync(_bucketName, fileKey);
            _logger.LogInformation($"Deleted file: {fileKey}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to delete file: {fileUrl}");
            return false;
        }
    }

    public string GeneratePresignedUrl(string fileKey, int expiryMinutes = 60)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = fileKey,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes)
        };

        return _s3Client.GetPreSignedURL(request);
    }

    private string ExtractFileKeyFromUrl(string fileUrl)
    {
        var prefix = $"https://{_bucketName}.storage.yandexcloud.net/";
        if (fileUrl.StartsWith(prefix))
        {
            return fileUrl.Substring(prefix.Length);
        }
        return string.Empty;
    }
}