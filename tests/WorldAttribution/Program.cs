using System.Globalization;
using VRCImageHelper.Core;

var originalDirectory = Directory.GetCurrentDirectory();
Directory.CreateDirectory(TestEnvironment.Root);
Directory.SetCurrentDirectory(TestEnvironment.Root); // Queue checkpoint writes stay in this disposable root.
var groups = 0;
try
{
    var beforeMidnight = At(28, 23, 55);
    var afterMidnight = At(29, 0, 10);
    var afterMove = At(29, 0, 21);
    var future = new State();
    future.RoomInfo.World_id = "wrld_B";
    future.RoomInfo.World_name = "World B";
    State.Current = future;

    Group("exact chronology, restart and repeated reprocessing preserve A/A/B", directory =>
    {
        var path = WriteLog(directory, At(28, 23, 40), Join(At(28, 23, 50), "A") + Player(At(28, 23, 51), "Alice"));
        var history = new WorldHistory(directory);
        history.Refresh();
        World(history, beforeMidnight, "A");
        World(history, afterMidnight, "A");
        File.AppendAllText(path, Join(At(29, 0, 20), "B") + Player(At(29, 0, 20, 1), "Bob"));
        for (var restart = 0; restart < 3; restart++)
        {
            history = new WorldHistory(directory);
            history.Refresh();
            World(history, beforeMidnight, "A");
            World(history, afterMidnight, "A");
            World(history, afterMove, "B");
            history.Refresh();
            World(history, afterMidnight, "A");
        }
        Check(State.Current == future && future.RoomInfo.World_name == "World B", "history parsing must not mutate live state");
    });
    Group("exact join boundary and full local date ordering", directory =>
    {
        WriteLog(directory, At(28, 23, 40), OriginalScenario());
        var history = Read(directory);
        World(history, At(29, 0, 19, 59, 999), "A");
        World(history, At(29, 0, 20), "B");
        Check(history.TryCreatePhotoState(afterMidnight, null, out var state), "known A");
        Check(state.RoomInfo.JoinDateTime == "2026.10.28 23:50:00" && state.Players.SequenceEqual(new[] { "Alice" }), "previous-day join and players carry forward");
    });
    Group("multiple worlds and players are selected at capture time", directory =>
    {
        WriteLog(directory, At(28, 23, 40), OriginalScenario() + Join(At(29, 1, 0), "C") + Player(At(29, 1, 1), "Carol"));
        var history = Read(directory);
        World(history, afterMidnight, "A");
        World(history, afterMove, "B");
        World(history, At(29, 1, 5), "C");
        Check(history.TryCreatePhotoState(afterMove, null, out var state) && state.Players.SequenceEqual(new[] { "Bob" }), "later players cannot leak into old photos");
    });
    Group("month/year boundaries do not reset a continuing session", directory =>
    {
        WriteLog(directory, new DateTime(2026, 12, 31, 23, 0, 0), Join(new DateTime(2026, 12, 31, 23, 50, 0), "A") + Join(new DateTime(2027, 1, 1, 0, 20, 0), "B"));
        var history = Read(directory);
        World(history, new DateTime(2027, 1, 1, 0, 10, 0), "A");
        World(history, new DateTime(2027, 1, 1, 0, 21, 0), "B");
    });
    Group("new sessions cannot inherit or borrow a future world", directory =>
    {
        // Intentionally create the later file first: creation/enumeration order is irrelevant.
        WriteLog(directory, At(29, 0, 0), Line(At(29, 0, 1), "session started") + Join(At(29, 0, 20), "B"));
        WriteLog(directory, At(28, 23, 40), Join(At(28, 23, 50), "A"));
        var history = Read(directory);
        World(history, beforeMidnight, "A");
        Check(!history.TryCreatePhotoState(afterMidnight, future, out _), "new session without a known world must remain unknown");
        World(history, afterMove, "B");
    });
    Group("log filename timezone differences do not change local capture times", directory =>
    {
        WriteLog(directory, At(29, 6, 40), OriginalScenario());
        var history = Read(directory);
        World(history, beforeMidnight, "A");
        World(history, afterMidnight, "A");
        World(history, afterMove, "B");
    });
    Group("gaps before a truncated new session are not guessed from the previous session", directory =>
    {
        WriteLog(directory, At(28, 23, 40), Join(At(28, 23, 50), "A"));
        WriteLog(directory, At(29, 0, 0), Join(At(29, 0, 20), "B"));
        Check(!Read(directory).TryCreatePhotoState(afterMidnight, future, out _), "a gap between separate sessions is unknown");
    });
    Group("quit and missing/truncated history leave photos untouched", directory =>
    {
        WriteLog(directory, At(28, 23, 40), Join(At(28, 23, 50), "A") + Line(At(28, 23, 59), "VRCApplication: OnApplicationQuit") + Join(At(29, 0, 20), "B"));
        var history = Read(directory);
        Check(!history.TryCreatePhotoState(At(28, 23, 45), future, out _), "never use first later world");
        Check(!history.TryCreatePhotoState(afterMidnight, future, out _), "never carry a world through quit");
        var missing = Path.Combine(directory, "empty");
        Directory.CreateDirectory(missing);
        Check(!Read(missing).TryCreatePhotoState(afterMidnight, future, out _), "missing history is not current world");
    });
    Group("partial lines and incomplete join pairs cannot produce mixed worlds", directory =>
    {
        var path = WriteLog(directory, At(28, 23, 40), Join(At(28, 23, 50), "A") + Line(At(29, 0, 20), "[Behaviour] Joining or Creating Room: World B"));
        File.AppendAllText(path, Line(At(29, 0, 20), "[Behaviour] Joining wrld_B:222").TrimEnd('\n'));
        var history = Read(directory);
        Check(!history.TryCreatePhotoState(afterMove, null, out _), "half-written ID must not be paired with the old room");
        World(history, afterMidnight, "A");
        File.AppendAllText(path, "\n");
        history.Refresh();
        World(history, afterMove, "B");
    });
    Group("large irrelevant lines are ignored without losing previous-day state", directory =>
    {
        WriteLog(directory, At(28, 23, 40), Join(At(28, 23, 50), "A") + Line(At(29, 0, 0), new string('x', 1_000_000) + "[Behaviour] Joining wrld_B:222"));
        World(Read(directory), afterMidnight, "A");
    });
    Group("join ID/name order and timestamp ordering are deterministic", directory =>
    {
        var reversed = Line(At(29, 0, 20), "[Behaviour] Joining wrld_B:222") + Line(At(29, 0, 20), "[Behaviour] Joining or Creating Room: World B");
        WriteLog(directory, At(28, 23, 40), reversed + Join(At(28, 23, 50), "A"));
        var history = Read(directory);
        World(history, afterMidnight, "A");
        World(history, At(29, 0, 20), "B");
    });
    Group("concurrent refreshes and lookups keep one consistent history snapshot", directory =>
    {
        WriteLog(directory, At(28, 23, 40), OriginalScenario());
        var history = Read(directory);
        Parallel.For(0, 12, _ => { history.Refresh(); World(history, afterMidnight, "A"); World(history, afterMove, "B"); });
    });
    Group("explicit camera snapshots retain camera data but cannot override world history", directory =>
    {
        WriteLog(directory, At(28, 23, 40), OriginalScenario());
        var history = Read(directory);
        var captured = future.Clone();
        captured.VirtualLens2.Enabled = true;
        captured.VirtualLens2.FocalLength = 85;
        Check(history.TryCreatePhotoState(afterMidnight, captured, out var state), "history available");
        Check(state.RoomInfo.World_id == "wrld_A" && state.VirtualLens2.FocalLength == 85 && state.VirtualLens2.Enabled, "camera/world separation");
        state.RoomInfo.World_name = "mutated";
        state.Players.Clear();
        World(history, afterMidnight, "A");
        Check(history.TryCreatePhotoState(afterMidnight, null, out var again) && again.Players.Count == 1, "returned snapshots cannot mutate history");
    });
    Group("existing photo scan is ordered across month folders", directory =>
    {
        var photos = Path.Combine(directory, "photos");
        var p3 = Photo(photos, new DateTime(2026, 11, 1, 0, 21, 0));
        var p2 = Photo(photos, new DateTime(2026, 11, 1, 0, 10, 0));
        var p1 = Photo(photos, new DateTime(2026, 10, 31, 23, 55, 0));
        Photo(photos, new DateTime(2026, 10, 31, 23, 30, 0));
        Check(FileWatcher.EnumeratePhotosSince(photos, new DateTime(2026, 10, 31, 23, 40, 0)).SequenceEqual(new[] { p1, p2, p3 }), "photo scan order/threshold");
    });

    Directory.CreateDirectory(LogReader.LogDirectory);
    var queueLog = WriteLog(LogReader.LogDirectory, At(28, 23, 40), OriginalScenario());
    var photoRoot = Path.Combine(TestEnvironment.Root, "queue-photos");
    var last = Photo(photoRoot, afterMove);
    var first = Photo(photoRoot, beforeMidnight);
    var middle = Photo(photoRoot, afterMidnight);
    var originals = new[] { first, middle, last }.ToDictionary(path => path, File.ReadAllBytes);
    // Out-of-order FS delivery must not reject older pending photos or a late snapshot.
    ImageProcessQueue.Enqueue(last);
    ImageProcessQueue.Enqueue(first);
    ImageProcessQueue.Enqueue(middle);
    ScreenshotWatcher.OnNewLogLine(new object(), new NewLineEventArgs(Line(afterMidnight, "[VRC Camera] Took screenshot to: " + middle).TrimEnd('\n')));
    Check(ImageProcessQueue.s_queue.Count == 3, "older pending photos must not be dropped");
    LogReader.CurrentHead = At(29, 0, 30);
    await Task.WhenAll(ImageProcessQueue.CheckQueue());
    Check(ImageProcessor.Processed[first].RoomInfo.World_id == "wrld_A", "23:55 should be A");
    Check(ImageProcessor.Processed[middle].RoomInfo.World_id == "wrld_A", "00:10 must remain A after restart, even with a future B snapshot");
    Check(ImageProcessor.Processed[last].RoomInfo.World_id == "wrld_B", "00:21 should be B");
    Check(originals.All(pair => File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)), "no photo fixture bytes modified");
    Console.WriteLine("PASS actual queue/screenshot handlers: out-of-order photos still resolve A/A/B; fixture images remain unchanged");
    groups++;

    ImageProcessor.Processed.Clear();
    ImageProcessQueue.Enqueue(middle);
    await Task.WhenAll(ImageProcessQueue.CheckQueue());
    Check(ImageProcessor.Processed[middle].RoomInfo.World_id == "wrld_A", "reprocessing is idempotent");
    Console.WriteLine("PASS actual queue reprocessing remains World A");
    groups++;

    File.Delete(queueLog);
    ImageProcessor.Processed.Clear();
    var pending = Photo(photoRoot, At(29, 0, 11));
    ImageProcessQueue.Enqueue(pending);
    Check(ImageProcessQueue.CheckQueue().Length == 0 && !ImageProcessor.Processed.ContainsKey(pending), "missing history must not call the processor");
    Check(ImageProcessQueue.s_queue.Count == 1, "unknown photo remains pending");
    WriteLog(LogReader.LogDirectory, At(28, 23, 40), OriginalScenario());
    await Task.WhenAll(ImageProcessQueue.CheckQueue());
    Check(ImageProcessor.Processed[pending].RoomInfo.World_id == "wrld_A", "a later history refresh can resolve a pending photo");
    Console.WriteLine("PASS missing history performs no processing and retries safely when logs become available");
    groups++;
    Console.WriteLine($"{groups} world-attribution groups passed; all logs, photo fixtures and queue checkpoints are disposable. No real metadata writer, app, watchers or services started.");
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    Directory.Delete(TestEnvironment.Root, recursive: true);
}

