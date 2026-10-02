using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace YabiDesktopPet
{
    internal static class UiThemeTests
    {
        public static IEnumerable<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            Type theme = typeof(UiThemeTests).Assembly.GetType("YabiDesktopPet.UiTheme");
            if (theme == null)
            {
                failures.Add("shared UI theme is missing");
                return failures;
            }
            try
            {
                StackPanel root = new StackPanel();
                theme.GetMethod("Apply").Invoke(null, new object[] { root });
                Control[] controls = { new Button { Content = "照顾" }, new TextBox { Text = "亚比" },
                    new ComboBox { Items = { "自动", "安静" }, SelectedIndex = 1 },
                    new CheckBox { Content = "启用", IsChecked = true }, new ProgressBar { Value = 63 },
                    new TabItem { Header = "常规", Content = new TextBlock { Text = "内容" } }, new Slider { Value = 37, Maximum = 100 } };
                foreach (Control control in controls)
                {
                    root.Children.Add(control);
                    control.Style = (Style)root.FindResource(control.GetType());
                    control.ApplyTemplate();
                    if (control.Template == null) failures.Add(control.GetType().Name + " has no template");
                }
                root.Measure(new Size(500, 800));
                root.Arrange(new Rect(0, 0, 500, 800));
                if (((ComboBox)controls[2]).SelectedIndex != 1) failures.Add("theme changed combo selection");
                if (((TextBox)controls[1]).Text != "亚比") failures.Add("theme changed text value");
                if (((CheckBox)controls[3]).IsChecked != true) failures.Add("theme changed checkbox value");
                if (((ProgressBar)controls[4]).Value != 63) failures.Add("theme changed progress value");
                foreach (Control control in controls)
                {
                    control.IsEnabled = false;
                    root.UpdateLayout();
                    if (control.Opacity >= 1) failures.Add(control.GetType().Name + " lacks disabled styling");
                    control.IsEnabled = true;
                }
            }
            catch (Exception error) { failures.Add("theme load/template failure: " + error.ToString()); }
            return failures;
        }

        public static IEnumerable<string> RunWindowSelfTests(Assembly assembly)
        {
            List<string> failures = new List<string>();
            Type statusType = assembly.GetType("YabiDesktopPet.StatusCardWindow");
            if (statusType == null) return failures;
            Window window = (Window)Activator.CreateInstance(statusType);
            try
            {
                if (window.Width != 360) failures.Add("status card width must be 360 DIP");
                if (window.SizeToContent != SizeToContent.Height) failures.Add("status card height must follow content");
                Type profileType = assembly.GetType("YabiDesktopPet.PetProfile");
                object profile = Activator.CreateInstance(profileType);
                object growth = profileType.GetField("Growth").GetValue(profile);
                growth.GetType().GetField("AffinityXp").SetValue(growth, 9800);
                statusType.GetMethod("Refresh", new[] { profileType, typeof(string) }).Invoke(window, new[] { profile, "开始专注" });
                ProgressBar affinity = (ProgressBar)statusType.GetField("_affinityBar", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                if (affinity.Value != 100) failures.Add("max level affinity progress must be 100");
                MethodInfo feedback = statusType.GetMethod("SetCareFeedback", new[] { typeof(TimeSpan), typeof(TimeSpan), typeof(bool), typeof(bool) });
                if (feedback == null) failures.Add("care feedback must consume typed cooldowns and queue flags");
                else
                {
                    feedback.Invoke(window, new object[] { TimeSpan.FromSeconds(1), TimeSpan.Zero, false, true });
                    Button feed = (Button)statusType.GetField("_feedButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                    Button pet = (Button)statusType.GetField("_petButton", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                    if (feed.IsEnabled || pet.IsEnabled) failures.Add("cooldown and queued care buttons must be disabled");
                    feedback.Invoke(window, new object[] { TimeSpan.Zero, TimeSpan.Zero, false, false });
                    if (!feed.IsEnabled || !pet.IsEnabled) failures.Add("ready care buttons must be reenabled");
                }
            }
            finally { window.Close(); }
            Type settingsType = assembly.GetType("YabiDesktopPet.SettingsWindow");
            if (settingsType.GetMethod("RefreshData") == null) failures.Add("settings data refresh must be public");
            if (settingsType.GetField("ProfileResetRequested") == null) failures.Add("profile reset must notify queue owner");
            return failures;
        }
    }
}
