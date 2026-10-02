using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YabiDesktopPet
{
    /// <summary>
    /// A small, catalog-independent action browser.  The caller owns action
    /// selection and playback; this window only presents the definitions and
    /// forwards preview/automatic-toggle requests.
    /// </summary>
    internal sealed class ActionLibraryWindow : Window
    {
        private readonly TextBlock _status;

        public ActionLibraryWindow(
            IEnumerable<ClipDefinition> clips,
            Func<string, BitmapSource> thumbnailProvider,
            Action<string> preview,
            Func<string, bool> autoEnabled,
            Action<string, bool> setAuto)
        {
            Title = "亚比动作库";
            Width = 520;
            Height = 620;
            MinWidth = 430;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = BrushFrom("#FFF9F4");
            FontFamily = new FontFamily("Microsoft YaHei UI");
            ShowInTaskbar = true;

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border header = new Border
            {
                Background = BrushFrom("#FBE5D7"),
                BorderBrush = BrushFrom("#E6C7B4"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 16, 22, 14)
            };
            StackPanel headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = "动作库",
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFrom("#4A352B")
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "完整播放真实身体动作，可单独预览或加入自动陪伴",
                FontSize = 12,
                Foreground = BrushFrom("#876E61"),
                Margin = new Thickness(0, 3, 0, 0)
            });
            header.Child = headerStack;
            root.Children.Add(header);

            _status = new TextBlock
            {
                Text = "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushFrom("#A5674F"),
                FontSize = 12,
                Margin = new Thickness(22, 9, 22, 4)
            };
            Grid.SetRow(_status, 1);
            root.Children.Add(_status);

            StackPanel list = new StackPanel { Margin = new Thickness(22, 6, 22, 18) };
            int count = 0;
            if (clips != null)
            {
                foreach (ClipDefinition clip in clips)
                {
                    if (clip == null || string.IsNullOrEmpty(clip.Id))
                    {
                        continue;
                    }
                    list.Children.Add(CreateActionCard(clip, thumbnailProvider, preview, autoEnabled, setAuto));
                    count++;
                }
            }
            if (count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "当前没有可用动作。",
                    Foreground = BrushFrom("#876E61"),
                    FontSize = 13,
                    Margin = new Thickness(0, 20, 0, 0)
                });
            }

            ScrollViewer scroll = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);
            Content = root;
        }

        public void SetStatus(string status)
        {
            _status.Text = status ?? string.Empty;
        }

        private static Border CreateActionCard(
            ClipDefinition clip,
            Func<string, BitmapSource> thumbnailProvider,
            Action<string> preview,
            Func<string, bool> autoEnabled,
            Action<string, bool> setAuto)
        {
            Grid grid = new Grid { Margin = new Thickness(0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border thumbnail = new Border
            {
                Width = 94,
                Height = 78,
                CornerRadius = new CornerRadius(12),
                Background = BrushFrom("#F4E4D9"),
                BorderBrush = BrushFrom("#E7CDBD"),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            BitmapSource image = null;
            if (thumbnailProvider != null)
            {
                try
                {
                    image = thumbnailProvider(clip.Id);
                }
                catch
                {
                    image = null;
                }
            }
            if (image != null)
            {
                thumbnail.Child = new Image { Source = image, Stretch = Stretch.Uniform, Margin = new Thickness(5) };
            }
            else
            {
                thumbnail.Child = new TextBlock
                {
                    Text = "亚比",
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrushFrom("#B27C5D"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            grid.Children.Add(thumbnail);

            Grid details = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock name = new TextBlock
            {
                Text = string.IsNullOrEmpty(clip.DisplayName) ? clip.Id : clip.DisplayName,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFrom("#4F3B31")
            };
            details.Children.Add(name);
            TextBlock meta = new TextBlock
            {
                Text = DurationText(clip) + " · 完整身体动作 · 可用",
                FontSize = 11,
                Foreground = BrushFrom("#8A7468"),
                Margin = new Thickness(0, 3, 0, 4)
            };
            Grid.SetRow(meta, 1);
            details.Children.Add(meta);

            StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal };
            Button play = CreateButton("预览", true);
            play.Click += delegate
            {
                if (preview != null)
                {
                    preview(clip.Id);
                }
            };
            controls.Children.Add(play);
            bool automaticAllowed = IsAutomaticToggleAllowed(clip.Id);
            bool careAction = clip.Id == "feed" || clip.Id == "pet";
            bool attentionAction = clip.Id == "notice_left" || clip.Id == "notice_right" || clip.Id == "notice_up";
            CheckBox automatic = new CheckBox
            {
                Content = automaticAllowed ? "自动陪伴" : (careAction ? "照顾按钮触发" : (attentionAction ? "鼠标停留触发" : "姿势与动作菜单")),
                Foreground = BrushFrom("#634A3D"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                IsChecked = automaticAllowed && SafeAutoEnabled(autoEnabled, clip.Id),
                IsEnabled = automaticAllowed
            };
            if (automaticAllowed)
            {
                automatic.Checked += delegate
                {
                    if (setAuto != null)
                    {
                        setAuto(clip.Id, true);
                    }
                };
                automatic.Unchecked += delegate
                {
                    if (setAuto != null)
                    {
                        setAuto(clip.Id, false);
                    }
                };
            }
            controls.Children.Add(automatic);
            Grid.SetRow(controls, 2);
            details.Children.Add(controls);

            TextBlock hint = new TextBlock
            {
                Text = automaticAllowed ? "动作完成后回到稳定坐姿" : (careAction ? "此处仅预览，不增加养成经验" : (attentionAction ? "仅坐姿空闲时自然回应" : "由手动菜单或姿势流程调用")),
                FontSize = 10.5,
                Foreground = BrushFrom("#A38A7C"),
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(hint, 3);
            details.Children.Add(hint);
            Grid.SetColumn(details, 1);
            grid.Children.Add(details);

            return new Border
            {
                Child = grid,
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Background = BrushFrom("#FFFCF8"),
                BorderBrush = BrushFrom("#EAD9CC"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14)
            };
        }

        private static bool SafeAutoEnabled(Func<string, bool> autoEnabled, string id)
        {
            if (autoEnabled == null)
            {
                return false;
            }
            try
            {
                return autoEnabled(id);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAutomaticToggleAllowed(string id)
        {
            return string.Equals(id, "groom", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "scratch", StringComparison.OrdinalIgnoreCase);
        }

        private static string DurationText(ClipDefinition clip)
        {
            double durationMs = clip.DurationMs;
            if (durationMs <= 0 && clip.Fps > 0 && clip.FrameCount > 0)
            {
                durationMs = 1000.0 * clip.FrameCount / clip.Fps;
            }
            return durationMs > 0
                ? (durationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒"
                : "时长未知";
        }

        private static Button CreateButton(string text, bool primary)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(12, 5, 12, 5),
                FontSize = 11.5,
                Background = primary ? BrushFrom("#FBE5D7") : BrushFrom("#FFF8F3"),
                BorderBrush = BrushFrom("#DDB69D"),
                Foreground = BrushFrom("#563B2F"),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private static SolidColorBrush BrushFrom(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
