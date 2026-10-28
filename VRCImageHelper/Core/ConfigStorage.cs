namespace VRCImageHelper.Core;

using System.Text.Json;

internal static class ConfigStorage
{
    // Settings have always lived beside the executable. The .NET 10 publish
    // directory differs from the old installation, so import only when absent.
    internal static Config Load(string currentDirectory, string legacyDirectory)
    {
        var currentPath = Path.Combine(currentDirectory, "config.json");
        var current = ReadIfPresent(currentPath);
        if (current is not null)
            return Deserialize(current);

        var legacy = ReadIfPresent(Path.Combine(legacyDirectory, "config.json"));
        if (legacy is null)
            return new Config();

        var config = Deserialize(legacy); // Validate before writing anything.
        Directory.CreateDirectory(currentDirectory);
        var temporaryPath = Path.Combine(currentDirectory, $".config-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, legacy);
            try
            {
                File.Move(temporaryPath, currentPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(currentPath))
            {
                // Another launch saved settings during the import. It wins.
                return Deserialize(File.ReadAllBytes(currentPath));
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
        return config;
    }

    private static byte[]? ReadIfPresent(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        // Access/IO errors must not turn into a fresh default configuration.
    }

    private static Config Deserialize(byte[] source)
    {
        // Match the old File.ReadAllText reader's UTF-8 / UTF-16 BOM support.
        using var stream = new MemoryStream(source, writable: false);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return JsonSerializer.Deserialize<Config>(reader.ReadToEnd())
            ?? throw new JsonException("The settings file must contain a configuration object.");
    }
}
