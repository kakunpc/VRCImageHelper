namespace VRCImageHelper.Tools;

using System.Diagnostics;
using System.Text;

internal sealed record ExifToolResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ExifToolCommand
{
    internal static async Task<ExifToolResult> WriteAsync(string executable, string path,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        // -@ stdin retains argument-file semantics without command-line length
        // limits or Windows argv recoding of Unicode image/temp file names.
        var temporaryFiles = new List<string>();
        try
        {
            var lines = new List<string>();
            foreach (var argument in arguments)
            {
                var equals = argument.IndexOf('=');
                if (argument.Contains('\r') || argument.Contains('\n') ||
                    (equals >= 0 && argument.Length > equals + 1 && argument[equals + 1] == ' '))
                {
                    if (!argument.StartsWith('-') || equals < 1)
                        throw new ArgumentException("A multiline argument must be a metadata assignment.", nameof(arguments));
                    // -TAG<=file reads literal UTF-8 bytes. This keeps newlines
                    // out of the argument file without shell quotes or escapes.
                    var valueFile = Path.GetTempFileName();
                    temporaryFiles.Add(valueFile);
                    await File.WriteAllTextAsync(valueFile, argument[(equals + 1)..], new UTF8Encoding(false), cancellationToken);
                    lines.Add(argument[..equals] + "<=" + valueFile);
                }
                else
                {
                    lines.Add(argument);
                }
            }
            lines.Add(FileArgument(path));
            return await RunAsync(executable, lines, cancellationToken);
        }
        finally
        {
            foreach (var temporaryFile in temporaryFiles) File.Delete(temporaryFile);
        }
    }

    internal static Task<ExifToolResult> CopyAsync(string executable, string source, string destination,
        CancellationToken cancellationToken) => RunAsync(executable,
            ["-TagsFromFile", FileArgument(source), "-XMP", FileArgument(destination)], cancellationToken);

    private static string FileArgument(string path)
    {
        if (path.Contains('\r') || path.Contains('\n'))
            throw new ArgumentException("ExifTool file paths cannot contain line breaks.", nameof(path));
        return Path.GetFullPath(path);
    }

    private static ProcessStartInfo CreateStartInfo(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-charset");
        start.ArgumentList.Add("filename=UTF8");
        start.ArgumentList.Add("-@");
        start.ArgumentList.Add("-");
        return start;
    }

    private static async Task<ExifToolResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(CreateStartInfo(executable)) ?? throw new IOException("ExifTool did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            foreach (var argument in arguments)
                await process.StandardInput.WriteLineAsync(argument.AsMemory(), cancellationToken);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw;
        }
        return new ExifToolResult(process.ExitCode, await output, await error);
    }
}
