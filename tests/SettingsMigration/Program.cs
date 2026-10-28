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

var tests = new (string Name, Action<string, string, string> Run)[]
{
    ("MSI to Rider imports values and schema without changing old JSON", (data, msi, debug) =>
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(legacyJson)).ToArray();
        File.WriteAllBytes(Legacy(msi), bytes);
        var config = ConfigManager.Load(data, debug);
        Check(config.DestDir == "D:\\写真\\VRChat" && config.Quality == 73 && config.ParallelCompressionProcesses == 3);
        Check(config.Format == "WEBP" && config.Encoder == "libwebp" && config.EncoderOption == "-lossless 1");
        Check(config.AlphaFormat == "AVIF" && config.AlphaQuality == 34 && config.DeleteOriginalFile);
        Check(config.VirtualLens2.FocalLengthDefault == 85 && float.IsPositiveInfinity(config.VirtualLens2.ApertureDefault));
        Check(config.Integral.BokehShapeDefault == 2 && config.Version == 2 && config.FilePattern.Contains("%{_LAYER}%"));
        Check(File.ReadAllBytes(Legacy(msi)).SequenceEqual(bytes) && !File.Exists(Legacy(debug)));
        Check(File.ReadAllText(Canonical(data)).Contains("<VRCImageHelperSettings"));
    }),
    ("MSI and different Rider build locations share the canonical settings", (data, msi, debug) =>
    {
        VRCImageHelper.Core.Environment.LocalApplicationData = data;
        Application.ExecutablePath = Path.Combine(msi, "VRCImageHelper.exe");
        var config = new Config { Quality = 67, DestDir = "shared", Version = 2 };
        ConfigManager.Save(config);
        Application.ExecutablePath = Path.Combine(debug, "VRCImageHelper.exe");
        Check(ConfigManager.Load().Quality == 67);
        Check(ConfigManager.Load(data, Path.Combine(debug, "another-version")).Quality == 67);
        Check(ConfigManager.Load(data, msi).DestDir == "shared");
        Check(!File.Exists(Legacy(msi)) && !File.Exists(Legacy(debug)));
    }),
    ("existing canonical XML wins over different or corrupt legacy settings", (data, msi, debug) =>
    {
        ConfigStorage.Save(data, new Config { Quality = 42 });
        var original = File.ReadAllBytes(Canonical(data));
        File.WriteAllText(Legacy(msi), "broken");
        File.WriteAllText(Legacy(debug), legacyJson);
        Check(ConfigManager.Load(data, debug).Quality == 42);
        Check(File.ReadAllBytes(Canonical(data)).SequenceEqual(original));
    }),
    ("save and repeated launches never write either legacy file", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), legacyJson);
        var config = ConfigManager.Load(data, debug);
        config.Quality = 91;
        VRCImageHelper.Core.Environment.LocalApplicationData = data;
        Application.ExecutablePath = Path.Combine(debug, "VRCImageHelper.exe");
        ConfigManager.Save(config);
        var saved = File.ReadAllBytes(Canonical(data));
        for (var i = 0; i < 3; i++) Check(ConfigManager.Load().Quality == 91);
        Check(File.ReadAllBytes(Canonical(data)).SequenceEqual(saved));
        Check(File.ReadAllText(Legacy(msi)) == legacyJson && !File.Exists(Legacy(debug)));
    }),
    ("executable-adjacent legacy import supports UTF-16", (data, _, debug) =>
    {
        File.WriteAllText(Legacy(debug), legacyJson, Encoding.Unicode);
        var original = File.ReadAllBytes(Legacy(debug));
        Check(ConfigManager.Load(data, debug).Quality == 73);
        Check(File.ReadAllBytes(Legacy(debug)).SequenceEqual(original));
    }),
    ("different legacy candidates are rejected without choosing one", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), legacyJson);
        File.WriteAllText(Legacy(debug), "{\"Quality\":8}");
        Throws<InvalidDataException>(() => ConfigManager.Load(data, debug));
        Check(!File.Exists(Canonical(data)) && File.ReadAllText(Legacy(msi)) == legacyJson);
        Check(File.ReadAllText(Legacy(debug)) == "{\"Quality\":8}");
    }),
    ("identical duplicate legacy files may be imported", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), legacyJson);
        File.WriteAllText(Legacy(debug), legacyJson);
        Check(ConfigManager.Load(data, debug).Quality == 73);
        Check(File.ReadAllText(Legacy(msi)) == legacyJson && File.ReadAllText(Legacy(debug)) == legacyJson);
    }),
    ("fresh profile defaults do not write any file", (data, msi, debug) =>
    {
        Check(ConfigManager.Load(data, debug).Quality == Config.Default.Quality);
        Check(!File.Exists(Canonical(data)) && !File.Exists(Legacy(msi)) && !File.Exists(Legacy(debug)));
    }),
    ("missing or relative user-data directory cannot fall back to a build folder", (_, _, debug) =>
    {
        Throws<ArgumentException>(() => ConfigStorage.Load("", debug));
        Throws<ArgumentException>(() => ConfigStorage.Save("relative-data", new Config()));
    }),
    ("invalid legacy JSON is neither changed nor imported", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), "broken");
        Throws<JsonException>(() => ConfigManager.Load(data, debug));
        Check(!File.Exists(Canonical(data)) && File.ReadAllText(Legacy(msi)) == "broken");
    }),
    ("null legacy configuration is rejected", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), "null");
        Throws<InvalidDataException>(() => ConfigManager.Load(data, debug));
        Check(!File.Exists(Canonical(data)) && File.ReadAllText(Legacy(msi)) == "null");
    }),
    ("corrupt canonical XML cannot fall back to legacy or be rewritten", (data, msi, debug) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Canonical(data))!);
        File.WriteAllText(Canonical(data), "broken XML");
        File.WriteAllText(Legacy(msi), legacyJson);
        Throws<InvalidOperationException>(() => ConfigManager.Load(data, debug));
        Check(File.ReadAllText(Canonical(data)) == "broken XML" && File.ReadAllText(Legacy(msi)) == legacyJson);
    }),
    ("canonical read failure cannot silently return defaults", (data, msi, debug) =>
    {
        Directory.CreateDirectory(Canonical(data));
        File.WriteAllText(Legacy(msi), legacyJson);
        Throws<UnauthorizedAccessException>(() => ConfigManager.Load(data, debug));
        Check(File.ReadAllText(Legacy(msi)) == legacyJson);
    }),
    ("import write failure leaves the legacy file untouched", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), legacyJson);
        File.WriteAllText(Path.Combine(data, "nekomimiStudio"), "blocked destination");
        Throws<IOException>(() => ConfigManager.Load(data, debug));
        Check(File.ReadAllText(Legacy(msi)) == legacyJson && !File.Exists(Legacy(debug)));
    }),
    ("save serialization failure preserves canonical XML and in-memory input", (data, _, _) =>
    {
        ConfigStorage.Save(data, new Config { Quality = 57 });
        var original = File.ReadAllBytes(Canonical(data));
        var config = new Config { DestDir = "\u0001", Quality = 12 };
        VRCImageHelper.Core.Environment.LocalApplicationData = data;
        Throws<InvalidOperationException>(() => ConfigManager.Save(config));
        Check(File.ReadAllBytes(Canonical(data)).SequenceEqual(original));
        Check(float.IsPositiveInfinity(config.VirtualLens2.ApertureDefault));
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(Canonical(data))!, "*.tmp").Any());
    }),
    ("concurrent initial imports do not overwrite or corrupt canonical settings", (data, msi, debug) =>
    {
        File.WriteAllText(Legacy(msi), legacyJson);
        Parallel.For(0, 12, _ => Check(ConfigStorage.Load(data, debug).Quality == 73));
        Check(ConfigManager.Load(data, debug).Quality == 73 && File.ReadAllText(Legacy(msi)) == legacyJson);
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(Canonical(data))!, "*.tmp").Any());
    }),
    ("MSI launch imports its adjacent file once, without ambiguity", (data, msi, _) =>
    {
        File.WriteAllText(Legacy(msi), legacyJson);
        VRCImageHelper.Core.Environment.LocalApplicationData = data;
        Application.ExecutablePath = Path.Combine(msi, "VRCImageHelper.exe");
        Check(ConfigManager.Load().Quality == 73 && File.Exists(Canonical(data)));
        Check(File.ReadAllText(Legacy(msi)) == legacyJson);
    })
};

try
{
    var index = 0;
    foreach (var test in tests)
    {
        var root = Path.Combine(TestEnvironment.Root, (++index).ToString());
        var data = Path.Combine(root, "LocalAppData");
        var msi = Path.Combine(data, "Programs", "VRCImageHelper");
        var debug = Path.Combine(root, "Rider", "bin", "Debug", "net10.0-windows", "win-x64");
        Directory.CreateDirectory(msi);
        Directory.CreateDirectory(debug);
        test.Run(data, msi, debug);
        Console.WriteLine("PASS " + test.Name);
    }
    Console.WriteLine($"{tests.Length} settings checks passed; all storage is isolated in temporary folders.");
}
finally
{
    if (Directory.Exists(TestEnvironment.Root)) Directory.Delete(TestEnvironment.Root, recursive: true);
}

static string Legacy(string directory) => Path.Combine(directory, "config.json");
static string Canonical(string data) => ConfigStorage.GetSettingsPath(data);
static void Check(bool condition)
{
    if (!condition) throw new Exception("Settings assertion failed.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