void Group(string name, Action<string> action)
{
    var directory = Path.Combine(TestEnvironment.Root, "history-" + (++groups));
    Directory.CreateDirectory(directory);
    action(directory);
    Console.WriteLine("PASS " + name);
}
static DateTime At(int day, int hour, int minute, int second = 0, int milliseconds = 0) => new(2026, 10, day, hour, minute, second, milliseconds, DateTimeKind.Unspecified);
static string Line(DateTime time, string value) => time.ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture) + " Log        -  " + value + "\n";
static string Join(DateTime time, string world) => Line(time, "[Behaviour] Joining or Creating Room: World " + world) + Line(time, "[Behaviour] Joining wrld_" + world + ":" + (world == "A" ? "111~friends(usr_owner)" : "222"));
static string Player(DateTime time, string name) => Line(time, "[Behaviour] OnPlayerJoined " + name + " (usr_" + name + ")");
static string OriginalScenario() => Join(At(28, 23, 50), "A") + Player(At(28, 23, 51), "Alice") + Join(At(29, 0, 20), "B") + Player(At(29, 0, 20, 1), "Bob");
static string WriteLog(string directory, DateTime start, string contents)
{
    var path = Path.Combine(directory, start.ToString("'output_log_'yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".txt");
    File.WriteAllText(path, contents);
    return path;
}
static WorldHistory Read(string directory)
{
    var history = new WorldHistory(directory);
    history.Refresh();
    return history;
}
static void World(WorldHistory history, DateTime time, string world)
{
    Check(history.TryCreatePhotoState(time, null, out var state) && state.RoomInfo.World_id == "wrld_" + world && state.RoomInfo.World_name == "World " + world, "wrong world at " + time.ToString("O"));
}
static string Photo(string root, DateTime time)
{
    var folder = Path.Combine(root, time.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "VRChat_" + time.ToString("yyyy-MM-dd_HH-mm-ss.fff", CultureInfo.InvariantCulture) + "_1x1.png");
    File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC"));
    return path;
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
