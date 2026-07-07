using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Decodes each cosmetic icon PNG exactly once, at card resolution, into a
    /// frozen (thread-safe, immutable) bitmap and keeps it in memory. Frozen
    /// BitmapSources can be created off the UI thread, so the whole catalog is
    /// pre-warmed in parallel on background threads at startup — after that,
    /// scrolling and resizing only ever do a dictionary lookup, never a decode,
    /// which is what keeps the grid stutter-free.
    ///
    /// ~500 icons at this size is on the order of tens of MB — a deliberate
    /// memory-for-smoothness trade.
    /// </summary>
    public static class IconCache
    {
        // Decode a bit above the on-screen icon size so downscaling stays crisp
        // without paying to decode the PNG at its full native resolution.
        private const int DecodeWidth = 160;

        private static readonly ConcurrentDictionary<string, BitmapSource?> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cached bitmap for a path, decoding on demand if not pre-warmed.</summary>
        public static BitmapSource? Get(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return Cache.GetOrAdd(path, Decode);
        }

        /// <summary>
        /// Decodes any not-yet-cached paths in parallel on the thread pool.
        /// Safe to call repeatedly; already-cached entries are skipped.
        /// </summary>
        public static Task PreloadAsync(IReadOnlyCollection<string> paths)
        {
            if (paths.Count == 0)
                return Task.CompletedTask;

            return Task.Run(() =>
            {
                Parallel.ForEach(
                    paths,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    path =>
                    {
                        if (!Cache.ContainsKey(path))
                            Cache[path] = Decode(path);
                    });
            });
        }

        private static BitmapSource? Decode(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = DecodeWidth;
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // read the file now, don't hold a handle
                bmp.CreateOptions = BitmapCreateOptions.None;
                bmp.EndInit();
                bmp.Freeze();                                  // immutable => usable from any thread
                return bmp;
            }
            catch
            {
                // Missing/corrupt cache file — remember the miss so we don't retry.
                return null;
            }
        }
    }
}
