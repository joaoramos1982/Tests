using System.Net;
using System.Security.Cryptography;
using AppStoreConnect.IpaUploader.Models;
using AppStoreConnect.IpaUploader.Services;

namespace AppStoreConnect.IpaUploader.Tests;

public class JwtTokenServiceTests
{
    private static string GenerateTestP8Key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKeyBytes = ecdsa.ExportPkcs8PrivateKey();
        var base64 = Convert.ToBase64String(privateKeyBytes, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN PRIVATE KEY-----\n{base64}\n-----END PRIVATE KEY-----";
    }

    private static AppStoreConnectConfig CreateValidConfig() => new()
    {
        IssuerId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        KeyId = "ABCD123456",
        PrivateKeyContent = GenerateTestP8Key(),
        TokenExpiryMinutes = 20,
    };

    [Fact]
    public void GenerateToken_WithValidConfig_ReturnsNonEmptyToken()
    {
        var service = new JwtTokenService(CreateValidConfig());

        var token = service.GenerateToken();

        Assert.NotNull(token);
        Assert.NotEmpty(token);
        // JWT tokens consist of three Base64url parts separated by dots
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void GenerateToken_TokenContainsExpectedClaims()
    {
        var config = CreateValidConfig();
        var service = new JwtTokenService(config);

        var token = service.GenerateToken();

        // Decode the payload (second part of the JWT)
        var parts = token.Split('.');
        var payloadBase64 = parts[1];
        // Pad for Base64 decoding
        payloadBase64 = payloadBase64.PadRight(payloadBase64.Length + (4 - payloadBase64.Length % 4) % 4, '=');
        var payloadBytes = Convert.FromBase64String(payloadBase64);
        var payload = System.Text.Encoding.UTF8.GetString(payloadBytes);

        Assert.Contains(config.IssuerId, payload);
        Assert.Contains("appstoreconnect-v1", payload);
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new JwtTokenService(null!));
    }

    [Fact]
    public void GenerateToken_MissingIssuerId_ThrowsInvalidOperationException()
    {
        var config = CreateValidConfig();
        config.IssuerId = string.Empty;
        var service = new JwtTokenService(config);

        Assert.Throws<InvalidOperationException>(() => service.GenerateToken());
    }

    [Fact]
    public void GenerateToken_MissingKeyId_ThrowsInvalidOperationException()
    {
        var config = CreateValidConfig();
        config.KeyId = string.Empty;
        var service = new JwtTokenService(config);

        Assert.Throws<InvalidOperationException>(() => service.GenerateToken());
    }

    [Fact]
    public void GenerateToken_MissingPrivateKey_ThrowsInvalidOperationException()
    {
        var config = CreateValidConfig();
        config.PrivateKeyContent = string.Empty;
        var service = new JwtTokenService(config);

        Assert.Throws<InvalidOperationException>(() => service.GenerateToken());
    }

    [Fact]
    public void GenerateToken_InvalidBase64PrivateKey_ThrowsInvalidOperationException()
    {
        var config = CreateValidConfig();
        config.PrivateKeyContent = "-----BEGIN PRIVATE KEY-----\nNOT_VALID_BASE64!!!\n-----END PRIVATE KEY-----";
        var service = new JwtTokenService(config);

        Assert.Throws<InvalidOperationException>(() => service.GenerateToken());
    }
}

public class UploadResultTests
{
    [Fact]
    public void Succeeded_ReturnsSuccessfulResult()
    {
        var result = UploadResult.Succeeded("build-123");

        Assert.True(result.Success);
        Assert.Equal("build-123", result.BuildId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Failed_ReturnsFailedResult()
    {
        var result = UploadResult.Failed("Something went wrong", 400);

        Assert.False(result.Success);
        Assert.Equal("Something went wrong", result.ErrorMessage);
        Assert.Equal(400, result.HttpStatusCode);
        Assert.Null(result.BuildId);
    }
}

public class AppStoreConnectUploaderTests
{
    private static AppStoreConnectConfig CreateConfig() => new()
    {
        IssuerId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        KeyId = "ABCD123456",
        PrivateKeyContent = GenerateTestP8Key(),
    };

    private static string GenerateTestP8Key()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = ecdsa.ExportPkcs8PrivateKey();
        var b64 = Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN PRIVATE KEY-----\n{b64}\n-----END PRIVATE KEY-----";
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new AppStoreConnectUploader(null!));
    }

    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AppStoreConnectUploader(CreateConfig(), null!));
    }

    [Fact]
    public async Task UploadIpaAsync_NullPath_ThrowsArgumentNullException()
    {
        using var uploader = new AppStoreConnectUploader(CreateConfig(), new HttpClient());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            uploader.UploadIpaAsync(null!));
    }

    [Fact]
    public async Task UploadIpaAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        using var uploader = new AppStoreConnectUploader(CreateConfig(), new HttpClient());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            uploader.UploadIpaAsync("/nonexistent/path/app.ipa"));
    }

    [Fact]
    public async Task UploadIpaAsync_ServerReturnsError_ReturnsFailedResult()
    {
        // Arrange: create a temp file to act as the IPA
        var tmpFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmpFile, new byte[] { 0x50, 0x4B, 0x03, 0x04 });

            var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, "{\"errors\":[]}");
            using var httpClient = new HttpClient(handler);
            using var uploader = new AppStoreConnectUploader(CreateConfig(), httpClient);

            var result = await uploader.UploadIpaAsync(tmpFile);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    // -------------------------------------------------------------------------
    // Fake HTTP handler for unit testing
    // -------------------------------------------------------------------------

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody),
            };
            return Task.FromResult(response);
        }
    }
}
