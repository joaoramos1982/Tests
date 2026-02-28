namespace AppStoreConnect.IpaUploader.Models;

/// <summary>
/// Configuration required to authenticate with the App Store Connect API.
/// </summary>
public class AppStoreConnectConfig
{
    /// <summary>
    /// The issuer ID from your App Store Connect API key page (UUID format).
    /// </summary>
    public string IssuerId { get; set; } = string.Empty;

    /// <summary>
    /// The key ID for your App Store Connect API key (e.g. "ABC1234DEF").
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// The full PEM content of your App Store Connect API private key (.p8 file),
    /// including the "-----BEGIN PRIVATE KEY-----" header and footer lines.
    /// </summary>
    public string PrivateKeyContent { get; set; } = string.Empty;

    /// <summary>
    /// Number of minutes the generated JWT token remains valid. Default is 20.
    /// App Store Connect tokens must expire within 20 minutes.
    /// </summary>
    public int TokenExpiryMinutes { get; set; } = 20;
}
