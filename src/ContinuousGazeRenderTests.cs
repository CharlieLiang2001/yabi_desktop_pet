using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YabiDesktopPet
{
    internal static class ContinuousGazeRenderTests
    {
        internal static List<string> RunSelfTests(AnimationCatalog catalog, string folder)
        {
            List<string> failures = new List<string>();
            Directory.CreateDirectory(folder);
            foreach (bool standing in new[] { true, false })
            {
                string pose = standing ? "stand" : "sit";
                BitmapSource original = catalog.LoadResidentFrame(pose + "_idle", 0);
                WriteableBitmap sharedVideoSurface = new WriteableBitmap(original);
                ContinuousGazeView view = new ContinuousGazeView();
                view.SetTexture(sharedVideoSurface, standing);
                if (sharedVideoSurface.IsFrozen) failures.Add(pose + ": gaze froze the shared video surface");
                byte[] reference = Pixels(original);
                foreach (double x in new[] { -4.0, 0, 4 }) foreach (double y in new[] { -4.0, 0, 4 })
                {
                    ContinuousGazeController state = new ContinuousGazeController();
                    for (int i = 0; i < 60; i++) state.Update(x * 100, y * 100, 100, true, false, 1.0 / 30);
                    view.UpdatePose(state);
                    BitmapSource bitmap = Render(view);
                    byte[] actual = Pixels(bitmap);
                    string label = pose + "-" + x + "-" + y;
                    int solid = 0, visible = 0, body = 0, bodyVisible = 0, invalid = 0;
                    for (int pixel = 0; pixel < 320 * 480; pixel++)
                    {
                        int i = pixel * 4;
                        if (reference[i + 3] >= 250) solid++;
                        if (actual[i + 3] >= 240) visible++;
                        if (pixel / 320 > view.Rig.FadeEnd + 6 && reference[i + 3] >= 250)
                        { body++; if (actual[i + 3] >= 240) bodyVisible++; }
                        if (actual[i] > actual[i + 3] || actual[i + 1] > actual[i + 3] || actual[i + 2] > actual[i + 3]) invalid++;
                    }
                    if (visible < solid * .94) failures.Add(label + ": cat opacity lost; solid=" + solid + ", visible=" + visible);
                    if (bodyVisible < body * .99) failures.Add(label + ": lower body became transparent");
                    if (invalid != 0) failures.Add(label + ": invalid premultiplied alpha pixels=" + invalid);
                    foreach (int corner in new[] { 0, 319, 479 * 320, 480 * 320 - 1 })
                        if (actual[corner * 4 + 3] != 0) failures.Add(label + ": background corner is not transparent");
                    if (y == 0) Save(bitmap, Path.Combine(folder, label + ".png"));
                }
                // The gaze material must not freeze the animation's writable buffer.
                if (!sharedVideoSurface.IsFrozen)
                {
                    try { sharedVideoSurface.WritePixels(new Int32Rect(0, 0, 320, 480), reference, 320 * 4, 0); }
                    catch (Exception error) { failures.Add(pose + ": video write failed after gaze: " + error.Message); }
                }
            }
            File.WriteAllLines(Path.Combine(folder, "render-result.txt"), failures.Count == 0
                ? new[] { "PASS: both poses, 18 targets, opaque cat body, transparent corners, valid premultiplied alpha, shared video surface stays writable." }
                : failures.ToArray());
            return failures;
        }

        internal static BitmapSource Render(FrameworkElement visual)
        {
            visual.Measure(new Size(320, 480));
            visual.Arrange(new Rect(0, 0, 320, 480));
            visual.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(320, 480, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        internal static byte[] Pixels(BitmapSource bitmap)
        {
            BitmapSource converted = bitmap.Format == PixelFormats.Pbgra32 ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            converted.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return pixels;
        }

        internal static void Save(BitmapSource bitmap, string path)
        {
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
