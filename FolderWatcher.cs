using System.IO;
using System.Threading;

namespace Clothesline;

/// <summary>
/// 监听系统截图文件夹（Win+PrintScreen 落点），文件写完后回调 CaptureFound。
/// 文件系统事件在写完成前就会触发，所以做 350ms 去抖 + 可读性重试。
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _fsw;
    private readonly Timer _timer;
    private readonly Queue<string> _queue = new();
    private readonly object Gate = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(350);

    public Action<string>? CaptureFound;

    public FolderWatcher(string dir)
    {
        Directory.CreateDirectory(dir);
        _fsw = new FileSystemWatcher(dir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
        };
        _fsw.Created += (_, e) => Enqueue(e.FullPath);
        _fsw.Renamed += (_, e) => Enqueue(e.FullPath);
        _fsw.Error += (_, e) => Logger.Log("fsw error: " + e.GetException()?.Message);
        _fsw.EnableRaisingEvents = true;

        _timer = new Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);
        Logger.Log("watching folder: " + dir);
    }

    private void Enqueue(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg")) return;
        lock (Gate) _queue.Enqueue(path);
        _timer.Change((int)Debounce.TotalMilliseconds, Timeout.Infinite);
    }

    private void Fire(object? state)
    {
        var batch = new List<string>();
        lock (Gate)
        {
            while (_queue.Count > 0) batch.Add(_queue.Dequeue());
        }
        foreach (var path in batch)
        {
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        break;
                    }
                }
                catch (IOException) { Thread.Sleep(250); }
                catch { break; }
            }
            CaptureFound?.Invoke(path);
        }
    }

    public void Dispose()
    {
        _fsw.Dispose();
        _timer.Dispose();
    }
}

/// <summary>同时监视多个文件夹。某个路径建不起来只跳过该路径，不影响其余。</summary>
public sealed class FolderWatchSet : IDisposable
{
    private readonly List<FolderWatcher> _watchers = new();
    private Action<string>? _onFound;

    public void SetHandler(Action<string> onFound) => _onFound = onFound;

    public void Replace(IEnumerable<string> folders)
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in folders)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch (Exception ex)
            {
                Logger.Log("watch path: " + ex.Message);
                continue;
            }
            if (!seen.Add(full)) continue;
            try
            {
                var watcher = new FolderWatcher(full);
                watcher.CaptureFound = _onFound;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Logger.Log("watch skip " + full + ": " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }
}
