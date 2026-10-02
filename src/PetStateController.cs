using System;
using System.Collections.Generic;

namespace YabiDesktopPet
{
    /// <summary>
    /// Owns companionship timing and pose transitions.  It has no WPF or ZIP
    /// dependency, so state decisions remain separate from frame rendering.
    /// </summary>
    internal sealed class PetStateController
    {
        private readonly Random _random;
        private readonly double _timeScale;
        private DateTime _nextBlinkUtc;
        private DateTime _sitDueUtc;
        private DateTime _sleepDueUtc;
        private DateTime _sittingUntilUtc;
        private DateTime _lookDueUtc;
        private DateTime _wakeDueUtc;
        private DateTime _manualPauseUntilUtc;
        private bool _wakingFromSleep;

        public PetStateController(Random random, double timeScale)
        {
            _random = random ?? new Random();
            _timeScale = Math.Max(0.04, Math.Min(1.0, timeScale));
            Pose = PetPose.Standing;
            Mode = BehaviorMode.Companion;
            Frequency = InteractionFrequency.Normal;
        }

        public PetPose Pose { get; private set; }
        public BehaviorMode Mode { get; private set; }
        public InteractionFrequency Frequency { get; private set; }
        public bool IsBusy { get; private set; }
        public DateTime LastInteractionUtc { get; private set; }

        public void Start(DateTime now, BehaviorMode mode)
        {
            Pose = PetPose.Standing;
            Mode = mode;
            IsBusy = false;
            LastInteractionUtc = now;
            ScheduleStanding(now);
            _sleepDueUtc = now + Delay(240, 421);
            _manualPauseUntilUtc = DateTime.MinValue;
        }

        public void RegisterInteraction(DateTime now)
        {
            LastInteractionUtc = now;
            _sleepDueUtc = now + Delay(240, 421);
            _manualPauseUntilUtc = now + DelayFixed(3.0);
            if (Pose == PetPose.Standing)
            {
                _sitDueUtc = now + Delay(15, 31);
            }
            else if (Pose == PetPose.Sitting)
            {
                _sittingUntilUtc = now + Delay(120, 241);
            }
        }

        public void SetFrequency(InteractionFrequency frequency, DateTime now)
        {
            Frequency = frequency;
            if (Pose == PetPose.Standing)
            {
                ScheduleStanding(now);
            }
            else if (Pose == PetPose.Sitting)
            {
                ScheduleSitting(now);
            }
        }

        public void HoldAutomaticActions(DateTime now, double seconds)
        {
            _manualPauseUntilUtc = now + DelayFixed(seconds);
        }

        public void SetMode(BehaviorMode mode, DateTime now)
        {
            Mode = mode;
            LastInteractionUtc = now;
            if (mode == BehaviorMode.Companion)
            {
                _sleepDueUtc = now + Delay(240, 421);
                if (Pose == PetPose.Standing)
                {
                    ScheduleStanding(now);
                }
                else if (Pose == PetPose.Sitting)
                {
                    ScheduleSitting(now);
                }
                else if (Pose == PetPose.Sleeping)
                {
                    _wakeDueUtc = now + Delay(25, 56);
                }
            }
        }

        public PetCommand Tick(DateTime now, bool cursorHasBeenStill)
        {
            if (IsBusy || now < _manualPauseUntilUtc)
            {
                return PetCommand.None;
            }

            if (Mode == BehaviorMode.Quiet)
            {
                return Pose == PetPose.Sleeping ? PetCommand.None : PetCommand.Sleep;
            }

            if (Pose == PetPose.Standing)
            {
                if (now >= _sitDueUtc || now >= _sleepDueUtc)
                {
                    return PetCommand.Sit;
                }
                if (now >= _nextBlinkUtc)
                {
                    return PetCommand.Blink;
                }
            }
            else if (Pose == PetPose.Sitting)
            {
                if (now >= _sleepDueUtc)
                {
                    return PetCommand.Sleep;
                }
                if (now >= _sittingUntilUtc)
                {
                    return PetCommand.Wake;
                }
                // Complete-body natural idle clips are scheduled independently
                // at a lower frequency. Do not loop the old head-turn video.
            }
            else if (Pose == PetPose.Sleeping && now >= _wakeDueUtc)
            {
                return PetCommand.Wake;
            }

            return PetCommand.None;
        }

