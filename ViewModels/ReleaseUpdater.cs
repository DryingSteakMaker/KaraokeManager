using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeManager.ViewModels;

public sealed class ReleaseUpdater : IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/DryingSteakMaker/KaraokeManager/releases/latest";
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };

    public ReleaseUpdater()
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KaraokeManager", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateCheckResult> CheckAndStageAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "KaraokeManager.update")))
            return new(false, null, "Tự cập nhật chỉ khả dụng trong bản phát hành đã cài đặt.");

        try
        {
            using var response = await client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new(false, null, "GitHub chưa có bản phát hành nào.");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var releaseVersion))
                return new(false, null, $"Không nhận diện được phiên bản phát hành {tag}.");
            if (releaseVersion <= currentVersion)
                return new(false, null, $"Bạn đang dùng phiên bản mới nhất ({currentVersion}).");

            var rid = GetRuntimeIdentifier();
            if (rid is null) return new(false, null, "Bản phát hành chưa hỗ trợ hệ điều hành hoặc kiến trúc này.");
            var assetName = $"KaraokeManager-{rid}.zip";
            var assets = root.GetProperty("assets").EnumerateArray().ToArray();
            var archive = assets.FirstOrDefault(asset => asset.GetProperty("name").GetString() == assetName);
            var checksum = assets.FirstOrDefault(asset => asset.GetProperty("name").GetString() == assetName + ".sha256");
            if (archive.ValueKind == JsonValueKind.Undefined || checksum.ValueKind == JsonValueKind.Undefined)
                return new(false, null, $"Bản {tag} chưa có gói cập nhật {assetName} kèm mã kiểm tra.");

            var updateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaraokeManager", "updates", releaseVersion.ToString());
            Directory.CreateDirectory(updateRoot);
            var archivePath = Path.Combine(updateRoot, assetName);
            var extractedPath = Path.Combine(updateRoot, "package");
            var archiveUrl = archive.GetProperty("browser_download_url").GetString()!;
            var checksumUrl = checksum.GetProperty("browser_download_url").GetString()!;
            await DownloadAsync(checksumUrl, archivePath + ".sha256", 4096, cancellationToken);
            var expectedHash = (await File.ReadAllTextAsync(archivePath + ".sha256", cancellationToken)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (expectedHash is null || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
                return new(false, null, "Tệp kiểm tra SHA-256 của bản cập nhật không hợp lệ.");

            await DownloadAsync(archiveUrl, archivePath, 1_500_000_000, cancellationToken);
            await using (var stream = File.OpenRead(archivePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    return new(false, null, "Mã SHA-256 không khớp; đã hủy tải bản cập nhật.");
            }

            if (Directory.Exists(extractedPath)) Directory.Delete(extractedPath, recursive: true);
            Directory.CreateDirectory(extractedPath);
            ExtractSafely(archivePath, extractedPath);
            var appHost = OperatingSystem.IsWindows() ? "KaraokeManager.exe" : "KaraokeManager";
            if (!File.Exists(Path.Combine(extractedPath, appHost)) || !File.Exists(Path.Combine(extractedPath, "KaraokeManager.update")))
                return new(false, null, "Gói tải về không có cấu trúc bản phát hành hợp lệ.");
            return new(true, new StagedUpdate(releaseVersion, extractedPath), $"Đã tải phiên bản {releaseVersion}; sẽ cài đặt khi đóng ứng dụng.");
        }
        catch (TaskCanceledException) { return new(false, null, "Kiểm tra cập nhật hết thời gian chờ."); }
        catch (OperationCanceledException) { return new(false, null, "Đã hủy kiểm tra cập nhật."); }
        catch (HttpRequestException) { return new(false, null, "Không thể kết nối GitHub để kiểm tra cập nhật."); }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, null, $"Không thể tải bản cập nhật: {exception.Message}");
        }
    }

    public bool ScheduleApply(StagedUpdate update, int processId, out string error)
    {
        var target = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var parent = Directory.GetParent(target)?.FullName;
        if (parent is null) { error = "Không xác định được thư mục cài đặt."; return false; }
        var backup = target + ".backup-" + update.Version;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var script = Path.Combine(Directory.GetParent(update.PackageDirectory)!.FullName, "apply-update.ps1");
                File.WriteAllText(script, WindowsApplyScript);
                var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, processId.ToString(), update.PackageDirectory, target, backup }) start.ArgumentList.Add(arg);
                if (Process.Start(start) is null) throw new InvalidOperationException("PowerShell không khởi động được.");
            }
            else
            {
                var script = Path.Combine(Directory.GetParent(update.PackageDirectory)!.FullName, "apply-update.sh");
                File.WriteAllText(script, UnixApplyScript);
                var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { script, processId.ToString(), update.PackageDirectory, target, backup }) start.ArgumentList.Add(arg);
                if (Process.Start(start) is null) throw new InvalidOperationException("Trình cài cập nhật không khởi động được.");
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            error = $"Không thể khởi chạy trình cài cập nhật: {exception.Message}";
            return false;
        }
    }

    private async Task DownloadAsync(string url, string path, long maxBytes, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes)
            throw new IOException("Gói cập nhật vượt quá giới hạn kích thước.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > maxBytes) throw new IOException("Gói cập nhật vượt quá giới hạn kích thước.");
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
        }
    }

    private static void ExtractSafely(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        long expandedSize = 0;
        foreach (var entry in archive.Entries)
        {
            expandedSize += entry.Length;
            if (expandedSize > 2_000_000_000) throw new InvalidDataException("Tổng kích thước giải nén vượt quá giới hạn.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new InvalidDataException("Gói cập nhật không được chứa symbolic link.");
            var fullPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!fullPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Gói cập nhật chứa đường dẫn không hợp lệ.");
        }
        archive.ExtractToDirectory(destination);
    }

    private static string? GetRuntimeIdentifier()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };
        if (arch is null) return null;
        if (OperatingSystem.IsWindows()) return $"win-{arch}";
        if (OperatingSystem.IsLinux()) return $"linux-{arch}";
        if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
        return null;
    }

    public void Dispose() => client.Dispose();

    private const string WindowsApplyScript = """
param([int]$ParentProcessId,[string]$Source,[string]$Target,[string]$Backup)
$ErrorActionPreference = 'Stop'
while (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }
$New = "$Target.new"
try {
  if (Test-Path $New) { Remove-Item -Path $New -Recurse -Force }
  New-Item -ItemType Directory -Path $New | Out-Null
  Copy-Item -Path (Join-Path $Source '*') -Destination $New -Recurse -Force
  Move-Item -Path $Target -Destination $Backup
  try { Move-Item -Path $New -Destination $Target }
  catch { Move-Item -Path $Backup -Destination $Target; throw }
} catch { exit 1 }
""";

    private const string UnixApplyScript = """
pid="$1"; source="$2"; target="$3"; backup="$4"
while kill -0 "$pid" 2>/dev/null; do sleep 0.5; done
new="${target}.new"
if [ -e "$backup" ]; then exit 1; fi
if [ -e "$new" ]; then rm -rf "$new" || exit 1; fi
mkdir "$new" || exit 1
cp -R "$source"/. "$new"/ || exit 1
mv "$target" "$backup" || exit 1
if mv "$new" "$target"; then exit 0; fi
mv "$backup" "$target"
exit 1
""";
}

public sealed record StagedUpdate(Version Version, string PackageDirectory);
public sealed record UpdateCheckResult(bool HasUpdate, StagedUpdate? Staged, string Message);
