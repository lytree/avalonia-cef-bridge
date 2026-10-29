using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Tarui.Shell;

/// <summary>
/// Reads the icon resource embedded in the running executable and encodes it as PNG, so the window
/// icon can be restored to the application's own icon after a temporary override. This is the
/// equivalent of Tauri's <c>Image::from_app_icon_resource</c> on Windows; other platforms return
/// <see langword="false"/> instead of guessing at a bundle layout. Extraction is local to this
/// helper: no <c>System.Drawing</c> dependency and no reflection.
/// </summary>
internal static class WindowsAppIcon
{
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    /// <summary>
    /// Extracts the executable's large icon as PNG bytes. Returns <see langword="false"/> on
    /// non-Windows platforms, when the process has no executable path, or when the binary carries no
    /// icon resource.
    /// </summary>
    internal static bool TryLoadPng(out byte[] png)
    {
        png = [];
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        var count = ExtractIconExW(executable, 0, out var large, out var small, 1);
        if (count == 0)
        {
            return false;
        }

        try
        {
            var icon = large != 0 ? large : small;
            return icon != 0 && TryConvertToPng(icon, out png);
        }
        finally
        {
            if (large != 0)
            {
                DestroyIcon(large);
            }

            if (small != 0)
            {
                DestroyIcon(small);
            }
        }
    }

    private static bool TryConvertToPng(nint icon, out byte[] png)
    {
        png = [];
        if (!GetIconInfo(icon, out var info))
        {
            return false;
        }

        try
        {
            if (info.Color == 0 ||
                GetObjectW(info.Color, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0 ||
                bitmap.Width <= 0 ||
                bitmap.Height <= 0)
            {
                return false;
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = new byte[width * height * 4];
            var header = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    // Negative height requests top-down scanlines so the row order matches Avalonia's.
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };

            var dc = CreateCompatibleDC(0);
            if (dc == 0)
            {
                return false;
            }

            try
            {
                if (GetDIBits(dc, info.Color, 0, (uint)height, pixels, ref header, DibRgbColors) == 0)
                {
                    return false;
                }
            }
            finally
            {
                DeleteDC(dc);
            }

            using var writeable = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using (var framebuffer = writeable.Lock())
            {
                Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
            }

            using var stream = new MemoryStream();
            writeable.Save(stream, PngBitmapEncoderOptions.Default);
            png = stream.ToArray();
            return png.Length > 0;
        }
        finally
        {
            if (info.Color != 0)
            {
                DeleteObject(info.Color);
            }

            if (info.Mask != 0)
            {
                DeleteObject(info.Mask);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitCount;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public nint Mask;
        public nint Color;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconExW(string file, int iconIndex, out nint large, out nint small, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(nint icon, out IconInfo info);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetObjectW", SetLastError = true)]
    private static extern int GetObjectW(nint handle, int size, out NativeBitmap bitmap);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        nint hdc,
        nint bitmap,
        uint start,
        uint lines,
        byte[] bits,
        ref BitmapInfo info,
        uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);
}
