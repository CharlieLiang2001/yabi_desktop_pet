using System;
using System.Linq;

namespace YabiDesktopPet
{
    internal sealed class CareSnapshot
    {
        public int Fullness { get; private set; }
        public int Energy { get; private set; }
        public int Mood { get; private set; }
        public int Level { get; private set; }
        public int AffinityXp { get; private set; }
        public int AffinityProgress { get; private set; }
        public long TodayActiveSeconds { get; private set; }
        public long TotalActiveSeconds { get; private set; }
        public int CurrentStreak { get; private set; }
        public int BestStreak { get; private set; }
        public int TotalInteractions { get; private set; }
        public int RemindersCompleted { get; private set; }
        public string SaveError { get; private set; }
        public TimeSpan FeedCooldown { get; private set; }
        public TimeSpan PetCooldown { get; private set; }
        private readonly string[] _achievements;
        public string[] Achievements { get { return (string[])_achievements.Clone(); } }

        public CareSnapshot(PetProfile profile, string error, DateTime utcNow)
        {
            Fullness = profile.Care.Fullness; Energy = profile.Care.Energy; Mood = profile.Care.Mood;
            GrowthState g = profile.Growth;
            Level = g.Level; AffinityXp = g.AffinityXp; AffinityProgress = Level >= 99 ? 100 : AffinityXp % 100;
            TodayActiveSeconds = g.TodayActiveSeconds; TotalActiveSeconds = g.TotalActiveSeconds;
            CurrentStreak = g.CurrentStreak; BestStreak = g.BestStreak;
            TotalInteractions = g.TotalInteractions; RemindersCompleted = g.RemindersCompleted;
            SaveError = error;
            FeedCooldown = CareController.Remaining(profile.LastFedUtc, utcNow, TimeSpan.FromMinutes(20));
            PetCooldown = CareController.Remaining(profile.LastPettedUtc, utcNow, TimeSpan.FromMinutes(5));
            _achievements = g.Achievements.OrderBy(x => x).ToArray();
        }
    }
}
