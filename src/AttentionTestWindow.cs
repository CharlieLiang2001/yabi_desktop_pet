using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YabiDesktopPet
{
    internal sealed class AttentionTestWindow : Window
    {
        private readonly TextBlock _status, _values;
        private readonly CheckBox _enabled;
        private readonly Image _portrait;
        private bool _updating;
        public Action<bool> EnabledChanged;
        public AttentionTestWindow()
        {
            Title = "亚比 · 实时视线跟随";
            Width = 460; Height = 455; MinWidth = 380; MinHeight = 380;
            Background = Brush("#FFFAF5"); Foreground = Brush("#513B30");
            FontFamily = new FontFamily("Microsoft YaHei UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            StackPanel body = new StackPanel { Margin = new Thickness(22) };
            body.Children.Add(Text("你动，亚比也看过来", 23, "#513B30", 0));
            body.Children.Add(Text("实时连续注视 · 不再触发整段转头视频", 12, "#8B7162", 7));
            Grid card = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            card.ColumnDefinitions.Add(new ColumnDefinition());
            _portrait = new Image { Width = 70, Height = 105, Stretch = Stretch.Uniform };
            card.Children.Add(_portrait);
            _status = Text("移动桌面鼠标，观察亚比的眼睛和头部", 14, "#513B30", 0);
            _status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(_status, 1); card.Children.Add(_status);
            body.Children.Add(new Border { Background = Brush("#F6E9DE"), CornerRadius = new CornerRadius(14), Padding = new Thickness(13), Child = card });
            _enabled = new CheckBox { Content = "启用实时视线跟随（站立 / 坐姿）", FontSize = 14, Margin = new Thickness(0, 20, 0, 0) };
            _enabled.Checked += EnableChanged; _enabled.Unchecked += EnableChanged;
            body.Children.Add(_enabled);
            _values = Text("", 12, "#9A6548", 12); body.Children.Add(_values);
            body.Children.Add(Text("无需停留、冷却或开启互动模式。鼠标停止时视线保持；跨屏、拖动、睡眠和专注免打扰时暂停。", 12, "#8B7162", 14));
            body.Children.Add(Text("当前为写实照片的小幅 2D 近似，眼球尚未独立分层；不支持真实大角度侧脸。身体、爪子和尾巴固定，其他动作沿用原速完整视频。", 11.5, "#8B7162", 10));
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        public void SetPortrait(BitmapSource bitmap) { _portrait.Source = bitmap; }
        private void EnableChanged(object sender, RoutedEventArgs e)
        { if (!_updating && EnabledChanged != null) EnabledChanged(_enabled.IsChecked == true); }
        public void SetContinuousState(string status, bool enabled, double eyeX, double eyeY, double headX, double headY)
        {
            _status.Text = status;
            _values.Text = string.Format("视线  X {0:+0.00;-0.00;0.00}  Y {1:+0.00;-0.00;0.00}\n头颈  X {2:+0.00;-0.00;0.00}  Y {3:+0.00;-0.00;0.00}", eyeX, eyeY, headX, headY);
            _updating = true; _enabled.IsChecked = enabled; _updating = false;
        }
        public void UpdateStatus(string status, double x, double y) { SetContinuousState("实时参数预览", true, x, y, x * .8, y * .8); }
        private static TextBlock Text(string text, double size, string color, double top)
        { return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) }; }
        private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
    }
}
