using System;
using System.Linq;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using static BaseAdmin.BaseAdmin;

namespace BaseAdmin;

public class Menu
{
    private readonly BaseAdmin _baseAdmin;

    public Menu(BaseAdmin baseAdmin)
    {
        _baseAdmin = baseAdmin;
    }

    private CCSPlayerController? GetTargetFromOptionText(string optionText)
    {
        var lastOpen = optionText.LastIndexOf('[');
        var lastClose = optionText.LastIndexOf(']');
        if (lastOpen != -1 && lastClose > lastOpen)
        {
            var str = optionText.Substring(lastOpen + 1, lastClose - lastOpen - 1);
            if (int.TryParse(str, out var userId))
            {
                var player = Utilities.GetPlayerFromUserid(userId);
                return player != null && player.IsValid ? player : null;
            }
        }
        return null;
    }

    public void CreateMenu()
    {
        var adminMenu = new ChatMenu(_baseAdmin.Prefix);
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.slay_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Slay))
                OpenSlayMenu(player);
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.kick_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Kick))
                OpenKickMenu(player);
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.mute_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Generic))
                OpenMuteMenu(player);
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.unmute_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Unban))
                OpenUnmuteMenu(player);
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.ban_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Ban))
                OpenBanMenu(player);
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.change_map"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Changemap))
                OpenChangeMapMenu(player);
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.add_admin"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Root))
                OpenAddAdminMenu(player);
        });

        _baseAdmin.AddCommand("css_admin", "admin menu", (player, info) =>
        {
            if (player == null) return;
            if (!_baseAdmin.IsAdmin(player.Index))
            {
                _baseAdmin.PrintToChat(player, _baseAdmin.Localizer["no_access"]);
                return;
            }

            MenuManager.OpenChatMenu(player, adminMenu);
        });
    }

    private bool CanPunish(CCSPlayerController controller, CCSPlayerController target, AdminFlag requiredFlag, bool silent = false)
    {
        if (!controller.IsValid || !target.IsValid)
            return false;

        if (!_baseAdmin.HasAdminFlag(controller, requiredFlag))
        {
            if (!silent) _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["no_access"]);
            return false;
        }

        if (controller.SteamID == target.SteamID)
        {
            if (!silent) _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["punish.self"]);
            return false;
        }

        if (_baseAdmin.IsAdmin(target.Index) &&
            !_baseAdmin.PlayerImmunityComparison(controller, target))
        {
            if (!silent) _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["punish.immunity"]);
            return false;
        }

        return true;
    }

    private void OpenSlayMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.all"], (controller, _) =>
        {
            if (!_baseAdmin.CheckingForAdminAndFlag(controller, AdminFlag.Slay)) return;

            foreach (var player in Utilities.GetPlayers())
            {
                if (!player.IsValid || !player.PawnIsAlive)
                    continue;

                if (!CanPunish(controller, player, AdminFlag.Slay, silent: true))
                    continue;

                player.PlayerPawn?.Value?.CommitSuicide(true, true);
            }

            _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["slay.all", controller.PlayerName]);
        });

        foreach (var player in Utilities.GetPlayers().Where(u => u.IsValid && u.PawnIsAlive))
        {
            menu.AddMenuOption($"{player.PlayerName} [{player.UserId}]", (controller, option) =>
            {
                var target = GetTargetFromOptionText(option.Text);

                if (target == null || !target.PawnIsAlive)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.dead_or_invalid"]);
                    return;
                }
                if (!CanPunish(controller, target, AdminFlag.Slay))
                    return;

                target.PlayerPawn?.Value?.CommitSuicide(true, true);
                _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["slay.killed_by", controller.PlayerName, target.PlayerName]);
            });
        }

        MenuManager.OpenChatMenu(caller, menu);
    }

    private void OpenKickMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV))
        {
            menu.AddMenuOption($"{player.PlayerName} [{player.UserId}]", (controller, option) =>
            {
                var target = GetTargetFromOptionText(option.Text);

                if (target == null || !target.IsValid)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                    return;
                }

                if (!CanPunish(controller, target, AdminFlag.Kick))
                    return;

                target.Kick(_baseAdmin.Localizer["kick.reason"]);
                _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["kick.msg", controller.PlayerName, target.PlayerName]);
            });
        }

        MenuManager.OpenChatMenu(caller, menu);
    }

    private void OpenBanMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && !p.IsBot))
        {
            menu.AddMenuOption($"{player.PlayerName} [{player.UserId}]", (controller, option) =>
            {
                var target = GetTargetFromOptionText(option.Text);

                if (target == null || !target.IsValid)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                    return;
                }

                SelectionReasonsForBanning(controller, target);
            });
        }

        MenuManager.OpenChatMenu(caller, menu);
    }

    private void SelectionReasonsForBanning(CCSPlayerController player, CCSPlayerController target)
    {
        var reasonMenu = new ChatMenu(_baseAdmin.Prefix);
        reasonMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_reason"], (_, _) => { }, true);
        foreach (var reason in _baseAdmin.ReadAllBanReasons())
        {
            reasonMenu.AddMenuOption(reason, (controller, option) =>
            {
                var timeMenu = new ChatMenu(_baseAdmin.Prefix);
                timeMenu.AddMenuOption(_baseAdmin.Localizer["menu.select_time"], (_, _) => { }, true);
                foreach (var time in _baseAdmin.ReadAllTime())
                {
                    if (!_baseAdmin.TryParseTimeOption(time, out var label, out var seconds))
                        continue;

                    timeMenu.AddMenuOption(label,
                        (playerController, _) =>
                        {
                            AddBanFromMenu(playerController, target, option.Text, seconds);
                        });
                }

                MenuManager.OpenChatMenu(controller, timeMenu);
            });
        }

        MenuManager.OpenChatMenu(player, reasonMenu);
    }

    private void AddBanFromMenu(CCSPlayerController controller, CCSPlayerController target, string reason, int time)
    {
        if (!CanPunish(controller, target, AdminFlag.Ban))
            return;

        var startBanTimeUnix = DateTime.UtcNow.GetUnixEpoch();
        var endBanTimeUnix = DateTime.UtcNow.AddSeconds(time).GetUnixEpoch();

        var user = new User
        {
            admin_username = controller.PlayerName,
            admin_steamid = new SteamID(controller.SteamID).SteamId2,
            username = target.PlayerName,
            steamid64 = target.SteamID,
            steamid = new SteamID(target.SteamID).SteamId2,
            reason = reason,
            unban_reason = string.Empty,
            admin_unlocked_username = string.Empty,
            admin_unlocked_steamid = string.Empty,
            start_ban_time = startBanTimeUnix,
            end_ban_time = time == 0 ? time : endBanTimeUnix,
            ban_active = true
        };

        Task.Run(async () =>
        {
            try
            {
                var result = await _baseAdmin.Database.AddBan(user);
                Server.NextFrame(() =>
                {
                    if (result.Success)
                    {
                        if (target.IsValid && target.SteamID == user.steamid64)
                            target.Kick(_baseAdmin.Localizer["ban"]);

                        _baseAdmin.PrintToChatAll(result.Message);
                    }
                    else if (controller.IsValid)
                    {
                        _baseAdmin.PrintToChat(controller, result.Message);
                    }
                });
            }
            catch (Exception e)
            {
                _baseAdmin.PrintLogError(e.ToString());
            }
        });
    }

    private void OpenMuteMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && !p.IsBot))
        {
            menu.AddMenuOption($"{player.PlayerName} [{player.UserId}]", (controller, option) =>
            {
                var target = GetTargetFromOptionText(option.Text);

                if (target == null || !target.IsValid)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                    return;
                }

                SelectionTypeMute(controller, target);
            });
        }

        MenuManager.OpenChatMenu(caller, menu);
    }

    private void SelectionTypeMute(CCSPlayerController player, CCSPlayerController target)
    {
        var reasonMenu = new ChatMenu(_baseAdmin.Prefix);
        reasonMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_type"], (_, _) => { }, true);
        reasonMenu.AddMenuOption(_baseAdmin.Localizer["menu.all"], (_, _) => { SelectionReasonsForMute(player, target, MuteType.All); });
        reasonMenu.AddMenuOption(_baseAdmin.Localizer["menu.mute"],
            (_, _) => { SelectionReasonsForMute(player, target, MuteType.Micro); });
        reasonMenu.AddMenuOption(_baseAdmin.Localizer["menu.gag"],
            (_, _) => { SelectionReasonsForMute(player, target, MuteType.Chat); });
        MenuManager.OpenChatMenu(player, reasonMenu);
    }

    private void SelectionReasonsForMute(CCSPlayerController player, CCSPlayerController target, MuteType muteType)
    {
        var reasonMenu = new ChatMenu(_baseAdmin.Prefix);
        reasonMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_reason"], (_, _) => { }, true);
        foreach (var reason in _baseAdmin.ReadAllMuteReasons())
        {
            reasonMenu.AddMenuOption(reason, (controller, option) =>
            {
                var timeMenu = new ChatMenu(_baseAdmin.Prefix);
                timeMenu.AddMenuOption(_baseAdmin.Localizer["menu.select_time"], (_, _) => { }, true);
                foreach (var time in _baseAdmin.ReadAllTime())
                {
                    if (!_baseAdmin.TryParseTimeOption(time, out var label, out var seconds))
                        continue;

                    timeMenu.AddMenuOption(label,
                        (playerController, _) =>
                        {
                            AddMuteFromMenu(playerController, target, muteType, option.Text, seconds);
                        });
                }

                MenuManager.OpenChatMenu(controller, timeMenu);
            });
        }

        MenuManager.OpenChatMenu(player, reasonMenu);
    }

    private void AddMuteFromMenu(CCSPlayerController controller, CCSPlayerController target, MuteType muteType, string reason, int time)
    {
        if (!CanPunish(controller, target, AdminFlag.Generic))
            return;

        _baseAdmin.ApplyMute(controller, target, muteType, reason, time);
    }

    private void OpenChangeMapMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_map"], (_, _) => { }, true);
        foreach (var map in _baseAdmin.GetMapFromMaps())
        {
            var mapsStarts = map.StartsWith("ws:", StringComparison.OrdinalIgnoreCase);
            var mapName = mapsStarts ? map.Substring(3) : map;
            menu.AddMenuOption($"{mapName}",
                (controller, _) =>
                {
                    _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["map.changing", controller.PlayerName, mapName]);
                    _baseAdmin.AddTimer(3.0f, () => _baseAdmin.ChangeMap(mapName, mapsStarts));
                });
        }

        MenuManager.OpenChatMenu(caller, menu);
    }

    private void OpenAddAdminMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && !p.IsBot))
        {
            if (_baseAdmin.IsAdmin(player.Index)) continue;

            menu.AddMenuOption($"{player.PlayerName} [{player.UserId}]",
                (controller, option) =>
                {
                    var target = GetTargetFromOptionText(option.Text);

                    if (target == null || !target.IsValid)
                    {
                        _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                        return;
                    }

                    SelectionTimeAndImmunityForAddAdmin(controller, target);
                });
        }

        MenuManager.OpenChatMenu(caller, menu);
    }

    private void SelectionTimeAndImmunityForAddAdmin(CCSPlayerController player, CCSPlayerController target)
    {
        var timeMenu = new ChatMenu(_baseAdmin.Prefix);
        timeMenu.AddMenuOption(_baseAdmin.Localizer["menu.select_time"], (_, _) => { }, true);
        foreach (var time in _baseAdmin.ReadAllTime())
        {
            if (!_baseAdmin.TryParseTimeOption(time, out var label, out var seconds))
                continue;

            timeMenu.AddMenuOption(label, (playerController, _) =>
            {
                var immunityMenu = new ChatMenu(_baseAdmin.Prefix);
                immunityMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_immunity"], (_, _) => { }, true);
                foreach (var immunity in _baseAdmin.ReadAllImmunity())
                {
                    if (!int.TryParse(immunity, out var immunityValue))
                        continue;

                    immunityMenu.AddMenuOption(immunity,
                        (client, _) =>
                        {
                            AddAdminFromMenu(client, target, immunityValue, "bcdfj", seconds);
                        });
                }

                MenuManager.OpenChatMenu(playerController, immunityMenu);
            });
        }

        MenuManager.OpenChatMenu(player, timeMenu);
    }

    private void AddAdminFromMenu(CCSPlayerController controller, CCSPlayerController target, int immunity, string flags, int time)
    {
        if (!controller.IsValid || !target.IsValid)
            return;

        if (!_baseAdmin.CheckingForAdminAndFlag(controller, AdminFlag.Root))
            return;

        if (_baseAdmin.IsAdmin(target.Index))
            return;

        var startTimeUnix = DateTime.UtcNow.GetUnixEpoch();
        var endTimeUnix = DateTime.UtcNow.AddSeconds(time).GetUnixEpoch();

        var newAdmin = new Admins
        {
            username = target.PlayerName,
            steamid = new SteamID(target.SteamID).SteamId2,
            start_time = startTimeUnix,
            end_time = time == 0 ? 0 : endTimeUnix,
            immunity = immunity,
            flags = string.IsNullOrEmpty(flags) ? "bcdfj" : flags
        };

        Task.Run(async () =>
        {
            try
            {
                await _baseAdmin.Database.AddAdmin(newAdmin, controller, true);
                Server.NextFrame(() => _baseAdmin.RefreshAdminFor(newAdmin.steamid));
            }
            catch (Exception e)
            {
                _baseAdmin.PrintLogError(e.ToString());
            }
        });
    }

    private void OpenUnmuteMenu(CCSPlayerController caller)
    {
        var menu = new ChatMenu(_baseAdmin.Prefix);
        menu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);

        var count = 0;

        foreach (var player in Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV && !p.IsBot))
        {
            if (player.VoiceFlags.HasFlag(VoiceFlags.Muted) || _baseAdmin.IsUserMute(player.Index))
            {
                count++;
                menu.AddMenuOption($"{player.PlayerName} [{player.UserId}]", (controller, option) =>
                {
                    var target = GetTargetFromOptionText(option.Text);
                    if (target == null || !target.IsValid)
                    {
                        _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                        return;
                    }

                    var adminName = controller.PlayerName;
                    var adminSteamId = new SteamID(controller.SteamID).SteamId2;
                    var targetSteamId = new SteamID(target.SteamID).SteamId2;

                    Task.Run(async () =>
                    {
                        try
                        {
                            var msg = await _baseAdmin.Database.UnmuteUser((int)MuteType.All, adminName, adminSteamId, targetSteamId,
                                _baseAdmin.Localizer["menu.unmute_players"]);

                            Server.NextFrame(() =>
                            {
                                _baseAdmin.PrintToChatAll(msg);
                                if (target.IsValid)
                                    _baseAdmin.RefreshMuteState(target);
                            });
                        }
                        catch (Exception e)
                        {
                            _baseAdmin.PrintLogError(e.ToString());
                        }
                    });
                });
            }
        }

        if (count == 0)
        {
            menu.AddMenuOption(_baseAdmin.Localizer["menu.no_muted_players"], (_, _) => { }, true);
        }

        MenuManager.OpenChatMenu(caller, menu);
    }
}