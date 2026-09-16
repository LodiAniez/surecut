using System.Text.Json;
using System.Text.Json.Serialization;

namespace SureCut.Models;

/// <summary>Persisted application state (PRD §5.5, DATA-1).</summary>
public sealed class AppConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public ButtonPosition Position { get; set; } = ButtonPosition.Default();

    /// <summary>"small" | "medium" | "large" (SET-5).</summary>
    public string ButtonSize { get; set; } = "medium";

    /// <summary>SET-2, default OFF.</summary>
    public bool HideOnLaunch { get; set; }

    /// <summary>SET-4, default ON.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>SET-3a. Null when unbound (combination was taken).</summary>
    public string? Hotkey { get; set; } = "Ctrl+Alt+Space";

    public List<Favorite> Favorites { get; set; } = new();

    /// <summary>Unknown fields are preserved across rewrites (DATA-3).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public const string SizeSmall = "small", SizeMedium = "medium", SizeLarge = "large";

    /// <summary>Maps any input to one of the three valid sizes (unknown → medium).</summary>
    public static string NormalizeSize(string? size) => size?.Trim().ToLowerInvariant() switch
    {
        SizeSmall => SizeSmall,
        SizeLarge => SizeLarge,
        _ => SizeMedium,
    };

    public int ButtonSizePx => ButtonSize switch
    {
        SizeSmall => 40,
        SizeLarge => 56,
        _ => 48,
    };

    public int MenuIconPx => ButtonSize switch
    {
        SizeSmall => 28,
        SizeLarge => 36,
        _ => 32,
    };
}

/// <summary>
/// DATA-4: the button lives on a monitor, anchored to the nearest corner of that monitor's
/// work area, with offsets in device-independent pixels (96 DPI units) from that corner to the
/// nearest button edge.
/// </summary>
public sealed class ButtonPosition
{
    public string Monitor { get; set; } = "";

    /// <summary>"bottom-right" | "bottom-left" | "top-right" | "top-left".</summary>
    public string Anchor { get; set; } = "bottom-right";

    public double OffsetX { get; set; } = 20;
    public double OffsetY { get; set; } = 20;

    [JsonIgnore] public bool AnchorTop => Anchor?.StartsWith("top", StringComparison.Ordinal) == true;
    [JsonIgnore] public bool AnchorLeft => Anchor?.EndsWith("left", StringComparison.Ordinal) == true;

    public static readonly string[] ValidAnchors = { "bottom-right", "bottom-left", "top-right", "top-left" };

    public static ButtonPosition Default() => new();

    /// <summary>Repairs anything a hand-edited or damaged file could contain (DATA-3).</summary>
    public void Normalize()
    {
        Monitor ??= "";
        if (!ValidAnchors.Contains(Anchor)) Anchor = "bottom-right";
        if (double.IsNaN(OffsetX) || double.IsInfinity(OffsetX) || OffsetX < 0) OffsetX = 20;
        if (double.IsNaN(OffsetY) || double.IsInfinity(OffsetY) || OffsetY < 0) OffsetY = 20;
    }
}

/// <summary>FAV-4.</summary>
public sealed class Favorite
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Path handed to ShellExecute: the .lnk itself when added from a shortcut.</summary>
    public string Target { get; set; } = "";

    public string Args { get; set; } = "";
    public string WorkingDir { get; set; } = "";

    /// <summary>Relative path (under the config folder) of the cached 32 px PNG.</summary>
    public string IconCache { get; set; } = "";

    [JsonIgnore] public bool IsMissing => !string.IsNullOrEmpty(Target) && !File.Exists(Target) && !Directory.Exists(Target);
}
