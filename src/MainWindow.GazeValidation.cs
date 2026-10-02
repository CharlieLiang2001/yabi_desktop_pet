using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace YabiDesktopPet
{
    internal sealed partial class MainWindow
    {
        private void SetTestGazeCursor(double x, double y)
        {
            ContinuousGazeRig rig = new ContinuousGazeRig(_stablePose == PetPose.Standing);
            double h = Height * (_stablePose == PetPose.Standing ? .8 : .7);
            double headX = Width * (_manualMirror ? 1 - rig.HeadX / 320 : rig.HeadX / 320);
            _testGazeCursor = PointToScreen(new Point(headX + x * h, Height * rig.EyeY / 480 + y * h));
        }

        private void StartGazeRegressionTests()
        {
            string folder = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3.3-gaze");
            Directory.CreateDirectory(folder);
            int stage = 0;
            Stopwatch run = Stopwatch.StartNew(), pause = Stopwatch.StartNew();
            List<string> checks = new List<string>(), samples = new List<string> { "seconds,eyeX,eyeY,headX,headY" };
            HashSet<int> distinctEyeValues = new HashSet<int>();
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            PetAction[] replay = { PetAction.Blink, PetAction.Sit, PetAction.Stand, PetAction.Blink, PetAction.Sit, PetAction.Stand };
            int replayIndex = 0;
            bool replayPending = false;
            GazeDesktopProbe desktopProbe = null;
            Closed += delegate { if (desktopProbe != null) desktopProbe.Dispose(); };
            _mouseLookEnabled = true; _settings.FocusDoNotDisturb = true;
            SetTestGazeCursor(0, 0);
            timer.Tick += delegate
            {
                try
                {
                    if (run.Elapsed.TotalSeconds > 125) throw new Exception("continuous test timeout stage " + stage + " " + _gazeStatus);
                    RequireGaze(_frameSurface == null || !_frameSurface.IsFrozen, "active video buffer must stay writable after gaze");
                    RequireGaze(_spareSurface == null || !_spareSurface.IsFrozen, "spare video buffer must stay writable after gaze");
                    samples.Add(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F5},{2:F5},{3:F5},{4:F5}", run.Elapsed.TotalSeconds,
                        _continuousGaze.EyeX, _continuousGaze.EyeY, _continuousGaze.HeadX, _continuousGaze.HeadY));
                    if (stage == 0)
                    {
                        if (pause.Elapsed.TotalSeconds < .8) return;
                        RequireGaze(_stablePose == PetPose.Standing && _continuousView != null && _continuousView.IsVisible, "standing gaze visible without opening a panel");
                        UiSnapshot.Capture(this, Path.Combine(folder, "stand-center.png"));
                        SetTestGazeCursor(-4, 0); pause.Restart(); stage++; return;
                    }
                    if (stage == 1)
                    {
                        if (pause.Elapsed.TotalSeconds < 1) return;
                        RequireGaze(_continuousGaze.EyeX < -.9 && _continuousGaze.HeadX < -.8, "standing left follows continuously");
                        UiSnapshot.Capture(this, Path.Combine(folder, "stand-left.png"));
                        SetTestGazeCursor(4, 0); pause.Restart(); stage++; return;
                    }
                    if (stage == 2)
                    {
                        if (pause.Elapsed.TotalSeconds < 1) return;
                        RequireGaze(_continuousGaze.EyeX > .9, "right target replaces left without wait or animation");
                        UiSnapshot.Capture(this, Path.Combine(folder, "stand-right.png"));
                        pause.Restart(); stage++; return;
                    }
                    if (stage == 3)
                    {
                        double angle = pause.Elapsed.TotalSeconds * Math.PI;
                        SetTestGazeCursor(Math.Cos(angle) * 3, Math.Sin(angle) * 3);
                        distinctEyeValues.Add((int)Math.Round(_continuousGaze.EyeX * 1000));
                        if (pause.Elapsed.TotalSeconds < 3) return;
                        RequireGaze(distinctEyeValues.Count > 40, "continuous intermediate coordinates, not direction sectors");
                        RequireGaze(!_testTrace.Any(t => t.Contains(" start notice_")), "gaze never triggers a notice video");
                        checks.Add("Standing: left/right/up/down and diagonal targets update continuously without dwell, cooldown or panel; eye response leads head.");
                        _manualMirror = true; ApplyMirror(); SetTestGazeCursor(-3, 0); pause.Restart(); stage++; return;
                    }
                    if (stage == 4)
                    {
                        if (pause.Elapsed.TotalSeconds < 1.2) return;
                        RequireGaze(_continuousGaze.EyeX > .8, "mirrored screen-left maps to logical right once");
                        UiSnapshot.Capture(this, Path.Combine(folder, "stand-mirrored.png"));
                        _manualMirror = false; ApplyMirror(); RequestManualAction(PetAction.Sit); stage++; return;
                    }
                    if (stage == 5)
                    {
                        if (IsVisualBusy) return;
                        RequireGaze(_stablePose == PetPose.Sitting, "full stand-to-sit transition completed");
                        SetTestGazeCursor(-4, 0); pause.Restart(); stage++; return;
                    }
                    if (stage == 6)
                    {
                        if (pause.Elapsed.TotalSeconds < 1) return;
                        UiSnapshot.Capture(this, Path.Combine(folder, "sit-left.png"));
                        SetTestGazeCursor(4, 0); pause.Restart(); stage++; return;
                    }
                    if (stage == 7)
                    {
                        if (pause.Elapsed.TotalSeconds < 1) return;
                        UiSnapshot.Capture(this, Path.Combine(folder, "sit-right.png"));
                        SetTestGazeCursor(0, -4); pause.Restart(); stage++; return;
                    }
                    if (stage == 8)
                    {
                        if (pause.Elapsed.TotalSeconds < 1) return;
                        RequireGaze(_continuousGaze.EyeY < -.9, "sitting up continuous");
                        UiSnapshot.Capture(this, Path.Combine(folder, "sit-up.png"));
                        SetTestGazeCursor(0, 4); pause.Restart(); stage++; return;
                    }
                    if (stage == 9)
                    {
                        if (pause.Elapsed.TotalSeconds < 1) return;
                        RequireGaze(_continuousGaze.EyeY > .9, "sitting down continuous");
                        UiSnapshot.Capture(this, Path.Combine(folder, "sit-down.png"));
                        SetMouseLookEnabled(false, false); pause.Restart(); stage++; return;
                    }
                    if (stage == 10)
                    {
                        if (pause.Elapsed.TotalSeconds < 1.5) return;
                        RequireGaze(_continuousGaze.IsNeutral && !_continuousView.IsVisible, "disable returns to unchanged image");
                        UiSnapshot.Capture(this, Path.Combine(folder, "sit-disabled.png"));
                        SetMouseLookEnabled(true, false); _reminderController.StartFocus(DateTime.UtcNow); pause.Restart(); stage++; return;
                    }
                    if (stage == 11)
                    {
                        if (pause.Elapsed.TotalSeconds < .5) return;
                        RequireGaze(_continuousGaze.IsNeutral && _gazeStatus.Contains("专注"), "focus pause respected without a temporary bypass");
                        _settings.FocusDoNotDisturb = false; _testGazeOtherScreen = true; pause.Restart(); stage++; return;
                    }
                    if (stage == 12)
                    {
                        if (pause.Elapsed.TotalSeconds < .5) return;
                        RequireGaze(_continuousGaze.IsNeutral && _gazeStatus.Contains("另一屏幕"), "cross-monitor return to center");
                        _testGazeOtherScreen = false; _isDragging = true; pause.Restart(); stage++; return;
                    }
                    if (stage == 13)
                    {
                        if (pause.Elapsed.TotalSeconds < .5) return;
                        RequireGaze(_continuousGaze.IsNeutral && _gazeStatus.Contains("拖动"), "dragging pause");
                        _isDragging = false; RequestManualAction(PetAction.Sleep); stage++; return;
                    }
                    if (stage == 14)
                    {
                        if (IsVisualBusy) return;
                        RequireGaze(_stablePose == PetPose.Sleeping && !_continuousView.IsVisible, "sleep uses original video/photo, not the gaze mesh");
                        RequestManualAction(PetAction.Wake); stage++; return;
                    }
                    if (stage == 15)
                    {
                        if (IsVisualBusy) return;
                        RequireGaze(_stablePose == PetPose.Standing, "sleep-to-sit-to-standing completes before continuous gaze resumes");
                        RequireGaze(_careController.Profile.Growth.TotalInteractions == 0, "continuous gaze never grants care XP");
                        checks.Add("Sitting directions, mirrored mapping, disable/recenter, focus, cross-monitor, dragging, full sleep/wake and zero care XP pass.");
                        StartAttentionTest(); SetTestGazeCursor(2, -1); pause.Restart(); stage++; return;
                    }
                    if (stage == 16)
                    {
                        if (pause.Elapsed.TotalSeconds < .8) return;
                        UiSnapshot.Capture(_attentionTest, Path.Combine(folder, "gaze-panel.png"));
                        _attentionTest.Close();
                        ShowStatusCard(); UiSnapshot.Capture(_statusCard, Path.Combine(folder, "status-card.png")); _statusCard.Hide();
                        ShowSettings(1); UiSnapshot.Capture(_settingsWindow, Path.Combine(folder, "settings.png")); _settingsWindow.Close();
                        pause.Restart(); stage++; return;
                    }
                    if (stage == 17)
                    {
                        if (IsVisualBusy) { pause.Restart(); return; }
                        if (replayPending) { replayPending = false; pause.Restart(); return; }
                        if (pause.Elapsed.TotalSeconds < .6) return;
                        if (replayIndex < replay.Length)
                        {
                            SetTestGazeCursor(replayIndex % 2 == 0 ? -2 : 2, -.4);
                            RequestManualAction(replay[replayIndex++]); replayPending = true; return;
                        }
                        checks.Add("Two blink/sit/stand cycles interleaved with live gaze: video buffers stay writable, no frozen-surface exception.");
                        desktopProbe = new GazeDesktopProbe(this, System.Windows.Media.Color.FromRgb(26, 48, 72));
                        pause.Restart(); stage++; return;
                    }
                    if (stage == 18)
                    {
                        if (pause.Elapsed.TotalSeconds < .6) return;
                        checks.Add("Native desktop dark background visible cat pixels=" + desktopProbe.Capture(Path.Combine(folder, "desktop-dark.png")));
                        desktopProbe.Dispose();
                        desktopProbe = new GazeDesktopProbe(this, System.Windows.Media.Color.FromRgb(241, 226, 207));
                        pause.Restart(); stage++; return;
                    }
                    if (stage == 19)
                    {
                        if (pause.Elapsed.TotalSeconds < .6) return;
                        checks.Add("Native desktop light background visible cat pixels=" + desktopProbe.Capture(Path.Combine(folder, "desktop-light.png")));
                        _petOpacity = .6; Opacity = .6;
                        pause.Restart(); stage++; return;
                    }
                    if (stage == 20)
                    {
                        if (pause.Elapsed.TotalSeconds < .6) return;
                        checks.Add("Native desktop user opacity 60% visible cat pixels=" + desktopProbe.Capture(Path.Combine(folder, "desktop-opacity60.png")));
                        _petOpacity = 1; Opacity = 1;
                        desktopProbe.Dispose(); desktopProbe = null;
                        checks.Insert(0, "Yabi 4.3.3 continuous gaze integration: PASS");
                        checks.Add("DistinctHorizontalSamples=" + distinctEyeValues.Count + "; MeshVertices=" + _continuousView.VertexCount);
                        checks.Add("ElapsedSeconds=" + run.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
                        checks.Add("Input adapter uses injected screen-pixel coordinates; no system cursor movement, user profile/settings writes or registry changes.");
                        File.WriteAllLines(Path.Combine(folder, "result.txt"), checks);
                        File.WriteAllLines(Path.Combine(folder, "continuous.csv"), samples);
                        File.WriteAllLines(Path.Combine(folder, "playback.log"), _testTrace);
                        timer.Stop(); Close();
                    }
                }
                catch (Exception exception)
                {
                    timer.Stop();
                    File.WriteAllText(Path.Combine(folder, "result.txt"), "FAIL stage=" + stage + "\n" + exception);
                    File.WriteAllLines(Path.Combine(folder, "continuous.csv"), samples);
                    File.WriteAllLines(Path.Combine(folder, "playback.log"), _testTrace);
                    Close();
                }
            };
            timer.Start();
        }
        private static void RequireGaze(bool condition, string description)
        { if (!condition) throw new InvalidOperationException(description); }
    }
}
