namespace FileService.Services;

public interface IStorageService
{
    Task<string> UploadFileAsync(IFormFile file, string fileName);
    Task<(string fileUrl, string thumbnailUrl)> UploadImageWithThumbnailAsync(IFormFile file, string fileName);
    Task<Stream?> DownloadFileAsync(string fileUrl);
    Task<Stream?> DownloadThumbnailAsync(string thumbnailKey);
    Task<bool> DeleteFileAsync(string fileUrl);
    string GeneratePresignedUrl(string fileKey, int expiryMinutes = 60);
}