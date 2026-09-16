using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SureCut.Interop;
using SureCut.Models;

namespace SureCut.Services;

/// <summary>FAV-4 / FAV-6: extract program icons via the Shell and cache them as PNGs.</summary>
public sealed class IconCache
{
    private readonly string _root;
    private readonly string _iconsFolder;
    private readonly Dictionary<string, ImageSource?> _memory = new(StringComparer.OrdinalIgnoreCase);

    public IconCache(string configFolder)
    {
        _root = configFolder;
        _iconsFolder = Path.Combine(configFolder, "icons");
        Directory.CreateDirectory(_iconsFolder);
    }

    /// <summary>Returns the best icon for the favorite, or null when nothing could be produced (use the generic glyph).</summary>
    public ImageSource? Get(Favorite f)
    {
        if (_memory.TryGetValue(f.Id, out var cached)) return cached;

        var large = LargePath(f);
        ImageSource? img = null;

        if (File.Exists(large)) img = LoadPng(large);
        if (img is null)
        {
            var extracted = Extract(f);
            if (extracted is not null) img = extracted;
        }

        _memory[f.Id] = img;
        return img;
    }

    /// <summary>Extracts and caches 32 px and 64 px PNGs. Returns the 64 px image.</summary>
    public ImageSource? Extract(Favorite f)
    {
        if (string.IsNullOrEmpty(f.Target) || (!File.Exists(f.Target) && !Directory.Exists(f.Target))) return null;
        try
        {
            var big = GetShellImage(f.Target, 64);
            if (big is null) return null;
            SavePng(big, LargePath(f));

            var small = GetShellImage(f.Target, 32) ?? big;
            SavePng(small, SmallPath(f));

            f.IconCache = Path.GetRelativePath(_root, SmallPath(f));
            _memory[f.Id] = big;
            return big;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Icon extraction failed for {f.Target}: {ex.Message}");
            return null;
        }
    }

    public void Forget(Favorite f)
    {
        _memory.Remove(f.Id);
        TryDelete(SmallPath(f));
        TryDelete(LargePath(f));
    }

    public void Invalidate(Favorite f) => _memory.Remove(f.Id);

    private string SmallPath(Favorite f) => Path.Combine(_iconsFolder, f.Id + ".png");
    private string LargePath(Favorite f) => Path.Combine(_iconsFolder, f.Id + "@2x.png");

    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ } }

    private static BitmapSource? GetShellImage(string path, int size)
    {
        var iid = NativeMethods.IID_IShellItemImageFactory;
        NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var obj);
        if (obj is not IShellItemImageFactory factory) return null;

        var hr = factory.GetImage(new SIZE(size, size), NativeMethods.SIIGBF_ICONONLY | NativeMethods.SIIGBF_BIGGERSIZEOK, out var hbitmap);
        if (hr != 0 || hbitmap == IntPtr.Zero)
        {
            // Some shell items refuse ICONONLY; try a plain thumbnail-or-icon request.
            hr = factory.GetImage(new SIZE(size, size), NativeMethods.SIIGBF_RESIZETOFIT, out hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero) return null;
        }

        try
        {
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // The shell returns a premultiplied 32bpp bitmap; convert so alpha is honored when rendering.
            var converted = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        finally
        {
            NativeMethods.DeleteObject(hbitmap);
        }
    }

    private static void SavePng(BitmapSource img, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(img));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    private static ImageSource? LoadPng(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Default display name: shortcut name, or the .exe's file description (FAV-4).</summary>
    public static string DefaultName(string target)
    {
        var name = Path.GetFileNameWithoutExtension(target);
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(target).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
            }
            catch { /* keep the file name */ }
        }
        return string.IsNullOrWhiteSpace(name) ? target : name;
    }
}
