namespace VRCImageHelper.Core;

// No OSC sockets/services are started. These are the input values consumed by
// the linked production camera state parsers, not a replacement for their logic.
public class OscEventArgs(string path, string data) : EventArgs
{
    public string Path { get; } = path;
    public string Data { get; } = data;
}
