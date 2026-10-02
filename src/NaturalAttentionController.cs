using System;

namespace YabiDesktopPet
{
    /// <summary>
    /// The directions supported by the complete-body attention clips.
    /// A direction is emitted once when its dwell interval has completed;
    /// mouse movement never changes an action which is already playing.
    /// </summary>
    internal enum AttentionDirection
    {
        None,
        Left,
        Right,
        Up
    }

    /// <summary>
    /// Converts height-normalized cursor samples into one-shot attention
    /// requests.  The controller deliberately has no WPF or playback
    /// dependency: the owner calls Complete after the whole-body clip ends.
    /// </summary>
    internal sealed class NaturalAttentionController
    {
        private const double DeadRadiusFactor = 0.12;
        private const double MaximumRadiusFactor = 6.0;
        private const double BelowHeadFactor = 0.45;
        private const double DirectionHysteresisFactor = 0.10;
        private const double MoveResetFactor = 0.08;
        private const double RetriggerMoveFactor = 0.20;

        private static readonly TimeSpan DwellDuration = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(300);

        private DateTime _lastNow;
        private bool _hasClock;
        private DateTime _cooldownUntil;

        private DateTime _pendingSince;
        private double _pendingX;
        private double _pendingY;
        private AttentionDirection _pendingDirection;

        // The stable direction is used only for the approximately 0.10H
        // directional hysteresis.  It is not an action or a queued sample.
        private AttentionDirection _stableDirection;

        private bool _busy;
        private AttentionDirection _candidateDirection;

        // The last trigger point is intentionally immutable until the next
        // trigger.  All values are normalized by that sample's body height.
        private bool _hasTriggerPoint;
        private AttentionDirection _lastTriggeredDirection;
        private double _lastTriggerX;
        private double _lastTriggerY;

        // A leave is only measured while attention is eligible.  A disabled
        // menu pauses/restarts this observation; it never arms a replay.
        private DateTime _outsideSince;
        private bool _leaveRearmReady;

        private double _dwellProgress;

        public NaturalAttentionController()
        {
            Reset();
        }

        /// <summary>
        /// The direction of the currently playing attention clip, or None
        /// while the controller is idle.
        /// </summary>
        public AttentionDirection CurrentDirection { get; private set; }

        /// <summary>
        /// A direction currently being considered from the latest valid
        /// sample.  It is None while disabled, off-screen, invalid, or
        /// playing an indivisible action.
        /// </summary>
        public AttentionDirection CandidateDirection
        {
            get { return _candidateDirection; }
        }

        /// <summary>
        /// Fraction of the 300 ms dwell interval completed for the current
        /// candidate.  The value is always in the inclusive range [0, 1].
        /// </summary>
        public double DwellProgress
        {
            get { return _dwellProgress; }
        }

        /// <summary>
        /// Remaining cooldown based on the last normalized timestamp supplied
        /// to this controller.  This keeps simulated/self-test clocks
        /// deterministic while remaining read-only to callers.
        /// </summary>
        public double CooldownRemainingSeconds
        {
            get { return GetCooldownRemainingSeconds(); }
        }

        /// <summary>
        /// True between Update returning a direction and Complete being
        /// called.  Samples observed in this interval are deliberately
        /// discarded rather than queued.
        /// </summary>
        public bool IsBusy
        {
            get { return _busy; }
        }

        /// <summary>
        /// Stable English status token.  Presentation code can localize it.
        /// </summary>
        public string Status { get; private set; }

