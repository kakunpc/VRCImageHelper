namespace VRCImageHelper.Core;

using System.Collections.Concurrent;

public class NewLineEventArgs(string line) : EventArgs
{
    public string Line { get; } = line;
}

// No log/photo watchers, OSC, UI or real metadata processing are started.
// Queue.CheckQueue is invoked explicitly; its timer stays disabled until the
// application's Initialize method, which these fixture checks never call.
internal static class LogReader
{
    internal static string LogDirectory => Path.Combine(TestEnvironment.Root, "queue-logs");
    internal static DateTime CurrentHead { get; set; }
}

internal static class ImageProcessor
{
    internal static readonly ConcurrentDictionary<string, State> Processed = new();
    internal static void ProcessImage(string path, State state) => Processed[path] = state.Clone();
}
