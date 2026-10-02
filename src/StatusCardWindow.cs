using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed class StatusCardWindow : Window
    {
        private readonly TextBlock _levelText;
        private readonly TextBlock _summaryText;
        private readonly ProgressBar _affinityBar;
        private readonly ProgressBar _fullnessBar;
        private readonly ProgressBar _energyBar;
        private readonly ProgressBar _moodBar;
        private readonly TextBlock _fullnessValue;
        private readonly TextBlock _energyValue;
        private readonly TextBlock _moodValue;
        private readonly TextBlock _fullnessHint;
        private readonly TextBlock _energyHint;
        private readonly TextBlock _moodHint;
        private readonly Button _focusButton;
        private readonly Button _feedButton;
        private readonly Button _petButton;
        private readonly TextBlock _activityText;
        private readonly TextBlock _gazeText;
        private readonly TextBlock _saveError;
        private readonly ScrollViewer _scroll;

        public Action FeedRequested;
        public Action RestRequested;
        public Action PetRequested;
        public Action FocusRequested;
        public Action SettingsRequested;
        public Action GazeRequested;

        public StatusCardWindow()
        {
            Title = "亚比状态";
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            UiTheme.Apply(this);
            Deactivated += delegate { Hide(); };

            StackPanel root = new StackPanel();
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new StackPanel();
            identity.Children.Add(new TextBlock { Text = "亚比", FontSize = 25, FontWeight = FontWeights.SemiBold });
            _levelText = MutedText("Lv.1 · 亲密度 0");
            _levelText.Margin = new Thickness(0, 4, 0, 0);
            identity.Children.Add(_levelText);
            header.Children.Add(identity);
            Button settings = CreateButton("设置", false);
            settings.VerticalAlignment = VerticalAlignment.Top;
            settings.Click += delegate { if (SettingsRequested != null) SettingsRequested(); };
            Grid.SetColumn(settings, 1);
            header.Children.Add(settings);
            root.Children.Add(header);
            _affinityBar = CreateBar(UiTheme.Green);
            _affinityBar.Height = 6;
            _affinityBar.Margin = new Thickness(0, 12, 0, 18);
            root.Children.Add(_affinityBar);

            StackPanel needs = new StackPanel();
            _fullnessBar = AddNeed(needs, "饱腹", UiTheme.Apricot, out _fullnessValue, out _fullnessHint);
            _energyBar = AddNeed(needs, "精力", UiTheme.Green, out _energyValue, out _energyHint);
            _moodBar = AddNeed(needs, "心情", BrushFrom("#D4AA91"), out _moodValue, out _moodHint);
            root.Children.Add(needs);

            UniformGrid actions = new UniformGrid { Columns = 3, Margin = new Thickness(-3, 4, -3, 0) };
            _feedButton = CreateButton("喂食", true);
            _feedButton.Click += delegate { if (FeedRequested != null) FeedRequested(); };
            Button rest = CreateButton("休息", true);
            rest.Click += delegate { if (RestRequested != null) RestRequested(); };
            _petButton = CreateButton("摸摸", true);
            _petButton.Click += delegate { if (PetRequested != null) PetRequested(); };
            actions.Children.Add(_feedButton); actions.Children.Add(rest); actions.Children.Add(_petButton);
            root.Children.Add(actions);
            _activityText = MutedText(string.Empty);
            _activityText.Margin = new Thickness(0, 7, 0, 0);
            _activityText.Visibility = Visibility.Collapsed;
            root.Children.Add(_activityText);

            _focusButton = CreateButton("开始专注（25分钟）", false);
            _focusButton.Background = BrushFrom("#E8EDDF");
            _focusButton.BorderBrush = BrushFrom("#CCD7C4");
            _focusButton.Margin = new Thickness(0, 14, 0, 0);
            _focusButton.Click += delegate { if (FocusRequested != null) FocusRequested(); };
            root.Children.Add(_focusButton);

            StackPanel summary = new StackPanel();
            summary.Children.Add(new TextBlock { Text = "我们的陪伴", FontSize = 12, FontWeight = FontWeights.SemiBold });
            _summaryText = MutedText("今日 0 分钟 · 连续 0 天");
            _summaryText.Margin = new Thickness(0, 5, 0, 0);
            summary.Children.Add(_summaryText);
            root.Children.Add(new Border { Child = summary, Background = BrushFrom("#F7EFE4"), CornerRadius = new CornerRadius(12), Padding = new Thickness(13), Margin = new Thickness(0, 16, 0, 0) });
            _saveError = MutedText(string.Empty);
            _saveError.Foreground = BrushFrom("#98623C");
            _saveError.Margin = new Thickness(0, 8, 0, 0);
            _saveError.Visibility = Visibility.Collapsed;
            root.Children.Add(_saveError);

            Button gaze = CreateButton("注视状态  ›", false);
            gaze.Background = Brushes.Transparent; gaze.BorderThickness = new Thickness(0);
            gaze.Padding = new Thickness(0, 8, 0, 2); gaze.FontSize = 11;
            gaze.HorizontalAlignment = HorizontalAlignment.Left;
            gaze.Foreground = UiTheme.Muted;
            gaze.Click += delegate { if (GazeRequested != null) GazeRequested(); };
            root.Children.Add(gaze);
            _gazeText = MutedText("站立和坐姿支持视线跟随");
            _gazeText.FontSize = 10;
            root.Children.Add(_gazeText);
            TextBlock hint = MutedText("点击空白处关闭 · 双击亚比切换姿势");
            hint.FontSize = 10; hint.Margin = new Thickness(0, 11, 0, 0);
            root.Children.Add(hint);

            _scroll = new ScrollViewer { Content = root, Padding = new Thickness(0, 0, 4, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Content = new Border { Child = _scroll, Background = UiTheme.Cream, BorderBrush = UiTheme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(20) };
        }

        public void Refresh(CareSnapshot snapshot, string focusLabel)
        {
            RefreshValues(snapshot.Fullness, snapshot.Energy, snapshot.Mood, snapshot.Level, snapshot.AffinityXp,
                snapshot.AffinityProgress, snapshot.TodayActiveSeconds, snapshot.CurrentStreak, snapshot.TotalActiveSeconds, focusLabel);
            SetSaveError(snapshot.SaveError);
        }

        public void Refresh(PetProfile profile, string focusLabel)
        {
            GrowthState growth = profile.Growth;
            RefreshValues(profile.Care.Fullness, profile.Care.Energy, profile.Care.Mood, growth.Level, growth.AffinityXp,
                growth.Level >= 99 ? 100 : growth.AffinityXp % 100, growth.TodayActiveSeconds, growth.CurrentStreak, growth.TotalActiveSeconds, focusLabel);
        }

        private void RefreshValues(int fullness, int energy, int mood, int level, int xp, int progress, long today, int streak, long total, string focusLabel)
        {
            _levelText.Text = level >= 99 ? "Lv.99 · 已满级" : "Lv." + level + " · 亲密度 " + xp;
            _affinityBar.Value = level >= 99 ? 100 : progress;
            _summaryText.Text = "今日 " + Math.Max(0, today / 60) + " 分钟 · 连续 " + streak + " 天\n累计相伴 " + Math.Max(0, total / 60) + " 分钟";
            SetNeed(_fullnessBar, _fullnessValue, _fullnessHint, fullness, "有点饿", "有些饿了，喂一点食物吧。", "肚子饱饱的，慢慢消化吧。");
            SetNeed(_energyBar, _energyValue, _energyHint, energy, "有点困", "休息一会儿，慢慢恢复精力。", "精神不错，可以一起陪伴。");
            SetNeed(_moodBar, _moodValue, _moodHint, mood, "想陪伴", "轻轻摸摸，陪亚比待一会儿。", "心情很好，谢谢你的陪伴。");
            _focusButton.Content = focusLabel;
        }

        public void SetActivity(string activity)
        {
            _activityText.Text = activity ?? string.Empty;
            _activityText.Visibility = string.IsNullOrEmpty(activity) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void SetSaveError(string error)
        {
            _saveError.Text = string.IsNullOrEmpty(error) ? string.Empty : "进度暂未保存，稍后会重试。";
            _saveError.ToolTip = error;
            _saveError.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void SetGazeStatus(string status) { _gazeText.Text = status ?? string.Empty; }

        public void SetCareFeedback(TimeSpan feed, TimeSpan pet, bool feedQueued, bool petQueued)
        {
            SetCareButtonFeedback(_feedButton, feed, feedQueued, "喂食");
            SetCareButtonFeedback(_petButton, pet, petQueued, "摸摸");
        }

        private static void SetCareButtonFeedback(Button button, TimeSpan remaining, bool queued, string label)
        {
            bool cooling = remaining > TimeSpan.Zero;
            string detail = queued ? "等待动作完成" : cooling ? "再等 " + Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) + " 分钟" : "现在可以" + label;
            button.Content = queued ? label + " · 等待" : cooling ? label + " · " + Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) + "分" : label;
            button.IsEnabled = !queued && !cooling;
            button.ToolTip = detail;
            ToolTipService.SetShowOnDisabled(button, true);
        }

        public void ShowNear(Window owner)
        {
            Owner = owner;
            Rect work = GetOwnerWorkArea(owner);
            Width = Math.Min(360, Math.Max(1, work.Width - 16));
            MaxHeight = Math.Max(1, work.Height - 16);
            _scroll.MaxHeight = Math.Max(1, MaxHeight - 42);
            // Measure content, not the native Window before its HWND is ready.
            // Window.Measure can fail-fast in GetWindowMinMax after another
            // utility window closes; content measurement has no HWND dependency.
            FrameworkElement surface = (FrameworkElement)Content;
            surface.Measure(new Size(Width, MaxHeight));
            double height = Math.Min(MaxHeight, surface.DesiredSize.Height);
            double top = owner.Top - height - 12;
            if (top < work.Top + 8) top = owner.Top + owner.Height + 12;
            Left = Math.Max(work.Left + 8, Math.Min(work.Right - Width - 8, owner.Left + owner.Width / 2 - Width / 2));
            Top = Math.Max(work.Top + 8, Math.Min(work.Bottom - height - 8, top));
            Show();
            Activate();
        }

        private static Rect GetOwnerWorkArea(Window owner)
        {
            try
            {
                Forms.Screen screen = Forms.Screen.FromHandle(new WindowInteropHelper(owner).Handle);
                Rect pixels = new Rect(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height);
                PresentationSource source = PresentationSource.FromVisual(owner);
                if (source != null && source.CompositionTarget != null)
                    return new Rect(source.CompositionTarget.TransformFromDevice.Transform(pixels.TopLeft), source.CompositionTarget.TransformFromDevice.Transform(pixels.BottomRight));
            }
            catch { }
            return SystemParameters.WorkArea;
        }

        private static ProgressBar AddNeed(StackPanel parent, string title, Brush color, out TextBlock value, out TextBlock hint)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
            value = MutedText("80 / 100 · 充足"); Grid.SetColumn(value, 1); row.Children.Add(value); parent.Children.Add(row);
            ProgressBar bar = CreateBar(color); bar.Margin = new Thickness(0, 7, 0, 5); parent.Children.Add(bar);
            hint = MutedText(string.Empty); hint.FontSize = 11; hint.Margin = new Thickness(0, 0, 0, 13); parent.Children.Add(hint);
            return bar;
        }

        private static ProgressBar CreateBar(Brush color)
        {
            return new ProgressBar { Minimum = 0, Maximum = 100, Value = 80, Height = 7, Foreground = color };
        }

        private static Button CreateButton(string text, bool compact)
        {
            return new Button { Content = text, Background = compact ? UiTheme.Apricot : UiTheme.Cream, Padding = new Thickness(compact ? 4 : 12, 9, compact ? 4 : 12, 9), FontSize = compact ? 12 : 13, Margin = compact ? new Thickness(3, 0, 3, 0) : new Thickness(0) };
        }

        private static TextBlock MutedText(string text)
        {
            return new TextBlock { Text = text, FontSize = 12, Foreground = UiTheme.Muted, TextWrapping = TextWrapping.Wrap };
        }

        private static void SetNeed(ProgressBar bar, TextBlock valueText, TextBlock hint, int value, string lowLabel, string lowAdvice, string highAdvice)
        {
            value = Math.Max(0, Math.Min(100, value));
            bar.Value = value;
            valueText.Text = value + " / 100 · " + (value < 30 ? lowLabel : value < 60 ? "适中" : "充足");
            hint.Text = value < 30 ? lowAdvice : value < 60 ? "状态尚好，记得偶尔照顾一下。" : highAdvice;
        }

        private static SolidColorBrush BrushFrom(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
    }
}
