# Tests

## AppStoreUploader DLL

This repository now includes a C# class library at `./AppStoreUploader`.

It exposes `AppStoreUploadApi` with `UploadIpaAsync(...)` to upload an iOS `.ipa` file to App Store Connect using Apple tooling (`xcrun altool`).

Example:

```csharp
using AppStoreUploader;

var api = new AppStoreUploadApi();
var result = await api.UploadIpaAsync(
    new AppStoreUploadRequest(
        "/path/to/MyApp.ipa",
        "YOUR_APP_STORE_CONNECT_API_KEY",
        "YOUR_APP_STORE_CONNECT_API_ISSUER"));
```

> Note: Upload requires macOS with Xcode command line tools (`xcrun`) installed and valid Apple credentials.
