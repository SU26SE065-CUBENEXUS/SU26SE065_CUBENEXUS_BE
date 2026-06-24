using System.ComponentModel.DataAnnotations;

namespace CubeNexus.Infrastructure.Options;

public class R2Options
{
    public const string SectionName = "R2";

    [Required]
    public string AccountId { get; set; } = string.Empty;

    [Required]
    [Url]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int UploadUrlExpirationMinutes { get; set; } = 15;

    [Range(1, 1440)]
    public int PlaybackUrlExpirationMinutes { get; set; } = 60;

    public bool HasCredentials()
        => !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}
