using System;
using System.Diagnostics;

namespace YabiDesktopPet
{
    // Active intervals use monotonic time. System sleep is explicitly excluded;
    // wall-clock corrections can only affect dates/cooldowns, never needs.
    internal sealed class CareRuntimeClock
    {
        private readonly Func<double> _seconds;
        private readonly object _gate = new object();
        private double _last;
        private bool _initialized;
        private bool _suspended;
        public CareRuntimeClock() : this(delegate { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; }) { }
        public CareRuntimeClock(Func<double> seconds) { _seconds = seconds; }
        public TimeSpan Sample()
        {
            lock (_gate)
            {
                double now = _seconds();
                double elapsed = _initialized && !_suspended ? Math.Max(0, now - _last) : 0;
                _last = now; _initialized = true;
                return TimeSpan.FromSeconds(elapsed);
            }
        }
        public void SetSuspended(bool suspended)
        {
            lock (_gate) { _suspended = suspended; _last = _seconds(); _initialized = true; }
        }
    }
}
