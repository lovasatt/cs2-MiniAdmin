using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;

namespace BaseAdmin;

public class AdminChat
{
    public void SendToAdminChat(CCSPlayerController player, string message, IStringLocalizer localizer)
    {
        player.PrintToChat(
            $"[{ChatColors.Blue} AdminChat {ChatColors.Default}]{localizer[message]}");
    }
    
    public void SendToAdminChatFromPlayer(CCSPlayerController player, string message, IStringLocalizer localizer)
    {
        player.PrintToChat(
            $"[{ChatColors.Blue} From Players {ChatColors.Default}]{localizer[message]}");
    }
    
    public void SendToAllFromAdmin(string message, IStringLocalizer localizer)
    {
        Server.PrintToChatAll(
            $"[{ChatColors.Blue}ALL{ChatColors.Default}] {ChatColors.Blue}ADMIN{ChatColors.Default}: {localizer[message]}");
    }
}