        public void Begin(PetCommand command, bool manual, DateTime now)
        {
            if (command == PetCommand.None)
            {
                return;
            }
            IsBusy = true;
            if (command == PetCommand.Wake) _wakingFromSleep = Pose == PetPose.Sleeping;
            if (manual)
            {
                _manualPauseUntilUtc = now + DelayFixed(7.0);
                LastInteractionUtc = now;
                _sleepDueUtc = now + Delay(240, 421);
            }
            if (command == PetCommand.Sit || command == PetCommand.Sleep || command == PetCommand.Wake)
            {
                Pose = PetPose.Transitioning;
            }
        }

        public void Complete(PetCommand command, DateTime now)
        {
            IsBusy = false;
            if (command == PetCommand.Blink)
            {
                Pose = PetPose.Standing;
                _nextBlinkUtc = now + Delay(4, 10);
            }
            else if (command == PetCommand.Sit)
            {
                Pose = PetPose.Sitting;
                ScheduleSitting(now);
            }
            else if (command == PetCommand.LookAround)
            {
                Pose = PetPose.Sitting;
                _lookDueUtc = now + Delay(12, 26);
            }
            else if (command == PetCommand.Sleep)
            {
                Pose = PetPose.Sleeping;
                _wakeDueUtc = Mode == BehaviorMode.Companion
                    ? now + Delay(25, 56)
                    : DateTime.MaxValue;
            }
            else if (command == PetCommand.Wake)
            {
                Pose = PetPose.Standing;
                ScheduleStanding(now);
                // An automatic stretch/stand is not a user interaction. Only
                // reset the nap deadline after actual sleep (or Begin/manual).
                if (_wakingFromSleep) _sleepDueUtc = now + Delay(240, 421);
            }
        }

        public void CancelTo(PetPose pose, DateTime now)
        {
            Pose = pose;
            IsBusy = false;
            if (pose == PetPose.Standing)
            {
                ScheduleStanding(now);
            }
            else if (pose == PetPose.Sitting)
            {
                ScheduleSitting(now);
            }
            else if (pose == PetPose.Sleeping)
            {
                _wakeDueUtc = Mode == BehaviorMode.Companion
                    ? now + Delay(25, 56)
                    : DateTime.MaxValue;
            }
        }

        private void ScheduleStanding(DateTime now)
        {
            _nextBlinkUtc = now + Delay(4, 10);
            _sitDueUtc = now + Delay(15, 31);
        }

        private void ScheduleSitting(DateTime now)
        {
            _sittingUntilUtc = now + Delay(120, 241);
            _lookDueUtc = now + Delay(8, 21);
        }

        private TimeSpan Delay(int minimumSeconds, int exclusiveMaximumSeconds)
        {
            int value = _random.Next(minimumSeconds, exclusiveMaximumSeconds);
            return DelayFixed(value);
        }

        private TimeSpan DelayFixed(double seconds)
        {
            double frequencyScale = Frequency == InteractionFrequency.Calm
                ? 1.55
                : (Frequency == InteractionFrequency.Lively ? 0.70 : 1.0);
            return TimeSpan.FromSeconds(Math.Max(0.12, seconds * _timeScale * frequencyScale));
        }

        internal static IList<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            PetStateController controller = new PetStateController(new Random(7), 1.0);
            controller.Start(now, BehaviorMode.Companion);

            controller.Begin(PetCommand.Blink, false, now);
            controller.Complete(PetCommand.Blink, now.AddSeconds(1));
            Check(failures, controller.Pose == PetPose.Standing, "standing -> blink -> standing");

            controller.Begin(PetCommand.Sit, false, now);
            controller.Complete(PetCommand.Sit, now.AddSeconds(1));
            Check(failures, controller.Pose == PetPose.Sitting, "standing -> sitting");

            controller.Begin(PetCommand.LookAround, false, now);
            controller.Complete(PetCommand.LookAround, now.AddSeconds(1));
            Check(failures, controller.Pose == PetPose.Sitting, "lookaround returns to sitting");

            controller.Begin(PetCommand.Sleep, false, now);
            controller.Complete(PetCommand.Sleep, now.AddSeconds(1));
            Check(failures, controller.Pose == PetPose.Sleeping, "sleep transition");

