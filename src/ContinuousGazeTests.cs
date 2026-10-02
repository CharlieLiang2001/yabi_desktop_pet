using System;
using System.Collections.Generic;
using System.Windows;

namespace YabiDesktopPet
{
    internal static class ContinuousGazeTests
    {
        public static IList<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            ContinuousGazeController state = new ContinuousGazeController();
            state.Update(200, -200, 100, true, false, .033);
            Check(failures, state.EyeX > state.HeadX && state.HeadX > 0, "eyes lead head from first sample without dwell");
            Check(failures, state.EyeY < state.HeadY && state.HeadY < 0, "vertical tracking starts immediately");
            double previous = state.EyeX;
            for (int i = 0; i < 20; i++)
            {
                state.Update(200, -200, 100, true, false, .033);
                Check(failures, state.EyeX >= previous && state.EyeX < 1, "bounded monotonic easing");
                previous = state.EyeX;
            }
            state.Update(-200, 200, 100, true, false, .033);
            Check(failures, state.EyeX < previous && state.TargetX < 0 && state.TargetY > 0, "direction changes without action completion or cooldown");
            for (int i = 0; i < 90; i++) state.Update(-200, 200, 100, true, false, 1.0 / 60);
            Check(failures, state.HeadX < -.8 && state.HeadY > .8, "downward and left targets remain held");
            state.Reset(); state.Update(200, 0, 100, true, true, .1);
            Check(failures, state.TargetX < 0, "mirror maps once");
            for (int i = 0; i < 90; i++) state.Update(200, 0, 100, false, false, 1.0 / 60);
            Check(failures, state.IsNeutral, "disable returns smoothly to neutral");
            foreach (int hz in new[] { 20, 30, 60, 144 })
            {
                state.Reset();
                for (int i = 0; i < hz; i++) state.Update(200, 0, 100, true, false, 1.0 / hz);
                double expected = state.TargetX * (1 - Math.Exp(-1 / .19));
                Check(failures, Math.Abs(state.HeadX - expected) < .0001, "frame-rate independent smoothing at " + hz);
            }
            state.Update(double.NaN, double.PositiveInfinity, 0, true, false, 1);
            Check(failures, state.TargetX == 0 && state.TargetY == 0 && !double.IsNaN(state.EyeX), "invalid coordinates safely recenter");
            state.Reset(); state.Update(1, 1, 100, true, false, .1);
            Check(failures, state.IsNeutral, "center dead-zone has no jump");
            state.Update(100, 0, 100, true, false, .1);
            Check(failures, state.TargetX > .7, "nearby cursor movement has a clearly stronger response");
            foreach (bool standing in new[] { false, true })
            {
                ContinuousGazeRig rig = new ContinuousGazeRig(standing);
                foreach (double ex in new[] { -1.0, 0, 1 }) foreach (double ey in new[] { -1.0, 0, 1 })
                foreach (double hx in new[] { -1.0, 0, 1 }) foreach (double hy in new[] { -1.0, 0, 1 })
                {
                    bool pinned = true, positive = true;
                    for (int y = 0; y <= 480; y += 4) for (int x = 0; x <= 320; x += 4)
                    {
                        Point p = rig.Deform(x, y, ex, ey, hx, hy);
                        if (y >= rig.FadeEnd && (p.X != x || p.Y != y)) pinned = false;
                        Point px = rig.Deform(x + .25, y, ex, ey, hx, hy), py = rig.Deform(x, y + .25, ex, ey, hx, hy);
                        double determinant = (px.X - p.X) * (py.Y - p.Y) - (px.Y - p.Y) * (py.X - p.X);
                        if (determinant <= 0 || double.IsNaN(determinant)) positive = false;
                    }
                    Check(failures, pinned, "all lower-body/paw/tail samples are invariant");
                    Check(failures, positive, "extreme parameter grid retains positive local orientation");
                }
                Point neutral = rig.Deform(rig.HeadX, rig.EyeY, 0, 0, 0, 0);
                Check(failures, neutral.X == rig.HeadX && neutral.Y == rig.EyeY, "neutral texture is unchanged");
                Point eye = rig.Deform(rig.EyeLeftX, rig.EyeY, 1, 0, 0, 0);
                Check(failures, eye.X - rig.EyeLeftX >= 2.0, "horizontal eye-region response is at least two source pixels");
            }
            return failures;
        }
        private static void Check(ICollection<string> failures, bool condition, string detail)
        { if (!condition) failures.Add("continuous gaze: " + detail); }
    }
}
