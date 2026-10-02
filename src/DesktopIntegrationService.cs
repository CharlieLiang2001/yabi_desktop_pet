using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed class DesktopIntegrationService
    {
        private const string AutoStartKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartName = "YabiDesktopPet";

        public string SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartKey, true))
                {
                    if (key == null)
                    {
                        return "无法打开当前用户的开机启动设置。";
                    }
                    if (enabled)
                    {
                        string path = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(AutoStartName, "\"" + path + "\"");
                    }
                    else
                    {
                        key.DeleteValue(AutoStartName, false);
                    }
                }
                return null;
            }
            catch (Exception exception)
            {
                return "开机启动设置失败：" + exception.Message;
            }
        }

        public bool IsEcoMode(PowerMode mode)
        {
            if (mode == PowerMode.Eco)
            {
                return true;
            }
            if (mode == PowerMode.Standard)
            {
                return false;
            }
            try
            {
                return Forms.SystemInformation.PowerStatus.PowerLineStatus != Forms.PowerLineStatus.Online;
            }
            catch
            {
                return false;
            }
        }

        public void RestorePosition(Window window, PetSettings settings)
        {
            if (!double.IsNaN(settings.Left) && !double.IsNaN(settings.Top))
            {
                window.Left = settings.Left;
                window.Top = settings.Top;
            }
            Forms.Screen screen = FindScreen(settings.MonitorDevice);
            if (screen != null
                && !double.IsNaN(settings.MonitorRelativeLeft)
                && !double.IsNaN(settings.MonitorRelativeTop))
            {
                Rect work = ScreenWorkingAreaInDips(window, screen);
                window.Left = work.Left + settings.MonitorRelativeLeft * Math.Max(0.0, work.Width - window.Width);
                window.Top = work.Top + settings.MonitorRelativeTop * Math.Max(0.0, work.Height - window.Height);
            }
            KeepInsideCurrentScreen(window);
        }

        public void CapturePosition(Window window, PetSettings settings)
        {
            Forms.Screen screen = ScreenForWindow(window);
            Rect work = ScreenWorkingAreaInDips(window, screen);
            settings.MonitorDevice = screen.DeviceName ?? string.Empty;
            settings.MonitorRelativeLeft = work.Width <= window.Width
                ? 0.0
                : Clamp((window.Left - work.Left) / (work.Width - window.Width), 0.0, 1.0);
            settings.MonitorRelativeTop = work.Height <= window.Height
                ? 0.0
                : Clamp((window.Top - work.Top) / (work.Height - window.Height), 0.0, 1.0);
        }

        public void SnapToEdges(Window window, double distance)
        {
            Forms.Screen screen = ScreenForWindow(window);
            Rect work = ScreenWorkingAreaInDips(window, screen);
            double right = work.Right - window.Width;
            double bottom = work.Bottom - window.Height;
            if (Math.Abs(window.Left - work.Left) <= distance) window.Left = work.Left;
            if (Math.Abs(window.Left - right) <= distance) window.Left = right;
            if (Math.Abs(window.Top - work.Top) <= distance) window.Top = work.Top;
            if (Math.Abs(window.Top - bottom) <= distance) window.Top = bottom;
            KeepInside(window, work);
        }

        public void KeepInsideCurrentScreen(Window window)
        {
            KeepInside(window, ScreenWorkingAreaInDips(window, ScreenForWindow(window)));
        }

        private static Forms.Screen ScreenForWindow(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            return handle == IntPtr.Zero ? Forms.Screen.PrimaryScreen : Forms.Screen.FromHandle(handle);
        }

        private static Forms.Screen FindScreen(string deviceName)
        {
            if (!string.IsNullOrEmpty(deviceName))
            {
                foreach (Forms.Screen screen in Forms.Screen.AllScreens)
                {
                    if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return screen;
                    }
                }
            }
            return Forms.Screen.PrimaryScreen;
        }

        private static Rect ScreenWorkingAreaInDips(Window window, Forms.Screen screen)
        {
            Matrix transform = Matrix.Identity;
            PresentationSource source = PresentationSource.FromVisual(window);
            if (source != null && source.CompositionTarget != null)
            {
                transform = source.CompositionTarget.TransformFromDevice;
            }
            System.Drawing.Rectangle area = screen.WorkingArea;
            Point topLeft = transform.Transform(new Point(area.Left, area.Top));
            Point bottomRight = transform.Transform(new Point(area.Right, area.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private static void KeepInside(Window window, Rect work)
        {
            double maximumLeft = Math.Max(work.Left, work.Right - window.Width);
            double maximumTop = Math.Max(work.Top, work.Bottom - window.Height);
            window.Left = Clamp(window.Left, work.Left, maximumLeft);
            window.Top = Clamp(window.Top, work.Top, maximumTop);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
