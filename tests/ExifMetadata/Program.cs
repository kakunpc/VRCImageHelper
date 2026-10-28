using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VRCImageHelper.Core;
using VRCImageHelper.Core.StateChecker;
using VRCImageHelper.Tools;

if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the path to a disposable ExifTool executable/script. No tool is downloaded automatically.");
var executable = Path.GetFullPath(args[0]);
var root = Path.Combine(Path.GetTempPath(), "VRCImageHelper-exif-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var culture = CultureInfo.CurrentCulture;
try
{
    var baseline = NewImage("baseline.jpg");
    var oldFile = Path.Combine(root, "old.args");
    await File.WriteAllTextAsync(oldFile, "-n\n-overwrite_original\n-sep \";\"\n-:DateCreated=2026:10:28:\n-XMP-photoshop:DateCreated=2026:10:28 12:34:56+09:00:00\n");
    var old = await Run("-@", oldFile, baseline);
    Check(old.Error.Contains("Invalid TAG name") && old.Error.Contains("Invalid date/time"), "original warnings reproduced");
    Console.WriteLine("PASS original separator and DateCreated warnings reproduced on a generated JPEG");

    foreach (var name in new[] { "fi-FI", "th-TH", "ar-SA", "fr-FR" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        Check(MetadataArguments.FormatCreationDate(new DateTime(2026, 10, 28, 12, 34, 56)) == "2026:10:28 12:34:56", "culture-independent queue date");
    }
    Console.WriteLine("PASS invariant Gregorian date and time separators under four non-English cultures");

    var zones = new[] { TimeSpan.Zero, TimeSpan.FromHours(9), TimeSpan.FromHours(-7), TimeSpan.FromMinutes(345), TimeSpan.FromMinutes(-210) };
    foreach (var offset in zones)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("fixture", offset, "fixture", "fixture");
        var dateArgs = new List<string> { "-overwrite_original" };
        MetadataArguments.AddDateTimeArgs(dateArgs, "2026:10:28 12:34:56", zone);
        var expectedOffset = new DateTimeOffset(2026, 10, 28, 12, 34, 56, offset).ToString("zzz", CultureInfo.InvariantCulture);
        var path = NewImage("date " + offset.Ticks + ".jpg");
        var write = await ExifToolCommand.WriteAsync(executable, path, dateArgs, default);
        Check(write.ExitCode == 0 && string.IsNullOrWhiteSpace(write.StandardError), write.StandardError);
        using var read = await Read(path);
        var tags = read.RootElement[0];
        Check(tags.GetProperty("XMP-photoshop:DateCreated").GetString() == "2026:10:28 12:34:56" + expectedOffset, "XMP date with HH:mm offset");
        Check(tags.GetProperty("IPTC:DateCreated").GetString() == "2026:10:28", "IPTC date without trailing colon");
        Check(tags.GetProperty("ExifIFD:OffsetTimeOriginal").GetString() == expectedOffset, "original offset");
    }
    Console.WriteLine("PASS real ExifTool date writes: UTC, positive/negative, half/quarter-hour offsets");
    var ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    foreach (var (date, offset) in new[] { ("2026:01:28 12:00:00", "-05:00"), ("2026:07:28 12:00:00", "-04:00") })
    {
        var values = new List<string>();
        MetadataArguments.AddDateTimeArgs(values, date, ny);
        Check(values.Contains("-:OffsetTime=" + offset), "offset follows date-specific daylight saving time");
    }
    var invalid = new List<string>();
    MetadataArguments.AddDateTimeArgs(invalid, "not a date", TimeZoneInfo.Utc);
    Check(invalid.Count == 0, "invalid state dates do not write invalid metadata");
    Console.WriteLine("PASS date-specific DST and invalid-date preservation");

    var state = new State { CreationDate = "2026:10:28 12:34:56" };
    state.RoomInfo.World_name = "日本 World;夜 \"quoted\"";
    state.Players = ["Alice Smith", "Bob;日本"];
    var description = "日本語 spaces; \"quotes\" \\path\\file\r\n-execute\n-overwrite_original\n$HOME @literal";
    var valuesToWrite = MetadataArguments.Build(description, "Zml4dHVyZQ==", state);
    var title = " leading spaces; \"quotes\" 日本 $HOME @literal";
    valuesToWrite.Add("-XMP-dc:Title=" + title);
    Check(valuesToWrite.Contains("-sep") && valuesToWrite[valuesToWrite.IndexOf("-sep") + 1] == ";", "separate option arguments");
    var specialPath = NewImage(OperatingSystem.IsWindows() ? "日本 spaces; file.jpg" : "日本 spaces; \"file\".jpg");
    var result = await ExifToolCommand.WriteAsync(executable, specialPath, valuesToWrite, default);
    Check(result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StandardError), result.StandardError);
    using (var read = await Read(specialPath))
    {
        var tags = read.RootElement[0];
        Check(tags.GetProperty("XMP-dc:Description").GetString() == description, "literal quotes, backslashes, newlines and Japanese");
        Check(tags.GetProperty("XMP-dc:Title").GetString() == title, "leading spaces and literal dollar/at signs");
        var keywords = tags.GetProperty("IPTC:Keywords").EnumerateArray().Select(v => v.GetString()).ToArray();
        Check(keywords.SequenceEqual(new[] { "日本 World", "夜 \"quoted\"", "Alice Smith", "Bob", "日本" }), "existing semicolon-separated keyword-list semantics");
        Check(tags.GetProperty("IFD0:Make").GetString() == "VRChat", "default camera intent preserved");
    }
    Check(!result.StandardOutput.Contains("{ready}"), "newlines cannot become execute commands");
    Console.WriteLine("PASS argument-file boundaries and literal values: spaces, semicolons, quotes, Japanese, newlines and backslashes");

    var copyPath = NewImage("copy 日本; file.jpg");
    var copied = await ExifToolCommand.CopyAsync(executable, specialPath, copyPath, default);
    Check(copied.ExitCode == 0 && string.IsNullOrWhiteSpace(copied.StandardError), copied.StandardError);
    using (var read = await Read(copyPath)) Check(read.RootElement[0].GetProperty("XMP-dc:Description").GetString() == description, "XMP copy");
    Console.WriteLine("PASS XMP copy with literal Unicode paths");

    var largePath = NewImage("long metadata.jpg");
    var largeTitle = new string('x', 40_000) + " 日本";
    var large = await ExifToolCommand.WriteAsync(executable, largePath, new[] { "-overwrite_original", "-XMP-dc:Title=" + largeTitle }, default);
    Check(large.ExitCode == 0 && string.IsNullOrWhiteSpace(large.StandardError), large.StandardError);
    using (var read = await Read(largePath)) Check(read.RootElement[0].GetProperty("XMP-dc:Title").GetString() == largeTitle, "large metadata remains outside the process command line");
    var invalidPath = Path.Combine(root, "invalid.jpg");
    await File.WriteAllTextAsync(invalidPath, "not an image");
    var failed = await ExifToolCommand.WriteAsync(executable, invalidPath, new[] { "-XMP-dc:Title=test" }, default);
    Check(failed.ExitCode != 0 && failed.StandardError.Contains("Error"), "failed writes capture stderr");
    Console.WriteLine("PASS large metadata argument files and failed-write stderr capture");

    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    state.VirtualLens2.Enabled = true;
    state.VirtualLens2.FocalLength = 42.5f;
    state.VirtualLens2.ApertureValue = 2.8f;
    var camera = VirtualLens2.Publish(state.VirtualLens2);
    Check(camera.Contains("-:FocalLength=42.5") && camera.Contains("-:FNumber=2.8"), "invariant lens numbers");
    state.Integral.Enabled = true;
    state.Integral.ExposureState = [new() { ExposureTime = 0.5f, FocalLength = 42.5f, ApertureValue = 2.8f }, new() { ExposureTime = 0.25f, FocalLength = 42.5f, ApertureValue = 2.8f, LensModel = "日本 lens; \"quoted\"" }];
    var integral = Integral.Publish(state.Integral);
    Check(integral.Contains("-:CompositeImageCount=2 2"), "no shell-only quotes around count");
    Check(integral.Any(v => v.StartsWith("-:CompositeImageExposureTimes=") && v.Contains("0.75") && !v.Contains('"')), "unquoted invariant exposure list");
    var cameraPath = NewImage("camera.jpg");
    var cameraResult = await ExifToolCommand.WriteAsync(executable, cameraPath, new[] { "-n", "-overwrite_original" }.Concat(integral).ToList(), default);
    Check(cameraResult.ExitCode == 0 && string.IsNullOrWhiteSpace(cameraResult.StandardError), cameraResult.StandardError);
    using (var read = await Read(cameraPath))
    {
        var tags = read.RootElement[0];
        Check(tags.GetProperty("ExifIFD:FocalLength").GetString() == "42.5 mm", "camera metadata round trip");
        Check(tags.GetProperty("ExifIFD:CompositeImageCount").GetString() == "2 2", "multi-exposure count round trip");
    }
    Console.WriteLine("PASS real ExifTool camera writes: invariant numbers and unquoted multi-exposure lists");
    Console.WriteLine("All metadata checks passed using disposable generated images and the supplied ExifTool only.");
}
finally
{
    CultureInfo.CurrentCulture = culture;
    Directory.Delete(root, recursive: true);
    if (Directory.Exists(TestEnvironment.Root)) Directory.Delete(TestEnvironment.Root, recursive: true);
}

string NewImage(string name)
{
    var path = Path.Combine(root, name);
    File.WriteAllBytes(path, GeneratedJpeg.Bytes);
    return path;
}
async Task<(int Code, string Output, string Error)> Run(params string[] options)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var option in options) start.ArgumentList.Add(option);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    return (process.ExitCode, await output, await error);
}
async Task<JsonDocument> Read(string path)
{
    var result = await Run("-j", "-G1", "-s", "-charset", "filename=UTF8", path);
    Check(result.Code == 0 && string.IsNullOrWhiteSpace(result.Error), result.Error);
    return JsonDocument.Parse(result.Output);
}
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
