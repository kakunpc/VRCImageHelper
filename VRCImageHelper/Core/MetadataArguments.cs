namespace VRCImageHelper.Core;

using System.Globalization;

internal static class MetadataArguments
{
    internal static string FormatCreationDate(DateTime value) =>
        value.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);

    internal static List<string> Build(string desc, string makernote, State state)
    {
        var args = new List<string>
        {
            "-n",
            "-overwrite_original",
            "-codedcharacterset=utf8",
            $"-:ImageDescription={desc}",
            $"-:Description={desc}",
            $"-:Comment={desc}",
            $"-makernote={makernote}",
            "-sep",
            ";",
            $"-Keywords={state.RoomInfo.World_name};{string.Join(';', state.Players)}"
        };

        AddDateTimeArgs(args, state.CreationDate, TimeZoneInfo.Local);
        AddCameraArgs(args, state);

        return args;
    }

    internal static void AddDateTimeArgs(List<string> args, string creationDate, TimeZoneInfo timeZone)
    {
        if (!DateTime.TryParseExact(creationDate, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var taken))
            return; // Preserve copied source dates instead of writing an invalid value.

        var timestamp = new DateTimeOffset(taken, timeZone.GetUtcOffset(taken));
        var offset = timestamp.ToString("zzz", CultureInfo.InvariantCulture);
        var dateTime = timestamp.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture) + offset;
        args.Add("-:OffsetTime=" + offset);
        args.Add("-:OffsetTimeOriginal=" + offset);
        args.Add("-:OffsetTimeDigitized=" + offset);
        args.Add("-IPTC:DateCreated=" + timestamp.ToString("yyyy:MM:dd", CultureInfo.InvariantCulture));
        args.Add("-IPTC:TimeCreated=" + timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + offset);
        args.Add("-:CreateDate=" + dateTime);
        args.Add("-:DateTimeOriginal=" + dateTime);
        args.Add("-XMP-photoshop:DateCreated=" + dateTime);
    }

    private static void AddCameraArgs(List<string> args, State state)
    {
        args.AddRange(StateChecker.VirtualLens2.Publish(state.VirtualLens2));
        args.AddRange(StateChecker.Integral.Publish(state.Integral));

        if (!(state.VirtualLens2.Enabled || state.Integral.Enabled))
        {
            args.Add("-:Make=VRChat");
            args.Add("-:Model=VRChat Camera");
        }
    }

}