        /// <summary>
        /// Samples a cursor offset relative to the cat's head.  dx/dy and
        /// bodyHeight use the same coordinate units; negative dy is above
        /// the head.  A negative logical x means left before mirror mapping.
        /// </summary>
        public AttentionDirection Update(
            DateTime now,
            double dx,
            double dy,
            double bodyHeight,
            bool eligible,
            bool sameDisplay,
            bool mirrored,
            InteractionFrequency frequency,
            bool testMode)
        {
            now = NormalizeNow(now);

            // A whole-body clip is indivisible.  Do not inspect, classify, or
            // retain any coordinates supplied while it is playing.
            if (_busy)
            {
                ClearPending();
                _candidateDirection = AttentionDirection.None;
                _dwellProgress = 0.0;
                SetStatus("Playing");
                return AttentionDirection.None;
            }

            if (!eligible)
            {
                ClearPending();
                _candidateDirection = AttentionDirection.None;
                _stableDirection = AttentionDirection.None;
                _dwellProgress = 0.0;

                // Ineligible/menu time is not physical leave time.  Clearing
                // the timer also prevents a stale pre-menu interval from
                // arming a replay when the menu closes.
                _outsideSince = DateTime.MinValue;
                SetStatus("Disabled");
                return AttentionDirection.None;
            }

            if (!sameDisplay)
            {
                MarkOutside(now);
                SetStatus("OtherDisplay");
                return AttentionDirection.None;
            }

            double normalizedX;
            double normalizedY;
            TargetKind target = ClassifyTarget(dx, dy, bodyHeight, mirrored,
                out normalizedX, out normalizedY);
            if (target != TargetKind.Valid)
            {
                MarkOutside(now);
                SetStatus(StatusForTarget(target));
                return AttentionDirection.None;
            }

            // Returning from a genuinely invalid/off-screen area can unlock
            // the fixed last trigger point, but only after 300 ms of active
            // leave time.  A short excursion is intentionally forgotten.
            MarkReturnedToValidArea(now);

            AttentionDirection candidate = ClassifyDirection(normalizedX, normalizedY);
            _candidateDirection = candidate;

            // Cooldown suppresses a fresh dwell but never consumes a valid
            // rearm reason.  testMode bypasses only this gate.
            if (!testMode && CooldownRemainingSeconds > 0.0)
            {
                ClearPending();
                SetStatus("CoolingDown");
                return AttentionDirection.None;
            }

            if (!CanRetrigger(candidate, normalizedX, normalizedY))
            {
                ClearPending();
                SetStatus("Stationary");
                return AttentionDirection.None;
            }

            bool directionChanged = _pendingDirection != AttentionDirection.None
                && _pendingDirection != candidate;
            bool movedDuringDwell = _pendingDirection != AttentionDirection.None
                && HasMovedMoreThan(_pendingX, _pendingY, normalizedX, normalizedY, MoveResetFactor);

            if (_pendingDirection != candidate || directionChanged || movedDuringDwell)
            {
                _pendingDirection = candidate;
                _pendingSince = now;
                _pendingX = normalizedX;
                _pendingY = normalizedY;
                _dwellProgress = 0.0;
                SetStatus("Waiting");
                return AttentionDirection.None;
            }

            _dwellProgress = DwellFraction(_pendingSince, now);
            if (!HasElapsed(_pendingSince, now, DwellDuration))
            {
                SetStatus("Waiting");
                return AttentionDirection.None;
            }

            _busy = true;
            CurrentDirection = candidate;
            _lastTriggeredDirection = candidate;
            _hasTriggerPoint = true;
            _lastTriggerX = normalizedX;
            _lastTriggerY = normalizedY;
            _leaveRearmReady = false;
            _outsideSince = DateTime.MinValue;
            ClearPending();
            _candidateDirection = AttentionDirection.None;
            _dwellProgress = 0.0;
            SetStatus("Playing");
            return candidate;
        }

        /// <summary>
        /// Marks the end of the complete-body clip and starts the
        /// frequency-dependent cooldown.  Duplicate completion is ignored.
        /// </summary>
        public void Complete(DateTime now, InteractionFrequency frequency)
        {
            now = NormalizeNow(now);
            if (!_busy)
            {
                return;
            }

            _busy = false;
            CurrentDirection = AttentionDirection.None;
            _cooldownUntil = AddSecondsSafe(now, CooldownSeconds(frequency));

            // Samples during playback were not retained.  The first sample
            // after this call must begin a new 300 ms dwell.
            ClearPending();
            _candidateDirection = AttentionDirection.None;
            _stableDirection = AttentionDirection.None;
            _dwellProgress = 0.0;
            _outsideSince = DateTime.MinValue;
            SetStatus("CoolingDown");
        }

