using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace YabiDesktopPet
{
    /// <summary>
    /// Indexes the embedded action ZIP once and decodes frames on demand.  The
    /// dynamic LRU is deliberately small; seven mouse-look frames and pose
    /// stills live in a separate resident cache.
    /// </summary>
    internal sealed partial class AnimationCatalog : IDisposable
    {
        public const string ResourceName = "Yabi.Assets.Animations.v4.zip";
        private const int DynamicCacheCapacity = 12;

        private readonly Dispatcher _dispatcher;
        private readonly Stream _resourceStream;
        private readonly ZipArchive _archive;
        private readonly object _archiveLock = new object();
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, ZipArchiveEntry> _entriesByPath;
        private readonly Dictionary<string, ClipDefinition> _clips;
        private readonly Dictionary<string, List<ZipArchiveEntry>> _clipEntries;
        private readonly Dictionary<MouseLookDirection, string> _lookPaths;
        private readonly Dictionary<string, BitmapSource> _residentCache;
        private readonly Dictionary<string, BitmapSource> _dynamicCache;
        private readonly Dictionary<string, LinkedListNode<string>> _lruNodes;
        private readonly LinkedList<string> _lru;
        private readonly HashSet<string> _pending;
        private readonly Dictionary<string, List<Action<BitmapSource>>> _waiters;
        private bool _disposed;
        private int _requestGeneration;

        public AnimationCatalog(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _entriesByPath = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            _clips = new Dictionary<string, ClipDefinition>(StringComparer.OrdinalIgnoreCase);
            _clipEntries = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
            _lookPaths = new Dictionary<MouseLookDirection, string>();
            _residentCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            _dynamicCache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            _lruNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
            _lru = new LinkedList<string>();
            _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _waiters = new Dictionary<string, List<Action<BitmapSource>>>(StringComparer.OrdinalIgnoreCase);

            _resourceStream = EmbeddedAssetStream.Open();
            if (_resourceStream == null)
            {
                throw new InvalidOperationException("找不到内置的亚比动作包。");
            }
            _archive = new ZipArchive(_resourceStream, ZipArchiveMode.Read, false);
            foreach (ZipArchiveEntry entry in _archive.Entries)
            {
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    _entriesByPath[NormalisePath(entry.FullName)] = entry;
                }
            }
            ParseManifest();
            IndexClips();
        }

        public string Version { get; private set; }
        public int PortraitWidth { get; private set; }
        public int PortraitHeight { get; private set; }
        public int SleepWidth { get; private set; }
        public int SleepHeight { get; private set; }
        public string LastDecodeError { get; private set; }
        public double HeadX { get; private set; }
        public double HeadY { get; private set; }
        public double BodyHeight { get; private set; }
        public IEnumerable<ClipDefinition> Clips { get { return _clips.Values; } }
        internal string TestFailClipId;

        public ClipDefinition GetClip(string clipId)
        {
            ClipDefinition definition;
            if (!_clips.TryGetValue(clipId, out definition))
            {
                throw new ArgumentException("未知动作：" + clipId, "clipId");
            }
            return definition;
        }

        public bool HasClip(string clipId)
        {
            return !string.IsNullOrEmpty(clipId) && _clips.ContainsKey(clipId);
        }

        public BitmapSource LoadResidentFrame(string clipId, int index)
        {
            string key = FrameKey(clipId, index);
            BitmapSource cached;
            lock (_cacheLock)
            {
                if (_residentCache.TryGetValue(key, out cached))
                {
                    return cached;
                }
            }
            BitmapSource bitmap = DecodeFrame(clipId, index);
            lock (_cacheLock)
            {
                _residentCache[key] = bitmap;
            }
            return bitmap;
        }

        public void RequestFrame(string clipId, int index, Action<BitmapSource> callback)
        {
            ClipDefinition definition = GetClip(clipId);
            int safeIndex = Math.Max(0, Math.Min(definition.FrameCount - 1, index));
            string key = FrameKey(clipId, safeIndex);
            List<ZipArchiveEntry> entries = _clipEntries[clipId];
            RequestEntry(key, entries[safeIndex], false, callback);
        }

        public void RequestFrameResult(string clipId, int index, Action<FrameLoadResult> callback)
        {
            if (clipId == TestFailClipId)
            {
                if (callback != null) callback(new FrameLoadResult { Error = "测试注入：帧解码失败" });
                return;
            }
            try
            {
                RequestPixels(clipId, index, callback);
            }
            catch (Exception exception)
            {
                LastDecodeError = exception.Message;
                if (callback != null) callback(new FrameLoadResult { Error = exception.Message });
            }
        }

        public void CancelPendingRequests()
        {
            List<Action<BitmapSource>> cancelled = new List<Action<BitmapSource>>();
            List<Action<FrameLoadResult>> cancelledPixels = new List<Action<FrameLoadResult>>();
            lock (_cacheLock)
            {
                _requestGeneration++;
                foreach (List<Action<BitmapSource>> callbacks in _waiters.Values) cancelled.AddRange(callbacks);
                _waiters.Clear();
                _pending.Clear();
                foreach (List<Action<FrameLoadResult>> callbacks in _pixelPending.Values) cancelledPixels.AddRange(callbacks);
                _pixelPending.Clear();
            }
            foreach (Action<BitmapSource> callback in cancelled) Dispatch(callback, null);
            foreach (Action<FrameLoadResult> callback in cancelledPixels) DispatchPixels(callback, new FrameLoadResult { Cancelled = true });
        }

        public void PrefetchFrames(string clipId, int startIndex, int direction, int count)
        {
            ClipDefinition clip = GetClip(clipId);
            for (int offset = 0; offset < count; offset++)
            {
                int index = startIndex + direction * offset;
                if (index < 0 || index >= clip.FrameCount)
                {
                    break;
                }
                RequestFrameResult(clipId, index, null);
            }
        }

        public void WarmMouseLookFrames()
        {
            foreach (KeyValuePair<MouseLookDirection, string> pair in _lookPaths)
            {
                ZipArchiveEntry entry;
                if (_entriesByPath.TryGetValue(pair.Value, out entry))
                {
                    RequestEntry(LookKey(pair.Key), entry, true, null);
                }
            }
        }

        public void WarmPoseFrames()
        {
            string[] ids = { "sit_idle", "sleep_idle" };
            foreach (string id in ids)
            {
                List<ZipArchiveEntry> entries;
                if (_clipEntries.TryGetValue(id, out entries) && entries.Count > 0)
                {
                    RequestEntry(FrameKey(id, 0), entries[0], true, null);
                }
            }
        }

        public void RequestMouseLook(MouseLookDirection direction, Action<BitmapSource> callback)
        {
            string path;
            if (!_lookPaths.TryGetValue(direction, out path))
            {
                direction = MouseLookDirection.Center;
                path = _lookPaths[direction];
            }
            ZipArchiveEntry entry = _entriesByPath[path];
            RequestEntry(LookKey(direction), entry, true, callback);
        }

        public int DynamicCacheCount
        {
            get
            {
                lock (_cacheLock)
                {
                    return _dynamicCache.Count + _pixelCache.Count;
                }
            }
        }

        public int ResidentCacheCount
        {
            get
            {
                lock (_cacheLock)
                {
                    return _residentCache.Count;
                }
            }
        }

        public void TrimDynamicCache(int maximum)
        {
            int safeMaximum = Math.Max(0, Math.Min(DynamicCacheCapacity, maximum));
            lock (_cacheLock)
            {
                while (_dynamicCache.Count > safeMaximum)
                {
                    LinkedListNode<string> oldest = _lru.First;
                    if (oldest == null)
                    {
                        break;
                    }
                    _lru.RemoveFirst();
                    _lruNodes.Remove(oldest.Value);
                    _dynamicCache.Remove(oldest.Value);
                }
                while (_pixelLru.Count > safeMaximum)
                {
                    string key = _pixelLru.First.Value;
                    _pixelLru.RemoveFirst();
                    _pixelCache.Remove(key);
                }
            }
        }

        public IList<string> ValidateEmbeddedPack()
        {
            List<string> failures = new List<string>();
            string[] required =
            {
                "stand_idle", "stand_blink", "stand_to_sit",
                "sit_idle", "sit_lookaround", "sit_to_sleep",
                "sleep_to_sit", "sleep_idle"
            };
            foreach (string id in required)
            {
                ClipDefinition clip;
                if (!_clips.TryGetValue(id, out clip))
                {
                    failures.Add("missing clip " + id);
                    continue;
                }
                if (!_clipEntries.ContainsKey(id) || _clipEntries[id].Count != clip.FrameCount)
                {
                    failures.Add("frame count mismatch " + id);
                }
            }
            if (Version == "4.2" && _lookPaths.Count != 7)
            {
                failures.Add("mouse look direction count is not 7");
            }
            if (Version != "4.2" && Version != "4.3")
            {
                failures.Add("unsupported asset version " + Version);
            }
            if (PortraitWidth != 320 || PortraitHeight != 480 || SleepWidth != 480 || SleepHeight != 320)
            {
                failures.Add("canvas dimensions are unexpected");
            }

            try
            {
                BitmapSource stand = LoadResidentFrame("stand_idle", 0);
                BitmapSource sleep = LoadResidentFrame("sleep_idle", 0);
                if (stand.PixelWidth != PortraitWidth || stand.PixelHeight != PortraitHeight)
                {
                    failures.Add("stand frame dimensions mismatch");
                }
                if (sleep.PixelWidth != SleepWidth || sleep.PixelHeight != SleepHeight)
                {
                    failures.Add("sleep frame dimensions mismatch");
                }
                if (!HasTransparentCorners(stand) || !HasTransparentCorners(sleep))
                {
                    failures.Add("static frame corner is not transparent");
                }
                DecodeFrame("stand_blink", GetClip("stand_blink").FrameCount - 1);
                DecodeFrame("stand_to_sit", GetClip("stand_to_sit").FrameCount - 1);
                DecodeFrame("sit_lookaround", GetClip("sit_lookaround").FrameCount - 1);
                DecodeFrame("sit_to_sleep", GetClip("sit_to_sleep").FrameCount - 1);
                DecodeFrame("sleep_to_sit", GetClip("sleep_to_sit").FrameCount - 1);
                if (Version == "4.3")
                {
                    foreach (string id in new[] { "feed", "pet", "groom", "scratch", "notice_all", "notice_left", "notice_right", "notice_up" })
                    {
                        if (!HasClip(id)) { failures.Add("missing 4.3 clip " + id); continue; }
                        ClipDefinition clip = GetClip(id);
                        BitmapSource first = DecodeFrame(id, 0);
                        BitmapSource last = DecodeFrame(id, clip.FrameCount - 1);
                        if (!HasTransparentCorners(first) || !HasTransparentCorners(last)) failures.Add("opaque corners " + id);
                        if (clip.StartPose != PetPose.Sitting || clip.EndPose != PetPose.Sitting || clip.DurationMs <= 0)
                            failures.Add("invalid pose/duration " + id);
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add("decode validation: " + exception.Message);
            }
            return failures;
        }

        private void ParseManifest()
        {
            ZipArchiveEntry manifestEntry;
            if (!_entriesByPath.TryGetValue("manifest.xml", out manifestEntry))
            {
                throw new InvalidOperationException("动作包缺少 manifest.xml。");
            }
            XDocument document;
            lock (_archiveLock)
            {
                using (Stream stream = manifestEntry.Open())
                {
                    document = XDocument.Load(stream);
                }
            }
            XElement root = document.Root;
            if (root == null || root.Name.LocalName != "yabiAssets")
            {
                throw new InvalidOperationException("动作清单格式无效。");
            }
            Version = ReadAttribute(root, "version", string.Empty);
            PortraitWidth = ReadInt(root, "portraitWidth", 320);
            PortraitHeight = ReadInt(root, "portraitHeight", 480);
            SleepWidth = ReadInt(root, "sleepWidth", 480);
            SleepHeight = ReadInt(root, "sleepHeight", 320);
            HeadX = ReadDouble(root, "headX", 0.5);
            HeadY = ReadDouble(root, "headY", 0.25);
            BodyHeight = ReadDouble(root, "bodyHeight", 0.90);

            foreach (XElement element in root.Elements("clip"))
            {
                ClipDefinition definition = new ClipDefinition();
                definition.Id = ReadAttribute(element, "id", string.Empty);
                definition.Path = NormalisePath(ReadAttribute(element, "path", string.Empty));
                definition.Fps = ReadInt(element, "fps", 1);
                definition.FrameCount = ReadInt(element, "frames", 0);
                definition.StartPose = ParsePose(ReadAttribute(element, "startPose", "Standing"));
                definition.EndPose = ParsePose(ReadAttribute(element, "endPose", "Standing"));
                definition.Mode = ReadAttribute(element, "mode", "once");
                definition.DisplayName = ReadAttribute(element, "displayName", definition.Id);
                definition.DurationMs = ReadDouble(element, "durationMs", definition.FrameCount * 1000.0 / Math.Max(1, definition.Fps));
                definition.FrameOffset = ReadInt(element, "frameOffset", 0);
                definition.Trigger = ReadAttribute(element, "trigger", "manual");
                definition.CooldownMs = ReadInt(element, "cooldownMs", 0);
                definition.Priority = ReadInt(element, "priority", 0);
                definition.InterruptPolicy = ReadAttribute(element, "interruptPolicy", "complete");
                definition.Optional = ReadAttribute(element, "optional", "false") == "true";
                XElement timing = element.Element("timing");
                if (timing != null)
                {
                    string[] values = ReadAttribute(timing, "ms", string.Empty).Split(',');
                    definition.FrameTimesMs = new double[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        double value;
                        if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                            || double.IsNaN(value) || double.IsInfinity(value) || value < 0
                            || (i > 0 && value <= definition.FrameTimesMs[i - 1]))
                            throw new InvalidOperationException("动作帧时间戳无效：" + definition.Id);
                        definition.FrameTimesMs[i] = value;
                    }
                    if (values.Length != definition.FrameCount || definition.FrameTimesMs[0] != 0
                        || definition.FrameTimesMs[values.Length - 1] >= definition.DurationMs)
                        throw new InvalidOperationException("动作时间戳与时长不匹配：" + definition.Id);
                }
                if (string.IsNullOrEmpty(definition.Id) || definition.FrameCount <= 0 || definition.FrameOffset < 0
                    || definition.Fps <= 0 || !IsFinitePositive(definition.DurationMs))
                {
                    throw new InvalidOperationException("动作清单包含无效片段。");
                }
                _clips[definition.Id] = definition;
            }

            XElement look = root.Element("mouseLook");
            if (look != null)
            {
                foreach (XElement direction in look.Elements("direction"))
                {
                    MouseLookDirection parsed = ParseLookDirection(ReadAttribute(direction, "id", "center"));
                    _lookPaths[parsed] = NormalisePath(ReadAttribute(direction, "path", string.Empty));
                }
            }
        }

        private void IndexClips()
        {
            foreach (ClipDefinition definition in _clips.Values)
            {
                string prefix = definition.Path.TrimEnd('/') + "/";
                List<ZipArchiveEntry> entries = new List<ZipArchiveEntry>();
                foreach (KeyValuePair<string, ZipArchiveEntry> pair in _entriesByPath)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && pair.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(pair.Value);
                    }
                }
                entries.Sort(delegate(ZipArchiveEntry left, ZipArchiveEntry right)
                {
                    return string.CompareOrdinal(left.FullName, right.FullName);
                });
                if (entries.Count < definition.FrameOffset + definition.FrameCount)
                {
                    throw new InvalidOperationException(
                        string.Format("动作 {0} 帧数错误：清单 {1}，实际 {2}。", definition.Id, definition.FrameCount, entries.Count));
                }
                _clipEntries[definition.Id] = entries.GetRange(definition.FrameOffset, definition.FrameCount);
            }
        }

        private BitmapSource DecodeFrame(string clipId, int index)
        {
            ClipDefinition definition = GetClip(clipId);
            int safeIndex = Math.Max(0, Math.Min(definition.FrameCount - 1, index));
            return DecodeEntry(_clipEntries[clipId][safeIndex]);
        }

        private BitmapSource DecodeEntry(ZipArchiveEntry entry)
        {
            FramePixels pixels = DecodePixels(entry);
            BitmapSource image = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32,
                null, pixels.Bytes, pixels.Width * 4);
            image.Freeze();
            return image;
        }

        private void RequestEntry(string key, ZipArchiveEntry entry, bool resident, Action<BitmapSource> callback)
        {
            BitmapSource cached;
            int generation;
            lock (_cacheLock)
            {
                generation = _requestGeneration;
                if (_residentCache.TryGetValue(key, out cached))
                {
                    Dispatch(callback, cached);
                    return;
                }
                if (_dynamicCache.TryGetValue(key, out cached))
                {
                    TouchLru(key);
                    Dispatch(callback, cached);
                    return;
                }
                if (_disposed)
                {
                    return;
                }
                if (_pending.Contains(key))
                {
                    if (callback != null)
                    {
                        List<Action<BitmapSource>> existing;
                        if (!_waiters.TryGetValue(key, out existing))
                        {
                            existing = new List<Action<BitmapSource>>();
                            _waiters[key] = existing;
                        }
                        existing.Add(callback);
                    }
                    return;
                }
                _pending.Add(key);
                if (callback != null)
                {
                    _waiters[key] = new List<Action<BitmapSource>> { callback };
                }
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                BitmapSource decoded = null;
                string error = null;
                try
                {
                    decoded = DecodeEntry(entry);
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
                List<Action<BitmapSource>> callbacks = null;
                lock (_cacheLock)
                {
                    if (generation != _requestGeneration || _disposed) return;
                    _pending.Remove(key);
                    _waiters.TryGetValue(key, out callbacks);
                    _waiters.Remove(key);
                    if (decoded != null && !_disposed)
                    {
                        if (resident)
                        {
                            _residentCache[key] = decoded;
                        }
                        else
                        {
                            AddDynamic(key, decoded);
                        }
                    }
                    if (error != null)
                    {
                        LastDecodeError = error;
                    }
                }
                // A failed decode must release callers waiting for this frame.
                if (callbacks != null)
                {
                    foreach (Action<BitmapSource> waiting in callbacks)
                    {
                        Dispatch(waiting, decoded);
                    }
                }
            });
        }

        private void Dispatch(Action<BitmapSource> callback, BitmapSource bitmap)
        {
            if (callback == null || _disposed)
            {
                return;
            }
            if (_dispatcher.CheckAccess())
            {
                callback(bitmap);
            }
            else
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate
                {
                    if (!_disposed)
                    {
                        callback(bitmap);
                    }
                }));
            }
        }

        private void AddDynamic(string key, BitmapSource bitmap)
        {
            _dynamicCache[key] = bitmap;
            TouchLru(key);
            while (_dynamicCache.Count > DynamicCacheCapacity)
            {
                LinkedListNode<string> oldest = _lru.First;
                if (oldest == null)
                {
                    break;
                }
                _lru.RemoveFirst();
                _lruNodes.Remove(oldest.Value);
                _dynamicCache.Remove(oldest.Value);
            }
        }

        private void TouchLru(string key)
        {
            LinkedListNode<string> node;
            if (_lruNodes.TryGetValue(key, out node))
            {
                _lru.Remove(node);
            }
            node = _lru.AddLast(key);
            _lruNodes[key] = node;
        }

        private static bool HasTransparentCorners(BitmapSource source)
        {
            FormatConvertedBitmap converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            byte[] pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            int topRight = (width - 1) * 4 + 3;
            int bottomLeft = (height - 1) * width * 4 + 3;
            int bottomRight = ((height * width) - 1) * 4 + 3;
            return pixels[3] == 0 && pixels[topRight] == 0 && pixels[bottomLeft] == 0 && pixels[bottomRight] == 0;
        }

        private string FrameKey(string clipId, int index)
        {
            return "frame:" + _clipEntries[clipId][index].FullName;
        }

        private static string LookKey(MouseLookDirection direction)
        {
            return "look:" + direction;
        }

        private static string NormalisePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        private static string ReadAttribute(XElement element, string name, string fallback)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? fallback : attribute.Value;
        }

        private static int ReadInt(XElement element, string name, int fallback)
        {
            int value;
            return int.TryParse(ReadAttribute(element, name, string.Empty), out value) ? value : fallback;
        }

        private static double ReadDouble(XElement element, string name, double fallback)
        {
            double value;
            return double.TryParse(ReadAttribute(element, name, string.Empty), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static PetPose ParsePose(string text)
        {
            PetPose value;
            return Enum.TryParse(text, true, out value) ? value : PetPose.Standing;
        }

        private static MouseLookDirection ParseLookDirection(string text)
        {
            string compact = (text ?? string.Empty).Replace("_", string.Empty);
            MouseLookDirection value;
            return Enum.TryParse(compact, true, out value) ? value : MouseLookDirection.Center;
        }

        public void Dispose()
        {
            lock (_cacheLock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _dynamicCache.Clear();
                _residentCache.Clear();
                _pending.Clear();
                _waiters.Clear();
                _pixelCache.Clear();
                _pixelPending.Clear();
                _pixelLru.Clear();
            }
            lock (_archiveLock)
            {
                _archive.Dispose();
                _resourceStream.Dispose();
            }
        }
    }

    internal sealed class FrameLoadResult
    {
        public FramePixels Pixels;
        public bool Cancelled;
        public string Error;
        public bool Succeeded { get { return !Cancelled && Error == null && Pixels != null; } }
    }
}
