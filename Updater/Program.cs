/* =======================================================
 * Copyright (c) 2026 Orest Zogju. All Rights Reserved.
 * Illyrian Vault — Auto Updater
 * ======================================================= */

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

// ── Configuration ────────────────────────────────────────────────────────────

const string GitHubOwner       = "Orest-Z";
const string GitHubRepo        = "Illyria-Vault";
const string MainExeName       = "Illyrian Vault.exe";           // local filename (with space)
const string GitHubAssetName   = "Illyrian.Vault.exe";           // GitHub converts spaces → dots
const string ApiUrl            = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

// ── Bootstrap ────────────────────────────────────────────────────────────────

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title          = "Illyrian Vault — Updater";

Banner();

// Locate the main exe — must be in the same folder as this updater.
var updaterDir = AppContext.BaseDirectory;
var mainExePath = Path.Combine(updaterDir, MainExeName);

if (!File.Exists(mainExePath))
{
    Error($"Could not find \"{MainExeName}\" in the same folder as this updater.");
    Info("Make sure Updater.exe is placed in the same directory as \"Illyrian Vault.exe\".");
    Pause();
    return 1;
}

// Read the currently installed version from the exe's FileVersion metadata.
var currentVersion = GetFileVersion(mainExePath);
Info($"Installed version : {currentVersion}");

// ── Check GitHub for latest release ──────────────────────────────────────────

Info("Checking for updates...");

ReleaseInfo? release;
try
{
    release = await FetchLatestReleaseAsync();
}
catch (Exception ex)
{
    Error($"Could not reach GitHub: {ex.Message}");
    Info("Check your internet connection and try again.");
    Pause();
    return 1;
}

var latestVersion  = release.TagName.TrimStart('v');
var latestParsed   = TryParseVersion(latestVersion);
var currentParsed  = TryParseVersion(currentVersion);

Info($"Latest version    : {latestVersion}");

if (latestParsed is not null && currentParsed is not null && latestParsed <= currentParsed)
{
    Success("You are already on the latest version. No update needed.");
    Pause();
    return 0;
}

// ── Find the downloadable exe asset ──────────────────────────────────────────

// GitHub replaces spaces with dots in uploaded asset names, so
// "Illyrian Vault.exe" becomes "Illyrian.Vault.exe" on the server.
// Check both the dot form (primary) and the space form, then fall
// back to the first .exe asset found in the release.
var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Equals(GitHubAssetName, StringComparison.OrdinalIgnoreCase))
         ?? release.Assets.FirstOrDefault(a =>
                a.Name.Equals(MainExeName, StringComparison.OrdinalIgnoreCase))
         ?? release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

if (asset is null)
{
    Error("The latest GitHub release does not contain a downloadable .exe asset.");
    Info("Upload \"Illyrian Vault.exe\" as a release asset on GitHub and try again.");
    Pause();
    return 1;
}

// ── Confirm with the user ─────────────────────────────────────────────────────

Console.WriteLine();
Highlight($"  Update available: v{currentVersion}  →  v{latestVersion}");
Highlight($"  Asset           : {asset.Name}  ({FormatBytes(asset.Size)})");
Console.WriteLine();
Console.Write("  Proceed with update? [Y/N]: ");
var answer = Console.ReadLine()?.Trim().ToUpperInvariant();
if (answer != "Y")
{
    Info("Update cancelled.");
    Pause();
    return 0;
}

// ── Close the main app if it is running ──────────────────────────────────────

Console.WriteLine();
var running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExeName));
if (running.Length > 0)
{
    Info($"Closing {running.Length} running instance(s) of Illyrian Vault...");
    foreach (var p in running)
    {
        try
        {
            p.CloseMainWindow();
            if (!p.WaitForExit(4000))
                p.Kill();
        }
        catch { /* process may have already exited */ }
    }
    await Task.Delay(500);
}

// ── Download new exe ─────────────────────────────────────────────────────────

var tempPath = Path.Combine(updaterDir, $"_update_{Guid.NewGuid():N}.exe");
Info($"Downloading {asset.Name}...");

try
{
    await DownloadWithProgressAsync(asset.DownloadUrl, tempPath, asset.Size);
}
catch (Exception ex)
{
    Error($"Download failed: {ex.Message}");
    TryDelete(tempPath);
    Pause();
    return 1;
}

Console.WriteLine();

// ── Swap the files ────────────────────────────────────────────────────────────

// Windows does not allow deleting a running exe, but it DOES allow renaming it.
// Rename the old exe first, drop the new one into its place, then clean up.
var backupPath = mainExePath + ".old";
try
{
    TryDelete(backupPath);                    // remove any leftover from a previous update
    File.Move(mainExePath, backupPath);       // rename old  (works even if process was open)
    File.Move(tempPath,    mainExePath);      // drop in new
    TryDelete(backupPath);                    // clean up old
}
catch (Exception ex)
{
    Error($"Could not replace the executable: {ex.Message}");
    Info("Try running this updater as Administrator, or close all vault windows first.");
    TryDelete(tempPath);
    Pause();
    return 1;
}

