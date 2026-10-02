using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace YabiDesktopPet
{
    internal sealed class FramePixels
    {
        public byte[] Bytes;
        public int Width;
        public int Height;
    }

    internal sealed partial class AnimationCatalog
    {
        private readonly Dictionary<string, FramePixels> _pixelCache = new Dictionary<string, FramePixels>();
        private readonly LinkedList<string> _pixelLru = new LinkedList<string>();
        private readonly Dictionary<string, List<Action<FrameLoadResult>>> _pixelPending = new Dictionary<string, List<Action<FrameLoadResult>>>();

        private FramePixels DecodePixels(ZipArchiveEntry entry)
        {
            byte[] encoded;
            lock (_archiveLock)
            {
                if (_disposed) throw new ObjectDisposedException("AnimationCatalog");
                // PNG size is already in the ZIP index. Read into one exact
                // buffer instead of growing a MemoryStream and cloning it.
                encoded = new byte[checked((int)entry.Length)];
                using (Stream input = entry.Open())
                {
                    int offset = 0;
                    while (offset < encoded.Length)
                    {
                        int read = input.Read(encoded, offset, encoded.Length - offset);
                        if (read == 0) throw new EndOfStreamException("动作帧数据不完整");
                        offset += read;
                    }
                }
            }
            using (MemoryStream input = new MemoryStream(encoded, false))
            using (Drawing.Image original = Drawing.Image.FromStream(input))
            using (Drawing.Bitmap bitmap = new Drawing.Bitmap(original.Width, original.Height, Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.DrawImageUnscaled(original, 0, 0);
                }
                FramePixels result = new FramePixels { Width = bitmap.Width, Height = bitmap.Height, Bytes = new byte[bitmap.Width * bitmap.Height * 4] };
                Imaging.BitmapData data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < bitmap.Height; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), result.Bytes, y * bitmap.Width * 4, bitmap.Width * 4);
                }
                finally { bitmap.UnlockBits(data); }
                return result;
            }
        }

        private void RequestPixels(string id, int index, Action<FrameLoadResult> callback)
        {
            ClipDefinition clip = GetClip(id);
            int safeIndex = Math.Max(0, Math.Min(clip.FrameCount - 1, index));
            ZipArchiveEntry entry = _clipEntries[id][safeIndex];
            string key = entry.FullName;
            int generation;
            lock (_cacheLock)
            {
                if (_disposed) { DispatchPixels(callback, new FrameLoadResult { Cancelled = true }); return; }
                generation = _requestGeneration;
                FramePixels cached;
                if (_pixelCache.TryGetValue(key, out cached))
                {
                    _pixelLru.Remove(key); _pixelLru.AddLast(key);
                    DispatchPixels(callback, new FrameLoadResult { Pixels = cached });
                    return;
                }
                List<Action<FrameLoadResult>> waiters;
                if (!_pixelPending.TryGetValue(key, out waiters))
                {
                    waiters = new List<Action<FrameLoadResult>>();
                    _pixelPending.Add(key, waiters);
                }
                else { if (callback != null) waiters.Add(callback); return; }
                if (callback != null) waiters.Add(callback);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                lock (_cacheLock) { if (generation != _requestGeneration || _disposed) return; }
                FrameLoadResult result = new FrameLoadResult();
                try { result.Pixels = DecodePixels(entry); }
                catch (Exception exception) { result.Error = exception.Message; }
                List<Action<FrameLoadResult>> waiters;
                lock (_cacheLock)
                {
                    if (_disposed || generation != _requestGeneration) return;
                    _pixelPending.TryGetValue(key, out waiters);
                    _pixelPending.Remove(key);
                    if (result.Pixels != null)
                    {
                        _pixelCache[key] = result.Pixels;
                        _pixelLru.Remove(key); _pixelLru.AddLast(key);
                        while (_pixelLru.Count > DynamicCacheCapacity)
                        { string oldest = _pixelLru.First.Value; _pixelLru.RemoveFirst(); _pixelCache.Remove(oldest); }
                    }
                    if (result.Error != null) LastDecodeError = result.Error;
                }
                if (waiters != null) foreach (Action<FrameLoadResult> waiter in waiters) DispatchPixels(waiter, result);
            });
        }

        private void DispatchPixels(Action<FrameLoadResult> callback, FrameLoadResult result)
        {
            if (callback == null || _disposed) return;
            if (_dispatcher.CheckAccess()) callback(result);
            else _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate { if (!_disposed) callback(result); }));
        }
    }
}
