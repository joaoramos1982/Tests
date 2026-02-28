namespace AppStoreConnect.IpaUploader.Models;

/// <summary>
/// Represents the result of an IPA upload operation.
/// </summary>
public class UploadResult
{
    /// <summary>
    /// Indicates whether the upload completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The build identifier returned by App Store Connect after a successful upload.
    /// </summary>
    public string? BuildId { get; set; }

    /// <summary>
    /// Human-readable description of the error when <see cref="Success"/> is false.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// HTTP status code returned by the App Store Connect API, if available.
    /// </summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static UploadResult Succeeded(string? buildId = null) =>
        new() { Success = true, BuildId = buildId };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static UploadResult Failed(string errorMessage, int? httpStatusCode = null) =>
        new() { Success = false, ErrorMessage = errorMessage, HttpStatusCode = httpStatusCode };
}
