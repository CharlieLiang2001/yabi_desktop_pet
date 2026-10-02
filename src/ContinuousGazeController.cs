using System;

namespace YabiDesktopPet
{
    // Continuous parameters, not an action trigger or a video playhead.
    internal sealed class ContinuousGazeController
    {
        public double EyeX { get; private set; }
        public double EyeY { get; private set; }
        public double HeadX { get; private set; }
        public double HeadY { get; private set; }
        public double TargetX { get; private set; }
        public double TargetY { get; private set; }
        public bool IsNeutral { get { return Math.Abs(EyeX) + Math.Abs(EyeY) + Math.Abs(HeadX) + Math.Abs(HeadY) < .002; } }

        public void Update(double dx, double dy, double bodyHeight, bool eligible, bool mirrored, double seconds)
        {
            if (!Finite(seconds) || seconds <= 0) return;
            seconds = Math.Min(seconds, .1);
            bool valid = eligible && Finite(dx) && Finite(dy) && Finite(bodyHeight) && bodyHeight > 0;
            TargetX = valid ? Map((mirrored ? -dx : dx) / bodyHeight, 1.0) : 0;
            TargetY = valid ? Map(dy / bodyHeight, .85) : 0;
            EyeX = Ease(EyeX, TargetX, .065, seconds);
            EyeY = Ease(EyeY, TargetY, .065, seconds);
            HeadX = Ease(HeadX, TargetX, .19, seconds);
            HeadY = Ease(HeadY, TargetY, .19, seconds);
        }

        private static double Map(double value, double scale)
        {
            if (!Finite(value)) return 0;
            // Continuous dead-zone and saturation, with no left/right/up sectors.
            double magnitude = Math.Max(0, Math.Abs(value) - .025);
            return Math.Sign(value) * Math.Tanh(magnitude / scale);
        }
        private static double Ease(double value, double target, double tau, double dt)
        {
            double result = target + (value - target) * Math.Exp(-dt / tau);
            return Math.Abs(result - target) < .00001 ? target : result;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public void Reset() { EyeX = EyeY = HeadX = HeadY = TargetX = TargetY = 0; }
    }

    // Calibration is in the ORIGINAL 320x480 idle texture, not screen pixels.
    // Body, paws and tail remain exactly pinned. No static head is pasted over a video.
    internal sealed class ContinuousGazeRig
    {
        public readonly double EyeLeftX, EyeRightX, EyeY, HeadX, NeckY, FadeStart, FadeEnd;
        public ContinuousGazeRig(bool standing)
        {
            EyeLeftX = standing ? 59 : 80;
            EyeRightX = standing ? 104 : 120;
            EyeY = standing ? 126 : 149;
            HeadX = standing ? 82 : 100;
            NeckY = standing ? 176 : 197;
            FadeStart = standing ? 162 : 182;
            FadeEnd = standing ? 242 : 278;
        }

        public System.Windows.Point Deform(double x, double y, double eyeX, double eyeY, double headX, double headY)
        {
            if (y >= FadeEnd || x >= HeadX + 110) return new System.Windows.Point(x, y);
            double neck = (1 - Smooth(FadeStart, FadeEnd, y)) * (1 - Smooth(80, 110, Math.Abs(x - HeadX)));
            double face = Oval(x, y, HeadX, EyeY + 10, 52, 56);
            double dx = neck * headX * (6.0 + face);
            double dy = neck * headY * (3.8 + face * .8);
            // Very small perspective/tilt cues on one connected neck/head mesh.
            dx -= neck * Math.Abs(headX) * .009 * (x - HeadX);
            dy += neck * headX * (x - HeadX) * .014;
            double eyes = Oval(x, y, EyeLeftX, EyeY, 8, 8) + Oval(x, y, EyeRightX, EyeY - 1, 8, 8);
            // Bounded iris-region approximation. Eyelids are NOT compressed to blink.
            dx += eyeX * 2.1 * eyes;
            dy += eyeY * 1.4 * eyes;
            return new System.Windows.Point(x + dx, y + dy);
        }
        private static double Smooth(double a, double b, double x)
        { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); }
        private static double Oval(double x, double y, double cx, double cy, double rx, double ry)
        { double r = (x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry); return r >= 1 ? 0 : (1 - r) * (1 - r); }
    }
}
