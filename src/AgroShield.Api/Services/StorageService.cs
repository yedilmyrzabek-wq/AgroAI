using Amazon.S3;
using Amazon.S3.Model;
using AgroShield.Application.Services;

namespace AgroShield.Api.Services;

public class StorageService : IStorageService
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;
    private readonly string _publicBase;

    public StorageService(IConfiguration config)
    {
        var endpoint = config["Storage:Endpoint"]!;
        var accessKey = config["Storage:AccessKey"]!;
        var secretKey = config["Storage:SecretKey"]!;
        _bucket = config["Storage:Bucket"] ?? "images";

        _client = new AmazonS3Client(accessKey, secretKey, new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
        });

        // Public URL base: strip /s3 suffix if present, add /object/public
        var baseUrl = endpoint.EndsWith("/s3") ? endpoint[..^3] : endpoint;
        _publicBase = $"{baseUrl}/object/public/{_bucket}";
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
        });
        return $"{_publicBase}/{key}";
    }

    public Task<string> GetPresignedUploadUrlAsync(string key, string contentType, TimeSpan expiry)
    {
        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = DateTime.UtcNow.Add(expiry),
            ContentType = contentType,
        });
        return Task.FromResult(url);
    }

    public async Task DeleteAsync(string key)
    {
        await _client.DeleteObjectAsync(_bucket, key);
    }
}