            controller.Begin(PetCommand.Wake, false, now);
            controller.Complete(PetCommand.Wake, now.AddSeconds(1));
            Check(failures, controller.Pose == PetPose.Standing, "sleep -> sit -> reverse stand wake");

            controller.Start(now, BehaviorMode.Companion);
            controller.Begin(PetCommand.Sit, false, now.AddSeconds(20));
            controller.Complete(PetCommand.Sit, now.AddSeconds(24));
            controller.Begin(PetCommand.Wake, false, now.AddSeconds(230));
            controller.Complete(PetCommand.Wake, now.AddSeconds(234));
            controller.Begin(PetCommand.Sit, false, now.AddSeconds(430));
            controller.Complete(PetCommand.Sit, now.AddSeconds(434));
            Check(failures, controller.Tick(now.AddSeconds(440), true) == PetCommand.Sleep,
                "automatic standing must not postpone sleep indefinitely");

            Check(failures, PetSettings.NormalizeBehavior("Wander") == BehaviorMode.Companion, "Wander migration");
            Check(failures, PetSettings.NormalizeBehavior("FollowMouse") == BehaviorMode.Companion, "FollowMouse migration");
            Check(failures, PetSettings.NormalizeBehavior("Quiet") == BehaviorMode.Quiet, "Quiet migration");

            MouseLookSelector selector = new MouseLookSelector();
            Check(failures, selector.Select(-500, 0, MouseLookDirection.Center) == MouseLookDirection.LeftSide, "left look mapping");
            Check(failures, selector.Select(500, 0, MouseLookDirection.Center) == MouseLookDirection.RightSide, "right look mapping");
            Check(failures, selector.Select(-120, -160, MouseLookDirection.Center) == MouseLookDirection.LeftUp, "upper look mapping");
            Check(failures, selector.Select(250, 180, MouseLookDirection.RightNear) == MouseLookDirection.Center, "downward cursor returns centre");
            return failures;
        }

        private static void Check(ICollection<string> failures, bool condition, string name)
        {
            if (!condition)
            {
                failures.Add(name);
            }
        }
    }

    internal sealed class MouseLookSelector
    {
        private const double DeadZone = 34.0;
        private const double Hysteresis = 18.0;
        private const double UpEnter = -82.0;
        private const double SideEnter = 360.0;

        public MouseLookDirection Select(double deltaX, double deltaY, MouseLookDirection current)
        {
            // The source contains no reliable downward pose.
            if (deltaY > 58.0)
            {
                return MouseLookDirection.Center;
            }

            bool currentLeft = IsLeft(current);
            bool currentRight = IsRight(current);
            double effectiveDeadZone = (currentLeft && deltaX < 0) || (currentRight && deltaX > 0)
                ? DeadZone - Hysteresis
                : DeadZone;
            if (Math.Abs(deltaX) < effectiveDeadZone)
            {
                return MouseLookDirection.Center;
            }

            bool left = deltaX < 0;
            bool keepUp = IsUp(current) && ((left && currentLeft) || (!left && currentRight));
            if (deltaY < (keepUp ? UpEnter + Hysteresis : UpEnter))
            {
                return left ? MouseLookDirection.LeftUp : MouseLookDirection.RightUp;
            }

            bool keepSide = IsSide(current) && ((left && currentLeft) || (!left && currentRight));
            if (Math.Abs(deltaX) > (keepSide ? SideEnter - Hysteresis : SideEnter))
            {
                return left ? MouseLookDirection.LeftSide : MouseLookDirection.RightSide;
            }

            return left ? MouseLookDirection.LeftNear : MouseLookDirection.RightNear;
        }

        private static bool IsLeft(MouseLookDirection direction)
        {
            return direction == MouseLookDirection.LeftNear
                || direction == MouseLookDirection.LeftUp
                || direction == MouseLookDirection.LeftSide;
        }

        private static bool IsRight(MouseLookDirection direction)
        {
            return direction == MouseLookDirection.RightNear
                || direction == MouseLookDirection.RightUp
                || direction == MouseLookDirection.RightSide;
        }

        private static bool IsUp(MouseLookDirection direction)
        {
            return direction == MouseLookDirection.LeftUp || direction == MouseLookDirection.RightUp;
        }

        private static bool IsSide(MouseLookDirection direction)
        {
            return direction == MouseLookDirection.LeftSide || direction == MouseLookDirection.RightSide;
        }
    }
}
