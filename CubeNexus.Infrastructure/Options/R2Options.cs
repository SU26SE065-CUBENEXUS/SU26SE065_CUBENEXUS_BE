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

    /// <summary>
    /// Public CDN base URL for Cloudflare R2 (e.g. https://pub-XXXX.r2.dev).
    /// If set, uploaded evidence photo keys are stored as full public URLs.
    /// Enable public access on your R2 bucket settings to use this.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>Builds a full public URL for the given object key if PublicUrl is configured.</summary>
    public string? GetPublicUrl(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(PublicUrl)) return null;
        var baseUrl = PublicUrl.TrimEnd('/');
        var key = objectKey.TrimStart('/');
        return $"{baseUrl}/{key}";
    }

    public bool HasCredentials()
        => !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey);
}
