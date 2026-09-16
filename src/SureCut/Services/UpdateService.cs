using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace SureCut.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, ReadyToRestart, Failed }

/// <summary>
/// Checks the GitHub Releases feed for a newer SureCut, downloads the portable exe, and swaps
/// it in place of the running one via a small helper that waits for this process to exit.
/// </summary>
public sealed class UpdateService
{
    private const string Owner = "LodiAniez";
    private const string Repo = "surecut";
    private const string AssetName = "SureCut.exe";
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static readonly HttpClient Http = CreateClient();

    private readonly string _updateFolder;
    private DispatcherTimer? _timer;
    private bool _busy;

    public Version Installed { get; }
    public Version? Latest { get; private set; }
    public string? ReleaseUrl { get; private set; }
    public UpdateState State { get; private set; } = UpdateState.Idle;
    public int Progress { get; private set; }
    public string? Error { get; private set; }
    public DateTime? LastChecked { get; private set; }

    private string? _downloadUrl;
    private long _assetSize;

    public bool IsUpdateAvailable => Latest is not null && Latest > Installed;

    public event Action? Changed;

    public UpdateService(string dataFolder)
    {
        _updateFolder = Path.Combine(dataFolder, "update");
        var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        Installed = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SureCut", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    // ---------------------------------------------------------------- scheduling

    public void StartPeriodicChecks()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = InitialDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync();
        };
        _timer.Start();
    }

    public void StopPeriodicChecks()
    {
        _timer?.Stop();
        _timer = null;
    }

    // ---------------------------------------------------------------- check

    public async Task CheckAsync()
    {
        if (_busy || State is UpdateState.Downloading or UpdateState.ReadyToRestart) return;
        _busy = true;
        Set(UpdateState.Checking, error: null);
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                throw new FormatException($"Unrecognized tag '{tag}'");
            Latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            ReleaseUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            _downloadUrl = null;
            _assetSize = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        _downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        _assetSize = asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0;
                        break;
                    }
                }
            }

            LastChecked = DateTime.Now;
            if (IsUpdateAvailable && _downloadUrl is not null)
            {
                Logger.Info($"Update available: {Latest} (installed {Installed}).");
                Set(UpdateState.Available);
            }
            else
            {
                Set(UpdateState.UpToDate);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Update check failed: {ex.Message}");
            Set(UpdateState.Failed, "Couldn't check for updates. Try again later.");
        }
        finally { _busy = false; }
    }

    // ---------------------------------------------------------------- download + apply

    /// <summary>
    /// Downloads the new exe, then starts a helper that replaces the running exe once this
    /// process exits and relaunches it. <paramref name="quit"/> is invoked when the helper is
    /// running and it is safe to exit.
    /// </summary>
    public async Task DownloadAndInstallAsync(Action quit)
    {
        if (_busy || _downloadUrl is null || Latest is null) return;
        _busy = true;
        Progress = 0;
        Set(UpdateState.Downloading, error: null);

        var target = Path.Combine(_updateFolder, $"SureCut-{Latest}.exe");
        var partial = target + ".partial";
        try
        {
            Directory.CreateDirectory(_updateFolder);

            using (var response = await Http.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? _assetSize;
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buffer = new byte[1 << 16];
                long read = 0;
                int n;
                var lastReport = -1;
                while ((n = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                    {
                        var pct = (int)(read * 100 / total);
                        if (pct != lastReport) { lastReport = pct; Progress = pct; Changed?.Invoke(); }
                    }
                }
            }

            var length = new FileInfo(partial).Length;
            if (_assetSize > 0 && length != _assetSize)
                throw new IOException($"Download incomplete ({length} of {_assetSize} bytes).");
            File.Move(partial, target, overwrite: true);

            var current = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the running exe path.");
            LaunchApplyHelper(target, current);

            Logger.Info($"Update {Latest} downloaded; handing over to the apply helper.");
            Set(UpdateState.ReadyToRestart);
            quit();
        }
        catch (Exception ex)
        {
            Logger.Error("Update download/apply failed.", ex);
            try { if (File.Exists(partial)) File.Delete(partial); } catch { /* ignore */ }
            Set(UpdateState.Failed, "Download failed. Check your connection and try again.");
        }
        finally { _busy = false; }
    }

    private void LaunchApplyHelper(string newExe, string currentExe)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var script = $$"""
            $procId = {{Environment.ProcessId}}
            $src = {{Q(newExe)}}
            $dst = {{Q(currentExe)}}
            try { Wait-Process -Id $procId -Timeout 60 -ErrorAction SilentlyContinue } catch { }
            $ok = $false
            for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
              try { Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop; $ok = $true }
              catch { Start-Sleep -Milliseconds 500 }
            }
            if ($ok) { Remove-Item -LiteralPath $src -Force -ErrorAction SilentlyContinue }
            Start-Process -FilePath $dst
            Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            """;
        var scriptPath = Path.Combine(_updateFolder, "apply-update.ps1");
        File.WriteAllText(scriptPath, script);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }

    private void Set(UpdateState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }
}
