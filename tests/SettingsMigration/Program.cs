using System.Text;
using System.Text.Json;
using VRCImageHelper.Core;

const string legacyJson = """
{
  "Version": 1,
  "DestDir": "D:\\写真\\VRChat",
  "Format": "WEBP",
  "Encoder": "libwebp",
  "EncoderOption": "-lossless 1",
  "Quality": 73,
  "AlphaFormat": "AVIF",
  "AlphaEncoder": "libaom-av1",
  "AlphaQuality": 34,
  "ParallelCompressionProcesses": 3,
  "DeleteOriginalFile": true,
  "FilePattern": "yyyy-MM\\VRChat_yyyy-MM-dd_hh-mm-ss.fff_XXXXxYYYY.webp",
  "VirtualLens2": { "ApertureMin": 22, "ApertureDefault": 22, "FocalLengthDefault": 85 },
  "Integral": { "BokehShapeDefault": 2 },
  "UnknownLegacyProperty": "keep original bytes"
}
""";

var tests = new (string Name, Action<string, string> Run)[]
{
    ("old settings import preserves bytes, values and existing schema upgrade", (current, legacy) =>
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(legacyJson)).ToArray();
        File.WriteAllBytes(ConfigPath(legacy), bytes);
        var config = ConfigManager.Load(current, legacy);
        Check(config.DestDir == "D:\\写真\\VRChat" && config.Quality == 73 && config.ParallelCompressionProcesses == 3);
        Check(config.Format == "WEBP" && config.Encoder == "libwebp" && config.EncoderOption == "-lossless 1");
        Check(config.AlphaFormat == "AVIF" && config.AlphaQuality == 34 && config.DeleteOriginalFile);
        Check(config.VirtualLens2.FocalLengthDefault == 85 && float.IsPositiveInfinity(config.VirtualLens2.ApertureDefault));
        Check(config.Integral.BokehShapeDefault == 2 && config.Version == 2 && config.FilePattern.Contains("%{_LAYER}%"));
        Check(File.ReadAllBytes(ConfigPath(current)).SequenceEqual(bytes));
        Check(File.ReadAllBytes(ConfigPath(legacy)).SequenceEqual(bytes));
    }),
    ("current settings win even if legacy JSON is malformed", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(current), "{\"Quality\":42}");
        File.WriteAllText(ConfigPath(legacy), "broken");
        Check(ConfigManager.Load(current, legacy).Quality == 42);
        Check(File.ReadAllText(ConfigPath(current)) == "{\"Quality\":42}");
    }),
    ("UTF-16 legacy files remain readable and byte-preserved", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(legacy), legacyJson, Encoding.Unicode);
        var original = File.ReadAllBytes(ConfigPath(legacy));
        Check(ConfigManager.Load(current, legacy).Quality == 73);
        Check(File.ReadAllBytes(ConfigPath(current)).SequenceEqual(original));
    }),
    ("save then repeated launches retain current settings and do not reimport", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(legacy), legacyJson);
        var config = ConfigManager.Load(current, legacy);
        config.Quality = 91;
        Application.ExecutablePath = Path.Combine(current, "VRCImageHelper.exe");
        ConfigManager.Save(config);
        var saved = File.ReadAllBytes(ConfigPath(current));
        File.WriteAllText(ConfigPath(legacy), "{\"Quality\":2}");
        for (var i = 0; i < 3; i++)
            Check(ConfigManager.Load(current, legacy).Quality == 91);
        Check(File.ReadAllBytes(ConfigPath(current)).SequenceEqual(saved));
        // The default production entry point also uses the executable directory.
        Check(ConfigManager.Load().Quality == 91);
    }),
    ("same installation directory loads without rewriting", (current, _) =>
    {
        File.WriteAllText(ConfigPath(current), legacyJson);
        Check(ConfigManager.Load(current, current).Quality == 73);
        Check(File.ReadAllText(ConfigPath(current)) == legacyJson);
    }),
    ("missing settings use defaults without creating a file", (current, legacy) =>
    {
        Check(ConfigManager.Load(current, legacy).Quality == Config.Default.Quality);
        Check(!File.Exists(ConfigPath(current)) && !File.Exists(ConfigPath(legacy)));
    }),
    ("invalid legacy JSON is not copied or overwritten", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(legacy), "broken");
        Throws<JsonException>(() => ConfigManager.Load(current, legacy));
        Check(!File.Exists(ConfigPath(current)) && File.ReadAllText(ConfigPath(legacy)) == "broken");
    }),
    ("invalid current JSON never falls back to legacy", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(current), "broken");
        File.WriteAllText(ConfigPath(legacy), legacyJson);
        Throws<JsonException>(() => ConfigManager.Load(current, legacy));
        Check(File.ReadAllText(ConfigPath(current)) == "broken" && File.ReadAllText(ConfigPath(legacy)) == legacyJson);
    }),
    ("JSON null cannot silently become defaults", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(legacy), "null");
        Throws<JsonException>(() => ConfigManager.Load(current, legacy));
        Check(!File.Exists(ConfigPath(current)));
    }),
    ("current read IO failure never falls back to legacy", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(legacy), legacyJson);
        // A directory at the destination is a deterministic cross-platform IO failure.
        Directory.CreateDirectory(ConfigPath(current));
        Throws<UnauthorizedAccessException>(() => ConfigManager.Load(current, legacy));
        Check(File.ReadAllText(ConfigPath(legacy)) == legacyJson);
        Check(!Directory.EnumerateFiles(current).Any());
    }),
    ("import write failure preserves the old file and does not use defaults", (current, legacy) =>
    {
        File.WriteAllText(ConfigPath(legacy), legacyJson);
        Directory.Delete(current);
        File.WriteAllText(current, "blocked destination");
        Throws<IOException>(() => ConfigManager.Load(current, legacy));
        Check(File.ReadAllText(ConfigPath(legacy)) == legacyJson && File.ReadAllText(current) == "blocked destination");
    }),
    ("default entry point discovers the old MSI installation", (current, _) =>
    {
        var installed = Path.Combine(TestEnvironment.Root, "LocalAppData", "Programs", "VRCImageHelper");
        Directory.CreateDirectory(installed);
        File.WriteAllText(ConfigPath(installed), legacyJson);
        Application.ExecutablePath = Path.Combine(current, "VRCImageHelper.exe");
        Check(ConfigManager.Load().Quality == 73);
        Check(File.ReadAllText(ConfigPath(current)) == legacyJson && File.ReadAllText(ConfigPath(installed)) == legacyJson);
    })
};

try
{
    var index = 0;
    foreach (var test in tests)
    {
        var root = Path.Combine(TestEnvironment.Root, (++index).ToString());
        var current = Path.Combine(root, "net10", "win-x64", "publish");
        var legacy = Path.Combine(root, "old-net6-installation");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(legacy);
        test.Run(current, legacy);
        Console.WriteLine("PASS " + test.Name);
    }
    Console.WriteLine($"{tests.Length} settings migration checks passed; no real user profile, UI, processes or sockets used.");
}
finally
{
    if (Directory.Exists(TestEnvironment.Root)) Directory.Delete(TestEnvironment.Root, recursive: true);
}

static string ConfigPath(string directory) => Path.Combine(directory, "config.json");
static void Check(bool condition)
{
    if (!condition) throw new Exception("Settings migration assertion failed.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
