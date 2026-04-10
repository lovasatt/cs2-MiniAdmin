using System;
using System.Linq;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Menu;
using static BaseAdmin.BaseAdmin;

namespace BaseAdmin;

public class Menu
{
    private readonly BaseAdmin _baseAdmin;
    private readonly ChatMenu _slayMenu;
    private readonly ChatMenu _kickMenu;
    private readonly ChatMenu _muteMenu;
    private readonly ChatMenu _banMenu;
    private readonly ChatMenu _changeMapMenu;
    private readonly ChatMenu _addAdminMenu;

    public Menu(BaseAdmin baseAdmin)
    {
        _baseAdmin = baseAdmin;

        _slayMenu = new ChatMenu(baseAdmin.Prefix);
        _kickMenu = new ChatMenu(baseAdmin.Prefix);
        _muteMenu = new ChatMenu(baseAdmin.Prefix);
        _banMenu = new ChatMenu(baseAdmin.Prefix);
        _changeMapMenu = new ChatMenu(baseAdmin.Prefix);
        _addAdminMenu = new ChatMenu(baseAdmin.Prefix);
    }

    public void CreateMenu()
    {
        var adminMenu = new ChatMenu(_baseAdmin.Prefix);
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.slay_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Slay))
            {
                CreateSlayMenu();
                MenuManager.OpenChatMenu(player, _slayMenu);
            }
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.kick_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Kick))
            {
                CreateKickMenu();
                MenuManager.OpenChatMenu(player, _kickMenu);
            }
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.mute_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Generic))
            {
                CreateMuteMenu();
                MenuManager.OpenChatMenu(player, _muteMenu);
            }
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.ban_players"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Ban))
            {
                CreateBanMenu();
                MenuManager.OpenChatMenu(player, _banMenu);
            }
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.change_map"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Changemap))
            {
                CreateChangeMapMenu();
                MenuManager.OpenChatMenu(player, _changeMapMenu);
            }
        });
        adminMenu.AddMenuOption(_baseAdmin.Localizer["menu.add_admin"], (player, _) =>
        {
            if (_baseAdmin.CheckingForAdminAndFlag(player, AdminFlag.Root))
            {
                CreateAddAdminMenu();
                MenuManager.OpenChatMenu(player, _addAdminMenu);
            }
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

    private void CreateSlayMenu()
    {
        var playerEntities =
            Utilities.GetPlayers().Where(u => u.PlayerPawn?.Value != null && u.PlayerPawn.Value.IsValid);
        _slayMenu.MenuOptions.Clear();
        _slayMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        _slayMenu.AddMenuOption(_baseAdmin.Localizer["menu.all"], (controller, option) =>
        {
            foreach (var player in playerEntities)
                player.PlayerPawn?.Value?.CommitSuicide(true, true);
        });
        foreach (var player in playerEntities)
        {
            if (!player.PawnIsAlive) continue;

            _slayMenu.AddMenuOption($"{player.PlayerName} [{player.Index}]", (controller, option) =>
            {
                var parts = option.Text.Split('[', ']');
                if (parts.Length < 2) return;
                var target = Utilities.GetPlayerFromIndex(int.Parse(parts[1]));

                if (target == null || !target.PawnIsAlive)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.dead_or_invalid"]);
                    return;
                }

                target.PlayerPawn?.Value?.CommitSuicide(true, true);

                _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["slay.killed_by", controller.PlayerName, target.PlayerName]);
            });
        }
    }

    private void CreateKickMenu()
    {
        _kickMenu.MenuOptions.Clear();
        _kickMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers())
        {
            _kickMenu.AddMenuOption($"{player.PlayerName} [{player.Index}]", (controller, option) =>
            {
                var parts = option.Text.Split('[', ']');
                if (parts.Length < 2) return;
                var target = Utilities.GetPlayerFromIndex(int.Parse(parts[1]));

                if (target == null || !target.IsValid)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                    return;
                }

                target.Kick("Kick");

                _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["kick.msg", controller.PlayerName, target.PlayerName]);
            });
        }
    }

    private void CreateBanMenu()
    {
        _banMenu.MenuOptions.Clear();
        _banMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers())
        {
            _banMenu.AddMenuOption($"{player.PlayerName}[{player.Index}]", (controller, option) =>
            {
                var parts = option.Text.Split('[', ']');
                if (parts.Length < 2) return;
                var target = Utilities.GetPlayerFromIndex(int.Parse(parts[1]));

                if (target == null || !target.IsValid)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                    return;
                }

                SelectionReasonsForBanning(controller, target);
            });
        }
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
                    var split = time.Split(':');
                    timeMenu.AddMenuOption(split[0],
                        (playerController, _) =>
                        {
                            AddBanFromMenu(playerController, target, option.Text, int.Parse(split[1]));
                        });
                }

                MenuManager.OpenChatMenu(controller, timeMenu);
            });
        }

        MenuManager.OpenChatMenu(player, reasonMenu);
    }

    private void AddBanFromMenu(CCSPlayerController controller, CCSPlayerController target, string reason, int time)
    {
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
            var msg = await _baseAdmin.Database.AddBan(user);
            Server.NextFrame(() =>
            {
                if (target.IsValid) target.Kick(_baseAdmin.Localizer["ban"]);
                _baseAdmin.PrintToChatAll(msg);
            });
        });
    }

    private void CreateMuteMenu()
    {
        _muteMenu.MenuOptions.Clear();
        _muteMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers())
        {
            _muteMenu.AddMenuOption($"{player.PlayerName} [{player.Index}]", (controller, option) =>
            {
                var parts = option.Text.Split('[', ']');
                if (parts.Length < 2) return;
                var target = Utilities.GetPlayerFromIndex(int.Parse(parts[1]));

                if (target == null || !target.IsValid)
                {
                    _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                    return;
                }

                SelectionTypeMute(controller, target);
            });
        }
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
                    var split = time.Split(':');
                    timeMenu.AddMenuOption(split[0],
                        (playerController, _) =>
                        {
                            AddMuteFromMenu(playerController, target, muteType, option.Text, int.Parse(split[1]));
                        });
                }

                MenuManager.OpenChatMenu(controller, timeMenu);
            });
        }

        MenuManager.OpenChatMenu(player, reasonMenu);
    }

    private void AddMuteFromMenu(CCSPlayerController controller, CCSPlayerController target, MuteType muteType, string reason, int time)
    {
        var startMuteTimeUnix = DateTime.UtcNow.GetUnixEpoch();
        var endMuteTimeUnix = DateTime.UtcNow.AddSeconds(time).GetUnixEpoch();

        _ = _baseAdmin.Database.AddMute(new MuteUser
        {
            mute_type = (int)muteType,
            admin_username = controller.PlayerName,
            admin_steamid = new SteamID(controller.SteamID).SteamId2,
            username = target.PlayerName,
            steamid64 = target.SteamID,
            steamid = new SteamID(target.SteamID).SteamId2,
            reason = reason, 
            unmute_reason = string.Empty,
            admin_unlocked_username = string.Empty,
            admin_unlocked_steamid = string.Empty,
            start_mute_time = startMuteTimeUnix,
            end_mute_time = time == 0 ? 0 : endMuteTimeUnix,
            mute_active = true
        }, controller);

        Server.NextFrame(() =>
        {
            if (muteType is MuteType.Micro or MuteType.All)
                target.VoiceFlags = VoiceFlags.Muted;
        });

        _baseAdmin.UpdateUserMuteLocal(target, time, endMuteTimeUnix, (int)muteType);
    }

    private void CreateChangeMapMenu()
    {
        _changeMapMenu.MenuOptions.Clear();
        _changeMapMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_map"], (_, _) => { }, true);
        foreach (var map in _baseAdmin.GetMapFromMaps())
        {
            var mapsStarts = map.StartsWith("ws:");
            var mapName = mapsStarts ? map.Substring(3) : map;
            _changeMapMenu.AddMenuOption($"{mapName}",
                (controller, option) =>
                {
                    _baseAdmin.PrintToChatAll(_baseAdmin.Localizer["map.changing", controller.PlayerName, mapName]);
                    _baseAdmin.AddTimer(3.0f, () => _baseAdmin.ChangeMap(mapName, mapsStarts));
                });
        }
    }

    private void CreateAddAdminMenu()
    {
        _addAdminMenu.MenuOptions.Clear();
        _addAdminMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_player"], (_, _) => { }, true);
        foreach (var player in Utilities.GetPlayers())
        {
            if (_baseAdmin.IsAdmin(player.Index)) continue;

            _addAdminMenu.AddMenuOption($"{player.PlayerName} [{player.Index}]",
                (controller, option) =>
                {
                    var parts = option.Text.Split('[', ']');
                    if (parts.Length < 2) return;
                    var target = Utilities.GetPlayerFromIndex(int.Parse(parts[1]));

                    if (target == null || !target.IsValid)
                    {
                        _baseAdmin.PrintToChat(controller, _baseAdmin.Localizer["player.invalid"]);
                        return;
                    }

                    SelectionTimeAndImmunityForAddAdmin(controller, target);
                });
        }
    }

    private void SelectionTimeAndImmunityForAddAdmin(CCSPlayerController player, CCSPlayerController target)
    {
        var timeMenu = new ChatMenu(_baseAdmin.Prefix);
        timeMenu.AddMenuOption(_baseAdmin.Localizer["menu.select_time"], (_, _) => { }, true);
        foreach (var time in _baseAdmin.ReadAllTime())
        {
            var split = time.Split(':');
            timeMenu.AddMenuOption(split[0], (playerController, _) =>
            {
                var immunityMenu = new ChatMenu(_baseAdmin.Prefix);
                immunityMenu.AddMenuOption(_baseAdmin.Localizer["menu.pick_immunity"], (_, _) => { }, true);
                foreach (var immunity in _baseAdmin.ReadAllImmunity())
                {
                    immunityMenu.AddMenuOption(immunity,
                        (client, _) =>
                        {
                            AddAdminFromMenu(client, target, int.Parse(immunity), "", int.Parse(split[1]));
                        });
                }

                MenuManager.OpenChatMenu(playerController, immunityMenu);
            });
        }

        MenuManager.OpenChatMenu(player, timeMenu);
    }

    private void AddAdminFromMenu(CCSPlayerController controller, CCSPlayerController target, int immunity, string flags, int time)
    {
        var startTimeUnix = DateTime.UtcNow.GetUnixEpoch();
        var endTimeUnix = DateTime.UtcNow.AddSeconds(time).GetUnixEpoch();

        _ = _baseAdmin.Database.AddAdmin(new Admins
        {
            username = target.PlayerName,
            steamid = new SteamID(target.SteamID).SteamId2,
            start_time = startTimeUnix,
            end_time = time == 0 ? 0 : endTimeUnix,
            immunity = immunity,
            flags = flags
        }, controller);
    }
}