// ── Launch the updated app ───────────────────────────────────────────────────

Success($"Successfully updated to v{latestVersion}!");
Info("Launching Illyrian Vault...");
await Task.Delay(800);

try
{
    Process.Start(new ProcessStartInfo(mainExePath) { UseShellExecute = true });
}
catch (Exception ex)
{
    Error($"Could not launch the app automatically: {ex.Message}");
    Info($"Please open \"{MainExeName}\" manually.");
}

return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

static void Banner()
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine("  ██╗██╗     ██╗  ██╗   ██╗██████╗ ██╗ █████╗ ███╗   ██╗");
    Console.WriteLine("  ██║██║     ██║  ╚██╗ ██╔╝██╔══██╗██║██╔══██╗████╗  ██║");
    Console.WriteLine("  ██║██║     ██║   ╚████╔╝ ██████╔╝██║███████║██╔██╗ ██║");
    Console.WriteLine("  ██║██║     ██║    ╚██╔╝  ██╔══██╗██║██╔══██║██║╚██╗██║");
    Console.WriteLine("  ██║███████╗███████╗██║   ██║  ██║██║██║  ██║██║ ╚████║");
    Console.WriteLine("  ╚═╝╚══════╝╚══════╝╚═╝   ╚═╝  ╚═╝╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  ─────────────────────────────────────────────────────────");
    Console.WriteLine("                       Auto Updater");
    Console.WriteLine("  ─────────────────────────────────────────────────────────");
    Console.ResetColor();
    Console.WriteLine();
}

static void Info(string msg)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  [·] ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static void Success(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("  [✓] ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static void Error(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write("  [✗] ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static void Highlight(string msg)
{
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine(msg);
    Console.ResetColor();
}

static void Pause()
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  Press any key to exit...");
    Console.ResetColor();
    Console.ReadKey(intercept: true);
}

static string GetFileVersion(string exePath)
{
    try
    {
        var info = FileVersionInfo.GetVersionInfo(exePath);
        return info.FileVersion ?? "unknown";
    }
    catch { return "unknown"; }
}

static Version? TryParseVersion(string raw)
{
    // Normalise e.g. "1.5.5" → "1.5.5.0" so Version.TryParse always succeeds.
    var parts = raw.Split('.');
    var normalised = parts.Length switch
    {
        1 => $"{raw}.0.0.0",
        2 => $"{raw}.0.0",
        3 => $"{raw}.0",
        _ => raw,
    };
    return Version.TryParse(normalised, out var v) ? v : null;
}

static async Task<ReleaseInfo> FetchLatestReleaseAsync()
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue("IllyrianVaultUpdater", "1.5.5"));
    http.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

    var json = await http.GetStringAsync(ApiUrl);
    return JsonSerializer.Deserialize<ReleaseInfo>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("GitHub returned an empty response.");
}

static async Task DownloadWithProgressAsync(string url, string dest, long totalBytes)
{
    using var http = new HttpClient();
    http.DefaultRequestHeaders.UserAgent.Add(
        new ProductInfoHeaderValue("IllyrianVaultUpdater", "1.5.5"));

    using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    await using var src  = await response.Content.ReadAsStreamAsync();
    await using var file = File.Create(dest);

    var buffer    = new byte[81920];
    long received = 0;
    int  read;
    int  lastPct  = -1;

    while ((read = await src.ReadAsync(buffer)) > 0)
    {
        await file.WriteAsync(buffer.AsMemory(0, read));
        received += read;

        if (totalBytes > 0)
        {
            int pct = (int)(received * 100 / totalBytes);
            if (pct != lastPct)
            {
                lastPct = pct;
                DrawProgressBar(pct, received, totalBytes);
            }
        }
    }
}

static void DrawProgressBar(int pct, long received, long total)
{
    const int barWidth = 40;
    int filled = barWidth * pct / 100;

    Console.CursorLeft = 0;
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  [");
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Write(new string('█', filled));
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write(new string('░', barWidth - filled));
    Console.Write($"]  {pct,3}%  {FormatBytes(received)} / {FormatBytes(total)}   ");
    Console.ResetColor();
}

static string FormatBytes(long bytes) => bytes switch
{
    < 1024               => $"{bytes} B",
    < 1024 * 1024        => $"{bytes / 1024.0:F1} KB",
    < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
    _                    => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
};

static void TryDelete(string path)
{
    try { if (File.Exists(path)) File.Delete(path); } catch { }
}

// ── GitHub API response models ────────────────────────────────────────────────

record ReleaseInfo(
    [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")]
    string          TagName,
    [property: System.Text.Json.Serialization.JsonPropertyName("assets")]
    List<AssetInfo> Assets);

record AssetInfo(
    string Name,
    long   Size,
    [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
    string DownloadUrl);
