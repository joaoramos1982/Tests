# Tests

## AppStoreUploader DLL

This repository now includes a C# class library at:

`/home/runner/work/Tests/Tests/AppStoreUploader`

It exposes `AppStoreUploadApi` with `UploadIpaAsync(...)` to upload an iOS `.ipa` file to App Store Connect using Apple tooling (`xcrun altool`).

Example:

```csharp
using AppStoreUploader;

var api = new AppStoreUploadApi();
var result = await api.UploadIpaAsync(
    new AppStoreUploadRequest(
        "/path/to/MyApp.ipa",
        "apple-id@example.com",
        "app-specific-password"));
```

> Note: Upload requires macOS with Xcode command line tools (`xcrun`) installed and valid Apple credentials.
