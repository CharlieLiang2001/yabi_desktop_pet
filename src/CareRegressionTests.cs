using System;
using System.Collections.Generic;
using System.IO;

namespace YabiDesktopPet
{
    internal static class CareRegressionTests
    {
        internal static IEnumerable<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            DateTime now = DateTime.UtcNow;
            Check(failures, "awake remainder survives sleep", delegate {
                PetProfile p = new PetProfile(); CareController c = new CareController(p, false);
                c.Tick(TimeSpan.FromMinutes(7), false, now, now.ToLocalTime());
                c.Tick(TimeSpan.FromSeconds(30), true, now.AddSeconds(30), now.AddSeconds(30).ToLocalTime());
                c.Tick(TimeSpan.FromMinutes(1), false, now.AddMinutes(2), now.AddMinutes(2).ToLocalTime());
                Require(p.Care.Energy == 79, "expected 79, got " + p.Care.Energy);
            });
            Check(failures, "fractional seconds accumulate", delegate {
                PetProfile p = new PetProfile(); CareController c = new CareController(p, false);
                for (int i = 1; i <= 10; i++) c.Tick(TimeSpan.FromMilliseconds(400), false, now.AddMilliseconds(i * 400), now.AddMilliseconds(i * 400).ToLocalTime());
                Require(p.Growth.TotalActiveSeconds == 4, "expected 4, got " + p.Growth.TotalActiveSeconds);
            });
            Check(failures, "midnight interval is split", delegate {
                PetProfile p = new PetProfile(); CareController c = new CareController(p, false);
                DateTime end = DateTime.Today.AddDays(1).AddSeconds(10);
                p.Growth.TodayDate = DateTime.Today.ToString("yyyy-MM-dd");
                p.Growth.TodayActiveSeconds = 300;
                c.Tick(TimeSpan.FromSeconds(20), false, end.ToUniversalTime(), end);
                Require(p.Growth.TodayActiveSeconds == 10, "expected 10, got " + p.Growth.TodayActiveSeconds);
                Require(p.Growth.TotalActiveSeconds == 20, "total duration changed");
            });
            Check(failures, "stale streak resets before interaction", delegate {
                PetProfile p = new PetProfile(); p.Growth.CurrentStreak = 7; p.Growth.BestStreak = 7;
                p.Growth.LastActiveDate = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd");
                CareController c = new CareController(p, false);
                Require(c.Profile.Growth.CurrentStreak == 0 && c.Profile.Growth.BestStreak == 7, "stale streak displayed");
            });
            Check(failures, "low need query does not consume cooldown", delegate {
                PetProfile p = new PetProfile(); p.Care.Fullness = 10; CareController c = new CareController(p, false);
                Require(c.GetLowNeedMessage(now) != null && c.GetLowNeedMessage(now.AddSeconds(1)) != null, "query consumed prompt");
            });
            Check(failures, "rest cooldown does not block sleeping", delegate {
                CareController c = new CareController(new PetProfile(), false);
                c.Rest(now, now.ToLocalTime()); int xp = c.Profile.Growth.AffinityXp;
                Require(c.Rest(now.AddMinutes(1), now.AddMinutes(1).ToLocalTime()).Applied, "sleep request rejected");
                Require(c.Profile.Growth.AffinityXp == xp, "reward duplicated");
            });
            Check(failures, "loaded awake remainder survives first tick", delegate {
                PetProfile p = new PetProfile(); p.Care.EnergyMinutes = 7;
                CareController c = new CareController(p, false);
                c.Tick(TimeSpan.FromMinutes(1), false, now, now.ToLocalTime());
                Require(p.Care.Energy == 79, "loaded remainder was discarded");
            });
            Check(failures, "nonfinite profile data is rejected", delegate {
                string folder = Path.Combine(Path.GetTempPath(), "Yabi434-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                try {
                    string path = Path.Combine(folder, "profile.xml");
                    File.WriteAllText(path, "<YabiProfile><Care><FullnessMinutes>Infinity</FullnessMinutes><EnergyMinutes>NaN</EnergyMinutes></Care><Growth><AffinityXp>2147483647</AffinityXp></Growth></YabiProfile>");
                    PetProfile p = PetProfile.LoadFrom(path, false);
                    Require(!double.IsInfinity(p.Care.FullnessMinutes) && !double.IsNaN(p.Care.EnergyMinutes), "nonfinite timer accepted");
                    Require(p.Growth.AffinityXp <= 9800, "experience exceeds cap");
                } finally { Directory.Delete(folder, true); }
            });
            Check(failures, "sleep remainder survives waking and restart", delegate {
                PetProfile p = new PetProfile(); CareController c = new CareController(p, false);
                c.Tick(TimeSpan.FromMinutes(1), true, now, now.ToLocalTime());
                c.Tick(TimeSpan.FromSeconds(1), false, now.AddSeconds(1), now.AddSeconds(1).ToLocalTime());
                c = new CareController(p, false);
                c.Tick(TimeSpan.FromMinutes(1), true, now.AddMinutes(1), now.AddMinutes(1).ToLocalTime());
                Require(p.Care.Energy == 81, "sleep recovery remainder lost");
            });
            Check(failures, "suspend resume excludes suspended interval", delegate {
                Type type = typeof(CareController).Assembly.GetType("YabiDesktopPet.CareRuntimeClock");
                Require(type != null, "runtime clock absent");
                double seconds = 0;
                object clock = Activator.CreateInstance(type, new object[] { new Func<double>(delegate { return seconds; }) });
                type.GetMethod("Sample").Invoke(clock, null);
                seconds = 1.5;
                Require((TimeSpan)type.GetMethod("Sample").Invoke(clock, null) == TimeSpan.FromSeconds(1.5), "monotonic interval");
                type.GetMethod("SetSuspended").Invoke(clock, new object[] { true });
                seconds = 7200;
                Require((TimeSpan)type.GetMethod("Sample").Invoke(clock, null) == TimeSpan.Zero, "suspended time counted");
                type.GetMethod("SetSuspended").Invoke(clock, new object[] { false });
                seconds += 0.5;
                Require((TimeSpan)type.GetMethod("Sample").Invoke(clock, null) == TimeSpan.FromSeconds(0.5), "resume baseline");
            });
            Check(failures, "rest already sleeping never rewards", delegate {
                CareController c = new CareController(new PetProfile(), false);
                CareActionResult result = c.Rest(now, now.ToLocalTime(), true);
                Require(result.RequestAccepted && !result.RewardGranted && c.Profile.Growth.AffinityXp == 0, "sleeping rewarded");
            });
            Check(failures, "save failure is visible and recoverable", delegate {
                bool fail = true;
                CareController c = new CareController(new PetProfile(), true, delegate { return now.ToLocalTime(); }, delegate(PetProfile p) { if (fail) throw new IOException("locked"); });
                c.Save(); Require(!string.IsNullOrEmpty(c.GetSnapshot(now, now.ToLocalTime()).SaveError), "save failure silent");
                fail = false; c.Save(); Require(c.SaveError == null, "save error did not clear");
            });
            Check(failures, "full level snapshot and immutable achievements", delegate {
                PetProfile p = new PetProfile(); p.Growth.AffinityXp = 9800; p.Growth.Achievements.Add("example");
                CareSnapshot snapshot = new CareSnapshot(p, null, now);
                Require(snapshot.Level == 99 && snapshot.AffinityProgress == 100, "max level empty progress");
                snapshot.Achievements[0] = "changed";
                Require(snapshot.Achievements[0] == "example", "snapshot mutable");
            });
            Check(failures, "low need acknowledgement and cooldown rollback", delegate {
                PetProfile p = new PetProfile(); p.Care.Fullness = 0; CareController c = new CareController(p, false);
                c.ConfirmLowNeedShown(now);
                Require(c.GetLowNeedMessage(now.AddMinutes(59)) == null && c.GetLowNeedMessage(now.AddHours(1)) != null, "prompt cooldown wrong");
                p.LastFedUtc = now.AddDays(4);
                Require(c.GetSnapshot(now, now.ToLocalTime()).FeedCooldown == TimeSpan.FromMinutes(20), "rollback cooldown unbounded");
            });
            Check(failures, "atomic save failure keeps original", delegate {
                string folder = Path.Combine(Path.GetTempPath(), "Yabi434-save-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                try {
                    string path = Path.Combine(folder, "profile.xml"); PetProfile p = new PetProfile(); p.SaveTo(path);
                    string original = File.ReadAllText(path); bool failed = false;
                    using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        p.Care.Fullness = 12;
                        try { p.SaveTo(path); } catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
                    }
                    Require(failed && File.ReadAllText(path) == original, "original changed on failure");
                    Require(Directory.GetFiles(folder, "*.tmp").Length == 0, "temporary file leaked");
                    p.SaveTo(path); Require(PetProfile.LoadFrom(path, false).Care.Fullness == 12, "retry failed");
                    Require(File.ReadAllText(path + ".bak") == original, "previous good backup absent");
                } finally { Directory.Delete(folder, true); }
            });
            Check(failures, "clock rollback does not postpone save retries", delegate {
                int saves = 0;
                CareController c = new CareController(new PetProfile(), true, delegate { return now.ToLocalTime(); }, delegate(PetProfile p) { saves++; });
                c.Tick(TimeSpan.FromSeconds(1), false, now, now.ToLocalTime());
                c.Tick(TimeSpan.FromSeconds(1), false, now.AddHours(-2), now.AddHours(-2).ToLocalTime());
                Require(saves == 2, "rollback postponed saving");
            });
            Check(failures, "profile v2 preserves both remainders and fractional time", delegate {
                string folder = Path.Combine(Path.GetTempPath(), "Yabi434-v2-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                try {
                    PetProfile p = new PetProfile(); p.Care.EnergyMinutes = 7.5; p.Care.SleepMinutes = 1.5; p.Growth.TotalSecondRemainder = .4;
                    p.SaveTo(Path.Combine(folder, "profile.xml")); p = PetProfile.LoadFrom(Path.Combine(folder, "profile.xml"), false);
                    CareController c = new CareController(p, false);
                    c.Tick(TimeSpan.FromSeconds(30), false, now, now.ToLocalTime());
                    c.Tick(TimeSpan.FromSeconds(30), true, now.AddSeconds(30), now.AddSeconds(30).ToLocalTime());
                    Require(p.Care.Energy == 80 && Math.Abs(p.Growth.TotalSecondRemainder - .4) < .00001, "v2 lost fractional state");
                } finally { Directory.Delete(folder, true); }
            });
            Check(failures, "need boundaries and full-value care keep existing rules", delegate {
                PetProfile p = new PetProfile(); p.Care.Fullness=0; p.Care.Energy=0; p.Care.Mood=0;
                CareController c = new CareController(p, false);
                c.Tick(TimeSpan.FromMinutes(60), false, now, now.ToLocalTime());
                Require(p.Care.Fullness==0 && p.Care.Energy==0 && p.Care.Mood==0, "need below zero");
                p.Care.Energy=100; c.Tick(TimeSpan.FromMinutes(10), true, now.AddMinutes(10), now.AddMinutes(10).ToLocalTime());
                Require(p.Care.Energy==100, "sleep exceeds maximum");
                p.Care.Fullness=100; Require(c.Feed(now, now.ToLocalTime()).RewardGranted && p.Care.Fullness==100, "full-value feeding rules changed");
            });
            return failures;
        }

        private static void Check(List<string> failures, string name, Action test)
        {
            try { test(); Console.WriteLine("PASS: " + name); }
            catch (Exception e) { failures.Add(name + ": " + e.Message); Console.WriteLine("FAIL: " + name + ": " + e.Message); }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
