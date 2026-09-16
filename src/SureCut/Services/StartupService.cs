using Microsoft.Win32;

namespace SureCut.Services;

/// <summary>SET-4: per-user Run key, kept pointing at the current .exe path.</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SureCut";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SureCut.exe");

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var wanted = $"\"{ExePath}\"";
            var current = key.GetValue(ValueName) as string;
            if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, wanted, RegistryValueKind.String);
                Logger.Info($"Run key set to {wanted}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not update the Run key.", ex);
        }
    }
}
