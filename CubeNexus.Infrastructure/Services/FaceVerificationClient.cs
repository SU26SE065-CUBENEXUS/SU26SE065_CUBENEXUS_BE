using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CubeNexus.Application.DTOs.FaceVerification;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CubeNexus.Infrastructure.Services;

public class FaceVerificationClient : IFaceVerificationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<FaceVerificationClient> _logger;

    public FaceVerificationClient(
        HttpClient httpClient,
        IOptions<FaceVerificationOptions> options,
        ILogger<FaceVerificationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var opts = options.Value;
        _httpClient.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(30, opts.TimeoutSeconds));
    }

    public Task<FaceAiCreateSessionResponse> CreateEnrollmentSessionAsync(
        FaceAiCreateSessionRequest request,
        CancellationToken cancellationToken = default)
        => PostJsonAsync<FaceAiCreateSessionResponse>("verification/enrollment-sessions", request, cancellationToken);

    public Task<FaceAiCreateSessionResponse> CreateVerificationSessionAsync(
        FaceAiCreateSessionRequest request,
        CancellationToken cancellationToken = default)
        => PostJsonAsync<FaceAiCreateSessionResponse>("verification/sessions", request, cancellationToken);

    public async Task<FaceAiSessionResultResponse> SubmitEnrollmentEvidenceAsync(
        string externalSessionId,
        string uploadToken,
        string metadataJson,
        Stream? evidenceVideo,
        string? evidenceVideoFileName,
        string? evidenceVideoContentType,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType)> images,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(uploadToken), "uploadToken");
        form.Add(new StringContent(metadataJson), "metadata");
        if (evidenceVideo is not null && !string.IsNullOrWhiteSpace(evidenceVideoFileName))
        {
            form.Add(
                CreateFileContent(evidenceVideo, evidenceVideoFileName, evidenceVideoContentType),
                "evidenceVideo",
                evidenceVideoFileName);
        }
        foreach (var image in images)
        {
            form.Add(CreateFileContent(image.Content, image.FileName, image.ContentType), "images", image.FileName);
        }

        using var response = await _httpClient.PostAsync(
            $"verification/enrollment-sessions/{Uri.EscapeDataString(externalSessionId)}/evidence",
            form,
            cancellationToken);
        return await ReadJsonAsync<FaceAiSessionResultResponse>(response, cancellationToken);
    }

    public async Task<FaceAiSessionResultResponse> SubmitPassiveEvidenceAsync(
        string externalSessionId,
        string uploadToken,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType)> finalFrames,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(uploadToken), "uploadToken");
        foreach (var frame in finalFrames)
        {
            form.Add(CreateFileContent(frame.Content, frame.FileName, frame.ContentType), "finalFrames", frame.FileName);
        }

        using var response = await _httpClient.PostAsync(
            $"verification/sessions/{Uri.EscapeDataString(externalSessionId)}/passive-evidence",
            form,
            cancellationToken);
        return await ReadJsonAsync<FaceAiSessionResultResponse>(response, cancellationToken);
    }

    public async Task<FaceAiSessionResultResponse> SubmitActiveEvidenceAsync(
        string externalSessionId,
        string uploadToken,
        string metadataJson,
        Stream? evidenceVideo,
        string? evidenceVideoFileName,
        string? evidenceVideoContentType,
        IReadOnlyList<(Stream Content, string FileName, string? ContentType)> finalFrames,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(uploadToken), "uploadToken");
        form.Add(new StringContent(metadataJson), "metadata");
        if (evidenceVideo is not null && !string.IsNullOrWhiteSpace(evidenceVideoFileName))
        {
            form.Add(CreateFileContent(evidenceVideo, evidenceVideoFileName, evidenceVideoContentType), "evidenceVideo", evidenceVideoFileName);
        }

        foreach (var frame in finalFrames)
        {
            form.Add(CreateFileContent(frame.Content, frame.FileName, frame.ContentType), "finalFrames", frame.FileName);
        }

        using var response = await _httpClient.PostAsync(
            $"verification/sessions/{Uri.EscapeDataString(externalSessionId)}/evidence",
            form,
            cancellationToken);
        return await ReadJsonAsync<FaceAiSessionResultResponse>(response, cancellationToken);
    }

    public async Task<object?> AnalyzeFrameAsync(Stream frame, string fileName, string? contentType, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(CreateFileContent(frame, fileName, contentType), "frame", fileName);
        using var response = await _httpClient.PostAsync("verification/analyze-frame", form, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<object>(JsonOptions, cancellationToken);
    }

    public async Task<object?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<object>("health", JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Face verification health check failed");
            return null;
        }
    }

    private async Task<T> PostJsonAsync<T>(string url, object body, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private static StreamContent CreateFileContent(Stream stream, string fileName, string? contentType)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        return content;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Face AI returned {(int)response.StatusCode}: {raw}",
                null,
                response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<T>(raw, JsonOptions);
        if (result is null)
        {
            throw new InvalidOperationException("Face AI returned an empty body.");
        }

        return result;
    }
}
