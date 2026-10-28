namespace VRCImageHelper.Core.StateChecker;

using System.Diagnostics;
using System.Text.RegularExpressions;

internal static class VRChat
{
    public static void WorldId(object sender, NewLineEventArgs e) => ApplyWorldId(State.Current, e.Line);

    internal static bool ApplyWorldId(State state, string line)
    {
        var match = Regex.Match(line, @"(?<DateTime>\d+\.\d+\.\d+ \d+:\d+:\d+) .*\[Behaviour\] Joining (?<WorldID>wrld_[^:]+):(?<InstanceID>[^~]*)(?:~(?<Options>.*))?$");
        if (match.Success)
        {
            Debug.WriteLine($"Joining {match.Groups["WorldID"]}, {match.Groups["InstanceID"]}, {match.Groups["Options"]}");
            if (!(state.RoomInfo.Instance_id == match.Groups["InstanceID"].Value && state.RoomInfo.World_id == match.Groups["WorldID"].Value))
            {
                state.RoomInfo.JoinDateTime = match.Groups["DateTime"].Value;
            }
            state.RoomInfo.World_id = match.Groups["WorldID"].Value;
            state.RoomInfo.Instance_id = match.Groups["InstanceID"].Value;

            if (match.Groups["Options"].Success)
            {
                var options = match.Groups["Options"].Value.Split("~").Select(param =>
                {
                    var match = Regex.Match(param, @"^(.*?)(?:\((.*?)\))?$");
                    return new[] { match.Groups[1].Value, match.Groups[2].Value };
                });

                state.RoomInfo.Permission = "public";
                state.RoomInfo.Organizer = "public";

                foreach (var option in options)
                {
                    var key = option[0];
                    var value = option[1];

                    if (key == "private" || key == "friends" || key == "hidden" || key == "group")
                    {
                        state.RoomInfo.Permission = key;
                        state.RoomInfo.Organizer = value;
                    }

                    if (key == "canRequestInvite")
                    {
                        state.RoomInfo.Permission += "_plus";
                    }

                    if (key == "groupAccessType")
                    {
                        state.RoomInfo.Permission += "_" + value;
                    }
                }
            } else
            {
                state.RoomInfo.Permission = "public";
                state.RoomInfo.Organizer = "public";
            }
            state.Players.Clear();
        }
        return match.Success;
    }

    public static void JoinRoom(object sender, NewLineEventArgs e) => ApplyJoinRoom(State.Current, e.Line);

    internal static bool ApplyJoinRoom(State state, string line)
    {
        var match = Regex.Match(line, "\\[Behaviour\\] Joining or Creating Room: (.*)");
        if (match.Success)
        {
            Debug.WriteLine($"Joining {match.Groups[1]}");
            state.RoomInfo.World_name = match.Groups[1].Value;
        }
        return match.Success;
    }

    public static void PlayerJoin(object sender, NewLineEventArgs e) => ApplyPlayerJoin(State.Current, e.Line);

    internal static bool ApplyPlayerJoin(State state, string line)
    {
        var match = Regex.Match(line, "\\[Behaviour\\] OnPlayerJoined (.*?) \\((usr_.*)\\)");
        if (match.Success)
        {
            Debug.WriteLine($"Join {match.Groups[1]}, {match.Groups[2]}");
            state.Players.Add(match.Groups[1].Value);
        }
        return match.Success;
    }

    public static void PlayerLeft(object sender, NewLineEventArgs e) => ApplyPlayerLeft(State.Current, e.Line);

    internal static bool ApplyPlayerLeft(State state, string line)
    {
        var match = Regex.Match(line, "\\[Behaviour\\] OnPlayerLeft (.*) \\((usr_.*)\\)");
        if (match.Success)
        {
            Debug.WriteLine($"Left {match.Groups[1]}, {match.Groups[2]}");
            state.Players.Remove(match.Groups[1].Value);
        }
        return match.Success;
    }

    public static void Quit(object sender, NewLineEventArgs e)
    {
        var match = Regex.Match(e.Line, "VRCApplication: OnApplicationQuit");
        if (match.Success)
        {
            Debug.WriteLine("Quit");
            State.Current = new State();
        }
    }

    public static void ChangeAvater(object sender, OscEventArgs e)
    {
        if (e.Path == "/avatar/change")
        {
            State.Current.VirtualLens2 = new VirtualLens2State();
            State.Current.Integral = new IntegralState();
        }
    }
}
