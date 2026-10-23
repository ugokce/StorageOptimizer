using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StorageOptimizer.App.Services;

namespace StorageOptimizer.App.Infrastructure;

/// <summary>Screenshots of the app's own window, for the smoke test and screenshot mode.</summary>
internal static class WindowCapture
{
    /// <summary>
    /// The window as Windows shows it, title bar and Mica included. Returns null when Windows can't
    /// provide the image (it comes back black without a desktop session).
    /// </summary>
    public static BitmapSource? CaptureWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var windowRect))
        {
            return null;
        }
        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var screen = NativeMethods.GetDC(IntPtr.Zero);
        var memory = NativeMethods.CreateCompatibleDC(screen);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
        var previous = NativeMethods.SelectObject(memory, bitmap);
        try
        {
            if (!NativeMethods.PrintWindow(handle, memory, NativeMethods.PW_RENDERFULLCONTENT))
            {
                return null;
            }
            NativeMethods.SelectObject(memory, previous);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            // The window rectangle includes invisible resize borders; the frame bounds don't.
            var crop = new Int32Rect(0, 0, width, height);
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame,
                    System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>()) == 0)
            {
                var left = Math.Clamp(frame.Left - windowRect.Left, 0, width - 1);
                var top = Math.Clamp(frame.Top - windowRect.Top, 0, height - 1);
                crop = new Int32Rect(left, top, Math.Min(frame.Right - frame.Left, width - left), Math.Min(frame.Bottom - frame.Top, height - top));
            }
            // GDI leaves the alpha channel undefined, so drop it.
            var result = new FormatConvertedBitmap(new CroppedBitmap(image, crop), PixelFormats.Bgr32, null, 0);
            result.Freeze();
            return HasDetail(result) ? result : null;
        }
        finally
        {
            NativeMethods.SelectObject(memory, previous);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>The window's content drawn by WPF, without the title bar. Works without a desktop session.</summary>
    public static BitmapSource? RenderContent(Window window, bool dark)
    {
        var root = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(root.ActualWidth);
        var height = (int)Math.Ceiling(root.ActualHeight);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        // Mica makes the window background transparent, so draw the theme's base color behind it.
        var background = window.Background is SolidColorBrush { Color.A: 255 } solid
            ? solid
            : new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, width, height);
            context.DrawRectangle(background, null, bounds);
            context.DrawRectangle(new VisualBrush(root), null, bounds);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>
    /// Whether an image shows real content. A failed capture is black or one flat color; a real page
    /// can be mostly one background color, so only those two cases are rejected.
    /// </summary>
    private static bool HasDetail(BitmapSource image)
    {
        var colors = new HashSet<int>();
        var black = 0;
        var samples = 0;
        var pixel = new byte[4];
        for (var row = 0; row < 32; row++)
        {
            for (var column = 0; column < 32; column++)
            {
                var x = image.PixelWidth * (2 * column + 1) / 64;
                var y = image.PixelHeight * (2 * row + 1) / 64;
                image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                colors.Add(pixel[0] | pixel[1] << 8 | pixel[2] << 16);
                if (pixel[0] + pixel[1] + pixel[2] < 24)
                {
                    black++;
                }
                samples++;
            }
        }
        return colors.Count >= 3 && black < samples / 2;
    }
}
