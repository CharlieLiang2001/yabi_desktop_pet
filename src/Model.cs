using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace YabiDesktopPet
{
    internal enum PetPose
    {
        Standing,
        Transitioning,
        Sitting,
        Sleeping
    }

    internal enum PetAction
    {
        Stand,
        Blink,
        Sit,
        LookAround,
        Sleep,
        Wake
    }

    internal enum PetCommand
    {
        None,
        Blink,
        Sit,
        LookAround,
        Sleep,
        Wake
    }

    internal enum BehaviorMode
    {
        Companion,
        Quiet
    }

    internal enum InteractionFrequency
    {
        Calm,
        Normal,
        Lively
    }

    internal enum PowerMode
    {
        Automatic,
        Standard,
        Eco
    }

    internal enum ReminderKind
    {
        Water,
        Stretch,
        Focus,
        Custom
    }

    internal enum MouseLookDirection
    {
        Center,
        LeftNear,
        LeftUp,
        LeftSide,
        RightNear,
        RightUp,
        RightSide
    }

    internal sealed class ClipDefinition
    {
        public string Id;
        public string Path;
        public int Fps;
        public int FrameCount;
        public PetPose StartPose;
        public PetPose EndPose;
        public string Mode;
        public string DisplayName;
        public double DurationMs;
        public int FrameOffset;
        public string Trigger;
        public int CooldownMs;
        public int Priority;
        public string InterruptPolicy;
        public bool Optional;
        public double[] FrameTimesMs;

        public int FrameAt(double elapsedMs, bool reverse)
        {
            if (FrameTimesMs == null || FrameTimesMs.Length != FrameCount)
                return PlaybackTimeline.FrameAt(elapsedMs, DurationMs, FrameCount, reverse);
            double sourceTime = reverse ? Math.Max(0, DurationMs - elapsedMs - 0.001) : Math.Max(0, elapsedMs);
            int found = Array.BinarySearch(FrameTimesMs, sourceTime);
            return Math.Max(0, Math.Min(FrameCount - 1, found >= 0 ? found : ~found - 1));
        }
    }

    internal sealed class ReminderDefinition
    {
        public string Id;
        public string Name;
        public string Message;
        public int IntervalMinutes;
        public bool Enabled;
        public ReminderKind Kind;

        public ReminderDefinition Clone()
        {
            return (ReminderDefinition)MemberwiseClone();
        }
    }

    internal sealed class PetSettings
    {
        internal static string LastSaveError;
        public double Left = double.NaN;
        public double Top = double.NaN;
        public double Scale = 1.0;
        public double Opacity = 1.0;
        public bool ManualMirror;
        public bool MouseLookEnabled = true;
        public bool FocusDoNotDisturb = true;
        public readonly List<string> DisabledAutomaticClips = new List<string>();
        public BehaviorMode Behavior = BehaviorMode.Companion;
        public bool StartWithWindows;
        public bool LockPosition;
        public bool EdgeSnap = true;
        public bool AlwaysOnTop = true;
        public InteractionFrequency InteractionFrequency = InteractionFrequency.Normal;
        public PowerMode PowerMode = PowerMode.Automatic;
        public bool WaterReminderEnabled;
        public int WaterReminderMinutes = 45;
        public bool StretchReminderEnabled;
        public int StretchReminderMinutes = 60;
        public int FocusMinutes = 25;
        public string MonitorDevice = string.Empty;
        public double MonitorRelativeLeft = double.NaN;
        public double MonitorRelativeTop = double.NaN;
        public readonly List<string> CustomPhrases = new List<string>();
        public readonly List<ReminderDefinition> CustomReminders = new List<ReminderDefinition>();

        internal static readonly string[] DefaultPhrases =
        {
            "亚比一直在这里陪你。",
            "休息一下，看看远处吧～",
            "今天也要慢慢来。",
            "摸摸亚比，心情会变好。",
            "记得喝水呀。",
            "喵～"
        };

        internal static string SettingsPath
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "YabiDesktopPet");
                return Path.Combine(folder, "settings.xml");
            }
        }

        public static PetSettings Load()
        {
            return LoadFrom(SettingsPath);
        }

        internal static PetSettings LoadFrom(string path)
        {
            PetSettings settings = new PetSettings();
            try
            {
                if (!File.Exists(path))
                {
                    return settings;
                }

                XElement root = XDocument.Load(path).Root;
                if (root == null)
                {
                    return settings;
                }

                settings.Left = ReadDouble(root, "Left", double.NaN);
                settings.Top = ReadDouble(root, "Top", double.NaN);
                settings.Scale = Clamp(ReadDouble(root, "Scale", 1.0), 0.75, 1.35);
                settings.Opacity = Clamp(ReadDouble(root, "Opacity", 1.0), 0.60, 1.0);
                settings.ManualMirror = ReadBool(root, "ManualMirror", false);
                settings.MouseLookEnabled = ReadBool(root, "MouseLookEnabled", true);
                settings.FocusDoNotDisturb = ReadBool(root, "FocusDoNotDisturb", true);
                XElement disabledClips = root.Element("DisabledAutomaticClips");
                if (disabledClips != null)
                {
                    foreach (XElement item in disabledClips.Elements("Clip"))
                    {
                        string id = NormalizeText(item.Value, 64);
                        if (id.Length > 0 && !settings.DisabledAutomaticClips.Contains(id))
                            settings.DisabledAutomaticClips.Add(id);
                    }
                }
                settings.StartWithWindows = ReadBool(root, "StartWithWindows", false);
                settings.LockPosition = ReadBool(root, "LockPosition", false);
                settings.EdgeSnap = ReadBool(root, "EdgeSnap", true);
                settings.AlwaysOnTop = ReadBool(root, "AlwaysOnTop", true);
                settings.InteractionFrequency = ReadEnum(
                    root,
                    "InteractionFrequency",
                    InteractionFrequency.Normal);
                settings.PowerMode = ReadEnum(root, "PowerMode", PowerMode.Automatic);
                settings.WaterReminderEnabled = ReadBool(root, "WaterReminderEnabled", false);
                settings.WaterReminderMinutes = ClampInt(ReadInt(root, "WaterReminderMinutes", 45), 5, 240);
                settings.StretchReminderEnabled = ReadBool(root, "StretchReminderEnabled", false);
                settings.StretchReminderMinutes = ClampInt(ReadInt(root, "StretchReminderMinutes", 60), 5, 240);
                settings.FocusMinutes = ClampInt(ReadInt(root, "FocusMinutes", 25), 5, 240);
                settings.MonitorDevice = ReadString(root, "MonitorDevice", string.Empty);
                settings.MonitorRelativeLeft = Clamp(ReadDouble(root, "MonitorRelativeLeft", double.NaN), 0.0, 1.0);
                settings.MonitorRelativeTop = Clamp(ReadDouble(root, "MonitorRelativeTop", double.NaN), 0.0, 1.0);

                XElement phrases = root.Element("CustomPhrases");
                if (phrases != null)
                {
                    foreach (XElement phrase in phrases.Elements("Phrase"))
                    {
                        string value = NormalizeText(phrase.Value, 80);
                        if (value.Length > 0 && settings.CustomPhrases.Count < 50)
                        {
                            settings.CustomPhrases.Add(value);
                        }
                    }
                }

                XElement reminders = root.Element("CustomReminders");
                if (reminders != null)
                {
                    foreach (XElement item in reminders.Elements("Reminder"))
                    {
                        if (settings.CustomReminders.Count >= 5)
                        {
                            break;
                        }
                        string id = ReadAttribute(item, "id", Guid.NewGuid().ToString("N"));
                        string name = NormalizeText(ReadAttribute(item, "name", "自定义提醒"), 20);
                        string message = NormalizeText(ReadAttribute(item, "message", "休息一下吧。"), 80);
                        settings.CustomReminders.Add(new ReminderDefinition
                        {
                            Id = id,
                            Name = name.Length == 0 ? "自定义提醒" : name,
                            Message = message.Length == 0 ? "休息一下吧。" : message,
                            IntervalMinutes = ClampInt(ReadAttributeInt(item, "minutes", 30), 5, 240),
                            Enabled = ReadAttributeBool(item, "enabled", false),
                            Kind = ReminderKind.Custom
                        });
                    }
                }

                string oldBehavior = ReadString(root, "Behavior", "Companion");
                settings.Behavior = NormalizeBehavior(oldBehavior);

                // Version 3 exposed AutoSwitch separately.  An explicitly
                // disabled companion becomes the closest 4.0 mode: quiet.
                if (string.Equals(oldBehavior, "Companion", StringComparison.OrdinalIgnoreCase)
                    && !ReadBool(root, "AutoSwitch", true))
                {
                    settings.Behavior = BehaviorMode.Quiet;
                }
            }
            catch
            {
                // A damaged or older settings file must never block startup.
            }

            return settings;
        }

        public void Save()
        {
            SaveTo(SettingsPath);
        }

        internal void SaveTo(string path)
        {
            LastSaveError = null;
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                XDocument document = new XDocument(
                    new XElement(
                        "YabiSettings",
                        new XAttribute("version", "4.3"),
                        new XElement("Left", Left.ToString(CultureInfo.InvariantCulture)),
                        new XElement("Top", Top.ToString(CultureInfo.InvariantCulture)),
                        new XElement("Scale", Scale.ToString(CultureInfo.InvariantCulture)),
                        new XElement("Opacity", Opacity.ToString(CultureInfo.InvariantCulture)),
                        new XElement("ManualMirror", ManualMirror),
                        new XElement("MouseLookEnabled", MouseLookEnabled),
                        new XElement("FocusDoNotDisturb", FocusDoNotDisturb),
                        new XElement("DisabledAutomaticClips", BuildDisabledClipElements()),
                        new XElement("StartWithWindows", StartWithWindows),
                        new XElement("LockPosition", LockPosition),
                        new XElement("EdgeSnap", EdgeSnap),
                        new XElement("AlwaysOnTop", AlwaysOnTop),
                        new XElement("InteractionFrequency", InteractionFrequency.ToString()),
                        new XElement("PowerMode", PowerMode.ToString()),
                        new XElement("WaterReminderEnabled", WaterReminderEnabled),
                        new XElement("WaterReminderMinutes", WaterReminderMinutes),
                        new XElement("StretchReminderEnabled", StretchReminderEnabled),
                        new XElement("StretchReminderMinutes", StretchReminderMinutes),
                        new XElement("FocusMinutes", FocusMinutes),
                        new XElement("MonitorDevice", MonitorDevice ?? string.Empty),
                        new XElement("MonitorRelativeLeft", MonitorRelativeLeft.ToString(CultureInfo.InvariantCulture)),
                        new XElement("MonitorRelativeTop", MonitorRelativeTop.ToString(CultureInfo.InvariantCulture)),
                        new XElement("CustomPhrases", BuildPhraseElements()),
                        new XElement("CustomReminders", BuildReminderElements()),
                        new XElement("AutoSwitch", Behavior == BehaviorMode.Companion),
                        new XElement("Behavior", Behavior.ToString())));
                string temporary = path + ".tmp";
                document.Save(temporary);
                if (File.Exists(path))
                {
                    string backup = path + ".bak";
                    try
                    {
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Replace(temporary, path, backup);
                        if (File.Exists(backup)) File.Delete(backup);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        File.Copy(temporary, path, true);
                        File.Delete(temporary);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporary, path, true);
                        File.Delete(temporary);
                    }
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch (Exception exception)
            {
                // Settings persistence is optional; the pet remains usable.
                LastSaveError = exception.ToString();
                try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
            }
        }

        internal static BehaviorMode NormalizeBehavior(string value)
        {
            if (string.Equals(value, "Quiet", StringComparison.OrdinalIgnoreCase))
            {
                return BehaviorMode.Quiet;
            }

            // Companion, Wander, FollowMouse and unknown legacy values all
            // migrate to stationary automatic companionship.
            return BehaviorMode.Companion;
        }

        private static string ReadString(XElement root, string name, string fallback)
        {
            XElement element = root.Element(name);
            return element == null ? fallback : element.Value;
        }

        private static double ReadDouble(XElement root, string name, double fallback)
        {
            double value;
            return double.TryParse(
                ReadString(root, name, string.Empty),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value)
                ? value
                : fallback;
        }

        private static bool ReadBool(XElement root, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(ReadString(root, name, string.Empty), out value) ? value : fallback;
        }

        private IEnumerable<XElement> BuildPhraseElements()
        {
            int count = 0;
            foreach (string raw in CustomPhrases)
            {
                string value = NormalizeText(raw, 80);
                if (value.Length > 0 && count++ < 50)
                {
                    yield return new XElement("Phrase", value);
                }
            }
        }

        private IEnumerable<XElement> BuildDisabledClipElements()
        {
            foreach (string id in DisabledAutomaticClips)
                yield return new XElement("Clip", NormalizeText(id, 64));
        }

        private IEnumerable<XElement> BuildReminderElements()
        {
            int count = 0;
            foreach (ReminderDefinition reminder in CustomReminders)
            {
                if (reminder == null || count++ >= 5)
                {
                    continue;
                }
                yield return new XElement(
                    "Reminder",
                    new XAttribute("id", string.IsNullOrEmpty(reminder.Id) ? Guid.NewGuid().ToString("N") : reminder.Id),
                    new XAttribute("name", NormalizeText(reminder.Name, 20)),
                    new XAttribute("message", NormalizeText(reminder.Message, 80)),
                    new XAttribute("minutes", ClampInt(reminder.IntervalMinutes, 5, 240)),
                    new XAttribute("enabled", reminder.Enabled));
            }
        }

        private static int ReadInt(XElement root, string name, int fallback)
        {
            int value;
            return int.TryParse(ReadString(root, name, string.Empty), out value) ? value : fallback;
        }

        private static T ReadEnum<T>(XElement root, string name, T fallback) where T : struct
        {
            T value;
            return Enum.TryParse(ReadString(root, name, string.Empty), true, out value) ? value : fallback;
        }

        private static string ReadAttribute(XElement element, string name, string fallback)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? fallback : attribute.Value;
        }

        private static int ReadAttributeInt(XElement element, string name, int fallback)
        {
            int value;
            return int.TryParse(ReadAttribute(element, name, string.Empty), out value) ? value : fallback;
        }

        private static bool ReadAttributeBool(XElement element, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(ReadAttribute(element, name, string.Empty), out value) ? value : fallback;
        }

        internal static string NormalizeText(string value, int maximumLength)
        {
            value = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        private static int ClampInt(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value))
            {
                return value;
            }
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
