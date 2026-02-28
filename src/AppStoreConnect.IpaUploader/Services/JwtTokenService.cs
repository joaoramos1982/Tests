using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AppStoreConnect.IpaUploader.Models;

namespace AppStoreConnect.IpaUploader.Services;

/// <summary>
/// Generates short-lived JWT tokens for authenticating with the App Store Connect API.
/// Apple requires tokens signed with ES256 using a P8 private key.
/// </summary>
public class JwtTokenService
{
    private readonly AppStoreConnectConfig _config;

    /// <summary>
    /// Initialises the service with the provided configuration.
    /// </summary>
    /// <param name="config">App Store Connect API credentials.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    public JwtTokenService(AppStoreConnectConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Generates a signed JWT token for use as a Bearer token in App Store Connect API requests.
    /// </summary>
    /// <returns>A signed JWT string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the configuration is incomplete or the private key cannot be parsed.
    /// </exception>
    public string GenerateToken()
    {
        if (string.IsNullOrWhiteSpace(_config.IssuerId))
            throw new InvalidOperationException("IssuerId must be set in configuration.");
        if (string.IsNullOrWhiteSpace(_config.KeyId))
            throw new InvalidOperationException("KeyId must be set in configuration.");
        if (string.IsNullOrWhiteSpace(_config.PrivateKeyContent))
            throw new InvalidOperationException("PrivateKeyContent must be set in configuration.");

        var privateKeyBase64 = _config.PrivateKeyContent
            .Replace("-----BEGIN PRIVATE KEY-----", string.Empty)
            .Replace("-----END PRIVATE KEY-----", string.Empty)
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Trim();

        byte[] privateKeyBytes;
        try
        {
            privateKeyBytes = Convert.FromBase64String(privateKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("PrivateKeyContent is not valid Base64.", ex);
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to import the private key. Ensure PrivateKeyContent is a valid PKCS#8 EC private key.", ex);
        }

        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = _config.KeyId };
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var now = DateTimeOffset.UtcNow;
        var claims = new[]
        {
            new Claim("iss", _config.IssuerId),
            new Claim("aud", "appstoreconnect-v1"),
            new Claim("iat", now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddMinutes(_config.TokenExpiryMinutes).UtcDateTime,
            SigningCredentials = credentials,
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}
