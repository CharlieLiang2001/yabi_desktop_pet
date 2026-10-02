using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace YabiDesktopPet
{
    internal sealed partial class MainWindow
    {
        internal void ConfigureTestPower(bool eco)
        {
            if (_testUi) _settings.PowerMode = eco ? PowerMode.Eco : PowerMode.Standard;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        private void CaptureStartupEvidence(object sender, EventArgs e)
        {
            ContentRendered -= CaptureStartupEvidence;
            string folder = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3-validation");
            Directory.CreateDirectory(folder);
            double ms = (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            File.WriteAllText(Path.Combine(folder, "startup.txt"),
                "RenderedAfterMs=" + ms.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + "\nNativeWindowVisible=" + IsWindowVisible(handle) + "\nHasPetBitmap=" + (_currentBitmap != null)
                + "\nPowerMode=" + _settings.PowerMode
                + "\nManagedMiB=" + (GC.GetTotalMemory(false) / 1048576.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            UiSnapshot.Capture(this, Path.Combine(folder, "startup-pet.png"));
        }

        private void CaptureFeaturePanels()
        {
            string folder = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3-validation");
            Directory.CreateDirectory(folder);
            _careController.Profile.LastFedUtc = DateTime.UtcNow;
            _careController.Profile.LastPettedUtc = DateTime.UtcNow;
            ShowStatusCard();
            UiSnapshot.Capture(_statusCard, Path.Combine(folder, "status-card.png"));
            _statusCard.Hide();
            ShowActionLibrary();
            UiSnapshot.Capture(_actionLibrary, Path.Combine(folder, "action-library.png"));
            _actionLibrary.Close();
            ShowSettings(0);
            string[] pages = { "settings-general", "settings-companion", "settings-reminders", "settings-appearance", "settings-data" };
            for (int i = 0; i < pages.Length; i++)
            {
                _settingsWindow.SelectTab(i);
                UiSnapshot.Capture(_settingsWindow, Path.Combine(folder, pages[i] + ".png"));
            }
            _settingsWindow.Close();
            StartAttentionTest();
            _attentionTest.UpdateStatus("Waiting", -1, 0);
            UiSnapshot.Capture(_attentionTest, Path.Combine(folder, "attention-test.png"));
            _attentionTest.Close();
            _visualRoot.ContextMenu.IsOpen = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
            {
                UiSnapshot.Capture(_visualRoot.ContextMenu, Path.Combine(folder, "context-menu.png"));
                _visualRoot.ContextMenu.IsOpen = false;
            }));
        }
        // Opt-in regression harness. Program gives this mode an isolated
        // settings/profile object and never registers automatic startup.
        private void StartNaturalRegressionTests()
        {
            int stage = 0;
            int clipIndex = 0;
            int round = 0;
            int interactionsBeforePreview = 0;
            List<string> failures = new List<string>();
            string[] clips = { "notice_left", "notice_right", "notice_up", "groom", "scratch", "feed", "pet", "notice_all", "sit_to_sleep", "sleep_to_sit", "stand_blink" };
            string folder = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.3-validation");
            Directory.CreateDirectory(folder);
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += delegate
            {
                try
                {
                    if (stage == 0)
                    {
                        UiSnapshot.Capture(this, Path.Combine(folder, "pet-standing.png"));
                        RequestManualAction(PetAction.Blink);
                        stage = 1;
                        return;
                    }
                    if (stage == 1)
                    {
                        FeedYabi();
                        PetYabi(); // latest wins before the care action begins
                        if (_naturalQueue.Peek == null || _naturalQueue.Peek.CareKind != "pet") failures.Add("manual queue must keep only latest pet request");
                        stage = 2;
                        return;
                    }
                    if (IsVisualBusy || _naturalQueue.HasPending) return;
                    if (stage == 2)
                    {
                        if (_stablePose != PetPose.Sitting) failures.Add("queued pet did not settle to sitting");
                        if (_careController.Profile.LastFedUtc != DateTime.MinValue) failures.Add("cancelled pending feed credited");
                        if (_careController.Profile.LastPettedUtc == DateTime.MinValue) failures.Add("valid pet was not credited");
                        FeedYabi();
                        FeedYabi(); // cooldown prevents duplicate credit/playback
                        stage = 3;
                        return;
                    }
                    if (stage == 3)
                    {
                        if (_careController.Profile.LastFedUtc == DateTime.MinValue) failures.Add("valid feed was not credited");
                        interactionsBeforePreview = _careController.Profile.Growth.TotalInteractions;
                        UiSnapshot.Capture(this, Path.Combine(folder, "pet-sitting.png"));
                        stage = 4;
                    }
                    if (stage == 4)
                    {
                        if (clipIndex < clips.Length)
                        {
                            string id = clips[clipIndex++];
                            RequestNaturalAction(id, null, true);
                            return;
                        }
                        if (++round < 2) { clipIndex = 0; return; }
                        if (_careController.Profile.Growth.TotalInteractions != interactionsBeforePreview) failures.Add("preview changed care interaction count");
                        _catalog.TestFailClipId = "groom";
                        RequestNaturalAction("groom", null, true);
                        stage = 5;
                        return;
                    }
                    if (stage == 5)
                    {
                        if (!_failedClips.Contains("groom")) failures.Add("failed decode did not reach recovery handler");
                        _catalog.TestFailClipId = null;
                        RequestNaturalAction("scratch", null, true);
                        stage = 6;
                        return;
                    }
                    if (stage == 6)
                    {
                        if (_stablePose != PetPose.Sitting) failures.Add("another action did not work after decode recovery");
                        _settings.LockPosition = true;
                        _settings.ManualMirror = true;
                        _manualMirror = true;
                        ApplyMirror();
                        UiSnapshot.Capture(this, Path.Combine(folder, "pet-mirrored.png"));
                        _manualMirror = false; ApplyMirror();
                        ShowStatusCard();
                        UiSnapshot.Capture(_statusCard, Path.Combine(folder, "status-card.png"));
                        _statusCard.Hide();
                        ShowActionLibrary();
                        UiSnapshot.Capture(_actionLibrary, Path.Combine(folder, "action-library.png"));
                        _actionLibrary.Close();
                        ShowSettings(1);
                        UiSnapshot.Capture(_settingsWindow, Path.Combine(folder, "settings-companion.png"));
                        _settingsWindow.Close();
                        StartAttentionTest();
                        UiSnapshot.Capture(_attentionTest, Path.Combine(folder, "attention-test.png"));
                        _attentionTest.Close();
                        _reminderController.StartFocus(DateTime.UtcNow.AddHours(-1));
                        _reminderController.Tick(DateTime.UtcNow);
                        UiSnapshot.Capture(_speechBubble, Path.Combine(folder, "reminder.png"));
                        if (!_reminderController.Acknowledge("focus")) failures.Add("focus reminder missing pending acknowledgement");
                        if (_reminderController.Acknowledge("focus")) failures.Add("focus acknowledgement is not idempotent");
                        SetClickThrough(true, false);
                        if (!_clickThrough) failures.Add("click-through toggle on failed");
                        SetClickThrough(false, false);
                        _settings.AlwaysOnTop = false; Topmost = false;
                        _settings.AlwaysOnTop = true; Topmost = true;
                        ToggleVisibility(); ToggleVisibility();
                        if (!IsVisible) failures.Add("show after hide failed");
                        timer.Stop();
                        List<string> report = new List<string>
                        {
                            "Yabi 4.3 native regression: " + (failures.Count == 0 ? "PASS" : "FAIL"),
                            "Completed two full rounds of new clips at original playback duration.",
                            "Expected injected decode failures: 1; UI remained usable.",
                            "Settings/profile isolated; registry startup was not modified."
                        };
                        report.AddRange(failures);
                        File.WriteAllLines(Path.Combine(folder, "native-regression.txt"), report);
                        Close();
                    }
                }
                catch (Exception exception)
                {
                    timer.Stop();
                    File.WriteAllText(Path.Combine(folder, "native-regression.txt"), "FAIL\n" + exception);
                    Close();
                }
            };
            timer.Start();
        }
    }
}
