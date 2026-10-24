using System.Globalization;
using System.Runtime.InteropServices;

namespace Tarui.Shell;

/// <summary>
/// Renders the numeric taskbar badge as a 32x32 icon using GDI primitives. Windows has no dock-badge
/// API, so the count is drawn onto a red disc and later applied through the taskbar overlay-icon
/// facility. Drawing is local to this helper: no <c>System.Drawing</c> dependency and no reflection.
/// </summary>
internal static class WindowsBadgeIcon
{
    private const int Size = 32;
    private const int MaskStride = Size / 8;
    private const int AntialiasedQuality = 4;
    private const int NullPenStockIndex = 8;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const int TransparentBackground = 1;
    private const uint DtCenter = 0x0001;
    private const uint DtVCenter = 0x0004;
    private const uint DtSingleLine = 0x0020;
    private const uint BadgeColor = 0x002020D0; // COLORREF 0x00BBGGRR: red disc.
    private const uint BadgeTextColor = 0x00FFFFFF;

    /// <summary>Formats the badge label; values above nine collapse to <c>9+</c>.</summary>
    internal static string LabelFor(int count) =>
        count > 9 ? "9+" : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Creates an icon for <paramref name="text"/>, returning 0 when GDI cannot render it.</summary>
    public static nint Create(string text)
    {
        var dc = CreateCompatibleDC(0);
        if (dc == 0)
        {
            return 0;
        }

        nint bitmap = 0;
        nint previousBitmap = 0;
        nint brush = 0;
        nint previousBrush = 0;
        nint previousPen = 0;
        nint font = 0;
        nint previousFont = 0;
        try
        {
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = Size,
                    Height = -Size,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                },
            };
            bitmap = CreateDIBSection(dc, ref info, DibRgbColors, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0)
            {
                return 0;
            }

            previousBitmap = SelectObject(dc, bitmap);

            brush = CreateSolidBrush(BadgeColor);
            previousBrush = SelectObject(dc, brush);
            previousPen = SelectObject(dc, GetStockObject(NullPenStockIndex));
            Ellipse(dc, 0, 0, Size, Size);

            font = CreateFontW(-20, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, AntialiasedQuality, 0, "Segoe UI");
            previousFont = SelectObject(dc, font);
            SetBkMode(dc, TransparentBackground);
            SetTextColor(dc, BadgeTextColor);

            var rect = new Rect { Left = 0, Top = 0, Right = Size, Bottom = Size };
            if (DrawTextW(dc, text, text.Length, ref rect, DtCenter | DtVCenter | DtSingleLine) <= 0)
            {
                return 0;
            }

            var maskBits = new byte[MaskStride * Size];
            for (var index = 0; index < Size * Size; index++)
            {
                var color = Marshal.ReadInt32(bits, index * 4);
                if ((color & 0x00FFFFFF) != 0)
                {
                    Marshal.WriteInt32(bits, index * 4, color | unchecked((int)0xFF000000));
                    continue;
                }

                // CreateBitmap scanlines are bottom-up while the DIB is top-down: flip the row.
                var row = index / Size;
                var column = index % Size;
                maskBits[(Size - 1 - row) * MaskStride + column / 8] |= (byte)(0x80 >> (column % 8));
            }

            var mask = CreateBitmap(Size, Size, 1, 1, maskBits);
            if (mask == 0)
            {
                return 0;
            }

            var iconInfo = new IconInfo { IsIcon = true, Mask = mask, Color = bitmap };
            var icon = CreateIconIndirect(ref iconInfo);
            DeleteObject(mask);
            return icon;
        }
        finally
        {
            // Stock objects (the null pen) must be deselected but never deleted.
            if (previousFont != 0)
            {
                SelectObject(dc, previousFont);
            }

            if (previousPen != 0)
            {
                SelectObject(dc, previousPen);
            }

            if (previousBrush != 0)
            {
                SelectObject(dc, previousBrush);
            }

            if (previousBitmap != 0)
            {
                SelectObject(dc, previousBitmap);
            }

            if (font != 0)
            {
                DeleteObject(font);
            }

            if (brush != 0)
            {
                DeleteObject(brush);
            }

            if (bitmap != 0)
            {
                DeleteObject(bitmap);
            }

            DeleteDC(dc);
        }
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
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public nint Mask;
        public nint Color;
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint hdc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int index);

    [DllImport("gdi32.dll")]
    private static extern bool Ellipse(nint hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFontW")]
    private static extern nint CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    // Both GDI setters return the previous value, which this renderer intentionally discards.
    [DllImport("gdi32.dll", EntryPoint = "SetBkMode")]
    private static extern void SetBkMode(nint hdc, int mode);

    [DllImport("gdi32.dll", EntryPoint = "SetTextColor")]
    private static extern void SetTextColor(nint hdc, uint color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")]
    private static extern int DrawTextW(nint hdc, string text, int length, ref Rect rect, uint format);

    [DllImport("gdi32.dll")]
    private static extern nint CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateIconIndirect(ref IconInfo info);
}
