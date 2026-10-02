using System;
using System.Collections.Generic;

namespace YabiDesktopPet
{
    /// <summary>
    /// Dependency-free checks for the natural attention controller and the
    /// existing capacity-one action/timestamp helpers.  Program.cs includes
    /// these failures in --self-test without starting WPF.
    /// </summary>
    internal static class NaturalInteractionTests
    {
        private const double Epsilon = 0.000001;

        public static IList<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            DateTime origin = new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc);

            TestBoundariesAndMapping(failures, origin);
            TestDwellProgressMovementAndHysteresis(failures, origin.AddMinutes(1));
            TestRetriggerRules(failures, origin.AddMinutes(2));
            TestBusyCooldownAndClockSafety(failures, origin.AddMinutes(3));
            TestActionQueue(failures);
            TestTimeline(failures);
            NaturalActionRequest once = new NaturalActionRequest { CareKind = "feed", CareEpoch = 1 };
            Check(failures, once.TryBegin(1) && !once.TryBegin(1), "care request begins only once");
            NaturalActionRequest stale = new NaturalActionRequest { CareKind = "pet", CareEpoch = 1 };
            Check(failures, !stale.TryBegin(2), "reset invalidates prepared care request");
            return failures;
        }

        private static void TestBoundariesAndMapping(ICollection<string> failures, DateTime origin)
        {
            NaturalAttentionController controller = new NaturalAttentionController();

            Check(failures,
                controller.Update(origin, 0.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "TooClose"
                    && controller.CandidateDirection == AttentionDirection.None,
                "dead-zone centre reports TooClose");
            Check(failures,
                controller.Update(origin.AddMilliseconds(1), 12.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "TooClose",
                "0.12H boundary remains in dead zone");
            Check(failures,
                controller.Update(origin.AddMilliseconds(2), 12.01, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "Waiting"
                    && controller.CandidateDirection == AttentionDirection.Right,
                "just outside dead zone starts right dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(3), 600.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "Waiting"
                    && controller.CandidateDirection == AttentionDirection.Right,
                "6H maximum boundary is accepted");
            Check(failures,
                controller.Update(origin.AddMilliseconds(4), 600.01, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "TooFar"
                    && controller.CandidateDirection == AttentionDirection.None,
                "beyond 6H reports TooFar");
            Check(failures,
                controller.Update(origin.AddMilliseconds(5), 10.0, 45.01, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "BelowHead",
                "dy greater than 0.45H reports BelowHead");
            Check(failures,
                controller.Update(origin.AddMilliseconds(6), 10.0, 45.01, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "BelowHead",
                "below-head boundary is not armed by a nearby sample");

            Check(failures,
                controller.Update(origin.AddMilliseconds(7), double.NaN, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "InvalidTarget",
                "NaN coordinate reports InvalidTarget");
            Check(failures,
                controller.Update(origin.AddMilliseconds(8), 1.0, 0.0, 0.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "InvalidTarget",
                "non-positive body height reports InvalidTarget");
            Check(failures,
                controller.Update(origin.AddMilliseconds(9), 1.0, 0.0, 100.0, true, false, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "OtherDisplay",
                "other display is gated");
            Check(failures,
                controller.Update(origin.AddMilliseconds(10), 1.0, 0.0, 100.0, false, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "Disabled",
                "ineligible state is gated");

            controller.Reset();
            Check(failures,
                controller.Update(origin, 0.0, -20.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Up,
                "upper target starts Up dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(300), 0.0, -20.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.Up,
                "upper target triggers after 300ms");

            controller.Reset();
            Check(failures,
                controller.Update(origin, 20.0, 0.0, 100.0, true, true, true,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Left,
                "mirror maps screen right to logical left");
            Check(failures,
                controller.Update(origin.AddMilliseconds(300), 20.0, 0.0, 100.0, true, true, true,
                    InteractionFrequency.Normal, false) == AttentionDirection.Left,
                "mirrored left target triggers");
        }

        private static void TestDwellProgressMovementAndHysteresis(
            ICollection<string> failures,
            DateTime origin)
        {
            NaturalAttentionController controller = new NaturalAttentionController();

            Check(failures,
                controller.Update(origin, -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "Waiting"
                    && controller.DwellProgress < Epsilon,
                "dwell starts at zero progress");
            controller.Update(origin.AddMilliseconds(150), -50.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, false);
            Check(failures,
                Math.Abs(controller.DwellProgress - 0.5) < Epsilon,
                "dwell progress reaches one half at 150ms");
            Check(failures,
                controller.Update(origin.AddMilliseconds(299), -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None,
                "299ms does not trigger");
            Check(failures,
                controller.Update(origin.AddMilliseconds(300), -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.Left
                    && controller.DwellProgress < Epsilon
                    && controller.CandidateDirection == AttentionDirection.None,
                "300ms triggers and clears candidate progress");

            controller.Reset();
            controller.Update(origin, -50.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, false);
            Check(failures,
                controller.Update(origin.AddMilliseconds(200), -59.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.DwellProgress < Epsilon,
                "movement greater than 0.08H resets dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(499), -59.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None,
                "movement reset requires another full dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(500), -59.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.Left,
                "dwell completes at the moved point");

            controller.Reset();
            controller.Update(origin, -50.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, false);
            Check(failures,
                controller.Update(origin.AddMilliseconds(200), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Right
                    && controller.DwellProgress < Epsilon,
                "direction change resets dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(499), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None,
                "changed direction still needs 300ms");
            Check(failures,
                controller.Update(origin.AddMilliseconds(500), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.Right,
                "changed direction eventually triggers");

            // Horizontal and upper boundaries retain their previous direction
            // inside the 0.10H hysteresis band, then switch beyond it.
            controller.Reset();
            controller.Update(origin, -50.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, false);
            Check(failures,
                controller.Update(origin.AddMilliseconds(10), 9.0, 20.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Left,
                "left direction holds through 0.10H centre band");
            Check(failures,
                controller.Update(origin.AddMilliseconds(20), 11.0, 20.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Right,
                "left direction switches beyond hysteresis band");

            controller.Reset();
            controller.Update(origin, 50.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, false);
            Check(failures,
                controller.Update(origin.AddMilliseconds(10), 30.0, -35.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Right,
                "right direction holds near upper boundary");
            Check(failures,
                controller.Update(origin.AddMilliseconds(20), 30.0, -41.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.CandidateDirection == AttentionDirection.Up,
                "right direction switches to Up beyond hysteresis band");
        }

        private static void TestRetriggerRules(ICollection<string> failures, DateTime origin)
        {
            NaturalAttentionController controller = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            controller.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);

            Check(failures,
                controller.Update(origin.AddMilliseconds(302), -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && controller.Status == "Stationary",
                "test mode cannot replay a stationary point");
            Check(failures,
                controller.Update(origin.AddSeconds(20), -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && controller.Status == "Stationary",
                "fixed last trigger point remains stationary indefinitely");
            Check(failures,
                controller.Update(origin.AddSeconds(20).AddMilliseconds(1), -65.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && controller.Status == "Stationary",
                "same-side move below 0.20H stays stationary");
            Check(failures,
                controller.Update(origin.AddSeconds(20).AddMilliseconds(2), -70.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && controller.Status == "Waiting",
                "same-side move of 0.20H starts a new dwell");
            Check(failures,
                controller.Update(origin.AddSeconds(20).AddMilliseconds(302), -70.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.Left,
                "same-side moved point triggers without leaving sector");

            NaturalAttentionController progress = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            progress.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            DateTime progressBase = origin.AddSeconds(1);
            progress.Update(progressBase, -70.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, true);
            progress.Update(progressBase.AddMilliseconds(150), -70.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, true);
            Check(failures,
                Math.Abs(progress.DwellProgress - 0.5) < Epsilon,
                "rearmed dwell exposes halfway progress");
            Check(failures,
                progress.Update(progressBase.AddMilliseconds(151), -50.0, 0.0, 100.0,
                    true, true, false, InteractionFrequency.Normal, true) == AttentionDirection.None
                    && progress.Status == "Stationary"
                    && progress.DwellProgress < Epsilon,
                "return to last trigger point clears stale dwell progress");

            NaturalAttentionController changed = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            changed.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            Check(failures,
                changed.Update(origin.AddSeconds(1), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && changed.Status == "Waiting",
                "direction change starts a new dwell immediately");
            Check(failures,
                changed.Update(origin.AddSeconds(1).AddMilliseconds(300), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.Right,
                "changed direction triggers after dwell");

            // A short invalid-area excursion does not unlock the stationary
            // point; a full 300 ms excursion does.
            NaturalAttentionController shortLeave = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            shortLeave.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            shortLeave.Update(origin.AddSeconds(1), 0.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, true);
            Check(failures,
                shortLeave.Update(origin.AddSeconds(1).AddMilliseconds(299), -50.0, 0.0, 100.0,
                    true, true, false, InteractionFrequency.Normal, true) == AttentionDirection.None
                    && shortLeave.Status == "Stationary",
                "short leave and return does not replay");

            NaturalAttentionController longLeave = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            longLeave.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            longLeave.Update(origin.AddSeconds(1), 0.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, true);
            Check(failures,
                longLeave.Update(origin.AddSeconds(1).AddMilliseconds(300), -50.0, 0.0, 100.0,
                    true, true, false, InteractionFrequency.Normal, true) == AttentionDirection.None
                    && longLeave.Status == "Waiting",
                "300ms leave and return rearms the original point");
            Check(failures,
                longLeave.Update(origin.AddSeconds(1).AddMilliseconds(600), -50.0, 0.0, 100.0,
                    true, true, false, InteractionFrequency.Normal, true) == AttentionDirection.Left,
                "rearmed original point triggers after fresh dwell");

            NaturalAttentionController otherScreen = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            otherScreen.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            otherScreen.Update(origin.AddSeconds(1), -50.0, 0.0, 100.0, true, false, false,
                InteractionFrequency.Normal, true);
            Check(failures,
                otherScreen.Update(origin.AddSeconds(1).AddMilliseconds(300), -50.0, 0.0, 100.0,
                    true, true, false, InteractionFrequency.Normal, true) == AttentionDirection.None
                    && otherScreen.Status == "Waiting",
                "300ms off-screen leave rearms on return");

            // Disabled/menu time clears a leave timer rather than counting as
            // physical leave time, so it cannot rearm a stationary point.
            NaturalAttentionController paused = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            paused.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            paused.Update(origin.AddSeconds(1), 0.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, true);
            paused.Update(origin.AddSeconds(1).AddMilliseconds(100), -50.0, 0.0, 100.0,
                false, true, false, InteractionFrequency.Normal, true);
            Check(failures,
                paused.Update(origin.AddSeconds(2), -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && paused.Status == "Stationary",
                "disabled pause does not rearm");

            // Position comparison is normalized by H, not by raw pixels.
            NaturalAttentionController scaled = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, true);
            scaled.Complete(origin.AddMilliseconds(301), InteractionFrequency.Normal);
            Check(failures,
                scaled.Update(origin.AddSeconds(1), -100.0, 0.0, 200.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && scaled.Status == "Stationary",
                "same normalized point stays stationary at 2H size");
            Check(failures,
                scaled.Update(origin.AddSeconds(1).AddMilliseconds(1), -140.0, 0.0, 200.0,
                    true, true, false, InteractionFrequency.Normal, true) == AttentionDirection.None
                    && scaled.Status == "Waiting",
                "20 percent H movement rearms at 2H size");
        }

        private static void TestBusyCooldownAndClockSafety(
            ICollection<string> failures,
            DateTime origin)
        {
            NaturalAttentionController controller = NewTriggeredController(
                origin, -50.0, 0.0, InteractionFrequency.Normal, false);
            Check(failures,
                controller.IsBusy && controller.CurrentDirection == AttentionDirection.Left,
                "trigger enters indivisible playing state");
            Check(failures,
                controller.Update(origin.AddMilliseconds(100), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "Playing"
                    && controller.CandidateDirection == AttentionDirection.None,
                "busy controller discards changed coordinate");
            controller.Complete(origin.AddMilliseconds(200), InteractionFrequency.Normal);
            controller.Complete(origin.AddMilliseconds(201), InteractionFrequency.Calm);
            Check(failures,
                !controller.IsBusy && controller.CurrentDirection == AttentionDirection.None
                    && controller.Status == "CoolingDown"
                    && Math.Abs(controller.CooldownRemainingSeconds - 1.5) < Epsilon,
                "normal completion starts 1.5 second cooldown and duplicate is safe");
            Check(failures,
                controller.Update(origin.AddMilliseconds(201), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && controller.Status == "CoolingDown",
                "normal cooldown blocks a fresh dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(202), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.None
                    && controller.Status == "Waiting",
                "test mode bypasses cooldown but starts fresh dwell");
            Check(failures,
                controller.Update(origin.AddMilliseconds(700), 50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, true) == AttentionDirection.Right,
                "test mode still requires full dwell");

            CheckCooldownDuration(failures, origin.AddMinutes(1), InteractionFrequency.Calm, 4.0,
                "calm cooldown is four seconds");
            CheckCooldownDuration(failures, origin.AddMinutes(2), InteractionFrequency.Lively, 0.8,
                "lively cooldown is 0.8 seconds");

            // A backwards wall-clock sample cannot finish a dwell early.
            NaturalAttentionController clock = new NaturalAttentionController();
            clock.Update(origin.AddSeconds(20), -50.0, 0.0, 100.0, true, true, false,
                InteractionFrequency.Normal, false);
            Check(failures,
                clock.Update(origin.AddSeconds(19), -50.0, 0.0, 100.0, true, true, false,
                    InteractionFrequency.Normal, false) == AttentionDirection.None
                    && clock.DwellProgress < Epsilon,
                "clock rollback is clamped");
            Check(failures,
                clock.Update(origin.AddSeconds(20).AddMilliseconds(300), -50.0, 0.0, 100.0,
                    true, true, false, InteractionFrequency.Normal, false) == AttentionDirection.Left,
                "clamped clock still permits normal dwell completion");

            clock.Reset();
            Check(failures,
                !clock.IsBusy && clock.CurrentDirection == AttentionDirection.None
                    && clock.CandidateDirection == AttentionDirection.None
                    && clock.DwellProgress < Epsilon
                    && clock.CooldownRemainingSeconds < Epsilon
                    && clock.Status == "Ready",
                "Reset clears busy, progress, candidate, cooldown, and status");
        }

        private static NaturalAttentionController NewTriggeredController(
            DateTime origin,
            double dx,
            double dy,
            InteractionFrequency frequency,
            bool testMode)
        {
            NaturalAttentionController controller = new NaturalAttentionController();
            controller.Update(origin, dx, dy, 100.0, true, true, false, frequency, testMode);
            AttentionDirection result = controller.Update(origin.AddMilliseconds(300), dx, dy, 100.0,
                true, true, false, frequency, testMode);
            if (result == AttentionDirection.None)
                throw new InvalidOperationException("Natural attention self-test setup did not trigger.");
            return controller;
        }

        private static void CheckCooldownDuration(
            ICollection<string> failures,
            DateTime origin,
            InteractionFrequency frequency,
            double expected,
            string name)
        {
            NaturalAttentionController controller = NewTriggeredController(
                origin, 50.0, 0.0, frequency, true);
            controller.Complete(origin.AddMilliseconds(301), frequency);
            Check(failures,
                Math.Abs(controller.CooldownRemainingSeconds - expected) < Epsilon,
                name);
        }

        private static void TestActionQueue(ICollection<string> failures)
        {
            NaturalActionQueue queue = new NaturalActionQueue();
            queue.Enqueue(new NaturalActionRequest
            {
                ClipId = "sit_feed",
                CareKind = "feed",
                Preview = false,
                Label = "喂食"
            });
            queue.Enqueue(new NaturalActionRequest
            {
                ClipId = "sit_pet",
                CareKind = "pet",
                Preview = false,
                Label = "摸摸"
            });
            Check(failures,
                queue.Count == 1 && queue.Peek != null && queue.Peek.ClipId == "sit_pet",
                "queue latest-wins");
            NaturalActionRequest request = queue.Take();
            Check(failures,
                request != null && request.CareKind == "pet" && queue.Count == 0,
                "queue take clears");
            queue.Enqueue(null);
            Check(failures, !queue.HasPending, "null request is ignored");
            queue.Enqueue(new NaturalActionRequest { ClipId = "preview", Preview = true });
            queue.Clear();
            Check(failures, queue.Peek == null && !queue.HasPending, "queue clear");
        }

        private static void TestTimeline(ICollection<string> failures)
        {
            ClipDefinition vfr = new ClipDefinition
            {
                FrameCount = 4,
                DurationMs = 166.667,
                FrameTimesMs = new[] { 0.0, 50.0, 83.333, 133.333 }
            };
            Check(failures,
                vfr.FrameAt(49, false) == 0 && vfr.FrameAt(50, false) == 1,
                "VFR first frame duration");
            Check(failures,
                vfr.FrameAt(84, false) == 2 && vfr.FrameAt(166, false) == 3,
                "VFR timestamps respected");
            Check(failures,
                vfr.FrameAt(0, true) == 3 && vfr.FrameAt(166.667, true) == 0,
                "VFR reverse endpoints");
            Check(failures,
                PlaybackTimeline.FrameAt(0L, 1000L, 5, false) == 0,
                "timeline forward first");
            Check(failures,
                PlaybackTimeline.FrameAt(199L, 1000L, 5, false) == 0,
                "timeline frame zero interval");
            Check(failures,
                PlaybackTimeline.FrameAt(200L, 1000L, 5, false) == 1,
                "timeline equal frame intervals");
            Check(failures,
                PlaybackTimeline.FrameAt(999L, 1000L, 5, false) == 4,
                "timeline final interval");
            Check(failures,
                PlaybackTimeline.FrameAt(1000L, 1000L, 5, false) == 4,
                "timeline forward last");
            Check(failures,
                PlaybackTimeline.FrameAt(0L, 1000L, 5, true) == 4,
                "timeline reverse first");
            Check(failures,
                PlaybackTimeline.FrameAt(200L, 1000L, 5, true) == 3,
                "timeline reverse equal frame intervals");
            Check(failures,
                PlaybackTimeline.FrameAt(1000L, 1000L, 5, true) == 0,
                "timeline reverse last");
            Check(failures,
                PlaybackTimeline.FrameAt(500L, 1000L, 0, false) == 0,
                "timeline malformed frame count");
            Check(failures,
                PlaybackTimeline.IsComplete(1000L, 1000L),
                "timeline completion endpoint");
            Check(failures,
                !PlaybackTimeline.IsComplete(999L, 1000L),
                "timeline incomplete before endpoint");
        }

        private static void Check(ICollection<string> failures, bool condition, string name)
        {
            if (!condition)
                failures.Add(name);
        }
    }
}
