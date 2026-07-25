using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace VoiceInput.Windows.Transcription;

public sealed record LocalAsrAssets(string RuntimeDirectory, string ModelPath);

public sealed class LocalAsrAssetProvisioner
{
    private const string RuntimeVersion = "0.1.3";
    private const string RuntimeFolderName = "transcribe-native-windows-x86_64-cpu-vulkan";
    private const string RuntimeArchiveName = "transcribe-native-0.1.3-windows-x86_64-cpu-vulkan.tar.gz";
    private const string RuntimeUrl = "https://github.com/handy-computer/transcribe.cpp/releases/download/v0.1.3/transcribe-native-0.1.3-windows-x86_64-cpu-vulkan.tar.gz";
    private const string RuntimeSha256 = "9f536cb0fb839bd305e6d92fb214fd417c7718a416a6c7646a9911fbd56fdad5";

    private const string ModelFileName = "gigaam-v3-e2e-rnnt-Q4_K_M.gguf";
    private const string ModelUrl = "https://huggingface.co/handy-computer/gigaam-v3-e2e-rnnt-gguf/resolve/main/gigaam-v3-e2e-rnnt-Q4_K_M.gguf?download=true";
    private const string ModelSha256 = "7d69952fb431a8d7800ed9910dc61fea37d8406bfe96d10bf24c8bd4b7c68623";

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly string dataDirectory;

    public LocalAsrAssetProvisioner(string? dataDirectory = null)
    {
        this.dataDirectory = Path.GetFullPath(
            dataDirectory
            ?? Environment.GetEnvironmentVariable("VOICE_INPUT_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceInput"));
    }

    public async Task<LocalAsrAssets> EnsureAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var runtimeOverride = Environment.GetEnvironmentVariable("VOICE_INPUT_TRANSCRIBE_RUNTIME");
        var modelOverride = Environment.GetEnvironmentVariable("VOICE_INPUT_GIGAAM_MODEL");
        if (!string.IsNullOrWhiteSpace(runtimeOverride) || !string.IsNullOrWhiteSpace(modelOverride))
        {
            if (string.IsNullOrWhiteSpace(runtimeOverride) || string.IsNullOrWhiteSpace(modelOverride))
            {
                throw new InvalidOperationException(
                    "VOICE_INPUT_TRANSCRIBE_RUNTIME and VOICE_INPUT_GIGAAM_MODEL must be set together.");
            }

            var overrideAssets = new LocalAsrAssets(Path.GetFullPath(runtimeOverride), Path.GetFullPath(modelOverride));
            ValidateAssets(overrideAssets);
            return overrideAssets;
        }

        var downloadsDirectory = Path.Combine(dataDirectory, "downloads");
        var runtimeVersionDirectory = Path.Combine(dataDirectory, "runtime", RuntimeVersion);
        var runtimeDirectory = Path.Combine(runtimeVersionDirectory, RuntimeFolderName);
        var runtimeArchive = Path.Combine(downloadsDirectory, RuntimeArchiveName);
        var modelPath = Path.Combine(dataDirectory, "models", ModelFileName);

        Directory.CreateDirectory(downloadsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);

        progress?.Report("Проверяю локальный runtime…");
        await EnsureFileAsync(RuntimeUrl, runtimeArchive, RuntimeSha256, progress, cancellationToken);

        if (!File.Exists(Path.Combine(runtimeDirectory, "transcribe.dll")))
        {
            progress?.Report("Распаковываю локальный runtime…");
            if (Directory.Exists(runtimeVersionDirectory))
            {
                Directory.Delete(runtimeVersionDirectory, recursive: true);
            }

            Directory.CreateDirectory(runtimeVersionDirectory);
            await using var archive = File.OpenRead(runtimeArchive);
            await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, runtimeVersionDirectory, overwriteFiles: false);
        }

        progress?.Report("Проверяю модель GigaAM…");
        await EnsureFileAsync(ModelUrl, modelPath, ModelSha256, progress, cancellationToken);

        var assets = new LocalAsrAssets(runtimeDirectory, modelPath);
        ValidateAssets(assets);
        return assets;
    }

    private static async Task EnsureFileAsync(
        string url,
        string destination,
        string sha256,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (await AssetIntegrity.VerifySha256Async(destination, sha256, cancellationToken))
        {
            return;
        }

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        var temporary = destination + ".download";
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }

        progress?.Report($"Загружаю {Path.GetFileName(destination)}…");
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = new FileStream(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
        }

        if (!await AssetIntegrity.VerifySha256Async(temporary, sha256, cancellationToken))
        {
            File.Delete(temporary);
            throw new InvalidDataException($"Checksum verification failed for {Path.GetFileName(destination)}.");
        }

        File.Move(temporary, destination, overwrite: true);
    }

    private static void ValidateAssets(LocalAsrAssets assets)
    {
        if (!File.Exists(Path.Combine(assets.RuntimeDirectory, "transcribe.dll")))
        {
            throw new FileNotFoundException("transcribe.dll is missing from the local ASR runtime.");
        }

        if (!File.Exists(assets.ModelPath))
        {
            throw new FileNotFoundException("The local GigaAM model is missing.", assets.ModelPath);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VoiceInput", "0.1"));
        return client;
    }
}
