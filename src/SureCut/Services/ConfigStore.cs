using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using SureCut.Models;

namespace SureCut.Services;

/// <summary>
/// DATA-1..3: one JSON file, atomic replace via temp + move, debounced writes, defaults and
/// a .bak when the file cannot be parsed.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly DispatcherTimer _debounce;
    private AppConfig? _pending;

    public string Folder { get; }
    public string ConfigPath => Path.Combine(Folder, "config.json");
    public string IconsFolder => Path.Combine(Folder, "icons");

    public ConfigStore(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(IconsFolder);
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => Flush();
    }

    public AppConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
        {
            Logger.Info("No config found; starting with defaults.");
            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (cfg is null) throw new JsonException("config deserialized to null");
            if (cfg.Version > AppConfig.CurrentVersion)
            {
                // Written by a newer SureCut. Unknown fields survive via [JsonExtensionData]; keep a
                // one-time copy so nothing is lost if this version rewrites the file.
                Logger.Warn($"Config version {cfg.Version} is newer than this build supports ({AppConfig.CurrentVersion}); loading what is understood.");
                var keep = path + $".v{cfg.Version}.bak";
                if (!File.Exists(keep)) File.Copy(path, keep);
            }

            cfg.Position ??= ButtonPosition.Default();
            cfg.Position.Normalize();
            cfg.ButtonSize = AppConfig.NormalizeSize(cfg.ButtonSize);
            if (string.IsNullOrWhiteSpace(cfg.Hotkey)) cfg.Hotkey = null;
            cfg.Favorites ??= new List<Favorite>();
            cfg.Favorites.RemoveAll(f => f is null || string.IsNullOrWhiteSpace(f.Target));
            foreach (var f in cfg.Favorites)
            {
                if (string.IsNullOrWhiteSpace(f.Id)) f.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(f.Name)) f.Name = Path.GetFileNameWithoutExtension(f.Target);
                f.Args ??= "";
                f.WorkingDir ??= "";
                f.IconCache ??= "";
            }
            return cfg;
        }
        catch (Exception ex)
        {
            Logger.Error("Config unparseable; backing up and using defaults.", ex);
            try { File.Copy(path, path + ".bak", overwrite: true); }
            catch (Exception bex) { Logger.Error("Could not write config.json.bak", bex); }
            return new AppConfig();
        }
    }

    /// <summary>Schedules a write 500 ms after the last change (DATA-2).</summary>
    public void Save(AppConfig config)
    {
        _pending = config;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Writes immediately if a save is pending. Call on exit.</summary>
    public void Flush()
    {
        _debounce.Stop();
        var cfg = _pending;
        if (cfg is null) return;
        _pending = null;
        WriteAtomic(cfg);
    }

    private void WriteAtomic(AppConfig cfg)
    {
        var path = ConfigPath;
        var tmp = path + ".tmp";
        try
        {
            cfg.Version = Math.Max(cfg.Version, AppConfig.CurrentVersion); // never downgrade a newer file's marker
            var json = JsonSerializer.Serialize(cfg, JsonOptions);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to write config.", ex);
        }
    }
}
