using System;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;

namespace BaseAdmin;

public class AdminChat
{
    public void SendToAdminChat(CCSPlayerController player, string message, IStringLocalizer localizer)
    {
        player.PrintToChat($"[{ChatColors.Blue} {localizer["adminchat.label"]} {ChatColors.Default}] {message}");
    }

    public void SendToAdminChatFromPlayer(CCSPlayerController player, string message, IStringLocalizer localizer)
    {
        player.PrintToChat($"[{ChatColors.Blue} {localizer["adminchat.from_players"]} {ChatColors.Default}] {message}");
    }

    public void SendToAllFromAdmin(string message, IStringLocalizer localizer)
    {
        Server.PrintToChatAll($"[{ChatColors.Blue}{localizer["adminchat.all"]}{ChatColors.Default}] {ChatColors.Blue}{localizer["adminchat.admin"]}{ChatColors.Default}: {message}");
    }
    public void SendColorSay(string message, char color, IStringLocalizer localizer)
    {
        Server.PrintToChatAll($"[{ChatColors.Blue}{localizer["adminchat.admin"]}{ChatColors.Default}] {color}{message}");
    }

    public void SendSilentSay(string message, IStringLocalizer localizer)
    {
        Server.PrintToChatAll($"[{ChatColors.Blue}{localizer["adminchat.admin"]}{ChatColors.Default}]: {message}");
    }

    public void SendAdminSayWithName(string adminName, string message, IStringLocalizer localizer)
    {
        Server.PrintToChatAll($"[{ChatColors.Blue}{localizer["adminchat.admin"]} - {adminName}{ChatColors.Default}]: {message}");
    }

    public static (char Color, string Message) ParseColorMessage(string input, char defaultColor)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (defaultColor, string.Empty);

        var trimmed = input.Trim();
        var firstSpace = trimmed.IndexOf(' ');

        string firstWord;
        string remaining;

        if (firstSpace == -1)
        {
            firstWord = trimmed;
            remaining = string.Empty;
        }
        else
        {
            firstWord = trimmed.Substring(0, firstSpace);
            remaining = trimmed.Substring(firstSpace + 1).Trim();
        }

        char color;
        switch (firstWord.ToLowerInvariant())
        {
            case "red":
            case "r":
                color = ChatColors.Red;
                break;
            case "green":
            case "g":
                color = ChatColors.Green;
                break;
            case "lime":
            case "l":
                color = ChatColors.Lime;
                break;
            case "blue":
            case "b":
                color = ChatColors.Blue;
                break;
            case "cyan":
            case "c":
                color = ChatColors.BlueGrey;
                break;
            case "darkblue":
            case "db":
                color = ChatColors.DarkBlue;
                break;
            case "yellow":
            case "y":
                color = ChatColors.Yellow;
                break;
            case "gold":
                color = ChatColors.Gold;
                break;
            case "orange":
            case "o":
                color = ChatColors.Orange;
                break;
            case "purple":
            case "p":
                color = ChatColors.Purple;
                break;
            case "magenta":
            case "m":
                color = ChatColors.Magenta;
                break;
            case "grey":
            case "gray":
                color = ChatColors.Grey;
                break;
            case "olive":
                color = ChatColors.Olive;
                break;
            case "white":
            case "w":
            case "default":
                color = ChatColors.Default;
                break;
            default:
                return (defaultColor, trimmed);
        }

        if (string.IsNullOrWhiteSpace(remaining))
            return (color, string.Empty);

        return (color, remaining);
    }
}