using System;
using System.Collections.Generic;

namespace YabiDesktopPet
{
    internal sealed class ReminderDueEventArgs : EventArgs
    {
        public ReminderDefinition Reminder;
    }

    internal sealed class ReminderController
    {
        private readonly Dictionary<string, ReminderDefinition> _definitions = new Dictionary<string, ReminderDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _nextDueUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingReminders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly PetSettings _settings;
        private DateTime _focusDueUtc;
        private TimeSpan _focusRemaining;
        private bool _focusRunning;

        public ReminderController(PetSettings settings)
        {
            _settings = settings;
            Refresh(DateTime.UtcNow);
        }

        public event EventHandler<ReminderDueEventArgs> ReminderDue;

        public bool FocusRunning { get { return _focusRunning; } }
        public TimeSpan FocusRemaining
        {
            get
            {
                if (_focusRunning)
                {
                    return _focusDueUtc > DateTime.UtcNow ? _focusDueUtc - DateTime.UtcNow : TimeSpan.Zero;
                }
                return _focusRemaining;
            }
        }

        public IEnumerable<ReminderDefinition> Definitions { get { return _definitions.Values; } }

        /// <summary>
        /// Returns whether a reminder has been displayed and is waiting for an
        /// explicit user choice.  A pending reminder is never auto-completed.
        /// </summary>
        public bool IsPending(string reminderId)
        {
            return !string.IsNullOrEmpty(reminderId) && _pendingReminders.Contains(reminderId);
        }

        public void Refresh(DateTime utcNow)
        {
            Dictionary<string, DateTime> previous = new Dictionary<string, DateTime>(_nextDueUtc, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, bool> previousEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ReminderDefinition> pair in _definitions)
            {
                previousEnabled[pair.Key] = pair.Value != null && pair.Value.Enabled;
            }
            HashSet<string> previousPending = new HashSet<string>(_pendingReminders, StringComparer.OrdinalIgnoreCase);
            _definitions.Clear();
            _nextDueUtc.Clear();
            _pendingReminders.Clear();
            AddDefinition(new ReminderDefinition
            {
                Id = "water",
                Name = "喝水提醒",
                Message = "该喝点水啦，亚比陪你休息一下。",
                IntervalMinutes = Clamp(_settings.WaterReminderMinutes),
                Enabled = _settings.WaterReminderEnabled,
                Kind = ReminderKind.Water
            }, utcNow, previous, previousEnabled, previousPending);
            AddDefinition(new ReminderDefinition
            {
                Id = "stretch",
                Name = "伸展提醒",
                Message = "起来伸展一下肩颈吧。",
                IntervalMinutes = Clamp(_settings.StretchReminderMinutes),
                Enabled = _settings.StretchReminderEnabled,
                Kind = ReminderKind.Stretch
            }, utcNow, previous, previousEnabled, previousPending);
            foreach (ReminderDefinition custom in _settings.CustomReminders)
            {
                if (custom != null && _definitions.Count < 7)
                {
                    AddDefinition(custom.Clone(), utcNow, previous, previousEnabled, previousPending);
                }
            }
        }

        public void Tick(DateTime utcNow)
        {
            if (_focusRunning && utcNow >= _focusDueUtc)
            {
                _focusRunning = false;
                _focusRemaining = TimeSpan.Zero;
                _pendingReminders.Add("focus");
                RaiseDue(new ReminderDefinition
                {
                    Id = "focus",
                    Name = "专注完成",
                    Message = "专注完成啦，休息一下眼睛吧。",
                    IntervalMinutes = _settings.FocusMinutes,
                    Enabled = true,
                    Kind = ReminderKind.Focus
                });
            }

            foreach (ReminderDefinition definition in new List<ReminderDefinition>(_definitions.Values))
            {
                DateTime due;
                if (!definition.Enabled || !_nextDueUtc.TryGetValue(definition.Id, out due) || utcNow < due)
                {
                    continue;
                }
                _nextDueUtc[definition.Id] = utcNow.AddMinutes(Clamp(definition.IntervalMinutes));
                if (_pendingReminders.Contains(definition.Id))
                {
                    // An unanswered bubble is not completion.  Once the
                    // next interval arrives, replace that stale pending
                    // instance so the user can still receive the reminder.
                    _pendingReminders.Remove(definition.Id);
                }
                RaiseDue(definition.Clone());
            }
        }

