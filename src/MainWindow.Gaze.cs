using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed partial class MainWindow
    {
        private readonly ContinuousGazeController _continuousGaze = new ContinuousGazeController();
        private readonly Stopwatch _continuousClock = Stopwatch.StartNew();
        private ContinuousGazeView _continuousView;
        private TextBlock _gazeMenuStatus;
        private string _gazeStatus = "实时视线跟随 · 站立和坐姿可用";
        private bool _continuousEngaged;
        private bool _continuousUnavailable;
        private Point? _previousContinuousCursor;
        private DateTime _lastContinuousMoveUtc;
        private DateTime _lastContinuousInteractionUtc;
        private DateTime _lastGazeUiUtc;
        private bool _testGaze;
        private Point? _testGazeCursor;
        private bool _testGazeOtherScreen;

        private void StartAttentionTest()
        {
            if (_settingsWindow != null) _settingsWindow.Close();
            if (_statusCard != null) _statusCard.Hide();
            if (_actionLibrary != null) _actionLibrary.Close();
            if (_visualRoot.ContextMenu != null) _visualRoot.ContextMenu.IsOpen = false;
            if (_attentionTest == null)
            {
                _attentionTest = new AttentionTestWindow();
                _attentionTest.Owner = this;
                _attentionTest.SetPortrait(_catalog.LoadResidentFrame("sit_idle", 0));
                _attentionTest.EnabledChanged = delegate(bool enabled) { SetMouseLookEnabled(enabled, false); };
                _attentionTest.Closed += delegate { _attentionTest = null; };
                FitUtilityWindow(_attentionTest);
                _attentionTest.Show();
            }
            else { _attentionTest.Show(); _attentionTest.Activate(); }
            RefreshContinuousFeedback("移动鼠标即可观察实时响应；不需要停留或点击试播", true);
        }

        private void SuspendContinuousGaze()
        {
            if (_continuousView != null) _continuousView.Visibility = Visibility.Collapsed;
            _imageA.Visibility = Visibility.Visible;
            _imageB.Visibility = Visibility.Visible;
            _continuousGaze.Reset();
            _continuousClock.Restart();
            _continuousEngaged = false;
        }

        private string ContinuousPauseReason(bool sameDisplay)
        {
            if (_continuousUnavailable) return "实时渲染暂不可用，已恢复原始照片；原有动作仍可使用";
            if (!_mouseLookEnabled) return "实时注视已关闭，视线平滑回正";
            if (!IsVisible) return "已隐藏";
            if (_clickThrough) return "点击穿透中，暂停注视";
            if (_isDragging) return "拖动中，暂停注视";
            if (IsVisualBusy) return "正在播放" + GetStateLabel() + "，动作结束后恢复连续跟随";
            if (_stablePose == PetPose.Sleeping) return "亚比睡着了，唤醒后恢复连续跟随";
            if (HasInteractionPanel) return "其他菜单或面板打开，暂时回正";
            if (FocusIsQuiet) return "专注免打扰中，暂停注视";
            if (!sameDisplay) return "鼠标在另一屏幕，视线平滑回正";
            return null;
        }

        private void NaturalMouseTick()
        {
            if (!IsLoaded || _isClosing || (_testNatural && !_testGaze)) return;
            double dt = _continuousClock.Elapsed.TotalSeconds;
            _continuousClock.Restart();
            Forms.Screen petScreen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            System.Drawing.Point actual = Forms.Control.MousePosition;
            Point cursor = _testGazeCursor ?? new Point(actual.X, actual.Y);
            bool sameDisplay = _testGazeCursor.HasValue ? !_testGazeOtherScreen : Forms.Screen.FromPoint(actual).DeviceName == petScreen.DeviceName;
            string paused = ContinuousPauseReason(sameDisplay);
            bool eligible = paused == null;
            bool stable = !IsVisualBusy && (_stablePose == PetPose.Standing || _stablePose == PetPose.Sitting);
            if (!stable || !IsVisible)
            {
                SuspendContinuousGaze();
                RefreshContinuousFeedback(paused ?? "等待动作结束", false);
                return;
            }

            ContinuousGazeRig rig = new ContinuousGazeRig(_stablePose == PetPose.Standing);
            double anchor = rig.HeadX / 320;
            Point head = PointToScreen(new Point(Width * (_manualMirror ? 1 - anchor : anchor), Height * rig.EyeY / 480));
            Vector delta = cursor - head;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null) delta = source.CompositionTarget.TransformFromDevice.Transform(delta);
            double bodyHeight = Height * (_stablePose == PetPose.Standing ? .80 : .70);
            _continuousGaze.Update(delta.X, delta.Y, bodyHeight, eligible, _manualMirror, dt);

            DateTime now = DateTime.UtcNow;
            if (eligible && (!_previousContinuousCursor.HasValue || (cursor - _previousContinuousCursor.Value).Length > 2))
            { _lastContinuousMoveUtc = now; _previousContinuousCursor = cursor; }
            _continuousEngaged = eligible && now - _lastContinuousMoveUtc < TimeSpan.FromSeconds(2);
            if (_continuousEngaged && now - _lastContinuousInteractionUtc > TimeSpan.FromSeconds(1))
            { _controller.RegisterInteraction(now); _lastContinuousInteractionUtc = now; }

            // Keep the same renderer at neutral during temporary pauses. Switching
            // GPU texture sampling back and forth can otherwise look like fur flicker.
            if (eligible || !_continuousGaze.IsNeutral || (_mouseLookEnabled && !_continuousUnavailable && _continuousView != null))
            {
                try
                {
                    if (_continuousView == null)
                    {
                        _continuousView = new ContinuousGazeView();
                        Panel.SetZIndex(_continuousView, 4);
                        _visualRoot.Children.Add(_continuousView);
                    }
                    // Gaze owns a stable pose texture, never the video's reused
                    // WriteableBitmap. No freeze or mutation crosses renderers.
                    bool standing = _stablePose == PetPose.Standing;
                    _continuousView.SetTexture(_catalog.LoadResidentFrame(standing ? "stand_idle" : "sit_idle", 0), standing);
                    _continuousView.UpdatePose(_continuousGaze);
                    _continuousView.Visibility = Visibility.Visible;
                    _imageA.Visibility = Visibility.Hidden;
                    _imageB.Visibility = Visibility.Hidden;
                }
                catch (Exception error)
                {
                    SuspendContinuousGaze();
                    _continuousUnavailable = true;
                    LogPlaybackError("continuous gaze fallback: " + error.Message);
                    RefreshContinuousFeedback(ContinuousPauseReason(sameDisplay), true);
                    return;
                }
            }
            else SuspendContinuousGaze();
            RefreshContinuousFeedback(paused ?? "实时跟随中 · 眼睛先响应，头颈稍后跟上", false);
        }

        private void RefreshContinuousFeedback(string status, bool force)
        {
            _gazeStatus = status;
            if (!force && DateTime.UtcNow - _lastGazeUiUtc < TimeSpan.FromMilliseconds(120)) return;
            _lastGazeUiUtc = DateTime.UtcNow;
            if (_gazeMenuStatus != null && _gazeMenuStatus.Text != status) _gazeMenuStatus.Text = status;
            if (_statusCard != null) _statusCard.SetGazeStatus(status);
            if (_attentionTest != null) _attentionTest.SetContinuousState(status, _mouseLookEnabled, _continuousGaze.EyeX, _continuousGaze.EyeY, _continuousGaze.HeadX, _continuousGaze.HeadY);
        }
    }
}
