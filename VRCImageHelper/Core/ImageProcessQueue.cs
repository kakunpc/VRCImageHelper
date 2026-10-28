namespace VRCImageHelper.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

internal struct QueueTask
{
    public QueueTask(string path)
    {
        this.path = path;
    }

    public void SetIsProcessing(bool isProcessing)
    {
        this.isProcessing = isProcessing;
    }

    public void SetState(State state)
    {
        this.state = state;
    }

    public string path;
    public bool isProcessing = false;
    public State? state = null;
    public bool historyUnavailableReported = false;
}

/// <summary>
/// 画像処理のキューを管理するクラス
/// </summary>
internal static class ImageProcessQueue
{
    public static SemaphoreSlimWrapper? s_compressSemaphore;
    public static SortedDictionary<DateTime, LinkedList<QueueTask>> s_queue = new();
    private static readonly Timer s_timer;
    private static readonly WorldHistory s_history = new(LogReader.LogDirectory);
    private static bool s_initialized;
    private static bool s_scanAll;
    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> s_completedPaths = new(s_pathComparer);

    private static DateTime s_ignoreBefore;
    private static readonly object s_lockObject = new();
    private static readonly string s_lastProcessedTimeFile = "last_processed_time.txt";


    // 初期化メソッドを追加
    public static void Initialize(bool scanAll = false)
    {
        s_scanAll = scanAll;
        s_ignoreBefore = LoadLastProcessedTime(scanAll) + TimeSpan.FromMilliseconds(1);
        s_initialized = true;
        ResetTimer();
    }

    static ImageProcessQueue()
    {
        s_timer = new Timer(1000);
        s_timer.Elapsed += (sender, e) => CheckQueue();
        s_timer.AutoReset = false;

        if (ConfigManager.ParallelCompressionProcesses > 0)
        {
            s_compressSemaphore = new SemaphoreSlimWrapper(ConfigManager.ParallelCompressionProcesses, ConfigManager.ParallelCompressionProcesses);
        }
    }

    private static void SaveLastProcessedTime(DateTime time)
    {
        try
        {
            File.WriteAllText(s_lastProcessedTimeFile, time.ToString("O"));
        }
        catch (Exception)
        {
            // ファイル書き込みに失敗した場合は無視
        }
    }

    private static DateTime LoadLastProcessedTime(bool scanAll = false)
    {
        // scanAllがtrueの場合は常にMinValueを返す
        if (scanAll)
        {
            return DateTime.MinValue;
        }

        try
        {
            if (File.Exists(s_lastProcessedTimeFile))
            {
                var timeStr = File.ReadAllText(s_lastProcessedTimeFile);
                if (DateTime.TryParse(timeStr, out DateTime time))
                {
                    return time;
                }
            }
        }
        catch (Exception)
        {
            SaveLastProcessedTime(DateTime.MinValue);
        }
        return DateTime.MinValue;
    }

    private static void ResetTimer()
    {
        if (!s_initialized) return;
        s_timer.Stop();
        s_timer.Start();
    }

    public static void Enqueue(string path, State? state = null)
    {
        path = Path.GetFullPath(path);
        if (ParseDate.TryParseFilePathToDateTime(path, out var timestamp))
        {
            lock (s_lockObject)
            {
                if (timestamp < s_ignoreBefore || s_completedPaths.Contains(path))
                {
                    return;
                }

                if (!s_queue.ContainsKey(timestamp))
                {
                    s_queue[timestamp] = new LinkedList<QueueTask>();
                }

                if (!s_queue[timestamp].Any(item => s_pathComparer.Equals(item.path, path)))
                {
                    s_queue[timestamp].AddLast(new QueueTask(path) { state = state });
                }
                else if (state != null)
                {
                    // FS由来が先にあったとき
                    var node = s_queue[timestamp].First;
                    while (node != null)
                    {
                        if (s_pathComparer.Equals(node.Value.path, path) && node.Value.state == null)
                        {
                            var updated = node.Value;
                            updated.SetState(state);
                            node.Value = updated;
                        }
                        node = node.Next;
                    }
                }
            }

            ResetTimer();
        }
    }

    public static Task[] CheckQueue()
    {
        ResetTimer();
        var scheduled = new List<Task>();

        lock (s_lockObject)
        {
            if (s_queue.Count == 0) return [];
        }
        try
        {
            s_history.Refresh(s_scanAll);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"World history unavailable; photos remain queued: {ex.Message}");
            return [];
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"World history unavailable; photos remain queued: {ex.Message}");
            return [];
        }

        var currentLogTime = LogReader.CurrentHead;

        var now = DateTime.Now - TimeSpan.FromSeconds(5);

        // 5秒以上進んでいなければ、該当のファイルに関連するログの書き込みは存在しないとみなす
        if (currentLogTime < now)
        {
            currentLogTime = now;
        }

        lock (s_lockObject)
        {
            var keysToProcess = s_queue.Keys.Where(key => key < currentLogTime).ToList();

            foreach (var key in keysToProcess)
            {
                if (!s_queue.TryGetValue(key, out var taskList))
                    continue;

                var itemsToProcess = taskList.Where(item => !item.isProcessing).ToList();

                foreach (var item in itemsToProcess)
                {
                    var node = taskList.Find(item);
                    if (node != null)
                    {
                        var updatedItem = node.Value;
                        if (!s_history.TryCreatePhotoState(key, item.state, out var state))
                        {
                            if (!updatedItem.historyUnavailableReported)
                                Debug.WriteLine($"No world history at photo time; leaving untouched: {item.path}");
                            updatedItem.historyUnavailableReported = true;
                            node.Value = updatedItem;
                            continue;
                        }
                        updatedItem.SetIsProcessing(true);
                        node.Value = updatedItem;

                        state.CreationDate = MetadataArguments.FormatCreationDate(key);

                        scheduled.Add(Task.Run(() =>
                        {
                            Debug.WriteLine($"画像処理: 待機中: {item.path}");
                            using (s_compressSemaphore?.Wait())
                            {
                                Debug.WriteLine($"画像処理: 開始: {item.path}");
                                var succeeded = false;
                                try
                                {
                                    ImageProcessor.ProcessImage(item.path, state);
                                    succeeded = true;
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"画像処理でエラーが発生しました: {item.path}, エラー: {ex.Message}");
                                }
                                finally
                                {
                                    lock (s_lockObject)
                                    {
                                        // Record success before removing the queued item, so a
                                        // delayed file/log notification cannot schedule it again.
                                        if (succeeded) s_completedPaths.Add(item.path);
                                        if (s_queue.TryGetValue(key, out var currentTaskList))
                                        {
                                            var nodeToRemove = currentTaskList.First;
                                            while (nodeToRemove != null)
                                            {
                                                var nextNode = nodeToRemove.Next;
                                                if (nodeToRemove.Value.path == item.path)
                                                {
                                                    currentTaskList.Remove(nodeToRemove);
                                                    break;
                                                }
                                                nodeToRemove = nextNode;
                                            }

                                            if (currentTaskList.Count == 0)
                                            {
                                                s_queue.Remove(key);
                                            }
                                        }

                                        if (!s_queue.Keys.Any(k => k < key) && !s_queue.ContainsKey(key))
                                        {
                                            SaveLastProcessedTime(key);
                                        }
                                    }
                                }
                            }
                        }));
                    }
                }
            }
        }
        return scheduled.ToArray();
    }
}