        public void StartFocus(DateTime utcNow)
        {
            int minutes = Clamp(_settings.FocusMinutes);
            _focusRemaining = TimeSpan.FromMinutes(minutes);
            _focusDueUtc = utcNow + _focusRemaining;
            _focusRunning = true;
        }

        public void PauseFocus(DateTime utcNow)
        {
            if (!_focusRunning)
            {
                return;
            }
            _focusRemaining = _focusDueUtc > utcNow ? _focusDueUtc - utcNow : TimeSpan.Zero;
            _focusRunning = false;
        }

        public void ResumeFocus(DateTime utcNow)
        {
            if (_focusRunning || _focusRemaining <= TimeSpan.Zero)
            {
                return;
            }
            _focusDueUtc = utcNow + _focusRemaining;
            _focusRunning = true;
        }

        public void ResetFocus()
        {
            _focusRunning = false;
            _focusRemaining = TimeSpan.Zero;
        }

        public void SnoozeFocus(DateTime utcNow, int minutes)
        {
            _focusRemaining = TimeSpan.FromMinutes(Math.Max(5, Math.Min(30, minutes)));
            _focusDueUtc = utcNow + _focusRemaining;
            _focusRunning = true;
        }

        /// <summary>
        /// Acknowledges a visible reminder.  The return value is false for a
        /// second click or an id that is not currently pending, which keeps
        /// experience/statistics settlement idempotent in the caller.
        /// </summary>
        public bool AcknowledgeReminder(string reminderId)
        {
            if (string.IsNullOrEmpty(reminderId) || !_pendingReminders.Remove(reminderId))
            {
                return false;
            }
            return true;
        }

        // Short names are kept for the MainWindow reminder UI.  The longer
        // names above remain as descriptive compatibility aliases for older
        // callers.
        public bool Acknowledge(string reminderId)
        {
            return AcknowledgeReminder(reminderId);
        }

        /// <summary>
        /// Delays a visible reminder and clears its pending state.  The next
        /// interval is measured from the snooze action, not the old deadline.
        /// Focus keeps its existing pause/resume semantics while ordinary
        /// reminders use the same bounded five-to-thirty-minute choices.
        /// </summary>
        public bool SnoozeReminder(string reminderId, DateTime utcNow, int minutes)
        {
            if (string.IsNullOrEmpty(reminderId) || !_pendingReminders.Contains(reminderId))
            {
                return false;
            }

            int delay = Math.Max(5, Math.Min(30, minutes));
            if (string.Equals(reminderId, "focus", StringComparison.OrdinalIgnoreCase))
            {
                _pendingReminders.Remove(reminderId);
                _focusRemaining = TimeSpan.FromMinutes(delay);
                _focusDueUtc = utcNow + _focusRemaining;
                _focusRunning = true;
                return true;
            }

            if (_definitions.ContainsKey(reminderId))
            {
                _pendingReminders.Remove(reminderId);
                _nextDueUtc[reminderId] = utcNow.AddMinutes(delay);
                return true;
            }
            return false;
        }

        public bool Snooze(string reminderId, DateTime utcNow, int minutes)
        {
            return SnoozeReminder(reminderId, utcNow, minutes);
        }

        public bool Dismiss(string reminderId)
        {
            return AcknowledgeReminder(reminderId);
        }

        public string GetFocusLabel()
        {
            TimeSpan remaining = FocusRemaining;
            if (!_focusRunning && remaining <= TimeSpan.Zero)
            {
                return "开始专注（" + Clamp(_settings.FocusMinutes) + "分钟）";
            }
            int totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            return _focusRunning ? "暂停专注（剩余" + totalMinutes + "分钟）" : "继续专注（剩余" + totalMinutes + "分钟）";
        }

