using System;
using System.IO;

namespace RepoCosmeticTracker.Services
{
    /// <summary>
    /// Watches R.E.P.O.'s AppData folder for .es3 writes so ownership can be
    /// re-synced the moment the game saves. FileSystemWatcher is purely
    /// event-driven (the OS pushes notifications; there is no polling), so
    /// this costs effectively nothing while idle.
    ///
    /// The game writes saves in several bursts (temp file, rename, rewrite),
    /// so raw events are debounced: we only fire once things have been quiet
    /// for a moment.
    /// </summary>
    public sealed class SaveWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly System.Timers.Timer _debounce;

        /// <summary>Raised on a threadpool thread after save writes settle.</summary>
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
