namespace VRCImageHelper.Core;

using System.Globalization;
using System.Text;
using StateChecker;

/// <summary>Reconstructs world/player state at a photo's local capture time.</summary>
internal sealed class WorldHistory(string directory)
{
    private sealed record Entry(DateTime Time, State State);
    private sealed record Session(DateTime Start, DateTime LastObserved, bool KnownStart, IReadOnlyList<Entry> Entries);
    private sealed record CachedLog(long Length, DateTime LastWriteUtc, Session? Session);
    private readonly Dictionary<string, CachedLog> _cache = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private volatile List<Session> _sessions = [];
    private readonly object _refreshLock = new();

    internal void Refresh(bool scanAll = false, DateTime? today = null)
    {
        var day = (today ?? DateTime.Today).Date;
        lock (_refreshLock) RefreshCore(scanAll, day);
    }

    private void RefreshCore(bool scanAll, DateTime today)
    {
        // Filter metadata before opening any log. Last write time also includes
        // sessions that started earlier but are still active today.
        var files = Directory.EnumerateFiles(directory, "output_log_*.txt")
            .Select(path => new FileInfo(path))
            .Where(file => scanAll || (file.LastWriteTime >= today.AddDays(-1) && file.LastWriteTime < today.AddDays(1)))
            .ToList();
        var present = files.Select(file => file.FullName).ToHashSet(_cache.Comparer);
        foreach (var removed in _cache.Keys.Where(path => !present.Contains(path)).ToList())
            _cache.Remove(removed);
        foreach (var file in files)
        {
            if (_cache.TryGetValue(file.FullName, out var cached) && cached.Length == file.Length && cached.LastWriteUtc == file.LastWriteTimeUtc)
                continue;
            var length = file.Length;
            var lastWrite = file.LastWriteTimeUtc;
            using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            _cache[file.FullName] = new CachedLog(length, lastWrite, Parse(file.Name, reader));
        }
        _sessions = _cache.Values.Select(log => log.Session).OfType<Session>().OrderBy(session => session.Start).ToList();
    }

    internal bool TryCreatePhotoState(DateTime capturedAt, State? capturedState, out State state)
    {
        state = null!;
        var sessions = _sessions; // One immutable snapshot for this lookup.
        var sessionIndex = sessions.FindLastIndex(session => session.Start <= capturedAt);
        if (sessionIndex < 0) return false;
        var session = sessions[sessionIndex];
        // A gap between separate log sessions is not evidence that the old
        // world continued. Keep such photos pending instead of guessing.
        if (sessionIndex + 1 < sessions.Count && !sessions[sessionIndex + 1].KnownStart && capturedAt > session.LastObserved)
            return false;
        // Do not choose an arbitrary file if the session start itself is ambiguous.
        if (sessionIndex > 0 && sessions[sessionIndex - 1].Start == session.Start) return false;
        var low = 0;
        var high = session.Entries.Count - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (session.Entries[middle].Time <= capturedAt) { found = middle; low = middle + 1; }
            else high = middle - 1;
        }
        if (found < 0) return false;
        var historical = session.Entries[found].State;
        if (string.IsNullOrEmpty(historical.RoomInfo.World_id) || string.IsNullOrEmpty(historical.RoomInfo.World_name))
            return false;

