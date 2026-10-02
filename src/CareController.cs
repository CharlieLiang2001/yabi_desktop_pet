using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace YabiDesktopPet
{
    internal sealed class CareState
    {
        public int Fullness = 80;
        public int Energy = 80;
        public int Mood = 80;
        public double FullnessMinutes;
        public double EnergyMinutes;
        public double SleepMinutes;
        public double MoodMinutes;
        public double AffinityMinutes;
    }

    internal sealed class GrowthState
    {
        public int AffinityXp;
        public int Level { get { return Math.Min(99, 1 + AffinityXp / 100); } }
        public long TotalActiveSeconds;
        public long TodayActiveSeconds;
        public double TotalSecondRemainder;
        public double TodaySecondRemainder;
        public int TotalInteractions;
        public int TotalFeedings;
        public int TotalPettings;
        public int TotalRests;
        public int RemindersCompleted;
        public int FocusSessionsCompleted;
        public int CurrentStreak;
        public int BestStreak;
        public string TodayDate = string.Empty;
        public string LastActiveDate = string.Empty;
        public readonly HashSet<string> Achievements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class PetProfile
    {
        public readonly CareState Care = new CareState();
        public readonly GrowthState Growth = new GrowthState();
        public DateTime LastFedUtc = DateTime.MinValue;
        public DateTime LastPettedUtc = DateTime.MinValue;
        public DateTime LastRestedUtc = DateTime.MinValue;
        public DateTime LastLowNeedPromptUtc = DateTime.MinValue;

        internal static string ProfilePath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "YabiDesktopPet");
                return Path.Combine(folder, "profile.xml");
            }
        }

        public static PetProfile Load()
        {
            return LoadFrom(ProfilePath, true);
        }

        internal static PetProfile LoadFrom(string path, bool backUpCorrupt)
        {
            PetProfile profile = new PetProfile();
            if (!File.Exists(path))
            {
                return profile;
            }
            try
            {
                XElement root = XDocument.Load(path).Root;
                if (root == null || root.Name != "YabiProfile")
                {
                    throw new InvalidDataException("Invalid profile root.");
                }
                XElement care = root.Element("Care") ?? new XElement("Care");
                profile.Care.Fullness = ReadInt(care, "Fullness", 80, 0, 100);
                profile.Care.Energy = ReadInt(care, "Energy", 80, 0, 100);
                profile.Care.Mood = ReadInt(care, "Mood", 80, 0, 100);
                profile.Care.FullnessMinutes = ReadRemainder(care, "FullnessMinutes", 6);
                profile.Care.EnergyMinutes = ReadRemainder(care, "EnergyMinutes", 8);
                profile.Care.SleepMinutes = ReadRemainder(care, "SleepMinutes", 2);
                profile.Care.MoodMinutes = ReadRemainder(care, "MoodMinutes", 10);
                profile.Care.AffinityMinutes = ReadRemainder(care, "AffinityMinutes", 30);

                XElement growth = root.Element("Growth") ?? new XElement("Growth");
                profile.Growth.AffinityXp = ReadInt(growth, "AffinityXp", 0, 0, 9800);
                profile.Growth.TotalActiveSeconds = ReadLong(growth, "TotalActiveSeconds", 0);
                profile.Growth.TodayActiveSeconds = ReadLong(growth, "TodayActiveSeconds", 0);
                profile.Growth.TotalSecondRemainder = ReadRemainder(growth, "TotalSecondRemainder", 1);
                profile.Growth.TodaySecondRemainder = ReadRemainder(growth, "TodaySecondRemainder", 1);
                profile.Growth.TotalInteractions = ReadInt(growth, "TotalInteractions", 0, 0, int.MaxValue);
                profile.Growth.TotalFeedings = ReadInt(growth, "TotalFeedings", 0, 0, int.MaxValue);
                profile.Growth.TotalPettings = ReadInt(growth, "TotalPettings", 0, 0, int.MaxValue);
                profile.Growth.TotalRests = ReadInt(growth, "TotalRests", 0, 0, int.MaxValue);
                profile.Growth.RemindersCompleted = ReadInt(growth, "RemindersCompleted", 0, 0, int.MaxValue);
                profile.Growth.FocusSessionsCompleted = ReadInt(growth, "FocusSessionsCompleted", 0, 0, int.MaxValue);
                profile.Growth.CurrentStreak = ReadInt(growth, "CurrentStreak", 0, 0, int.MaxValue);
                profile.Growth.BestStreak = ReadInt(growth, "BestStreak", 0, 0, int.MaxValue);
                profile.Growth.TodayDate = ReadString(growth, "TodayDate", string.Empty);
                profile.Growth.LastActiveDate = ReadString(growth, "LastActiveDate", string.Empty);

                XElement achievements = root.Element("Achievements");
                if (achievements != null)
                {
                    foreach (XElement item in achievements.Elements("Achievement"))
                    {
                        string id = (item.Value ?? string.Empty).Trim();
                        if (id.Length > 0)
                        {
                            profile.Growth.Achievements.Add(id);
                        }
                    }
                }
                profile.LastFedUtc = ReadDate(root, "LastFedUtc");
                profile.LastPettedUtc = ReadDate(root, "LastPettedUtc");
                profile.LastRestedUtc = ReadDate(root, "LastRestedUtc");
                profile.LastLowNeedPromptUtc = ReadDate(root, "LastLowNeedPromptUtc");
                return profile;
            }
            catch
            {
                if (backUpCorrupt)
                {
                    try
                    {
                        string backup = Path.Combine(
                            Path.GetDirectoryName(path) ?? string.Empty,
                            "profile.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".xml");
                        File.Copy(path, backup, false);
                    }
                    catch
                    {
                    }
                }
                return new PetProfile();
            }
        }

        public void Save()
        {
            SaveTo(ProfilePath);
        }

        internal void SaveTo(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }
            XDocument document = new XDocument(
                new XElement(
                    "YabiProfile",
                    new XAttribute("version", "2"),
                    new XElement(
                        "Care",
                        new XElement("Fullness", Care.Fullness),
                        new XElement("Energy", Care.Energy),
                        new XElement("Mood", Care.Mood),
                        new XElement("FullnessMinutes", Format(Care.FullnessMinutes)),
                        new XElement("EnergyMinutes", Format(Care.EnergyMinutes)),
                        new XElement("SleepMinutes", Format(Care.SleepMinutes)),
                        new XElement("MoodMinutes", Format(Care.MoodMinutes)),
                        new XElement("AffinityMinutes", Format(Care.AffinityMinutes))),
                    new XElement(
                        "Growth",
                        new XElement("AffinityXp", Growth.AffinityXp),
                        new XElement("TotalActiveSeconds", Growth.TotalActiveSeconds),
                        new XElement("TodayActiveSeconds", Growth.TodayActiveSeconds),
                        new XElement("TotalSecondRemainder", Format(Growth.TotalSecondRemainder)),
                        new XElement("TodaySecondRemainder", Format(Growth.TodaySecondRemainder)),
                        new XElement("TotalInteractions", Growth.TotalInteractions),
                        new XElement("TotalFeedings", Growth.TotalFeedings),
                        new XElement("TotalPettings", Growth.TotalPettings),
                        new XElement("TotalRests", Growth.TotalRests),
                        new XElement("RemindersCompleted", Growth.RemindersCompleted),
                        new XElement("FocusSessionsCompleted", Growth.FocusSessionsCompleted),
                        new XElement("CurrentStreak", Growth.CurrentStreak),
                        new XElement("BestStreak", Growth.BestStreak),
                        new XElement("TodayDate", Growth.TodayDate ?? string.Empty),
                        new XElement("LastActiveDate", Growth.LastActiveDate ?? string.Empty)),
                    new XElement("Achievements", Growth.Achievements.OrderBy(id => id).Select(id => new XElement("Achievement", id))),
                    new XElement("LastFedUtc", Format(LastFedUtc)),
                    new XElement("LastPettedUtc", Format(LastPettedUtc)),
                    new XElement("LastRestedUtc", Format(LastRestedUtc)),
                    new XElement("LastLowNeedPromptUtc", Format(LastLowNeedPromptUtc))));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { document.Save(stream); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) { try { File.Delete(temporary); } catch { } } }
        }

        private static string ReadString(XElement root, string name, string fallback)
        {
            XElement item = root.Element(name);
            return item == null ? fallback : item.Value;
        }

        private static int ReadInt(XElement root, string name, int fallback, int minimum, int maximum)
        {
            int value;
            return int.TryParse(ReadString(root, name, string.Empty), out value)
                ? Math.Max(minimum, Math.Min(maximum, value))
                : fallback;
        }

        private static long ReadLong(XElement root, string name, long fallback)
        {
            long value;
            return long.TryParse(ReadString(root, name, string.Empty), out value) ? Math.Max(0, value) : fallback;
        }

        private static double ReadDouble(XElement root, string name, double fallback)
        {
            double value;
            return double.TryParse(ReadString(root, name, string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value)
                ? Math.Max(0.0, value)
                : fallback;
        }

        private static DateTime ReadDate(XElement root, string name)
        {
            DateTime value;
            return DateTime.TryParse(ReadString(root, name, string.Empty), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value)
                ? value.ToUniversalTime()
                : DateTime.MinValue;
        }

        private static double ReadRemainder(XElement root, string name, double interval)
        { double value = ReadDouble(root, name, 0); return value < interval ? value : 0; }
        private static string Format(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Format(DateTime value) { return value == DateTime.MinValue ? string.Empty : value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
    }

    internal sealed class CareActionResult
    {
        public bool Applied;
        public bool RequestAccepted { get { return Applied; } }
        public bool RewardGranted;
        public CareResultReason Reason;
        public string Message;
        public TimeSpan CooldownRemaining;
    }

    internal enum CareResultReason { Accepted, Cooldown, AlreadySleeping }

    internal sealed class CareController
    {
        private PetProfile _profile;
        private DateTime _lastSavedUtc = DateTime.MinValue;
        private readonly bool _persistenceEnabled;
        private readonly Func<DateTime> _localNow;
        private readonly Action<PetProfile> _saveProfile;
        public string SaveError { get; private set; }

        public CareController(PetProfile profile, bool persistenceEnabled)
            : this(profile, persistenceEnabled, delegate { return DateTime.Now; }, delegate(PetProfile p) { p.Save(); }) { }

        internal CareController(PetProfile profile, bool persistenceEnabled, Func<DateTime> localNow, Action<PetProfile> saveProfile)
        {
            _profile = profile ?? new PetProfile();
            _persistenceEnabled = persistenceEnabled;
            _localNow = localNow;
            _saveProfile = saveProfile;
            EnsureToday(_localNow());
            UpdateAchievements();
        }

        public PetProfile Profile { get { return _profile; } }

        public void Tick(TimeSpan elapsed, bool sleeping, DateTime utcNow, DateTime localNow)
        {
            double minutes = Math.Max(0.0, elapsed.TotalMinutes);
            if (minutes <= 0.0)
            {
                return;
            }
            AddActiveTime(elapsed, localNow);

            _profile.Care.FullnessMinutes += minutes;
            ApplyDecay(ref _profile.Care.Fullness, ref _profile.Care.FullnessMinutes, 6.0);

            if (sleeping)
            {
                _profile.Care.SleepMinutes += minutes;
                ApplyRecovery(ref _profile.Care.Energy, ref _profile.Care.SleepMinutes, 2.0);
            }
            else
            {
                _profile.Care.EnergyMinutes += minutes;
                ApplyDecay(ref _profile.Care.Energy, ref _profile.Care.EnergyMinutes, 8.0);
            }

            _profile.Care.MoodMinutes += minutes;
            ApplyDecay(ref _profile.Care.Mood, ref _profile.Care.MoodMinutes, 10.0);

            _profile.Care.AffinityMinutes += minutes;
            while (_profile.Care.AffinityMinutes >= 30.0)
            {
                _profile.Care.AffinityMinutes -= 30.0;
                AddAffinity(1);
            }

            if (_profile.Growth.TodayActiveSeconds >= 600)
            {
                MarkActiveDay(localNow.Date);
            }
            UpdateAchievements();
            if (utcNow < _lastSavedUtc || utcNow - _lastSavedUtc >= TimeSpan.FromMinutes(1))
            {
                Save();
                _lastSavedUtc = utcNow;
            }
        }

        public CareActionResult Feed(DateTime utcNow, DateTime localNow)
        {
            return ApplyCare(
                utcNow,
                localNow,
                _profile.LastFedUtc,
                TimeSpan.FromMinutes(20),
                delegate
                {
                    _profile.LastFedUtc = utcNow;
                    _profile.Care.Fullness = Math.Min(100, _profile.Care.Fullness + 25);
                    _profile.Growth.TotalFeedings++;
                    AddAffinity(5);
                },
                "亚比吃饱啦，谢谢你～",
                "亚比刚刚吃过，再等{0}分钟吧。" );
        }

        public CareActionResult Pet(DateTime utcNow, DateTime localNow)
        {
            return ApplyCare(
                utcNow,
                localNow,
                _profile.LastPettedUtc,
                TimeSpan.FromMinutes(5),
                delegate
                {
                    _profile.LastPettedUtc = utcNow;
                    _profile.Care.Mood = Math.Min(100, _profile.Care.Mood + 15);
                    _profile.Growth.TotalPettings++;
                    AddAffinity(2);
                },
                "亚比眯起眼睛，很喜欢被摸摸。",
                "亚比还在回味刚才的摸摸，再等{0}分钟吧。" );
        }

        public CareActionResult Rest(DateTime utcNow, DateTime localNow)
        { return Rest(utcNow, localNow, false); }

        public CareActionResult Rest(DateTime utcNow, DateTime localNow, bool alreadySleeping)
        {
            if (alreadySleeping) return new CareActionResult { Applied = true, Reason = CareResultReason.AlreadySleeping, Message = "亚比已经在安心休息了。" };
            CareActionResult result = ApplyCare(
                utcNow,
                localNow,
                _profile.LastRestedUtc,
                TimeSpan.FromMinutes(10),
                delegate
                {
                    _profile.LastRestedUtc = utcNow;
                    _profile.Growth.TotalRests++;
                    AddAffinity(3);
                },
                "亚比安心地休息一会儿。",
                "亚比刚休息过，再等{0}分钟吧。" );
            if (!result.Applied) { result.Applied = true; result.Message = "亚比安心地休息一会儿。休息奖励还在冷却中。"; }
            return result;
        }

        public void CompleteReminder(ReminderKind kind, DateTime localNow)
        {
            EnsureToday(localNow);
            _profile.Growth.RemindersCompleted++;
            if (kind == ReminderKind.Focus)
            {
                _profile.Growth.FocusSessionsCompleted++;
            }
            AddAffinity(3);
            MarkActiveDay(localNow.Date);
            UpdateAchievements();
            Save();
        }

        public string GetLowNeedMessage(DateTime utcNow)
        {
            if (Remaining(_profile.LastLowNeedPromptUtc, utcNow, TimeSpan.FromHours(1)) > TimeSpan.Zero)
            {
                return null;
            }
            string message = null;
            if (_profile.Care.Fullness < 30) message = "亚比有一点饿了，可以喂喂它。";
            else if (_profile.Care.Energy < 30) message = "亚比有点困，想休息一会儿。";
            else if (_profile.Care.Mood < 30) message = "亚比想要一个摸摸。";
            return message;
        }

        public void ConfirmLowNeedShown(DateTime utcNow) { _profile.LastLowNeedPromptUtc = utcNow; Save(); }

        public void Reset()
        {
            _profile = new PetProfile();
            EnsureToday(_localNow());
            Save();
        }

        public void Save()
        {
            if (!_persistenceEnabled)
            {
                return;
            }
            try { _saveProfile(_profile); SaveError = null; }
            catch { SaveError = "养成数据暂未保存，已保留上一份存档；稍后会自动重试。"; }
        }

        public void Export(string path)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("亚比桌宠 4.3.4 养成数据");
            text.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            text.AppendLine("等级：" + _profile.Growth.Level);
            text.AppendLine("亲密度经验：" + _profile.Growth.AffinityXp);
            text.AppendLine("饱腹：" + _profile.Care.Fullness);
            text.AppendLine("精力：" + _profile.Care.Energy);
            text.AppendLine("心情：" + _profile.Care.Mood);
            text.AppendLine("累计陪伴分钟：" + (_profile.Growth.TotalActiveSeconds / 60));
            text.AppendLine("互动次数：" + _profile.Growth.TotalInteractions);
            text.AppendLine("提醒完成：" + _profile.Growth.RemindersCompleted);
            text.AppendLine("连续天数：" + _profile.Growth.CurrentStreak);
            text.AppendLine("最佳连续：" + _profile.Growth.BestStreak);
            text.AppendLine("成就：" + string.Join("、", _profile.Growth.Achievements.OrderBy(id => id).ToArray()));
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
        }

        private CareActionResult ApplyCare(
            DateTime utcNow,
            DateTime localNow,
            DateTime lastUtc,
            TimeSpan cooldown,
            Action apply,
            string success,
            string waiting)
        {
            TimeSpan remaining = Remaining(lastUtc, utcNow, cooldown);
            if (lastUtc != DateTime.MinValue && remaining > TimeSpan.Zero)
            {
                return new CareActionResult
                {
                    Applied = false,
                    Reason = CareResultReason.Cooldown,
                    CooldownRemaining = remaining,
                    Message = string.Format(waiting, Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)))
                };
            }
            EnsureToday(localNow);
            apply();
            _profile.Growth.TotalInteractions++;
            MarkActiveDay(localNow.Date);
            UpdateAchievements();
            Save();
            return new CareActionResult { Applied = true, RewardGranted = true, Reason = CareResultReason.Accepted, Message = success };
        }

        private void AddAffinity(int amount)
        {
            _profile.Growth.AffinityXp = (int)Math.Max(0L, Math.Min(9800L, (long)_profile.Growth.AffinityXp + Math.Max(0, amount)));
        }

        private void EnsureToday(DateTime localNow)
        {
            string key = localNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(_profile.Growth.TodayDate, key, StringComparison.Ordinal))
            {
                _profile.Growth.TodayDate = key;
                _profile.Growth.TodayActiveSeconds = 0;
                _profile.Growth.TodaySecondRemainder = 0;
            }
            DateTime last;
            if (DateTime.TryParseExact(_profile.Growth.LastActiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out last)
                && last.Date < localNow.Date.AddDays(-1)) _profile.Growth.CurrentStreak = 0;
        }

        internal static TimeSpan Remaining(DateTime last, DateTime now, TimeSpan duration)
        {
            if (last == DateTime.MinValue) return TimeSpan.Zero;
            double seconds = duration.TotalSeconds - (now - last).TotalSeconds;
            return TimeSpan.FromSeconds(Math.Max(0, Math.Min(duration.TotalSeconds, seconds)));
        }

        private void AddActiveTime(TimeSpan elapsed, DateTime localNow)
        {
            GrowthState growth = _profile.Growth;
            AddSeconds(ref growth.TotalActiveSeconds, ref growth.TotalSecondRemainder, elapsed.TotalSeconds);
            DateTime cursor = localNow - elapsed;
            while (cursor < localNow)
            {
                DateTime end = cursor.Date.AddDays(1);
                if (end > localNow) end = localNow;
                EnsureToday(cursor);
                AddSeconds(ref growth.TodayActiveSeconds, ref growth.TodaySecondRemainder, (end - cursor).TotalSeconds);
                if (growth.TodayActiveSeconds >= 600) MarkActiveDay(cursor.Date);
                cursor = end;
            }
            EnsureToday(localNow);
        }

        private static void AddSeconds(ref long seconds, ref double remainder, double elapsed)
        {
            double total = remainder + elapsed;
            long whole = (long)Math.Floor(total + 1e-9);
            seconds += whole;
            remainder = Math.Max(0, total - whole);
        }

        public CareSnapshot GetSnapshot(DateTime utcNow, DateTime localNow)
        { EnsureToday(localNow); return new CareSnapshot(_profile, SaveError, utcNow); }

        private void MarkActiveDay(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.Equals(_profile.Growth.LastActiveDate, key, StringComparison.Ordinal))
            {
                return;
            }
            DateTime previous;
            if (DateTime.TryParseExact(_profile.Growth.LastActiveDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out previous)
                && previous.Date == date.AddDays(-1))
            {
                _profile.Growth.CurrentStreak++;
            }
            else
            {
                _profile.Growth.CurrentStreak = 1;
            }
            _profile.Growth.LastActiveDate = key;
            _profile.Growth.BestStreak = Math.Max(_profile.Growth.BestStreak, _profile.Growth.CurrentStreak);
        }

        private void UpdateAchievements()
        {
            GrowthState growth = _profile.Growth;
            if (growth.TotalInteractions >= 1) growth.Achievements.Add("初次相遇");
            if (growth.TotalActiveSeconds >= 3600) growth.Achievements.Add("陪伴一小时");
            if (growth.TotalInteractions >= 10) growth.Achievements.Add("贴心照顾");
            if (growth.RemindersCompleted >= 10) growth.Achievements.Add("规律生活");
            if (growth.BestStreak >= 7) growth.Achievements.Add("七日相伴");
            if (growth.Level >= 5) growth.Achievements.Add("亲密伙伴");
        }

        private static void ApplyDecay(ref int value, ref double minutes, double interval)
        {
            while (minutes >= interval)
            {
                minutes -= interval;
                value = Math.Max(0, value - 1);
            }
        }

        private static void ApplyRecovery(ref int value, ref double minutes, double interval)
        {
            while (minutes >= interval)
            {
                minutes -= interval;
                value = Math.Min(100, value + 1);
            }
        }

        internal static IEnumerable<string> RunSelfTests()
        {
            List<string> failures = new List<string>();
            PetProfile profile = new PetProfile();
            CareController controller = new CareController(profile, false);
            DateTime now = new DateTime(2026, 8, 19, 1, 0, 0, DateTimeKind.Utc);
            controller.Tick(TimeSpan.FromMinutes(12), false, now, now.ToLocalTime());
            if (profile.Care.Fullness != 78 || profile.Care.Energy != 79 || profile.Care.Mood != 79)
            {
                failures.Add("care decay intervals are incorrect");
            }
            CareActionResult fed = controller.Feed(now, now.ToLocalTime());
            CareActionResult blocked = controller.Feed(now.AddMinutes(1), now.AddMinutes(1).ToLocalTime());
            if (!fed.Applied || blocked.Applied || profile.Care.Fullness != 100)
            {
                failures.Add("feeding or cooldown behavior is incorrect");
            }
            int energy = profile.Care.Energy;
            profile.Care.EnergyMinutes = 0.0;
            controller.Tick(TimeSpan.FromMinutes(4), true, now.AddMinutes(4), now.AddMinutes(4).ToLocalTime());
            if (profile.Care.Energy != energy + 2)
            {
                failures.Add("sleep energy recovery is incorrect");
            }
            string folder = Path.Combine(Path.GetTempPath(), "YabiDesktopPet-v4.2-profile-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "profile.xml");
            try
            {
                profile.SaveTo(path);
                PetProfile loaded = PetProfile.LoadFrom(path, false);
                if (loaded.Care.Fullness != profile.Care.Fullness || loaded.Growth.TotalInteractions != profile.Growth.TotalInteractions)
                {
                    failures.Add("profile round-trip did not preserve care data");
                }
                File.WriteAllText(path, "not xml");
                PetProfile recovered = PetProfile.LoadFrom(path, true);
                if (recovered.Care.Fullness != 80 || Directory.GetFiles(folder, "profile.corrupt-*.xml").Length != 1)
                {
                    failures.Add("corrupt profile was not backed up and recovered");
                }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                }
                catch
                {
                }
            }
            return failures;
        }
    }
}
