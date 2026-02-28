using System.Net.Http.Headers;
using System.Text.Json;
using AppStoreConnect.IpaUploader.Models;
using AppStoreConnect.IpaUploader.Services;

namespace AppStoreConnect.IpaUploader;

/// <summary>
/// Uploads iOS IPA files to App Store Connect using the App Store Connect REST API.
/// </summary>
/// <remarks>
/// Upload flow:
/// 1. A JWT token is generated from your API key credentials.
/// 2. A package upload reservation is created with the App Store Connect API.
/// 3. The IPA file is uploaded in chunks to the pre-signed URLs supplied by Apple.
/// 4. The upload is committed to finalise the operation.
/// </remarks>
public class AppStoreConnectUploader : IDisposable
{
    private const string BaseUrl = "https://contentdelivery.itunes.apple.com/WebObjects/MZLabelService.woa/wo/";
    private const string AppStoreConnectApiBaseUrl = "https://api.appstoreconnect.apple.com/v1";

    private readonly AppStoreConnectConfig _config;
    private readonly JwtTokenService _jwtService;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    /// <summary>
    /// Initialises the uploader with the provided configuration using a new <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="config">App Store Connect API credentials and settings.</param>
    public AppStoreConnectUploader(AppStoreConnectConfig config)
        : this(config, new HttpClient())
    {
    }

    /// <summary>
    /// Initialises the uploader with the provided configuration and an existing <see cref="HttpClient"/>.
    /// Useful for unit testing or when sharing a single <see cref="HttpClient"/> instance.
    /// </summary>
    /// <param name="config">App Store Connect API credentials and settings.</param>
    /// <param name="httpClient">HTTP client to use for all API requests.</param>
    public AppStoreConnectUploader(AppStoreConnectConfig config, HttpClient httpClient)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _jwtService = new JwtTokenService(config);
    }

    /// <summary>
    /// Uploads the specified IPA file to App Store Connect.
    /// </summary>
    /// <param name="ipaFilePath">
    /// Absolute path to the IPA file to upload. The file must exist and be readable.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="UploadResult"/> describing the outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ipaFilePath"/> is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the IPA file does not exist.</exception>
    public async Task<UploadResult> UploadIpaAsync(
        string ipaFilePath,
        CancellationToken cancellationToken = default)
    {
        if (ipaFilePath is null) throw new ArgumentNullException(nameof(ipaFilePath));
        if (!File.Exists(ipaFilePath))
            throw new FileNotFoundException($"IPA file not found: {ipaFilePath}", ipaFilePath);

        try
        {
            var jwtToken = _jwtService.GenerateToken();

            // Step 1: Reserve an upload slot
            var reservation = await CreateUploadReservationAsync(ipaFilePath, jwtToken, cancellationToken);
            if (reservation is null)
                return UploadResult.Failed("Failed to create upload reservation.");

            // Step 2: Upload the file to the pre-signed URL(s) provided by Apple
            var uploadSuccess = await UploadFileAsync(ipaFilePath, reservation, cancellationToken);
            if (!uploadSuccess)
                return UploadResult.Failed("Failed to upload IPA file to the provided upload URL.");

            // Step 3: Commit the upload to trigger processing
            var buildId = await CommitUploadAsync(reservation, jwtToken, cancellationToken);

            return UploadResult.Succeeded(buildId);
        }
        catch (HttpRequestException ex)
        {
            return UploadResult.Failed($"HTTP error during upload: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return UploadResult.Failed("Upload was cancelled.");
        }
        catch (InvalidOperationException ex)
        {
            return UploadResult.Failed($"Configuration error: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<UploadReservation?> CreateUploadReservationAsync(
        string ipaFilePath,
        string jwtToken,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(ipaFilePath);
        var fileName = Path.GetFileName(ipaFilePath);

        // The App Store Connect API uses a "packages" endpoint to reserve upload slots.
        // Reference: https://developer.apple.com/documentation/appstoreconnectapi
        var reservationPayload = new
        {
            data = new
            {
                type = "bundleIds",
                attributes = new
                {
                    name = Path.GetFileNameWithoutExtension(fileName),
                    fileSize = fileInfo.Length,
                    fileName = fileName,
                }
            }
        };

        var json = JsonSerializer.Serialize(reservationPayload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{AppStoreConnectApiBaseUrl}/apps/uploadRequests")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseReservationResponse(responseBody);
    }

    private static UploadReservation? ParseReservationResponse(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Extract the upload ID from the response
            string? uploadId = null;
            string? uploadUrl = null;

            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("id", out var idProp))
                    uploadId = idProp.GetString();

                if (data.TryGetProperty("attributes", out var attrs))
                {
                    if (attrs.TryGetProperty("uploadUrl", out var urlProp))
                        uploadUrl = urlProp.GetString();
                }
            }

            return new UploadReservation { UploadId = uploadId, UploadUrl = uploadUrl };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> UploadFileAsync(
        string ipaFilePath,
        UploadReservation reservation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reservation.UploadUrl))
            return false;

        await using var fileStream = new FileStream(
            ipaFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        fileContent.Headers.ContentLength = fileStream.Length;

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, reservation.UploadUrl)
        {
            Content = fileContent,
        };

        var response = await _httpClient.SendAsync(putRequest, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<string?> CommitUploadAsync(
        UploadReservation reservation,
        string jwtToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reservation.UploadId))
            return null;

        var commitPayload = new
        {
            data = new
            {
                type = "uploadRequests",
                id = reservation.UploadId,
                attributes = new { committed = true }
            }
        };

        var json = JsonSerializer.Serialize(commitPayload);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(
            new HttpMethod("PATCH"),
            $"{AppStoreConnectApiBaseUrl}/apps/uploadRequests/{reservation.UploadId}")
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString();
            }
        }
        catch (JsonException) { /* fall through */ }

        return reservation.UploadId;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    // -------------------------------------------------------------------------
    // Internal model used only within this class
    // -------------------------------------------------------------------------

    private sealed class UploadReservation
    {
        public string? UploadId { get; set; }
        public string? UploadUrl { get; set; }
    }
}