        private void AddDefinition(
            ReminderDefinition definition,
            DateTime utcNow,
            IDictionary<string, DateTime> previous,
            IDictionary<string, bool> previousEnabled,
            ISet<string> previousPending)
        {
            definition.Id = string.IsNullOrEmpty(definition.Id) ? Guid.NewGuid().ToString("N") : definition.Id;
            definition.IntervalMinutes = Clamp(definition.IntervalMinutes);
            _definitions[definition.Id] = definition;
            DateTime previousDue;
            bool wasEnabled;
            bool canKeepDeadline = previous.TryGetValue(definition.Id, out previousDue)
                && previousEnabled.TryGetValue(definition.Id, out wasEnabled)
                && wasEnabled
                && definition.Enabled;
            _nextDueUtc[definition.Id] = canKeepDeadline
                ? previousDue
                : utcNow.AddMinutes(definition.IntervalMinutes);
            if (definition.Enabled && previousPending.Contains(definition.Id) && canKeepDeadline)
            {
                _pendingReminders.Add(definition.Id);
            }
        }

        private void RaiseDue(ReminderDefinition reminder)
        {
            if (reminder != null && !string.IsNullOrEmpty(reminder.Id))
            {
                _pendingReminders.Add(reminder.Id);
            }
            EventHandler<ReminderDueEventArgs> handler = ReminderDue;
            if (handler != null)
            {
                handler(this, new ReminderDueEventArgs { Reminder = reminder });
            }
        }

        private static int Clamp(int minutes)
        {
            return Math.Max(5, Math.Min(240, minutes));
        }

        internal static IEnumerable<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            PetSettings settings = new PetSettings();
            settings.WaterReminderEnabled = true;
            settings.WaterReminderMinutes = 5;
            settings.FocusMinutes = 5;
            ReminderController controller = new ReminderController(settings);
            int due = 0;
            controller.ReminderDue += delegate { due++; };
            DateTime now = DateTime.UtcNow;
            controller.Refresh(now);
            controller.Refresh(now.AddMinutes(1));
            controller.Tick(now.AddMinutes(5.5));
            if (due != 1)
            {
                failures.Add("refresh did not preserve the existing reminder deadline");
            }
            if (!controller.IsPending("water"))
            {
                failures.Add("due reminder was not marked pending");
            }

            ReminderController unanswered = new ReminderController(settings);
            int unansweredDue = 0;
            unanswered.ReminderDue += delegate { unansweredDue++; };
            DateTime unansweredNow = DateTime.UtcNow;
            unanswered.Refresh(unansweredNow);
            unanswered.Tick(unansweredNow.AddMinutes(5.5));
            unanswered.Tick(unansweredNow.AddMinutes(11));
            if (unansweredDue != 2 || !unanswered.IsPending("water"))
            {
                failures.Add("unanswered reminder was not replaced at the next deadline");
            }

            if (!controller.AcknowledgeReminder("water") || controller.AcknowledgeReminder("water"))
            {
                failures.Add("duplicate reminder acknowledgement was not rejected");
            }

            controller.Tick(now.AddMinutes(11));
            if (due != 2)
            {
                failures.Add("recurring reminder did not fire after acknowledgement");
            }
            if (!controller.SnoozeReminder("water", now.AddMinutes(11), 10))
            {
                failures.Add("reminder snooze was rejected");
            }
            controller.Tick(now.AddMinutes(20.5));
            if (due != 2)
            {
                failures.Add("snoozed reminder fired too early");
            }
            controller.Tick(now.AddMinutes(21.1));
            if (due != 3)
            {
                failures.Add("snoozed reminder did not fire once");
            }
            controller.StartFocus(now);
            controller.PauseFocus(now.AddMinutes(2));
            controller.ResumeFocus(now.AddMinutes(3));
            controller.Tick(now.AddMinutes(7));
            if (due != 4 || !controller.IsPending("focus"))
            {
                failures.Add("focus pause/resume did not preserve remaining time");
            }
            if (!controller.SnoozeReminder("focus", now.AddMinutes(7), 5))
            {
                failures.Add("focus reminder snooze was rejected");
            }
            return failures;
        }
    }
}
