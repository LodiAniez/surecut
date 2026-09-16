using System.Runtime.InteropServices;
using SureCut.Interop;
using SureCut.Models;

namespace SureCut.Services;

public sealed record LaunchResult(bool Success, uint ProcessId, string? ErrorMessage);

/// <summary>MENU-7 / ARCH-8: ShellExecuteEx with foreground handoff and friendly errors.</summary>
public static class ProgramLauncher
{
    public static LaunchResult Launch(Favorite f)
    {
        if (f.IsMissing) return new LaunchResult(false, 0, "Can't open — file not found");

        // We received the click/key that triggered this, so we are allowed to hand foreground on.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = NativeMethods.SEE_MASK_NOCLOSEPROCESS | NativeMethods.SEE_MASK_FLAG_NO_UI,
            lpVerb = "open",
            lpFile = f.Target,
            lpParameters = string.IsNullOrWhiteSpace(f.Args) ? null : f.Args,
            lpDirectory = string.IsNullOrWhiteSpace(f.WorkingDir) ? null : f.WorkingDir,
            nShow = NativeMethods.SW_SHOWNORMAL,
        };

        if (!NativeMethods.ShellExecuteEx(ref info))
        {
            var err = Marshal.GetLastWin32Error();
            Logger.Warn($"ShellExecuteEx failed for '{f.Target}': {err}");
            return new LaunchResult(false, 0, Describe(err));
        }

        uint pid = 0;
        if (info.hProcess != IntPtr.Zero)
        {
            pid = NativeMethods.GetProcessId(info.hProcess);
            NativeMethods.CloseHandle(info.hProcess);
        }
        Logger.Info($"Launched '{f.Name}' ({f.Target}) pid={pid}");
        return new LaunchResult(true, pid, null);
    }

    public static void OpenFileLocation(Favorite f)
    {
        try
        {
            var target = f.Target;
            if (!File.Exists(target) && !Directory.Exists(target)) return;
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = NativeMethods.SEE_MASK_FLAG_NO_UI,
                lpFile = "explorer.exe",
                lpParameters = $"/select,\"{target}\"",
                nShow = NativeMethods.SW_SHOWNORMAL,
            };
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            NativeMethods.ShellExecuteEx(ref info);
        }
        catch (Exception ex) { Logger.Error("Open file location failed", ex); }
    }

    private static string Describe(int win32Error) => win32Error switch
    {
        2 or 3 => "Can't open — file not found",
        5 => "Can't open — access denied",
        1223 => "Launch cancelled",
        8 or 14 => "Can't open — out of memory",
        31 or 1155 => "Can't open — no program is associated with this file",
        _ => $"Can't open — error {win32Error}",
    };
}
