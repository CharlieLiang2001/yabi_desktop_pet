using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing = System.Drawing;

namespace YabiDesktopPet
{
    // Test-only: capture just the pet's rectangle, with our own opaque window
    // behind it. This checks DWM composition, not a PNG viewer's RGB handling.
    internal sealed class GazeDesktopProbe : IDisposable
    {
        private readonly Window _pet, _backdrop;
        private readonly Color _color;

        internal GazeDesktopProbe(Window pet, Color color)
        {
            _pet = pet; _color = color;
            _backdrop = new Window
            {
                Title = "Yabi rendering verification",
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowActivated = false, ShowInTaskbar = false, Topmost = true,
                Left = pet.Left - 8, Top = pet.Top - 8,
                Width = pet.Width + 16, Height = pet.Height + 16,
                Background = new SolidColorBrush(color)
            };
            _backdrop.Show();
            SetWindowPos(new WindowInteropHelper(pet).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        }

        internal int Capture(string path)
        {
            Point topLeft = _pet.PointToScreen(new Point(0, 0));
            Point bottomRight = _pet.PointToScreen(new Point(_pet.ActualWidth, _pet.ActualHeight));
            int width = (int)Math.Round(bottomRight.X - topLeft.X), height = (int)Math.Round(bottomRight.Y - topLeft.Y);
            using (Drawing.Bitmap bitmap = new Drawing.Bitmap(width, height))
            {
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen((int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y), 0, 0, bitmap.Size);
                foreach (Drawing.Point corner in new[] { new Drawing.Point(0, 0), new Drawing.Point(width - 1, 0),
                    new Drawing.Point(0, height - 1), new Drawing.Point(width - 1, height - 1) })
                    if (Difference(bitmap.GetPixel(corner.X, corner.Y)) > 12)
                        throw new InvalidOperationException("Desktop probe backdrop is obscured; screenshot is not valid evidence.");
                int catPixels = 0;
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                    if (Difference(bitmap.GetPixel(x, y)) > 50) catPixels++;
                if (catPixels < width * height * .12)
                    throw new InvalidOperationException("Pet disappeared in native desktop composition: " + catPixels + " visible pixels.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                bitmap.Save(path, Drawing.Imaging.ImageFormat.Png);
                return catPixels;
            }
        }

        private int Difference(Drawing.Color pixel)
        { return Math.Abs(pixel.R - _color.R) + Math.Abs(pixel.G - _color.G) + Math.Abs(pixel.B - _color.B); }
        public void Dispose() { _backdrop.Close(); }

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
