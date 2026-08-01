using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace CubeNexus.Infrastructure.Services;

public class R2RecordingStorageService : IRecordingStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly R2Options _options;

    public R2RecordingStorageService(IOptions<R2Options> options)
    {
        _options = options.Value;
        EnsureConfigured();

        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint.TrimEnd('/'),
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            SignatureVersion = "4"
        };

        _s3Client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKeyId, _options.SecretAccessKey),
            config);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountId)
            || string.IsNullOrWhiteSpace(_options.Endpoint)
            || string.IsNullOrWhiteSpace(_options.BucketName))
        {
            throw new InvalidOperationException("R2 recording is not configured. Missing R2 account, endpoint, or bucket settings.");
        }

        if (!_options.HasCredentials())
        {
            throw new InvalidOperationException("R2 recording is not configured. Set R2:AccessKeyId and R2:SecretAccessKey in User Secrets or environment variables.");
        }
    }

    public Task<RecordingUploadUrlResult> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.UploadUrlExpirationMinutes);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = contentType
        };

        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult(new RecordingUploadUrlResult
        {
            Url = new Uri(url),
            ExpiresAtUtc = expiresAt
        });
    }

    public Task<RecordingPlaybackUrlResult> CreatePlaybackUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.PlaybackUrlExpirationMinutes);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt
        };

        return Task.FromResult(new RecordingPlaybackUrlResult
        {
            Url = new Uri(_s3Client.GetPreSignedURL(request)),
            ExpiresAtUtc = expiresAt
        });
    }

    public async Task<RecordingObjectMetadataResult?> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey
            }, cancellationToken);

            return new RecordingObjectMetadataResult
            {
                ObjectKey = objectKey,
                ContentType = response.Headers.ContentType ?? "application/octet-stream",
                FileSizeBytes = response.Headers.ContentLength,
                LastModifiedUtc = response.LastModified
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task UploadStreamAsync(
        string objectKey,
        Stream contentStream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = contentStream,
            ContentType = contentType,
            DisablePayloadSigning = true,
            UseChunkEncoding = false
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);
    }
}
