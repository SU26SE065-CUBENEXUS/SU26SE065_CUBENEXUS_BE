namespace CubeNexus.Application.DTOs.FaceVerification;

public sealed class FaceUploadFile
{
    public required Stream Content { get; init; }
    public required string FileName { get; init; }
    public string? ContentType { get; init; }
}
