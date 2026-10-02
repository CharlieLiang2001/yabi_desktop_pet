using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed class SpeechBubbleWindow : Window
    {
        private readonly TextBlock _text;
        private readonly StackPanel _content;
        private readonly StackPanel _reminderActions;
        private readonly DispatcherTimer _hideTimer;
        private Action _reminderCompleted;
        private Action _reminderSnoozed;
        private Action _reminderDismissed;
        private bool _reminderActive;
        public bool ReminderActive { get { return _reminderActive; } }

        public SpeechBubbleWindow()
        {
            UiTheme.Apply(this);
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;

            Border bubble = new Border();
            bubble.Background = UiTheme.Cream;
            bubble.BorderBrush = UiTheme.Line;
            bubble.BorderThickness = new Thickness(1.2);
            bubble.CornerRadius = new CornerRadius(16);
            bubble.Padding = new Thickness(13, 8, 13, 8);
            bubble.Margin = new Thickness(8);
            bubble.Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.28
            };

            _content = new StackPanel();
            _text = new TextBlock();
            _text.Foreground = new SolidColorBrush(Color.FromRgb(72, 48, 33));
            _text.FontFamily = new FontFamily("Microsoft YaHei UI");
            _text.FontSize = 14;
            _text.TextWrapping = TextWrapping.Wrap;
            _text.MaxWidth = 230;
            _content.Children.Add(_text);

            _reminderActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _reminderActions.Children.Add(CreateActionButton("完成", FinishCompleted));
            _reminderActions.Children.Add(CreateActionButton("稍后", FinishSnoozed));
            _reminderActions.Children.Add(CreateActionButton("忽略", FinishDismissed));
            _content.Children.Add(_reminderActions);
            bubble.Child = _content;
            Content = bubble;

            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromSeconds(4);
            _hideTimer.Tick += delegate
            {
                FinishReminder(null);
            };
        }

        public void Say(string message, MainWindow owner)
        {
            ClearReminderState();
            _text.Text = message;
            if (!IsVisible)
            {
                Show();
            }
            UpdateLayout();
            PositionNear(owner);
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromSeconds(4);
            _hideTimer.Start();
        }

        /// <summary>
        /// Compatibility alias for callers that use a message-oriented name.
        /// </summary>
        public void ShowMessage(string message, MainWindow owner)
        {
            Say(message, owner);
        }

        /// <summary>
        /// Configures the three explicit reminder responses.  The callbacks
        /// are invoked at most once and are cleared before invocation so a
        /// callback that immediately shows another message cannot be followed
        /// by an old timer or old button action.
        /// </summary>
        public void SetReminderActions(Action completed, Action snoozed, Action dismissed)
        {
            _reminderCompleted = completed;
            _reminderSnoozed = snoozed;
            _reminderDismissed = dismissed;
            if (IsVisible && (completed != null || snoozed != null || dismissed != null))
            {
                _reminderActive = true;
                _reminderActions.Visibility = Visibility.Visible;
                UpdateLayout();
                _hideTimer.Stop();
                _hideTimer.Interval = TimeSpan.FromSeconds(12);
                _hideTimer.Start();
            }
        }

        public void ShowReminder(string message, MainWindow owner)
        {
            ShowReminderCore(message, owner, null);
        }

        public void ShowReminder(string message, MainWindow owner, Action completed, Action snoozed, Action dismissed)
        {
            SetReminderActions(completed, snoozed, dismissed);
            ShowReminderCore(message, owner, null);
        }

        public void ShowReminder(string message, Window owner)
        {
            ShowReminderCore(message, owner, null);
        }

        public void ShowReminder(string message, Point position, Action completed, Action snoozed, Action dismissed)
        {
            SetReminderActions(completed, snoozed, dismissed);
            ShowReminderCore(message, null, position);
        }

        public void ShowReminder(string message, Point position)
        {
            ShowReminderCore(message, null, position);
        }

        public void ShowReminder(string message, double left, double top)
        {
            ShowReminderCore(message, null, new Point(left, top));
        }

        public void PositionNear(MainWindow owner)
        {
            if (!IsVisible)
            {
                return;
            }
            double bubbleWidth = ActualWidth > 0 ? ActualWidth : 180;
            double bubbleHeight = ActualHeight > 0 ? ActualHeight : 55;
            double left = owner.Left + owner.Width / 2.0 - bubbleWidth / 2.0;
            double top = owner.Top - bubbleHeight + 8;
            Rect work = GetOwnerWorkArea(owner);
            if (top < work.Top)
            {
                top = owner.Top + owner.Height - 8;
            }
            double minLeft = work.Left + 8;
            double maxLeft = Math.Max(minLeft, work.Right - bubbleWidth - 8);
            Left = Math.Max(minLeft, Math.Min(maxLeft, left));
            Top = Math.Max(work.Top + 8, Math.Min(work.Bottom - bubbleHeight - 8, top));
        }

        public void StopAndHide()
        {
            FinishReminder(null);
        }

        private void ShowReminderCore(string message, Window owner, Point? position)
        {
            _text.Text = message;
            _reminderActive = true;
            _reminderActions.Visibility = Visibility.Visible;
            if (!IsVisible)
            {
                Show();
            }
            UpdateLayout();
            if (position.HasValue)
            {
                Left = position.Value.X;
                Top = position.Value.Y;
            }
            else if (owner is MainWindow)
            {
                PositionNear((MainWindow)owner);
            }
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromSeconds(12);
            _hideTimer.Start();
        }

        private void FinishCompleted(object sender, RoutedEventArgs e)
        {
            FinishReminder(_reminderCompleted);
        }

        private void FinishSnoozed(object sender, RoutedEventArgs e)
        {
            FinishReminder(_reminderSnoozed);
        }

        private void FinishDismissed(object sender, RoutedEventArgs e)
        {
            FinishReminder(_reminderDismissed);
        }

        private void FinishReminder(Action callback)
        {
            _hideTimer.Stop();
            bool active = _reminderActive;
            _reminderActive = false;
            _reminderActions.Visibility = Visibility.Collapsed;
            _reminderCompleted = null;
            _reminderSnoozed = null;
            _reminderDismissed = null;
            _hideTimer.Interval = TimeSpan.FromSeconds(4);
            Hide();
            if (active && callback != null)
            {
                callback();
            }
        }

        private void ClearReminderState()
        {
            _hideTimer.Stop();
            _reminderActive = false;
            _reminderActions.Visibility = Visibility.Collapsed;
            _reminderCompleted = null;
            _reminderSnoozed = null;
            _reminderDismissed = null;
            _hideTimer.Interval = TimeSpan.FromSeconds(4);
        }

        private static Button CreateActionButton(string text, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = text,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(3, 0, 3, 0),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 59, 47)),
                Background = new SolidColorBrush(Color.FromRgb(251, 229, 215)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(221, 182, 157)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += handler;
            return button;
        }

        private static Rect GetOwnerWorkArea(MainWindow owner)
        {
            try
            {
                Forms.Screen screen = Forms.Screen.FromHandle(new WindowInteropHelper(owner).Handle);
                Rect pixelRect = new Rect(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height);
                PresentationSource source = PresentationSource.FromVisual(owner);
                if (source != null && source.CompositionTarget != null)
                {
                    Point topLeft = source.CompositionTarget.TransformFromDevice.Transform(pixelRect.TopLeft);
                    Point bottomRight = source.CompositionTarget.TransformFromDevice.Transform(pixelRect.BottomRight);
                    return new Rect(topLeft, bottomRight);
                }
            }
            catch
            {
                // The owner can be between construction and HWND creation.
            }
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }
    }
}