        /// <summary>
        /// Returns to a fresh idle state.  Safe while playing or after a
        /// duplicate completion.
        /// </summary>
        public void Reset()
        {
            _lastNow = DateTime.MinValue;
            _hasClock = false;
            _cooldownUntil = DateTime.MinValue;
            _pendingSince = DateTime.MinValue;
            _pendingX = 0.0;
            _pendingY = 0.0;
            _pendingDirection = AttentionDirection.None;
            _stableDirection = AttentionDirection.None;
            _busy = false;
            _candidateDirection = AttentionDirection.None;
            _hasTriggerPoint = false;
            _lastTriggeredDirection = AttentionDirection.None;
            _lastTriggerX = 0.0;
            _lastTriggerY = 0.0;
            _outsideSince = DateTime.MinValue;
            _leaveRearmReady = false;
            _dwellProgress = 0.0;
            CurrentDirection = AttentionDirection.None;
            Status = "Ready";
        }

        private AttentionDirection ClassifyDirection(double normalizedX, double normalizedY)
        {
            AttentionDirection raw = normalizedY < -Math.Abs(normalizedX)
                ? AttentionDirection.Up
                : (normalizedX < 0.0 ? AttentionDirection.Left : AttentionDirection.Right);

            // The 0.10H band is stateful: crossing a geometric boundary is
            // not enough to change direction until the cursor clears the
            // opposite side of the band.  This prevents diagonal/centre
            // jitter from repeatedly resetting dwell.
            switch (_stableDirection)
            {
                case AttentionDirection.Up:
                    if (normalizedY >= -Math.Abs(normalizedX) + DirectionHysteresisFactor)
                        raw = normalizedX < 0.0 ? AttentionDirection.Left : AttentionDirection.Right;
                    else
                        raw = AttentionDirection.Up;
                    break;

                case AttentionDirection.Left:
                    if (normalizedY < -Math.Abs(normalizedX) - DirectionHysteresisFactor)
                        raw = AttentionDirection.Up;
                    else if (normalizedX > DirectionHysteresisFactor)
                        raw = AttentionDirection.Right;
                    else
                        raw = AttentionDirection.Left;
                    break;

                case AttentionDirection.Right:
                    if (normalizedY < -Math.Abs(normalizedX) - DirectionHysteresisFactor)
                        raw = AttentionDirection.Up;
                    else if (normalizedX < -DirectionHysteresisFactor)
                        raw = AttentionDirection.Left;
                    else
                        raw = AttentionDirection.Right;
                    break;

                default:
                    break;
            }

            _stableDirection = raw;
            return raw;
        }

        private bool CanRetrigger(
            AttentionDirection candidate,
            double normalizedX,
            double normalizedY)
        {
            if (!_hasTriggerPoint)
                return true;
            if (_leaveRearmReady)
                return true;
            if (candidate != _lastTriggeredDirection)
                return true;
            return HasMovedAtLeast(_lastTriggerX, _lastTriggerY,
                normalizedX, normalizedY, RetriggerMoveFactor);
        }

        private void MarkOutside(DateTime now)
        {
            ClearPending();
            _candidateDirection = AttentionDirection.None;
            _stableDirection = AttentionDirection.None;
            _dwellProgress = 0.0;

            if (!_hasTriggerPoint)
                return;

            if (_outsideSince == DateTime.MinValue)
                _outsideSince = now;
            else if (HasElapsed(_outsideSince, now, LeaveDuration))
                _leaveRearmReady = true;
        }

        private void MarkReturnedToValidArea(DateTime now)
        {
            if (_outsideSince != DateTime.MinValue)
            {
                if (HasElapsed(_outsideSince, now, LeaveDuration))
                    _leaveRearmReady = true;
                _outsideSince = DateTime.MinValue;
            }
        }

        private void ClearPending()
        {
            _pendingDirection = AttentionDirection.None;
            _pendingSince = DateTime.MinValue;
            _pendingX = 0.0;
            _pendingY = 0.0;
            _dwellProgress = 0.0;
        }

