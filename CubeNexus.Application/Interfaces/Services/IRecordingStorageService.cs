namespace CubeNexus.Application.Interfaces.Services;

public interface IRecordingStorageService
{
    Task<RecordingUploadUrlResult> CreateUploadUrlAsync(
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<RecordingPlaybackUrlResult> CreatePlaybackUrlAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task<RecordingObjectMetadataResult?> GetObjectMetadataAsync(
        string objectKey,
        CancellationToken cancellationToken = default);

    Task UploadStreamAsync(
        string objectKey,
        Stream contentStream,
        string contentType,
        CancellationToken cancellationToken = default);
}

public sealed class RecordingUploadUrlResult
{
    public required Uri Url { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}

public sealed class RecordingObjectMetadataResult
{
    public required string ObjectKey { get; init; }
    public required string ContentType { get; init; }
    public required long FileSizeBytes { get; init; }
    public DateTimeOffset? LastModifiedUtc { get; init; }
}

public sealed class RecordingPlaybackUrlResult
{
    public required Uri Url { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
