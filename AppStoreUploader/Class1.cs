using System.Diagnostics;

namespace AppStoreUploader;

public sealed class AppStoreUploadApi
{
    public async Task<AppStoreUploadResult> UploadIpaAsync(AppStoreUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IpaFilePath))
            throw new ArgumentException("IPA file path is required.", nameof(request));
        if (!File.Exists(request.IpaFilePath))
            throw new FileNotFoundException("IPA file was not found.", request.IpaFilePath);
        if (!string.Equals(Path.GetExtension(request.IpaFilePath), ".ipa", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("File must have .ipa extension.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppleId))
            throw new ArgumentException("Apple ID is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AppSpecificPassword))
            throw new ArgumentException("App-specific password is required.", nameof(request));

        var startInfo = new ProcessStartInfo("xcrun")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("altool");
        startInfo.ArgumentList.Add("--upload-app");
        startInfo.ArgumentList.Add("--type");
        startInfo.ArgumentList.Add("ios");
        startInfo.ArgumentList.Add("--file");
        startInfo.ArgumentList.Add(request.IpaFilePath);
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(request.AppleId);
        startInfo.ArgumentList.Add("--password");
        startInfo.ArgumentList.Add(request.AppSpecificPassword);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Could not start upload process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(true);
            throw;
        }

        var output = await stdOutTask;
        var error = await stdErrTask;

        return new AppStoreUploadResult(process.ExitCode == 0, process.ExitCode, output, error);
    }
}

public sealed record AppStoreUploadRequest(string IpaFilePath, string AppleId, string AppSpecificPassword);

public sealed record AppStoreUploadResult(bool Success, int ExitCode, string Output, string Error);
