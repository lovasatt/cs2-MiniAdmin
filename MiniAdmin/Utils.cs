using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;

namespace BaseAdmin;

public static class Utils
{
    private static readonly Regex ArgRegex = new(
        @"\G(?:""(?<q>[^""]*)""|(?<s>STEAM_[0-5]\s*:\s*[01]\s*:\s*\d+)|(?<w>\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Kick(this CCSPlayerController? player, string reason)
    {
        if (player == null || !player.IsValid)
        {
            Console.WriteLine("[BaseAdmin] Player is null or invalid on kick");
            return;
        }

        if (player.UserId.HasValue)
        {
            var safeReason = (reason ?? "Kicked").Replace("\"", "'").Replace(";", " ").Replace("\n", " ");
            Server.ExecuteCommand(string.Create(CultureInfo.InvariantCulture, $"kickid {player.UserId.Value} \"{safeReason}\""));
        }
    }

    public static long GetUnixEpoch(this DateTime dateTime)
    {
        var unixTime = dateTime.ToUniversalTime() -
                       new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        return (long)unixTime.TotalSeconds;
    }

    public static string[] SplitArgs(string? input, int maxArgs = int.MaxValue)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return Array.Empty<string>();

        var pos = 0;
        while (pos < input.Length)
        {
            while (pos < input.Length && char.IsWhiteSpace(input[pos])) pos++;
            if (pos >= input.Length) break;

            if (result.Count == maxArgs - 1)
            {
                result.Add(input.Substring(pos).Trim().Trim('"', '\'').Trim());
                break;
            }

            var match = ArgRegex.Match(input, pos);
            if (!match.Success) break;

            if (match.Groups["q"].Success)
                result.Add(match.Groups["q"].Value);
            else if (match.Groups["s"].Success)
                result.Add(Regex.Replace(match.Groups["s"].Value, @"\s+", string.Empty));
            else
                result.Add(match.Groups["w"].Value);

            pos = match.Index + match.Length;
        }

        return result.ToArray();
    }

    public static bool TryParseDuration(string input, out int seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var clean = input.Trim().Trim('"', '\'', '`').Trim().ToLowerInvariant();

        if (clean is "0" or "perm" or "permanent" or "perma" or "never")
        {
            seconds = 0;
            return true;
        }

        var match = Regex.Match(clean, @"^(\d+)\s*([a-z]*)$");
        if (!match.Success) return false;

        if (!long.TryParse(match.Groups[1].Value, out var value) || value < 0 || value > 1_000_000)
            return false;

        long multiplier = match.Groups[2].Value switch
        {
            "s" or "sec" or "secs" or "second" or "seconds" or "" => 1,
            "m" or "min" or "mins" or "minute" or "minutes" => 60,
            "h" or "hr" or "hrs" or "hour" or "hours" => 3600,
            "d" or "day" or "days" => 86400,
            "w" or "wk" or "wks" or "week" or "weeks" => 604800,
            "mo" or "month" or "months" => 2592000,
            "y" or "yr" or "yrs" or "year" or "years" => 31536000,
            _ => -1
        };

        if (multiplier < 0) return false;

        seconds = (int)Math.Min(value * multiplier, 315_360_000L);
        return true;
    }

    public static bool TryParseSteamId(string input, out ulong steamId64, out string steamId2)
    {
        steamId64 = 0;
        steamId2 = string.Empty;

        if (string.IsNullOrWhiteSpace(input)) return false;
        var clean = input.Trim().Trim('"', '\'', ' ');

        var parts = clean.Split(':');
        if (parts.Length == 3 && parts[0].StartsWith("STEAM_", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(parts[1], out var y) && ulong.TryParse(parts[2], out var z))
            {
                steamId64 = 76561197960265728UL + (z * 2) + y;
                steamId2 = $"STEAM_1:{y}:{z}";
                return true;
            }
        }

        if (ulong.TryParse(clean, out var s64) && s64 >= 76561197960265728UL)
        {
            steamId64 = s64;
            var diff = s64 - 76561197960265728UL;
            var y = diff % 2;
            var z = diff / 2;
            steamId2 = $"STEAM_1:{y}:{z}";
            return true;
        }

        return false;
    }
}