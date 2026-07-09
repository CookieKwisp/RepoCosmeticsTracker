using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace RepoCosmeticTracker.Services
{
    public static class IconCache
    {
        private const int DecodeWidth = 160;

        private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource? Get(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            return Cache.GetOrAdd(path, Decode);
        }
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
                bmp.CacheOption = BitmapCacheOption.OnLoad;   
                bmp.CreateOptions = BitmapCreateOptions.None;
                bmp.EndInit();
                bmp.Freeze();                                  
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
