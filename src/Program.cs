using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

[assembly: AssemblyTitle("亚比桌宠 4.3.4 养成优化版")]
[assembly: AssemblyDescription("写实风格、带养成与提醒功能的 Windows 透明桌面宠物")]
[assembly: AssemblyCompany("Yabi Desktop Pet")]
[assembly: AssemblyProduct("亚比桌宠")]
[assembly: AssemblyCopyright("Personal desktop pet build")]
[assembly: AssemblyVersion("4.3.4.0")]
[assembly: AssemblyFileVersion("4.3.4.0")]

namespace YabiDesktopPet
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static int Main(string[] args)
        {
            if (HasArgument(args, "--ui-preview-output")) return UiPreview434.Run(ReadStringArgument(args, "--ui-preview-output"));
            if (HasArgument(args, "--self-test"))
            {
                return RunSelfTest(args);
            }

            bool testCycle = HasArgument(args, "--test-cycle");
            bool testFeatures = HasArgument(args, "--test-features");
            bool testIdle = HasArgument(args, "--test-idle");
            bool testNatural = HasArgument(args, "--test-natural");
            bool testGaze = HasArgument(args, "--test-gaze");
            bool testUi = testCycle || testFeatures || testIdle || testNatural || testGaze || HasArgument(args, "--test-ui");

            bool createdNew;
            string mutexName = testUi ? @"Local\YabiDesktopPet.Test" : @"Local\YabiDesktopPet";
            _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "亚比已经在桌面上啦。",
                    "亚比桌宠",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return 2;
            }

            try
            {
                double exitAfter = ReadDoubleArgument(args, "--exit-after", 0.0);
                Application application = new Application();
                application.ShutdownMode = ShutdownMode.OnMainWindowClose;
                MainWindow window = new MainWindow(testUi, testCycle, testFeatures, testIdle, testNatural, testGaze);
                if (testUi) window.ConfigureTestPower(HasArgument(args, "--test-eco"));
                application.MainWindow = window;

                DispatcherTimer exitTimer = null;
                if (exitAfter > 0.0)
                {
                    exitTimer = new DispatcherTimer();
                    exitTimer.Interval = TimeSpan.FromSeconds(exitAfter);
                    exitTimer.Tick += delegate
                    {
                        exitTimer.Stop();
                        window.Close();
                    };
                    exitTimer.Start();
                }
                application.Run(window);
                return 0;
            }
            catch (Exception exception)
            {
                WriteStartupError(exception);
                MessageBox.Show(
                    "亚比启动失败：\n" + exception.Message + "\n\n诊断信息已写入临时目录。",
                    "亚比桌宠",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return 1;
            }
            finally
            {
                if (_singleInstanceMutex != null)
                {
                    try
                    {
                        _singleInstanceMutex.ReleaseMutex();
                    }
                    catch (ApplicationException)
                    {
                    }
                    _singleInstanceMutex.Dispose();
                }
            }
        }

        private static int RunSelfTest(string[] args)
        {
            List<string> failures = new List<string>();
            string outputPath = ReadStringArgument(args, "--self-test-output");
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4-self-test.txt");
            }
            try
            {
                using (AnimationCatalog catalog = new AnimationCatalog(Dispatcher.CurrentDispatcher))
                {
                    failures.AddRange(catalog.ValidateEmbeddedPack());
                    // Optional care slots must be safe to query even when the
                    // current photo/video pack does not provide them.
                    catalog.HasClip("feed");
                    catalog.HasClip("pet");
                    catalog.HasClip("play");
                    catalog.HasClip("sit_to_sleep");
                    catalog.HasClip("sleep_to_sit");
                    failures.AddRange(ContinuousGazeRenderTests.RunSelfTests(catalog,
                        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)), "gaze-render")));
                }
                failures.AddRange(PetStateController.RunSelfTests());
                failures.AddRange(NaturalInteractionTests.RunSelfTests());
                failures.AddRange(ContinuousGazeTests.RunSelfTests());
                failures.AddRange(CareController.RunSelfTests());
                failures.AddRange(CareRegressionTests.RunSelfTests());
                failures.AddRange(UiThemeTests.RunSelfTests());
                failures.AddRange(UiThemeTests.RunWindowSelfTests(typeof(Program).Assembly));
                failures.AddRange(ReminderController.RunSelfTests());
                failures.AddRange(MainWindow.RunMenuThemeSelfTests());
                ValidateSettingsMigration(failures);
            }
            catch (Exception exception)
            {
                failures.Add(exception.ToString());
            }

            string folder = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using (StreamWriter writer = new StreamWriter(outputPath, false, new System.Text.UTF8Encoding(false)))
            {
                writer.WriteLine("亚比桌宠 4.3.4 自检");
                writer.WriteLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                writer.WriteLine("结果：" + (failures.Count == 0 ? "PASS" : "FAIL"));
                writer.WriteLine("失败数：" + failures.Count);
                foreach (string failure in failures)
                {
                    writer.WriteLine("- " + failure);
                }
            }
            return failures.Count == 0 ? 0 : 1;
        }

        private static void ValidateSettingsMigration(ICollection<string> failures)
        {
            string folder = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4-settings-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "settings.xml");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    path,
                    "<YabiSettings><Left>123.5</Left><Top>45</Top><Scale>1.25</Scale>" +
                    "<Opacity>0.8</Opacity><ManualMirror>true</ManualMirror>" +
                    "<Behavior>FollowMouse</Behavior><AutoSwitch>true</AutoSwitch></YabiSettings>");
                PetSettings loaded = PetSettings.LoadFrom(path);
                if (loaded.Behavior != BehaviorMode.Companion)
                {
                    failures.Add("legacy FollowMouse did not migrate to Companion");
                }
                if (!loaded.MouseLookEnabled)
                {
                    failures.Add("MouseLookEnabled did not default to true");
                }
                if (!loaded.EdgeSnap || !loaded.AlwaysOnTop || loaded.InteractionFrequency != InteractionFrequency.Normal)
                {
                    failures.Add("4.2 desktop settings did not receive safe defaults");
                }
                if (Math.Abs(loaded.Left - 123.5) > 0.01
                    || Math.Abs(loaded.Scale - 1.25) > 0.01
                    || Math.Abs(loaded.Opacity - 0.8) > 0.01
                    || !loaded.ManualMirror)
                {
                    failures.Add("legacy appearance or position settings were not preserved");
                }
                loaded.StartWithWindows = true;
                loaded.FocusDoNotDisturb = false;
                loaded.DisabledAutomaticClips.Add("groom");
                loaded.LockPosition = true;
                loaded.PowerMode = PowerMode.Eco;
                loaded.CustomPhrases.Add("测试台词");
                loaded.CustomReminders.Add(new ReminderDefinition
                {
                    Id = "test",
                    Name = "测试",
                    Message = "测试提醒",
                    IntervalMinutes = 30,
                    Enabled = true,
                    Kind = ReminderKind.Custom
                });
                loaded.SaveTo(path);
                PetSettings roundTrip = PetSettings.LoadFrom(path);
                if (!roundTrip.StartWithWindows
                    || !roundTrip.LockPosition
                    || roundTrip.PowerMode != PowerMode.Eco
                    || roundTrip.FocusDoNotDisturb
                    || !roundTrip.DisabledAutomaticClips.Contains("groom")
                    || roundTrip.CustomPhrases.Count != 1
                    || roundTrip.CustomReminders.Count != 1)
                {
                    failures.Add(
                        "4.2 settings round-trip did not preserve new options: start=" + roundTrip.StartWithWindows
                        + ", lock=" + roundTrip.LockPosition
                        + ", power=" + roundTrip.PowerMode
                        + ", phrases=" + roundTrip.CustomPhrases.Count
                        + ", reminders=" + roundTrip.CustomReminders.Count
                        + ", saveError=" + (PetSettings.LastSaveError ?? "none"));
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool HasArgument(IEnumerable<string> args, string name)
        {
            foreach (string argument in args)
            {
                if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ReadStringArgument(string[] args, string name)
        {
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }
            return null;
        }

        private static double ReadDoubleArgument(string[] args, string name, double fallback)
        {
            string text = ReadStringArgument(args, name);
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static void WriteStartupError(Exception exception)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3-startup-error.txt"),
                    exception.ToString());
            }
            catch
            {
            }
        }
    }
}
