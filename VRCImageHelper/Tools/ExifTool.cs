namespace VRCImageHelper.Tools;

using Microsoft.Toolkit.Uwp.Notifications;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal class ExifTool
{
    public static CancellationToken s_cancellationToken;
    private static string? s_exitToolPath;
    private static bool s_exitToolDownloading;
    public static string GetExifTool()
    {
        while (s_exitToolDownloading)
        {
            Task.Delay(100).Wait();
            if (s_cancellationToken.IsCancellationRequested)
                return "";
        }

        if (s_exitToolPath is null)
        {
            s_exitToolDownloading = true;
            s_exitToolPath = CheckExistsAndDownload("exiftool.exe", "https://sourceforge.net/projects/exiftool/files/latest/download");
            s_exitToolDownloading = false;
        }

        return s_exitToolPath;
    }
    private static string CheckExistsAndDownload(string fileName, string url)
    {
        var path = Executables.Find("exiftool.exe");

        if (path is not null) return path;

        var dir = Executables.Download(fileName, url, s_cancellationToken);

        var extracted = Directory.GetDirectories(dir).First();

        File.Move(extracted + "\\exiftool(-k).exe", dir + "\\exiftool.exe");
        Directory.Move(extracted + "\\exiftool_files", dir + "\\exiftool_files");

        return dir + "\\exiftool.exe";
    }

    public static Task<bool> Copy(string source, string destination) =>
        Execute(() => ExifToolCommand.CopyAsync(GetExifTool(), source, destination, s_cancellationToken));

    public static Task<bool> Write(string path, List<string> args) =>
        Execute(() => ExifToolCommand.WriteAsync(GetExifTool(), path, args, s_cancellationToken));

    private static async Task<bool> Execute(Func<Task<ExifToolResult>> run)
    {
        try
        {
            var result = await run();
            // Preserve diagnostics in Rider/the inherited error stream even
            // when ExifTool reports warnings with a successful exit code.
            if (!string.IsNullOrEmpty(result.StandardError))
                Console.Error.Write(result.StandardError);
            if (result.ExitCode == 0)
                return true;

            var log = result.StandardOutput + result.StandardError;
            UI.SendNotify.Send(Properties.Resources.NotifyErrorExiftool + ":\n", false, (e) =>
            {
                MessageBox.Show(ToastArguments.Parse(e.Argument).Get("Message"));
            }, new Dictionary<string, string> { { "Message", log } });
            return false;
        }
        catch (OperationCanceledException) when (s_cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            UI.SendNotify.Send(Properties.Resources.NotifyError + ":\n" + ex.Message, false, (e) =>
            {
                MessageBox.Show(ToastArguments.Parse(e.Argument).Get("Message"));
            }, new Dictionary<string, string> { { "Message", ex.Message } });
            return false;
        }
    }
}