        private void SetStatus(string value)
        {
            Status = value;
        }

        private DateTime NormalizeNow(DateTime now)
        {
            if (!_hasClock)
            {
                _lastNow = now;
                _hasClock = true;
                return now;
            }

            // Wall-clock adjustments must not shorten a dwell or cooldown.
            if (now < _lastNow)
                now = _lastNow;
            _lastNow = now;
            return now;
        }

        private static TargetKind ClassifyTarget(
            double dx,
            double dy,
            double bodyHeight,
            bool mirrored,
            out double normalizedX,
            out double normalizedY)
        {
            normalizedX = 0.0;
            normalizedY = 0.0;
            if (!IsFinitePositive(bodyHeight) || !IsFinite(dx) || !IsFinite(dy))
                return TargetKind.InvalidTarget;

            double logicalX = mirrored ? -dx : dx;
            normalizedX = logicalX / bodyHeight;
            normalizedY = dy / bodyHeight;

            // Finite input can still overflow when divided by a very small H;
            // that is a well-defined too-far sample rather than a crash.
            if (!IsFinite(normalizedX) || !IsFinite(normalizedY))
                return TargetKind.TooFar;

            double radiusSquared = normalizedX * normalizedX + normalizedY * normalizedY;
            if (!IsFinite(radiusSquared) || radiusSquared > MaximumRadiusFactor * MaximumRadiusFactor)
                return TargetKind.TooFar;
            if (radiusSquared <= DeadRadiusFactor * DeadRadiusFactor)
                return TargetKind.TooClose;
            if (normalizedY > BelowHeadFactor)
                return TargetKind.BelowHead;
            return TargetKind.Valid;
        }

        private static string StatusForTarget(TargetKind target)
        {
            switch (target)
            {
                case TargetKind.TooClose:
                    return "TooClose";
                case TargetKind.TooFar:
                    return "TooFar";
                case TargetKind.BelowHead:
                    return "BelowHead";
                default:
                    return "InvalidTarget";
            }
        }

        private double GetCooldownRemainingSeconds()
        {
            if (!_hasClock || _cooldownUntil == DateTime.MinValue || _cooldownUntil <= _lastNow)
                return 0.0;

            TimeSpan remaining = _cooldownUntil - _lastNow;
            return Math.Max(0.0, remaining.TotalSeconds);
        }

        private static double DwellFraction(DateTime start, DateTime now)
        {
            if (start == DateTime.MinValue || now <= start)
                return 0.0;
            double fraction = (now - start).TotalMilliseconds / DwellDuration.TotalMilliseconds;
            return Math.Max(0.0, Math.Min(1.0, fraction));
        }

        private static bool HasElapsed(DateTime start, DateTime now, TimeSpan duration)
        {
            return start != DateTime.MinValue && now >= start && (now - start) >= duration;
        }

        private static bool HasMovedMoreThan(
            double firstX,
            double firstY,
            double secondX,
            double secondY,
            double threshold)
        {
            double dx = secondX - firstX;
            double dy = secondY - firstY;
            return Math.Sqrt(dx * dx + dy * dy) > threshold;
        }

        private static bool HasMovedAtLeast(
            double firstX,
            double firstY,
            double secondX,
            double secondY,
            double threshold)
        {
            double dx = secondX - firstX;
            double dy = secondY - firstY;
            return Math.Sqrt(dx * dx + dy * dy) + 1e-12 >= threshold;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0.0 && IsFinite(value);
        }

        private static DateTime AddSecondsSafe(DateTime value, double seconds)
        {
            try
            {
                return value.AddSeconds(seconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return DateTime.MaxValue;
            }
        }

        private static double CooldownSeconds(InteractionFrequency frequency)
        {
            switch (frequency)
            {
                case InteractionFrequency.Calm:
                    return 4.0;
                case InteractionFrequency.Lively:
                    return 0.8;
                default:
                    return 1.5;
            }
        }

        private enum TargetKind
        {
            Valid,
            TooClose,
            TooFar,
            BelowHead,
            InvalidTarget
        }
    }
}
