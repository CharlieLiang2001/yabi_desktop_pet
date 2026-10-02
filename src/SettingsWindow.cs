using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed class SettingsWindow : Window
    {
        private readonly PetSettings _settings;
        private readonly CareController _care;
        private readonly CheckBox _startWithWindows;
        private readonly CheckBox _lockPosition;
        private readonly CheckBox _edgeSnap;
        private readonly CheckBox _alwaysOnTop;
        private readonly CheckBox _mouseLook;
        private readonly CheckBox _focusDoNotDisturb;
        private readonly CheckBox _mirror;
        private readonly ComboBox _frequency;
        private readonly ComboBox _powerMode;
        private readonly ComboBox _behavior;
        private readonly Slider _scale;
        private readonly Slider _opacity;
        private readonly CheckBox _waterEnabled;
        private readonly TextBox _waterMinutes;
        private readonly CheckBox _stretchEnabled;
        private readonly TextBox _stretchMinutes;
        private readonly TextBox _focusMinutes;
        private readonly TextBox _phrases;
        private readonly List<CustomReminderRow> _customRows = new List<CustomReminderRow>();
        private readonly TextBlock _dataSummary;
        private readonly TextBlock _achievements;
        private readonly TabControl _tabs;
        private readonly System.Windows.Controls.Primitives.UniformGrid _stats;
        private readonly WrapPanel _achievementCards;
        private string _lastDataKey;

        public SettingsWindow(PetSettings settings, CareController care, int initialTab)
        {
            _settings = settings;
            _care = care;
            Title = "亚比桌宠设置";
            Width = 760;
            Height = 650;
            MinWidth = 620;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BrushFrom("#FFF9F4");
            FontFamily = new FontFamily("Microsoft YaHei UI");
            ShowInTaskbar = true;
            UiTheme.Apply(this);
            Background = UiTheme.Cream;

            Grid root = new Grid { Background = UiTheme.Cream };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border header = new Border
            {
                Background = BrushFrom("#FBE5D7"),
                BorderBrush = BrushFrom("#E6C7B4"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 17, 24, 15)
            };
            StackPanel headerText = new StackPanel();
            headerText.Children.Add(new TextBlock
            {
                Text = "亚比桌宠 4.3.4",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFrom("#4A352B")
            });
            headerText.Children.Add(new TextBlock
            {
                Text = "陪伴、提醒和桌面行为都可以在这里调整",
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = BrushFrom("#876E61"),
                FontSize = 12
            });
            header.Child = headerText;
            root.Children.Add(header);

            _tabs = new TabControl
            {
                TabStripPlacement = Dock.Left,
                Margin = new Thickness(18, 14, 18, 8),
                Background = Brushes.Transparent,
                BorderBrush = BrushFrom("#E6D7C8")
            };
            Grid.SetRow(_tabs, 1);
            root.Children.Add(_tabs);

            StackPanel general = PagePanel();
            _startWithWindows = AddToggle(general, "开机时启动亚比", "使用当前用户启动项，不需要管理员权限", settings.StartWithWindows);
            _alwaysOnTop = AddToggle(general, "始终置顶", "关闭后亚比可以被其他窗口覆盖", settings.AlwaysOnTop);
            _lockPosition = AddToggle(general, "锁定位置", "保留点击互动，但禁止拖动", settings.LockPosition);
            _edgeSnap = AddToggle(general, "吸附屏幕边缘", "拖动到边缘18像素内自动贴边", settings.EdgeSnap);
            _frequency = AddChoice(general, "互动频率", new[] { "安静", "正常", "活泼" }, (int)settings.InteractionFrequency);
            _powerMode = AddChoice(general, "功耗模式", new[] { "自动", "标准", "节能" }, (int)settings.PowerMode);
            _tabs.Items.Add(CreateTab("常规", general));

            StackPanel companion = PagePanel();
            _behavior = AddChoice(companion, "陪伴模式", new[] { "自动陪伴", "安静睡觉" }, (int)settings.Behavior);
            _mouseLook = AddToggle(companion, "实时视线跟随：站立和坐姿可用", "鼠标移动时连续更新上下左右视线，眼睛先响应、头颈稍后。无驻留、冷却或两分钟限制；照片网格仅模拟小幅注视。", settings.MouseLookEnabled);
            _focusDoNotDisturb = AddToggle(companion, "专注时免打扰", "专注计时期间暂停随机动作、台词和鼠标停留回应", settings.FocusDoNotDisturb);
            Button testAttention = CreateButton("实时注视 · 查看状态", false);
            testAttention.HorizontalAlignment = HorizontalAlignment.Left;
            testAttention.Margin = new Thickness(0, 0, 0, 4);
            testAttention.Click += delegate
            {
                EventHandler handler = TestAttentionRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };
            companion.Children.Add(testAttention);
            companion.Children.Add(new TextBlock
            {
                Text = "无需打开面板即可使用。拖动、睡眠、其他动作及专注免打扰时暂停；不增加养成经验。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = BrushFrom("#8A7468"),
                Margin = new Thickness(0, 0, 0, 5)
            });
            companion.Children.Add(SectionTitle("随机台词（每行一句，最多50句）"));
            _phrases = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 205,
                Padding = new Thickness(10),
                Text = string.Join(Environment.NewLine, (settings.CustomPhrases.Count == 0 ? PetSettings.DefaultPhrases : settings.CustomPhrases.ToArray()))
            };
            companion.Children.Add(_phrases);
            Button resetPhrases = CreateButton("恢复默认台词", false);
            resetPhrases.HorizontalAlignment = HorizontalAlignment.Left;
            resetPhrases.Margin = new Thickness(0, 8, 0, 0);
            resetPhrases.Click += delegate { _phrases.Text = string.Join(Environment.NewLine, PetSettings.DefaultPhrases); };
            companion.Children.Add(resetPhrases);
            _tabs.Items.Add(CreateTab("陪伴", companion));

            StackPanel reminders = PagePanel();
            _waterEnabled = AddReminderLine(reminders, "喝水提醒", settings.WaterReminderEnabled, settings.WaterReminderMinutes, out _waterMinutes);
            _stretchEnabled = AddReminderLine(reminders, "伸展提醒", settings.StretchReminderEnabled, settings.StretchReminderMinutes, out _stretchMinutes);
            reminders.Children.Add(SectionTitle("专注计时"));
            _focusMinutes = AddNumberInput(reminders, "每次专注分钟", settings.FocusMinutes);
            reminders.Children.Add(SectionTitle("自定义提醒（最多5个）"));
            reminders.Children.Add(new TextBlock { Text = "启用 · 名称 · 间隔（分钟） · 提醒内容", FontSize = 12, Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            for (int index = 0; index < 5; index++)
            {
                ReminderDefinition definition = index < settings.CustomReminders.Count ? settings.CustomReminders[index] : null;
                CustomReminderRow row = new CustomReminderRow(definition);
                _customRows.Add(row);
                reminders.Children.Add(row.Root);
            }
            _tabs.Items.Add(CreateTab("提醒", reminders));

            StackPanel appearance = PagePanel();
            appearance.Children.Add(SectionTitle("大小"));
            _scale = CreateSlider(0.75, 1.35, settings.Scale, 0.05);
            appearance.Children.Add(_scale);
            appearance.Children.Add(SectionTitle("透明度"));
            _opacity = CreateSlider(0.60, 1.0, settings.Opacity, 0.05);
            appearance.Children.Add(_opacity);
            _mirror = AddToggle(appearance, "镜像朝向", "只翻转完整画面，不替换身体局部", settings.ManualMirror);
            _tabs.Items.Add(CreateTab("外观", appearance));

            StackPanel data = PagePanel();
            _stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 12) };
            data.Children.Add(_stats);
            _dataSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = BrushFrom("#59443A"), LineHeight = 25 };
            data.Children.Add(_dataSummary);
            data.Children.Add(SectionTitle("已获得成就"));
            _achievements = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = BrushFrom("#795D4F"), LineHeight = 24 };
            data.Children.Add(_achievements);
            _achievementCards = new WrapPanel();
            data.Children.Add(_achievementCards);
            StackPanel dataButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
            Button export = CreateButton("导出养成数据", true);
            export.Click += ExportClick;
            Button reset = CreateButton("重置养成进度", false);
            reset.Margin = new Thickness(10, 0, 0, 0);
            reset.Click += ResetClick;
            dataButtons.Children.Add(export);
            dataButtons.Children.Add(reset);
            data.Children.Add(dataButtons);
            _tabs.Items.Add(CreateTab("数据", data));
            _tabs.SelectedIndex = Math.Max(0, Math.Min(4, initialTab));
            RefreshData();

            Border footer = new Border
            {
                Background = BrushFrom("#FFFCF8"),
                BorderBrush = BrushFrom("#E6D7C8"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(18, 12, 18, 12)
            };
            StackPanel footerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancel = CreateButton("取消", false);
            cancel.Click += delegate { Close(); };
            Button apply = CreateButton("保存设置", true);
            apply.Margin = new Thickness(10, 0, 0, 0);
            apply.Click += ApplyClick;
            footerButtons.Children.Add(cancel);
            footerButtons.Children.Add(apply);
            footer.Child = footerButtons;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;
        }

        public Action SettingsChanged;
        public Action ProfileResetRequested;
        public event EventHandler TestAttentionRequested;

        public void SelectTab(int index)
        {
            _tabs.SelectedIndex = Math.Max(0, Math.Min(4, index));
            UpdateLayout();
        }

        private void ApplyClick(object sender, RoutedEventArgs e)
        {
            _settings.StartWithWindows = _startWithWindows.IsChecked == true;
            _settings.AlwaysOnTop = _alwaysOnTop.IsChecked == true;
            _settings.LockPosition = _lockPosition.IsChecked == true;
            _settings.EdgeSnap = _edgeSnap.IsChecked == true;
            _settings.MouseLookEnabled = _mouseLook.IsChecked == true;
            _settings.FocusDoNotDisturb = _focusDoNotDisturb.IsChecked == true;
            _settings.ManualMirror = _mirror.IsChecked == true;
            _settings.InteractionFrequency = (InteractionFrequency)Math.Max(0, _frequency.SelectedIndex);
            _settings.PowerMode = (PowerMode)Math.Max(0, _powerMode.SelectedIndex);
            _settings.Behavior = (BehaviorMode)Math.Max(0, _behavior.SelectedIndex);
            _settings.Scale = _scale.Value;
            _settings.Opacity = _opacity.Value;
            _settings.WaterReminderEnabled = _waterEnabled.IsChecked == true;
            _settings.WaterReminderMinutes = ParseMinutes(_waterMinutes.Text, 45);
            _settings.StretchReminderEnabled = _stretchEnabled.IsChecked == true;
            _settings.StretchReminderMinutes = ParseMinutes(_stretchMinutes.Text, 60);
            _settings.FocusMinutes = ParseMinutes(_focusMinutes.Text, 25);
            _settings.CustomPhrases.Clear();
            foreach (string raw in _phrases.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = PetSettings.NormalizeText(raw, 80);
                if (value.Length > 0 && _settings.CustomPhrases.Count < 50)
                {
                    _settings.CustomPhrases.Add(value);
                }
            }
            _settings.CustomReminders.Clear();
            foreach (CustomReminderRow row in _customRows)
            {
                ReminderDefinition definition = row.Read();
                if (definition != null && _settings.CustomReminders.Count < 5)
                {
                    _settings.CustomReminders.Add(definition);
                }
            }
            if (SettingsChanged != null)
            {
                SettingsChanged();
            }
            else
            {
                // The main window callback applies settings and persists them
                // atomically.  Keep standalone/tests usable without causing
                // a duplicate write in the normal path.
                _settings.Save();
            }
            Close();
        }

        private void ExportClick(object sender, RoutedEventArgs e)
        {
            Forms.SaveFileDialog dialog = new Forms.SaveFileDialog();
            dialog.Title = "导出亚比养成数据";
            dialog.Filter = "文本文件 (*.txt)|*.txt";
            dialog.FileName = "亚比养成数据_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".txt";
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                try
                {
                    _care.Export(dialog.FileName);
                    MessageBox.Show(this, "养成数据已导出。", "亚比桌宠", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(this, "导出失败：" + exception.Message, "亚比桌宠", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void ResetClick(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "确定重置等级、需求值、统计和成就吗？此操作无法撤销。", "重置养成进度", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                if (ProfileResetRequested != null) ProfileResetRequested();
                _care.Reset();
                _lastDataKey = null;
                RefreshData();
            }
        }

        public void RefreshData()
        {
            CareSnapshot s = _care.GetSnapshot(DateTime.UtcNow, DateTime.Now);
            string key = string.Join("|", new object[] { s.Level, s.AffinityXp, s.Fullness, s.Energy, s.Mood, s.TodayActiveSeconds / 60,
                s.TotalActiveSeconds / 60, s.TotalInteractions, s.RemindersCompleted, s.CurrentStreak, s.BestStreak, s.SaveError, string.Join(",", s.Achievements) });
            if (key == _lastDataKey) return;
            _lastDataKey = key;
            _stats.Children.Clear();
            AddStat("亲密等级", "Lv." + s.Level + (s.Level == 99 ? " · 已满级" : ""));
            AddStat("累计亲密度", s.AffinityXp + " 经验");
            AddStat("今日陪伴", s.TodayActiveSeconds / 60 + " 分钟");
            AddStat("累计陪伴", s.TotalActiveSeconds / 60 + " 分钟");
            AddStat("照顾互动", s.TotalInteractions + " 次");
            AddStat("完成提醒", s.RemindersCompleted + " 次");
            _dataSummary.Text = "饱腹 " + s.Fullness + " · 精力 " + s.Energy + " · 心情 " + s.Mood
                + "\n连续陪伴 " + s.CurrentStreak + " 天 · 最佳 " + s.BestStreak + " 天"
                + (string.IsNullOrEmpty(s.SaveError) ? "" : "\n" + s.SaveError);
            _achievements.Text = s.Achievements.Length == 0 ? "还没有获得成就，和亚比多相处一会儿吧。" : "每一次照顾，都会留下温暖的记录。";
            _achievementCards.Children.Clear();
            foreach (string achievement in s.Achievements)
                _achievementCards.Children.Add(new Border { Background = BrushFrom("#E8EDDF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 6, 7, 0),
                    Child = new TextBlock { Text = "✓ " + achievement, Foreground = UiTheme.Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 } });
        }

        private void AddStat(string title, string value)
        {
            StackPanel content = new StackPanel();
            content.Children.Add(new TextBlock { Text = title, Foreground = UiTheme.Muted, FontSize = 12 });
            content.Children.Add(new TextBlock { Text = value, Foreground = UiTheme.Ink, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            _stats.Children.Add(new Border { Child = content, Padding = new Thickness(14), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), Background = BrushFrom("#F7F0E5") });
        }

        private static TabItem CreateTab(string title, StackPanel content)
        {
            ScrollViewer scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(18) };
            Border card = new Border { Child = scroll, Background = UiTheme.Cream, BorderBrush = UiTheme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16) };
            return new TabItem { Header = title, Content = card, Padding = new Thickness(16, 12, 16, 12), FontSize = 13 };
        }

        private static StackPanel PagePanel()
        {
            return new StackPanel { Margin = new Thickness(2) };
        }

        private static CheckBox AddToggle(StackPanel panel, string title, string detail, bool value)
        {
            CheckBox box = new CheckBox
            {
                IsChecked = value,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = BrushFrom("#4F3B31") },
                        new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = BrushFrom("#8A7468"), Margin = new Thickness(0, 2, 0, 0) }
                    }
                },
                Margin = new Thickness(2, 8, 2, 8)
            };
            panel.Children.Add(box);
            return box;
        }

        private static ComboBox AddChoice(StackPanel panel, string title, string[] values, int selected)
        {
            panel.Children.Add(SectionTitle(title));
            ComboBox combo = new ComboBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8) };
            foreach (string value in values) combo.Items.Add(value);
            combo.SelectedIndex = Math.Max(0, Math.Min(values.Length - 1, selected));
            panel.Children.Add(combo);
            return combo;
        }

        private static CheckBox AddReminderLine(StackPanel panel, string title, bool enabled, int minutes, out TextBox minuteBox)
        {
            Grid row = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            CheckBox check = new CheckBox { Content = title, IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
            row.Children.Add(check);
            minuteBox = new TextBox { Text = minutes.ToString(), Padding = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(minuteBox, 1);
            row.Children.Add(minuteBox);
            TextBlock unit = new TextBlock { Text = "分钟", VerticalAlignment = VerticalAlignment.Center, Foreground = BrushFrom("#806B60"), Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(unit, 2);
            row.Children.Add(unit);
            panel.Children.Add(row);
            return check;
        }

        private static TextBox AddNumberInput(StackPanel panel, string title, int value)
        {
            Grid row = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
            TextBox box = new TextBox { Text = value.ToString(), Padding = new Thickness(7), HorizontalContentAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            panel.Children.Add(row);
            return box;
        }

        private static Slider CreateSlider(double minimum, double maximum, double value, double tick)
        {
            return new Slider
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
                TickFrequency = tick,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(4, 5, 4, 13)
            };
        }

        private static TextBlock SectionTitle(string title)
        {
            return new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = BrushFrom("#5A4034"), Margin = new Thickness(0, 14, 0, 7), FontSize = 13 };
        }

        private static Button CreateButton(string text, bool primary)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(15, 8, 15, 8),
                Background = primary ? BrushFrom("#EBC1A7") : BrushFrom("#FFF8F3"),
                BorderBrush = BrushFrom("#DDB69D"),
                Foreground = BrushFrom("#563B2F"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private static int ParseMinutes(string text, int fallback)
        {
            int value;
            return int.TryParse(text, out value) ? Math.Max(5, Math.Min(240, value)) : fallback;
        }

        private static SolidColorBrush BrushFrom(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private sealed class CustomReminderRow
        {
            public readonly Border Root;
            private readonly CheckBox _enabled;
            private readonly TextBox _name;
            private readonly TextBox _minutes;
            private readonly TextBox _message;
            private readonly string _id;

            public CustomReminderRow(ReminderDefinition definition)
            {
                _id = definition == null || string.IsNullOrEmpty(definition.Id) ? Guid.NewGuid().ToString("N") : definition.Id;
                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _enabled = new CheckBox { IsChecked = definition != null && definition.Enabled, VerticalAlignment = VerticalAlignment.Center };
                grid.Children.Add(_enabled);
                _name = new TextBox { Text = definition == null ? string.Empty : definition.Name, Padding = new Thickness(5), ToolTip = "名称" };
                Grid.SetColumn(_name, 1);
                grid.Children.Add(_name);
                _minutes = new TextBox { Text = definition == null ? "30" : definition.IntervalMinutes.ToString(), Padding = new Thickness(5), Margin = new Thickness(5, 0, 5, 0), ToolTip = "间隔分钟" };
                Grid.SetColumn(_minutes, 2);
                grid.Children.Add(_minutes);
                _message = new TextBox { Text = definition == null ? string.Empty : definition.Message, Padding = new Thickness(5), ToolTip = "提醒内容" };
                Grid.SetColumn(_message, 3);
                grid.Children.Add(_message);
                Root = new Border { Child = grid, BorderBrush = BrushFrom("#EEE0D5"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 7, 0, 7) };
            }

            public ReminderDefinition Read()
            {
                string name = PetSettings.NormalizeText(_name.Text, 20);
                string message = PetSettings.NormalizeText(_message.Text, 80);
                if (name.Length == 0 && message.Length == 0 && _enabled.IsChecked != true)
                {
                    return null;
                }
                return new ReminderDefinition
                {
                    Id = _id,
                    Name = name.Length == 0 ? "自定义提醒" : name,
                    Message = message.Length == 0 ? "休息一下吧。" : message,
                    IntervalMinutes = ParseMinutes(_minutes.Text, 30),
                    Enabled = _enabled.IsChecked == true,
                    Kind = ReminderKind.Custom
                };
            }
        }
    }
}
