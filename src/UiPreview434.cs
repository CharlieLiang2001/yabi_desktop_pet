using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace YabiDesktopPet
{
    // Opt-in isolated UI rendering, never loads user preferences or profiles.
    internal static class UiPreview434
    {
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            List<string> checks = new List<string>();
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                foreach (string state in new[] { "normal", "low", "cooldown", "max-level", "long-text" })
                {
                    PetProfile profile = new PetProfile();
                    profile.Growth.TodayActiveSeconds = 5400; profile.Growth.TotalActiveSeconds = 32400; profile.Growth.CurrentStreak = 3;
                    if (state == "low") { profile.Care.Fullness = 12; profile.Care.Energy = 20; profile.Care.Mood = 25; }
                    if (state == "max-level") profile.Growth.AffinityXp = 9800;
                    StatusCardWindow card = new StatusCardWindow();
                    card.Refresh(new CareSnapshot(profile, state == "long-text" ? "模拟保存失败：上一份存档已保留。" : null, DateTime.UtcNow), "开始专注（25分钟）");
                    card.SetCareFeedback(state == "cooldown" ? TimeSpan.FromMinutes(20) : TimeSpan.Zero, TimeSpan.Zero, false, state == "cooldown");
                    card.SetActivity(state == "long-text" ? "坐姿 · 等待：摸摸。当前完整动作结束后会回应，照顾奖励不会重复计算。" : "坐姿 · 安心陪伴");
                    card.SetGazeStatus("移动鼠标可小幅注视；完整动作期间暂停。");
                    card.Show(); Pump();
                    foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                        Capture(card, Path.Combine(folder, "status-" + state + "-" + (int)(scale * 100) + ".png"), scale);
                    checks.Add("PASS: status " + state + " at render scales 100/150/200; " + card.ActualWidth + "x" + card.ActualHeight + " DIP");
                    if (state == "long-text")
                    {
                        card.MaxHeight = 350; card.Width = 300; card.UpdateLayout();
                        Capture(card, Path.Combine(folder, "status-small-workarea.png"), 1);
                        if (card.ActualHeight > 350.5) throw new InvalidOperationException("status exceeded small workarea");
                    }
                    card.Close();
                }
                CareController care = new CareController(new PetProfile(), false);
                care.Profile.Growth.Achievements.Add("初次相遇"); care.Profile.Growth.Achievements.Add("七日相伴");
                SettingsWindow settings = new SettingsWindow(new PetSettings(), care, 0);
                settings.Show(); Pump();
                string[] pages = { "general", "companion", "reminders", "appearance", "data" };
                for (int i = 0; i < pages.Length; i++)
                {
                    settings.SelectTab(i); Pump();
                    foreach (double scale in new[] { 1.0, 1.5, 2.0 })
                        Capture(settings, Path.Combine(folder, "settings-" + pages[i] + "-" + (int)(scale * 100) + ".png"), scale);
                    checks.Add("PASS: settings " + pages[i] + " render scales 100/150/200");
                }
                settings.MinWidth = 500; settings.MinHeight = 400; settings.Width = 540; settings.Height = 420;
                settings.SelectTab(2); Pump(); Capture(settings, Path.Combine(folder, "settings-small-workarea.png"), 1);
                settings.Close();
                checks.Add("Render-scale tests are not physical mixed-DPI monitor tests.");
                File.WriteAllLines(Path.Combine(folder, "layout-checks.txt"), checks);
                return 0;
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(folder, "failure.txt"), e.ToString()); return 1; }
            finally { app.Shutdown(); }
        }
        private static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
        private static void Capture(Window window, string path, double scale)
        {
            window.UpdateLayout();
            FrameworkElement surface = (FrameworkElement)window.Content;
            RenderTargetBitmap bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale), (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
