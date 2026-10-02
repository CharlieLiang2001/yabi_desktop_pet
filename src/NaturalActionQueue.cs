using System;

namespace YabiDesktopPet
{
    /// <summary>
    /// A small request object passed from menus/status cards to the action
    /// coordinator.  ClipId identifies the complete-body clip; CareKind is
    /// optional metadata used to settle feeding/petting once.
    /// </summary>
    internal sealed class NaturalActionRequest
    {
        public string ClipId;
        public string CareKind;
        public bool Preview;
        public string Label;
        public int CareEpoch;
        private bool _begun;
        public bool TryBegin(int currentEpoch)
        {
            if (_begun || (!string.IsNullOrEmpty(CareKind) && CareEpoch != currentEpoch)) return false;
            _begun = true;
            return true;
        }
    }

    /// <summary>
    /// Capacity-one, latest-wins action handoff.  The window remains
    /// responsive when a user taps an action repeatedly, while the playback
    /// owner still has a single deterministic request to consume after its
    /// current complete-body clip ends.
    /// </summary>
    internal sealed class NaturalActionQueue
    {
        private NaturalActionRequest _pending;

        public NaturalActionRequest Peek
        {
            get { return _pending; }
        }

        public bool HasPending
        {
            get { return _pending != null; }
        }

        public int Count
        {
            get { return _pending == null ? 0 : 1; }
        }

        /// <summary>
        /// Stores the newest request.  A repeated CareKind or ClipId therefore
        /// replaces the old request instead of creating a second animation.
        /// Null requests are ignored so UI teardown can safely race a click.
        /// </summary>
        public void Enqueue(NaturalActionRequest request)
        {
            if (request == null)
            {
                return;
            }
            _pending = request;
        }

        /// <summary>
        /// Takes and clears the current request.  A later request can be
        /// queued immediately while the returned request is being played.
        /// </summary>
        public NaturalActionRequest Take()
        {
            NaturalActionRequest request = _pending;
            _pending = null;
            return request;
        }

        public void Clear()
        {
            _pending = null;
        }
    }

    /// <summary>
    /// Maps elapsed clip time to a frame without changing the clip duration.
    /// The helper uses the first and last frame as stable endpoints and is
    /// safe for malformed metadata.
    /// </summary>
    internal static class PlaybackTimeline
    {
        public static int FrameAt(long elapsedMs, long durationMs, int frameCount, bool reverse)
        {
            if (frameCount <= 1 || durationMs <= 0)
            {
                return 0;
            }

            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }
            if (elapsedMs > durationMs)
            {
                elapsedMs = durationMs;
            }

            return FrameAtProgress((double)elapsedMs, (double)durationMs, frameCount, reverse);
        }

        public static int FrameAt(double elapsedMs, double durationMs, int frameCount, bool reverse)
        {
            if (double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs))
            {
                elapsedMs = 0.0;
            }
            if (double.IsNaN(durationMs) || double.IsInfinity(durationMs) || durationMs <= 0.0)
            {
                return 0;
            }
            if (elapsedMs < 0.0)
            {
                elapsedMs = 0.0;
            }
            if (elapsedMs > durationMs)
            {
                elapsedMs = durationMs;
            }
            return FrameAtProgress(elapsedMs, durationMs, frameCount, reverse);
        }

        public static bool IsComplete(long elapsedMs, long durationMs)
        {
            return durationMs <= 0 || elapsedMs >= durationMs;
        }

        public static bool IsComplete(double elapsedMs, double durationMs)
        {
            return durationMs <= 0.0 || elapsedMs >= durationMs;
        }

        private static int FrameAtProgress(double elapsedMs, double durationMs, int frameCount, bool reverse)
        {
            if (frameCount <= 1 || durationMs <= 0.0)
            {
                return 0;
            }

            double progress = elapsedMs / durationMs;
            int frame = (int)Math.Floor(progress * frameCount);
            // At the completion timestamp progress is exactly 1.0.  The
            // player still renders the final frame once, then completes the
            // clip without changing its elapsed duration.
            if (frame < 0)
            {
                frame = 0;
            }
            if (frame >= frameCount)
            {
                frame = frameCount - 1;
            }
            return reverse ? frameCount - 1 - frame : frame;
        }
    }
}