        // Explicit screenshot snapshots retain their camera state. Room and
        // players always come from history, never the processing-time world.
        state = capturedState?.Clone() ?? historical.Clone();
        state.RoomInfo = new RoomInfo(historical.RoomInfo);
        state.Players = new List<string>(historical.Players);
        return true;
    }

    private static Session? Parse(string fileName, TextReader reader)
    {
        // Only complete, bounded lines are considered, matching LogReader's
        // relevant-line limit. Large logs are streamed rather than held in RAM.
        var timed = new List<(DateTime Time, string Line)>();
        DateTime? earliest = null;
        DateTime? latest = null;
        var buffer = new char[4096];
        var line = new StringBuilder();
        var oversized = false;
        int count;
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] != '\n')
                {
                    if (line.Length < 500) line.Append(buffer[i]);
                    else oversized = true;
                    continue;
                }
                if (!oversized && line.Length < 500)
                {
                    var value = line.ToString().TrimEnd('\r');
                    if (TryTimestamp(value, out var time))
                    {
                        if (earliest is null || time < earliest.Value) earliest = time;
                        if (latest is null || time > latest.Value) latest = time;
                        if (!value.Contains("Error      -  ", StringComparison.Ordinal) &&
                            !value.Contains("Warning    -  ", StringComparison.Ordinal) &&
                            (value.Contains("[Behaviour] Joining ", StringComparison.Ordinal) ||
                             value.Contains("[Behaviour] OnPlayer", StringComparison.Ordinal) ||
                             value.Contains("VRCApplication: OnApplicationQuit", StringComparison.Ordinal)))
                            timed.Add((time, value));
                    }
                }
                line.Clear();
                oversized = false;
            }
        }
        // The final unterminated line stays ignored until a later append.
        // Keep log/photo times on their original local clock. A filename may
        // bound a session before its first line, but must not discard earlier
        // log events if the two timestamp sources disagree. Never shift dates.
        var hasNamedStart = DateTime.TryParseExact(Path.GetFileNameWithoutExtension(fileName),
            "'output_log_'yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var namedStart);
        var knownStart = hasNamedStart && (earliest is null || namedStart <= earliest.Value);
        DateTime? start = knownStart ? namedStart : earliest;
        if (start is null) return null;
        var entries = new List<Entry>();
        var current = new State();
        string? pendingName = null;
        var waitingForName = false;
        foreach (var item in timed.OrderBy(item => item.Time))
        {
            if (item.Time < start.Value) continue;
            if (item.Line.Contains("VRCApplication: OnApplicationQuit", StringComparison.Ordinal))
            {
                current = new State();
                pendingName = null;
                waitingForName = false;
                entries.Add(new Entry(item.Time, current.Clone()));
                continue;
            }
            if (!item.Line.Contains("[Behaviour]", StringComparison.Ordinal)) continue;
            var oldWorld = current.RoomInfo.World_id;
            var oldInstance = current.RoomInfo.Instance_id;
            if (VRChat.ApplyWorldId(current, item.Line))
            {
                if (pendingName is not null)
                {
                    current.RoomInfo.World_name = pendingName;
                    pendingName = null;
                    waitingForName = false;
                }
                else if (oldWorld != current.RoomInfo.World_id || oldInstance != current.RoomInfo.Instance_id)
                {
                    current.RoomInfo.World_name = "";
                    waitingForName = true;
                }
                entries.Add(new Entry(item.Time, current.Clone()));
                continue;
            }
            if (item.Line.Contains("[Behaviour] Joining or Creating Room:", StringComparison.Ordinal))
            {
                var named = current.Clone();
                if (!VRChat.ApplyJoinRoom(named, item.Line)) continue;
                if (waitingForName)
                {
                    current.RoomInfo.World_name = named.RoomInfo.World_name;
                    waitingForName = false;
                    entries.Add(new Entry(item.Time, current.Clone()));
                }
                else
                {
                    pendingName = named.RoomInfo.World_name;
                    entries.Add(new Entry(item.Time, new State()));
                }
                continue;
            }
            if (VRChat.ApplyPlayerJoin(current, item.Line) || VRChat.ApplyPlayerLeft(current, item.Line))
                entries.Add(new Entry(item.Time, pendingName is null ? current.Clone() : new State()));
        }
        return new Session(start.Value, latest ?? start.Value, knownStart, entries);
    }

    private static bool TryTimestamp(string line, out DateTime time)
    {
        time = default;
        return line.Length >= 19 && DateTime.TryParseExact(line[..19], "yyyy.MM.dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
