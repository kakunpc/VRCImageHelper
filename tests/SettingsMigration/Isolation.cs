// Only UI location and codec probing are substituted. The production settings
// reader, writer, schema upgrades and JSON model are compiled unchanged.
namespace VRCImageHelper.Core
{
    internal static class TestEnvironment
    {
        internal static readonly string Root = Path.Combine(Path.GetTempPath(), "VRCImageHelper-settings-" + Guid.NewGuid().ToString("N"));
    }

    internal static class Application
    {
        internal static string ExecutablePath { get; set; } = Path.Combine(TestEnvironment.Root, "initial", "VRCImageHelper.exe");
    }

    internal static class Environment
    {
        internal enum SpecialFolder { LocalApplicationData }
        internal static string LocalApplicationData { get; set; } = Path.Combine(TestEnvironment.Root, "initial-data");
        internal static string GetFolderPath(SpecialFolder _) => LocalApplicationData;
    }
}

namespace VRCImageHelper.Tools
{
    internal static class FFMpeg
    {
        internal static string[] GetSupportedEncoder(string format) => format switch
        {
            "av1" => ["libaom-av1"],
            "webp" => ["libwebp"],
            "mjpeg" => ["mjpeg"],
            _ => []
        };
    }
}
