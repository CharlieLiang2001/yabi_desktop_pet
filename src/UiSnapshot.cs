using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YabiDesktopPet
{
    internal static class UiSnapshot
    {
        public static void Capture(FrameworkElement window, string path)
        {
            if (window == null)
            {
                throw new ArgumentNullException("window");
            }
            window.UpdateLayout();
            int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth > 0 ? window.ActualWidth : window.Width));
            int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight > 0 ? window.ActualHeight : window.Height));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }
    }
}
