namespace VRCImageHelper.Core;

using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;

internal static class ConfigStorage
{
    private static readonly XmlSerializer s_serializer = new(typeof(Config), new XmlRootAttribute("VRCImageHelperSettings"));

    internal static string GetSettingsPath(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        if (!Path.IsPathFullyQualified(localApplicationData))
            throw new ArgumentException("The per-user application data directory must be absolute.", nameof(localApplicationData));
        return Path.Combine(localApplicationData, "nekomimiStudio", "VRCImageHelper", "settings.xml");
    }

    internal static Config Load(string localApplicationData, string executableDirectory)
    {
        var path = GetSettingsPath(localApplicationData);
        var current = ReadIfPresent(path);
        if (current is not null)
            return ReadXml(current);

        // Read only these two known locations. Never search other projects or
        // pick an arbitrary older build's settings by timestamp.
        var candidates = new[]
        {
            Path.Combine(executableDirectory, "config.json"),
            Path.Combine(localApplicationData, "Programs", "VRCImageHelper", "config.json")
        }.Select(Path.GetFullPath).Distinct(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        byte[]? legacy = null;
        foreach (var candidate in candidates)
        {
            var source = ReadIfPresent(candidate);
            if (source is null)
                continue;
            if (legacy is not null && !legacy.SequenceEqual(source))
                throw new InvalidDataException("Multiple different legacy settings files exist. Choose the settings to import before starting VRCImageHelper.");
            legacy = source;
        }
        if (legacy is null)
            return new Config();

        using var stream = new MemoryStream(legacy, writable: false);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var config = Validate(JsonSerializer.Deserialize<Config>(reader.ReadToEnd()));
        try
        {
            WriteXml(path, config, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // A concurrent launch created canonical settings. Never replace it.
            return ReadXml(File.ReadAllBytes(path));
        }
        return config;
    }

    internal static void Save(string localApplicationData, Config config) =>
        WriteXml(GetSettingsPath(localApplicationData), Validate(config), overwrite: true);

    private static Config Validate(Config? config)
    {
        if (config is null || config.VirtualLens2 is null || config.Integral is null)
            throw new InvalidDataException("The settings must contain a configuration object and camera settings.");
        return config;
    }

    private static Config ReadXml(byte[] source)
    {
        using var stream = new MemoryStream(source, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        return Validate(s_serializer.Deserialize(reader) as Config);
    }

    private static void WriteXml(string path, Config config, bool overwrite)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, CloseOutput = false }))
                    s_serializer.Serialize(writer, config);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static byte[]? ReadIfPresent(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        // Access/IO errors must not turn into fresh default settings.
    }
}
