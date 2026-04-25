namespace AgroShield.Application.Services;

public interface IStorageService
{
    Task<string> UploadAsync(Stream stream, string key, string contentType);
    Task<string> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry);
    Task DeleteAsync(string key);
}
