using System.IO;

namespace RepoCosmeticTracker.Services
{

    public sealed class SaveWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly System.Timers.Timer _debounce;
        public event Action? SaveChanged;

        public SaveWatcher(string repoDataRoot)
        {
            _debounce = new System.Timers.Timer(900) { AutoReset = false };
            _debounce.Elapsed += (_, _) => SaveChanged?.Invoke();

            _watcher = new FileSystemWatcher(repoDataRoot, "*.es3")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            // Restart the quiet-period timer on every event in the burst.
            _debounce.Stop();
            _debounce.Start();
        }

        public void Dispose()
        {
            _watcher.Dispose();
            _debounce.Dispose();
        }
    }
}
