using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace YabiDesktopPet
{
    internal sealed partial class MainWindow
    {
        private readonly NaturalAttentionController _attention = new NaturalAttentionController();
        private readonly NaturalActionQueue _naturalQueue = new NaturalActionQueue();
        private readonly Stopwatch _playbackClock = new Stopwatch();
        private readonly Stopwatch _frameRequestClock = new Stopwatch();
        private WriteableBitmap _frameSurface;
        private WriteableBitmap _spareSurface;
        private readonly HashSet<string> _failedClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextNaturalIdleUtc;
        private string _activityText;
        private int _careEpoch;
        private ActionLibraryWindow _actionLibrary;
        private AttentionTestWindow _attentionTest;
        private ReminderDefinition _latestReminder;
        private readonly List<string> _testTrace = new List<string>();

        private bool IsVisualBusy { get { return _clipPlaying || _framePending || _isCrossfading || _controller.IsBusy; } }
        private bool FocusIsQuiet { get { return _settings.FocusDoNotDisturb && _reminderController.FocusRunning; } }
        private bool HasInteractionPanel
        {
            get
            {
                return (_visualRoot.ContextMenu != null && _visualRoot.ContextMenu.IsOpen)
                    || (_statusCard != null && _statusCard.IsVisible)
                    || (_settingsWindow != null && _settingsWindow.IsVisible)
                    || (_actionLibrary != null && _actionLibrary.IsVisible);
            }
        }
        private string ActivityLabel
        {
            get { return _naturalQueue.Peek == null ? GetStateLabel() : GetStateLabel() + " · 等待：" + _naturalQueue.Peek.Label; }
        }

        private void InitializeNaturalFeatures()
        {
            _mouseLookMenuItem.Header = "实时视线跟随（站立 / 坐姿）";
            _trayIcon.BalloonTipClicked += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!IsVisible) ToggleVisibility();
                    if (_clickThrough) SetClickThrough(false, false);
                    if (_latestReminder != null) PresentReminder(_latestReminder);
                }));
            };
        }

        private static string ActionLabel(PetAction action)
        {
            switch (action)
            {
                case PetAction.Stand: return "站立";
                case PetAction.Blink: return "眨眼";
                case PetAction.Sit: return "坐下";
                case PetAction.LookAround: return "环顾";
                case PetAction.Sleep: return "睡觉";
                default: return "唤醒";
            }
        }

        private void QueueNaturalRequest(NaturalActionRequest request)
        {
            if (_naturalQueue.Peek != null && _naturalQueue.Peek.ClipId == request.ClipId
                && _naturalQueue.Peek.CareKind == request.CareKind) return;
            _naturalQueue.Enqueue(request);
            ShowSpeech("收到，等当前动作自然结束后" + request.Label + "。");
            RefreshStatusCard();
            if (_actionLibrary != null) _actionLibrary.SetStatus(ActivityLabel);
        }

        private bool ProcessNaturalQueue()
        {
            if (_isClosing || IsVisualBusy) return false;
            NaturalActionRequest request = _naturalQueue.Take();
            if (request == null) return false;
            _queuedManualAction = null;
            if (request.ClipId == "@rest") { BeginRestRequest(request); return true; }
            if (request.ClipId.StartsWith("@pose:", StringComparison.Ordinal))
            {
                PetAction action;
                if (Enum.TryParse(request.ClipId.Substring(6), out action)) RequestManualAction(action);
            }
            else BeginNaturalRequest(request);
            return true;
        }

        private void RequestNaturalAction(string clipId, string careKind, bool preview)
        {
            if (!_catalog.HasClip(clipId) || _failedClips.Contains(clipId))
            {
                ShowSpeech("这段动作素材暂不可用，其他功能不受影响。");
                return;
            }
            if (!string.IsNullOrEmpty(careKind) && CareCooldownSeconds(careKind) > 0)
            {
                ShowSpeech(CareCooldownLabel(careKind));
                return;
            }
            _controller.RegisterInteraction(DateTime.UtcNow);
            NaturalActionRequest request = new NaturalActionRequest
            {
                ClipId = clipId, CareKind = careKind, Preview = preview,
                CareEpoch = _careEpoch,
                Label = _catalog.GetClip(clipId).DisplayName
            };
            if (IsVisualBusy) { QueueNaturalRequest(request); return; }
            _naturalQueue.Clear();
            _queuedManualAction = null;
            BeginNaturalRequest(request);
        }

        private void BeginNaturalRequest(NaturalActionRequest request)
        {
            ClipDefinition clip = _catalog.GetClip(request.ClipId);
            if (_stablePose != clip.StartPose)
            {
                _activityText = "准备" + request.Label;
                if (clip.StartPose == PetPose.Sitting)
                {
                    EnsureSitting(true, delegate
                    {
                        if (!ProcessNaturalQueue()) BeginRegisteredClip(request, false);
                    });
                }
                else
                {
                    _naturalQueue.Enqueue(request);
                    if (clip.StartPose == PetPose.Sleeping) StartSleep(true);
                    else StartWake(true);
                }
                return;
            }
            BeginRegisteredClip(request, false);
        }

        private void BeginRegisteredClip(NaturalActionRequest request, bool automatic)
        {
            if (!request.TryBegin(_careEpoch)) { ProcessQueuedManualAction(); return; }
            ClipDefinition clip = _catalog.GetClip(request.ClipId);
            if (!request.Preview && !string.IsNullOrEmpty(request.CareKind))
            {
                CareActionResult result = request.CareKind == "feed"
                    ? _careController.Feed(DateTime.UtcNow, DateTime.Now)
                    : _careController.Pet(DateTime.UtcNow, DateTime.Now);
                ShowSpeech(result.Message);
                if (!result.Applied) { ProcessQueuedManualAction(); return; }
            }
            _activityText = request.Label;
            _controller.Begin(PetCommand.LookAround, !automatic, DateTime.UtcNow);
            _activeCommand = PetCommand.LookAround;
            _currentAction = PetAction.LookAround;
            ResetNaturalIdleTimer();
            SyncMenus();
            RefreshStatusCard();
            if (_actionLibrary != null) _actionLibrary.SetStatus(ActivityLabel);
            PlayClip(request.ClipId, false, delegate
            {
                _stablePose = clip.EndPose;
                _controller.CancelTo(clip.EndPose, DateTime.UtcNow);
                _controller.HoldAutomaticActions(DateTime.UtcNow, 3);
                _activeCommand = PetCommand.None;
                if (request.ClipId.StartsWith("notice_", StringComparison.Ordinal))
                    _attention.Complete(DateTime.UtcNow, _settings.InteractionFrequency);
                string stable = clip.EndPose == PetPose.Sleeping ? "sleep_idle" : (clip.EndPose == PetPose.Standing ? "stand_idle" : "sit_idle");
                PetAction poseAction = clip.EndPose == PetPose.Sleeping ? PetAction.Sleep : (clip.EndPose == PetPose.Standing ? PetAction.Stand : PetAction.Sit);
                ShowStableFrame(stable, clip.EndPose, poseAction, clip.Optional, delegate
                {
                    _activityText = null;
                    ResetNaturalIdleTimer();
                    RefreshStatusCard();
                    if (_actionLibrary != null) _actionLibrary.SetStatus("已完整播放 · 预览不会增加养成经验");
                    ProcessQueuedManualAction();
                });
            });
        }

        private double CareCooldownSeconds(string kind)
        {
            DateTime last = kind == "feed" ? _careController.Profile.LastFedUtc : _careController.Profile.LastPettedUtc;
            if (last == DateTime.MinValue) return 0;
            return CareController.Remaining(last, DateTime.UtcNow, TimeSpan.FromMinutes(kind == "feed" ? 20 : 5)).TotalSeconds;
        }

        private string CareCooldownLabel(string kind)
        {
            double seconds = CareCooldownSeconds(kind);
            return seconds <= 0 ? (kind == "feed" ? "可以喂食" : "可以摸摸")
                : (kind == "feed" ? "喂食" : "摸摸") + "冷却剩余 " + (int)Math.Ceiling(seconds / 60) + " 分钟";
        }

        private void ResetNaturalIdleTimer()
        {
            int min = _settings.InteractionFrequency == InteractionFrequency.Calm ? 180 : (_settings.InteractionFrequency == InteractionFrequency.Lively ? 45 : 90);
            int max = _settings.InteractionFrequency == InteractionFrequency.Calm ? 301 : (_settings.InteractionFrequency == InteractionFrequency.Lively ? 91 : 181);
            _nextNaturalIdleUtc = DateTime.UtcNow.AddSeconds(_random.Next(min, max));
        }

        private bool TryNaturalIdleAction()
        {
            if (_stablePose != PetPose.Sitting || _controller.Mode != BehaviorMode.Companion
                || DateTime.UtcNow < _nextNaturalIdleUtc || _isDragging || _clickThrough) return false;
            string[] candidates = new[] { "groom", "scratch" }.Where(id => _catalog.HasClip(id)
                && !_failedClips.Contains(id) && !_settings.DisabledAutomaticClips.Contains(id)).ToArray();
            ResetNaturalIdleTimer();
            if (candidates.Length == 0) return false;
            string chosen = candidates[_random.Next(candidates.Length)];
            BeginRegisteredClip(new NaturalActionRequest { ClipId = chosen, Label = _catalog.GetClip(chosen).DisplayName }, true);
            return true;
        }

        private void ShowActionLibrary()
        {
            if (_actionLibrary != null) { _actionLibrary.Activate(); return; }
            string[] featured = { "feed", "pet", "groom", "scratch", "notice_left", "notice_right", "notice_up", "notice_all" };
            _actionLibrary = new ActionLibraryWindow(_catalog.Clips.Where(c => c.Mode != "hold")
                .OrderBy(c => Array.IndexOf(featured, c.Id) < 0 ? 100 : Array.IndexOf(featured, c.Id)),
                id => _catalog.LoadResidentFrame(id, 0),
                id => RequestNaturalAction(id, null, true),
                id => !_settings.DisabledAutomaticClips.Contains(id),
                delegate(string id, bool enabled)
                {
                    _settings.DisabledAutomaticClips.Remove(id);
                    if (!enabled) _settings.DisabledAutomaticClips.Add(id);
                    SaveSettings();
                });
            _actionLibrary.Owner = this;
            _actionLibrary.Closed += delegate { _actionLibrary = null; };
            FitUtilityWindow(_actionLibrary);
            _actionLibrary.Show();
        }

        private void FitUtilityWindow(Window window)
        {
            Forms.Screen screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            Rect work = new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
                work = new Rect(source.CompositionTarget.TransformFromDevice.Transform(work.TopLeft),
                    source.CompositionTarget.TransformFromDevice.Transform(work.BottomRight));
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.MinWidth = Math.Min(window.MinWidth, Math.Max(100, work.Width - 16));
            window.MinHeight = Math.Min(window.MinHeight, Math.Max(100, work.Height - 16));
            window.Width = Math.Min(window.Width, work.Width - 16);
            window.Height = Math.Min(window.Height, work.Height - 16);
            window.Left = work.Left + (work.Width - window.Width) / 2;
            window.Top = work.Top + (work.Height - window.Height) / 2;
        }

        private void PresentReminder(ReminderDefinition reminder)
        {
            int reminderEpoch = _careEpoch;
            _latestReminder = reminder;
            if (!IsVisible || _clickThrough || _speechBubble == null) return;
            _speechBubble.ShowReminder(reminder.Message, this, delegate
            {
                if (_reminderController.Acknowledge(reminder.Id) && reminderEpoch == _careEpoch)
                {
                    _careController.CompleteReminder(reminder.Kind, DateTime.Now);
                    ShowSpeech("记下啦，亚比陪你一起保持好习惯。");
                    RefreshStatusCard();
                }
                if (_latestReminder == reminder) _latestReminder = null;
            }, delegate
            {
                _reminderController.Snooze(reminder.Id, DateTime.UtcNow, 5);
                if (_latestReminder == reminder) _latestReminder = null;
                ShowSpeech("5 分钟后再提醒你。");
            }, delegate
            {
                _reminderController.Dismiss(reminder.Id);
                if (_latestReminder == reminder) _latestReminder = null;
            });
        }

        private void HandlePlaybackFailure(string error)
        {
            TracePlayback("failed", _playingClip);
            if (!string.IsNullOrEmpty(_playingClip)) _failedClips.Add(_playingClip);
            _frameTimer.Stop();
            _playbackToken++;
            _catalog.CancelPendingRequests();
            _framePending = false;
            _clipPlaying = false;
            _playbackCompleted = null;
            _activityText = null;
            _activeCommand = PetCommand.None;
            _controller.CancelTo(_stablePose, DateTime.UtcNow);
            _attention.Reset();
            _naturalQueue.Clear();
            _queuedManualAction = null;
            LogPlaybackError(error);
            string stable = _stablePose == PetPose.Sleeping ? "sleep_idle" : (_stablePose == PetPose.Sitting ? "sit_idle" : "stand_idle");
            ShowStableFrame(stable, _stablePose, _stablePose == PetPose.Sleeping ? PetAction.Sleep : (_stablePose == PetPose.Sitting ? PetAction.Sit : PetAction.Stand), false, null);
            ShowSpeech("这段动作暂时无法播放，亚比已恢复，可以继续操作。");
        }

        private void LogPlaybackError(string error)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3-errors.log"), DateTime.Now.ToString("o") + " " + error + Environment.NewLine); }
            catch { }
        }

        private void DisplayDecodedFrame(FrameLoadResult result, bool crossfade, Action completed)
        {
            FramePixels pixels = result.Pixels;
            if (crossfade)
            {
                WriteableBitmap swap = _frameSurface;
                _frameSurface = _spareSurface;
                _spareSurface = swap;
            }
            if (_frameSurface == null || _frameSurface.IsFrozen || _frameSurface.PixelWidth != pixels.Width || _frameSurface.PixelHeight != pixels.Height)
                _frameSurface = new WriteableBitmap(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null);
            _frameSurface.WritePixels(new Int32Rect(0, 0, pixels.Width, pixels.Height), pixels.Bytes, pixels.Width * 4, 0);
            DisplayBitmap(_frameSurface, crossfade, completed);
        }

        private void TracePlayback(string result, string clip)
        {
            if (!_testUi) return;
            _testTrace.Add(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + result + " " + clip);
        }

        private void CloseNaturalFeatures()
        {
            if (_actionLibrary != null) _actionLibrary.Close();
            if (_attentionTest != null) _attentionTest.Close();
            if (_settingsWindow != null) _settingsWindow.Close();
            if (_testUi)
            {
                try { File.WriteAllLines(Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3-playback.log"), _testTrace); } catch { }
            }
        }
    }
}
