using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using CubeNexus.Application.DTOs.OnlineArena;
using CubeNexus.Application.Interfaces.Services;
using CubeNexus.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CubeNexus.Infrastructure.Services;

public class AiRubikClient : IAiRubikClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<AiRubikClient> _logger;
    private readonly AiRubikOptions _options;

    public AiRubikClient(HttpClient httpClient, IOptions<AiRubikOptions> options, ILogger<AiRubikClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-Api-Key");
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", _options.ApiKey);
        }
    }

    private bool IsClientTimeout(Exception ex, CancellationToken cancellationToken)
        => !cancellationToken.IsCancellationRequested
           && (ex is TaskCanceledException || ex is OperationCanceledException || ex.InnerException is TimeoutException);

    private static bool ShouldFallbackToBase64(HttpResponseMessage response)
        => response.StatusCode is System.Net.HttpStatusCode.NotFound
            or System.Net.HttpStatusCode.MethodNotAllowed
            or System.Net.HttpStatusCode.UnsupportedMediaType
            or System.Net.HttpStatusCode.UnprocessableEntity
            or System.Net.HttpStatusCode.BadRequest;

    public async Task<AiRubikHealthDto?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AiRubikHealthDto>("health", JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call AI Rubik health endpoint at {BaseUrl}", _options.BaseUrl);
            return null;
        }
    }

    public Task<AiRubikCheckResultDto> PreCheckAsync(AiRubikCheckRequestDto request, CancellationToken cancellationToken = default)
        => PostCheckAsync("ai/pre-check", request, cancellationToken);

    public Task<AiRubikCheckResultDto> ScrambleCheckAsync(AiRubikCheckRequestDto request, CancellationToken cancellationToken = default)
        => PostCheckAsync("ai/scramble-check", request, cancellationToken);

    public Task<AiRubikCheckResultDto> FinishCheckAsync(AiRubikCheckRequestDto request, CancellationToken cancellationToken = default)
        => PostCheckAsync("ai/finish-check", request, cancellationToken);

    public async Task<AiRubikScannerSessionDto> StartScannerTestSessionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("ai/scanner-test/session/start", new { metadata = new { source = "cubenexus-api" } }, JsonOptions, cancellationToken);
        return await ReadScannerResponseAsync(response, cancellationToken);
    }

    public async Task<AiRubikScannerSessionDto> GetScannerTestSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"ai/scanner-test/session/{Uri.EscapeDataString(sessionId)}", cancellationToken);
        return await ReadScannerResponseAsync(response, cancellationToken);
    }

    public async Task<AiRubikScannerPreviewDto> PreviewScannerTestFrameAsync(string sessionId, byte[] imageBytes, string fileName, string? contentType, Dictionary<string, object?> metadata, CancellationToken cancellationToken = default)
    {
        return await PostScannerPreviewAsync($"ai/scanner-test/session/{Uri.EscapeDataString(sessionId)}/preview", imageBytes, fileName, contentType, metadata, cancellationToken);
    }

    public async Task<AiRubikScannerPreviewDto> ObserveScannerTestFrameAsync(string sessionId, byte[] imageBytes, string fileName, string? contentType, Dictionary<string, object?> metadata, CancellationToken cancellationToken = default)
        => await PostScannerPreviewAsync($"ai/scanner-test/session/{Uri.EscapeDataString(sessionId)}/observe", imageBytes, fileName, contentType, metadata, cancellationToken);

    public async Task<AiRubikScannerSessionDto> ScanScannerTestFaceAsync(string sessionId, IReadOnlyCollection<string> framesBase64, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"ai/scanner-test/session/{Uri.EscapeDataString(sessionId)}/scan-face",
            new { framesBase64, metadata = new { source = "cubenexus-api" } },
            JsonOptions,
            cancellationToken);

        return await ReadScannerResponseAsync(response, cancellationToken);
    }

    public async Task<AiRubikScannerSessionDto> RetryScannerTestFaceAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"ai/scanner-test/session/{Uri.EscapeDataString(sessionId)}/retry-face", null, cancellationToken);
        return await ReadScannerResponseAsync(response, cancellationToken);
    }

    public async Task<AiRubikScannerSessionDto> ResetScannerTestSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync($"ai/scanner-test/session/{Uri.EscapeDataString(sessionId)}/reset", null, cancellationToken);
        return await ReadScannerResponseAsync(response, cancellationToken);
    }

    private async Task<AiRubikCheckResultDto> PostCheckAsync(string relativeUrl, AiRubikCheckRequestDto request, CancellationToken cancellationToken)
    {
        if (request.ImageBytes is { Length: > 0 })
        {
            var multipartResult = await TryPostCheckMultipartAsync(relativeUrl, request, cancellationToken);
            if (multipartResult is not null)
                return multipartResult;
        }

        var payload = new
        {
            matchId = request.MatchId,
            playerId = request.PlayerId,
            scrambleSequence = request.ScrambleSequence,
            imageBase64 = request.ImageBase64,
            imageUrl = request.ImageUrl,
            metadata = request.Metadata,
            evidence = new
            {
                imageBase64 = request.ImageBase64,
                imageUrl = request.ImageUrl,
                storageKey = request.ImageUrl
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(relativeUrl, payload, JsonOptions, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI Rubik endpoint {RelativeUrl} failed with {StatusCode}: {Body}", relativeUrl, response.StatusCode, content);
                return BuildUnavailable(request.CheckType, $"AI service returned {(int)response.StatusCode}.");
            }

            var result = JsonSerializer.Deserialize<AiRubikCheckResultDto>(content, JsonOptions);
            return result ?? BuildUnavailable(request.CheckType, "AI service response was empty.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call AI Rubik endpoint {RelativeUrl}", relativeUrl);
            return BuildUnavailable(request.CheckType, "AI service is unavailable or timed out.");
        }
    }

    private async Task<AiRubikCheckResultDto?> TryPostCheckMultipartAsync(string relativeUrl, AiRubikCheckRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(request.ImageBytes!);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(request.ImageContentType) ? "image/jpeg" : request.ImageContentType);
            form.Add(fileContent, "snapshot", request.ImageFileName ?? "snapshot.jpg");
            form.Add(new StringContent(request.MatchId.ToString()), "matchId");
            form.Add(new StringContent(request.PlayerId.ToString()), "playerId");
            form.Add(new StringContent(request.CheckType), "checkType");
            if (!string.IsNullOrWhiteSpace(request.ScrambleSequence))
                form.Add(new StringContent(request.ScrambleSequence), "scrambleSequence");

            using var response = await _httpClient.PostAsync(relativeUrl, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (ShouldFallbackToBase64(response))
                {
                    _logger.LogInformation("AI Rubik endpoint {RelativeUrl} does not support multipart yet ({StatusCode}). Falling back to base64 payload.", relativeUrl, response.StatusCode);
                    return null;
                }

                _logger.LogWarning("AI Rubik multipart endpoint {RelativeUrl} failed with {StatusCode}: {Body}", relativeUrl, response.StatusCode, body);
                return BuildUnavailable(request.CheckType, $"AI service returned {(int)response.StatusCode}.");
            }

            var result = JsonSerializer.Deserialize<AiRubikCheckResultDto>(body, JsonOptions);
            return result ?? BuildUnavailable(request.CheckType, "AI service response was empty.");
        }
        catch (Exception ex) when (IsClientTimeout(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "AI Rubik multipart endpoint {RelativeUrl} timed out", relativeUrl);
            return BuildUnavailable(request.CheckType, $"AI service did not respond within {_options.TimeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "AI Rubik multipart call for {RelativeUrl} failed before fallback. Continuing with base64 payload.", relativeUrl);
            return null;
        }
    }

    private static AiRubikCheckResultDto BuildUnavailable(string checkType, string reason)
        => new()
        {
            CheckType = checkType,
            Status = "AI_CHECK_UNAVAILABLE",
            Confidence = 0.0,
            DetectedCube = false,
            DetectedStickers = 0,
            Grid3x3 = null,
            Reason = reason,
            ModelVersion = "unknown",
            ModelLoaded = false,
            CreatedAt = DateTime.UtcNow
        };

    private static async Task<AiRubikScannerSessionDto> ReadScannerResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<AiRubikScannerSessionDto>(body, JsonOptions)
               ?? throw new InvalidOperationException("AI scanner session response was empty.");
    }

    private async Task<AiRubikScannerPreviewDto> PostScannerPreviewAsync(string relativeUrl, byte[] imageBytes, string fileName, string? contentType, Dictionary<string, object?> metadata, CancellationToken cancellationToken)
    {
        try
        {
            // Optimization: always use multipart — no base64 fallback overhead per frame.
            // The /observe and /preview endpoints are Python AI service endpoints that
            // always support multipart. Removing the fallback eliminates the extra 
            // base64 re-encode round-trip that was doubling latency on every frame scan.
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
            form.Add(fileContent, "snapshot", string.IsNullOrWhiteSpace(fileName) ? "snapshot.jpg" : fileName);

            foreach (var pair in metadata)
            {
                if (pair.Value is null)
                    continue;
                form.Add(new StringContent(pair.Value.ToString() ?? string.Empty), pair.Key);
            }

            using var response = await _httpClient.PostAsync(relativeUrl, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<AiRubikScannerPreviewDto>(body, JsonOptions)
                   ?? throw new InvalidOperationException("AI scanner preview response was empty.");
        }
        catch (Exception ex) when (IsClientTimeout(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "AI scanner preview timed out after {TimeoutSeconds}s for {RelativeUrl}", _options.TimeoutSeconds, relativeUrl);
            throw new TimeoutException($"AI scanner did not respond within {_options.TimeoutSeconds} seconds. Please keep the cube fully visible, reduce glare, and try again.");
        }
    }
}
