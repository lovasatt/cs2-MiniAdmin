using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using MySqlConnector;
using System.Diagnostics.CodeAnalysis;

namespace BaseAdmin;

public class BaseAdmin : BasePlugin
{
    public override string ModuleAuthor => "thesamefabius & lovasatt";
    public override string ModuleName => "Mini Admin";
    public override string ModuleVersion => "v1.2.0";

    private static string _dbConnectionString = string.Empty;

    public readonly string Prefix = "[\x0C Admin Menu \x01]";

    private static readonly string[] BlockedCvars =
    {
        "rcon_password", "sv_password", "sv_rcon_whitelist_address", "sv_setsteamaccount"
    };

    private readonly MuteUserLocal?[] _muteUsers = new MuteUserLocal[70];
    private readonly Admins?[] _adminUsers = new Admins[70];

    private Task _databaseReady = Task.CompletedTask;
    public Database Database = null!;
    private AdminChat _adminChat = null!;
    private Config _config = null!;

    public enum MuteType
    {
        Micro = 0,
        Chat,
        All
    }

    public enum AdminFlag
    {
        Reservation = 'a',
        Generic = 'b',
        Kick = 'c',
        Ban = 'd',
        Unban = 'e',
        Slay = 'f',
        Changemap = 'g',
        Cvar = 'h',
        Config = 'i',
        Chat = 'j',
        Vote = 'k',
        Password = 'l',
        Rcon = 'm',
        Cheats = 'n',
        Vip = 'o',
        Root = 'z'
    }

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();
        _dbConnectionString = BuildConnectionString();
        Database = new Database(this, _dbConnectionString);
        _adminChat = new AdminChat();
        new Menu(this).CreateMenu();

        _databaseReady = Task.Run(Database.InitializeDatabaseAsync);

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.SteamID != 0))
                _ = OnClientAuthorizedAsync((int)player.Index - 1, new SteamID(player.SteamID));
        }

        RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
        {
            _ = OnClientConnectedAsync(slot, id);
            _ = OnClientAuthorizedAsync(slot, id);
        });

        RegisterListener<Listeners.OnClientDisconnectPost>(slot =>
        {
            if (slot + 1 < _muteUsers.Length)
            {
                _muteUsers[slot + 1] = null;
                _adminUsers[slot + 1] = null;
            }
        });

        AddCommandListener("say", CommandListener_Say);
        AddCommandListener("say_team", CommandListener_SayTeam);

        AddTimer(300, () => { _ = Task.Run(Database.DeleteExpiredAdminsAsync); }, TimerFlags.REPEAT);
        AddTimer(60, () =>
        {
            foreach (var player in Utilities.GetPlayers().Where(u => !u.IsBot))
            {
                if (player.Index >= _muteUsers.Length) continue;

                var user = _muteUsers[(int)player.Index];
                if (user == null) continue;

                if (user.EndMuteTime == 0) continue;

                var endTime = DateTimeOffset.FromUnixTimeSeconds(user.EndMuteTime).UtcDateTime;

                if (endTime <= DateTime.UtcNow)
                {
                    if (user.MuteType is (int)MuteType.Micro or (int)MuteType.All)
                        player.VoiceFlags = VoiceFlags.Normal;

                    var expiredSteamId = user.SteamId;
                    _muteUsers[player.Index] = null;

                    _ = Task.Run(() => Database.UnmuteUser(-1, _config.BanFromConsoleName, _config.BanFromConsoleName,
                        expiredSteamId, Localizer["unban.deadline_passed"]));
                }
            }
        }, TimerFlags.REPEAT);
    }

    private static bool IsChatCommand(string msg, string name, out string args)
    {
        args = string.Empty;

        foreach (var prefix in new[] { "", "!", "/" })
        {
            var cmd = prefix + name;

            if (msg.Equals(cmd, StringComparison.OrdinalIgnoreCase))
                return true;

            if (msg.StartsWith(cmd + " ", StringComparison.OrdinalIgnoreCase))
            {
                args = msg.Substring(cmd.Length + 1).Trim();
                return true;
            }
        }

        return false;
    }

    private HookResult ProcessChatMessage(CCSPlayerController? controller, CommandInfo info, bool sendToAllAdmins)
    {
        if (controller == null || !controller.IsValid) return HookResult.Continue;

        var msg = GetTextInsideQuotes(info.ArgString);
        if (string.IsNullOrWhiteSpace(msg)) return HookResult.Continue;

        var entityIndex = controller.Index;

        if (IsChatCommand(msg, "csay", out var csayArgs))
        {
            if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return HookResult.Handled;

            var (csayColor, csayText) = AdminChat.ParseColorMessage(csayArgs, ChatColors.Green);

            if (string.IsNullOrWhiteSpace(csayText))
            {
                PrintToChat(controller, Localizer["csay.usage"]);
                return HookResult.Handled;
            }

            _adminChat.SendColorSay(csayText, csayColor, Localizer);
            return HookResult.Handled;
        }

        if (IsChatCommand(msg, "ssay", out var ssayArgs))
        {
            if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return HookResult.Handled;

            if (string.IsNullOrWhiteSpace(ssayArgs))
            {
                PrintToChat(controller, Localizer["ssay.usage"]);
                return HookResult.Handled;
            }

            _adminChat.SendSilentSay(ssayArgs, Localizer);
            return HookResult.Handled;
        }

        if (IsChatCommand(msg, "psay", out var psayArgs))
        {
            if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return HookResult.Handled;

            var parts = Utils.SplitArgs(psayArgs, 2);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                PrintToChat(controller, Localizer["psay.usage"]);
                return HookResult.Handled;
            }

            var psayTarget = GetPlayerFromUserIdOrName(parts[0]);
            if (psayTarget == null)
            {
                ReplyToCommand(controller, Localizer["player.not_found"]);
                return HookResult.Handled;
            }

            PrintToChat(psayTarget, $"{Localizer["psay.from"]} - {ChatColors.Blue}{controller.PlayerName}{ChatColors.Default}: {parts[1]}");
            ReplyToCommand(controller, $"{Localizer["psay.to"]} - {ChatColors.Blue}{psayTarget.PlayerName}{ChatColors.Default}: {parts[1]}");
            return HookResult.Handled;
        }

        if (IsChatCommand(msg, "asay", out var asayArgs))
        {
            if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return HookResult.Handled;

            if (string.IsNullOrWhiteSpace(asayArgs))
            {
                PrintToChat(controller, Localizer["asay.usage"]);
                return HookResult.Handled;
            }

            _adminChat.SendAdminSayWithName(controller.PlayerName, asayArgs, Localizer);
            return HookResult.Handled;
        }

        if (msg.StartsWith('@'))
        {
            var cleanMsg = msg.TrimStart('@').Trim();

            if (HasAdminFlag(controller, AdminFlag.Chat))
            {
                if (sendToAllAdmins)
                {
                    _adminChat.SendToAllFromAdmin(cleanMsg, Localizer);
                    return HookResult.Handled;
                }

                foreach (var player in Utilities.GetPlayers())
                {
                    if (HasAdminFlag(player, AdminFlag.Chat))
                    {
                        _adminChat.SendToAdminChat(player,
                            $" {ChatColors.Blue}{controller.PlayerName}{ChatColors.Default}: {cleanMsg}", Localizer);
                    }
                }

                return HookResult.Handled;
            }
            else
            {
                if (!IsChatMuted(entityIndex))
                {
                    var admins = Utilities.GetPlayers().Where(p => HasAdminFlag(p, AdminFlag.Chat)).ToList();

                    foreach (var admin in admins)
                    {
                        _adminChat.SendToAdminChatFromPlayer(admin,
                            $" {ChatColors.Blue}{controller.PlayerName}{ChatColors.Default}: {cleanMsg}", Localizer);
                    }

                    PrintToChat(controller, $"{ChatColors.Green}{Localizer["adminchat.sent", cleanMsg]}");
                    return HookResult.Handled;
                }
            }
        }

        if (IsUserMute(entityIndex))
            return CheckMuteStatus(controller, MuteType.Chat) ? HookResult.Handled : HookResult.Continue;

        return HookResult.Continue;
    }

    private HookResult CommandListener_SayTeam(CCSPlayerController? controller, CommandInfo info)
    {
        return ProcessChatMessage(controller, info, sendToAllAdmins: false);
    }

    private HookResult CommandListener_Say(CCSPlayerController? controller, CommandInfo info)
    {
        return ProcessChatMessage(controller, info, sendToAllAdmins: true);
    }

    private bool CheckMuteStatus(CCSPlayerController controller, MuteType requiredMuteType)
    {
        if (controller.Index >= _muteUsers.Length) return false;

        var user = _muteUsers[controller.Index];
        if (user == null || !user.MuteActive) return false;

        if (user.MuteType != (int)MuteType.All && user.MuteType != (int)requiredMuteType) return false;

        var prefix = requiredMuteType == MuteType.Chat
            ? Localizer["chat.disabled_until"]
            : Localizer["mic.disabled_until"];

        if (user.EndMuteTime == 0)
        {
            PrintToChat(controller, $"{prefix} {ChatColors.Red}{Localizer["permanently"]}");
            return true;
        }

        var endTime = DateTimeOffset.FromUnixTimeSeconds(user.EndMuteTime).UtcDateTime;
        if (endTime <= DateTime.UtcNow) return false;

        var timeEnd = endTime - DateTime.UtcNow;
        var time = $"{(timeEnd.Days == 0 ? "" : $"{timeEnd.Days}d, ")}{timeEnd.Hours:00}:{timeEnd.Minutes:00}:{timeEnd.Seconds:00}";
        PrintToChat(controller, prefix + time);
        return true;
    }

    private async Task OnClientAuthorizedAsync(int playerSlot, SteamID steamId)
    {
        try
        {
            await _databaseReady;

            var targetSteamId2 = steamId.SteamId2;
            var targetSteamId64 = steamId.SteamId64;

            var userAdmin = await Database.GetAdminFromDb(targetSteamId2);
            var userMuted = await Database.GetActiveMuteAsync(targetSteamId2);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var completion = new TaskCompletionSource<bool>();

                Server.NextFrame(() =>
                {
                    try
                    {
                        completion.TrySetResult(ApplyAuthorizedData(playerSlot, targetSteamId64, userAdmin, userMuted));
                    }
                    catch (Exception e)
                    {
                        PrintLogError(e.ToString());
                        completion.TrySetResult(true);
                    }
                });

                if (await completion.Task) return;

                await Task.Delay(1000);
            }
        }
        catch (Exception e)
        {
            PrintLogError(e.ToString());
        }
    }

    private bool ApplyAuthorizedData(int playerSlot, ulong targetSteamId64, Admins? userAdmin, MuteUser? userMuted)
    {
        if (playerSlot < 0 ||
            playerSlot + 1 >= _adminUsers.Length ||
            playerSlot + 1 >= _muteUsers.Length)
            return true;

        var player = Utilities.GetPlayerFromSlot(playerSlot);

        if (player == null || !player.IsValid || player.SteamID == 0)
            return false;

        if (player.SteamID != targetSteamId64)
            return true;

        _adminUsers[playerSlot + 1] = null;
        _muteUsers[playerSlot + 1] = null;
        player.VoiceFlags = VoiceFlags.Normal;

        if (userAdmin != null)
        {
            _adminUsers[playerSlot + 1] = new Admins
            {
                username = userAdmin.username,
                steamid = userAdmin.steamid,
                start_time = userAdmin.start_time,
                end_time = userAdmin.end_time,
                immunity = userAdmin.immunity,
                flags = userAdmin.flags
            };
        }

        if (userMuted != null &&
            userMuted.mute_active &&
            (userMuted.end_mute_time == 0 ||
             userMuted.end_mute_time > DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
        {
            _muteUsers[playerSlot + 1] = new MuteUserLocal
            {
                MuteType = userMuted.mute_type,
                SteamId = userMuted.steamid,
                EndMuteTime = userMuted.end_mute_time,
                MuteActive = true
            };

            if (userMuted.mute_type is (int)MuteType.Micro or (int)MuteType.All)
                player.VoiceFlags = VoiceFlags.Muted;
        }

        return true;
    }

    public async Task LoadAdminUserAsync(SteamID steamId, int playerSlot)
    {
        try
        {
            await _databaseReady;

            var userAdmin = await Database.GetAdminFromDb(steamId.SteamId2);
            var targetSteamId64 = steamId.SteamId64;

            Server.NextFrame(() =>
            {
                if (playerSlot < 0 || playerSlot + 1 >= _adminUsers.Length)
                    return;

                var player = Utilities.GetPlayerFromSlot(playerSlot);
                if (player == null || !player.IsValid || player.SteamID != targetSteamId64)
                    return;

                _adminUsers[playerSlot + 1] = userAdmin;
            });
        }
        catch (Exception e)
        {
            PrintLogError(e.ToString());
        }
    }

    public void RefreshAdminFor(string steamId2)
    {
        ulong target64 = 0;
        if (Utils.TryParseSteamId(steamId2, out var s64, out _))
            target64 = s64;

        var player = Utilities.GetPlayers().FirstOrDefault(p =>
            p.IsValid && !p.IsBot && p.SteamID != 0 && (p.SteamID == target64 || new SteamID(p.SteamID).SteamId2 == steamId2));

        if (player != null)
            _ = LoadAdminUserAsync(new SteamID(player.SteamID), (int)player.Index - 1);
    }

    public void RefreshMuteState(CCSPlayerController player)
    {
        var index = player.Index;
        var steamId64 = player.SteamID;
        var steamId2 = new SteamID(steamId64).SteamId2;

        _ = Task.Run(async () =>
        {
            try
            {
                var mute = await Database.GetActiveMuteAsync(steamId2);

                Server.NextFrame(() =>
                {
                    var current = Utilities.GetPlayers().FirstOrDefault(p =>
                        p.IsValid && p.Index == index && p.SteamID == steamId64);

                    if (current == null || index >= _muteUsers.Length) return;

                    _muteUsers[index] = null;
                    current.VoiceFlags = VoiceFlags.Normal;

                    if (mute == null || !mute.mute_active) return;

                    if (mute.end_mute_time != 0 && mute.end_mute_time <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                        return;

                    _muteUsers[index] = new MuteUserLocal
                    {
                        MuteType = mute.mute_type,
                        SteamId = mute.steamid,
                        EndMuteTime = mute.end_mute_time,
                        MuteActive = true
                    };

                    if (mute.mute_type is (int)MuteType.Micro or (int)MuteType.All)
                        current.VoiceFlags = VoiceFlags.Muted;
                });
            }
            catch (Exception e)
            {
                PrintLogError(e.ToString());
            }
        });
    }

    private async Task OnClientConnectedAsync(int slot, SteamID steamId)
    {
        try
        {
            await _databaseReady;

            await using var connection = new MySqlConnection(_dbConnectionString);

            var unbanUsers = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM miniadmin_bans WHERE steamid64 = @SteamId64 AND end_ban_time <= @CurrentTime AND ban_active = 1 AND end_ban_time != 0",
                new { CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), steamId.SteamId64 });

            if (unbanUsers != null)
            {
                PrintLogInfo("Unban: {steamid}", unbanUsers.steamid);
                await Database.UnbanUser(_config.BanFromConsoleName, _config.BanFromConsoleName, unbanUsers.steamid, Localizer["unban.deadline_passed"]);
            }

            var unmuteUsers = await connection.QueryFirstOrDefaultAsync<MuteUser>(
                "SELECT * FROM miniadmin_mute WHERE steamid64 = @SteamId64 AND end_mute_time <= @CurrentTime AND mute_active = 1 AND end_mute_time != 0",
                new { CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), steamId.SteamId64 });

            if (unmuteUsers != null)
            {
                PrintLogInfo("Unmute: {steamid}", unmuteUsers.steamid);
                await Database.UnmuteUser(-1, _config.BanFromConsoleName, _config.BanFromConsoleName, unmuteUsers.steamid, Localizer["unban.deadline_passed"]);

                if (unmuteUsers.mute_type is (int)MuteType.All or (int)MuteType.Micro)
                {
                    Server.NextFrame(() =>
                    {
                        var currentPlayer = Utilities.GetPlayerFromSlot(slot);

                        if (currentPlayer == null ||
                            !currentPlayer.IsValid ||
                            currentPlayer.SteamID != steamId.SteamId64)
                            return;

                        currentPlayer.VoiceFlags = VoiceFlags.Normal;
                    });
                }
            }

            await Database.DeleteExpiredAdminsAsync();

            var banUser = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM miniadmin_bans WHERE steamid64 = @SteamId64 AND ban_active = 1",
                new { steamId.SteamId64 });

            if (banUser != null)
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var completion = new TaskCompletionSource<bool>();

                    Server.NextFrame(() =>
                    {
                        var currentPlayer = Utilities.GetPlayerFromSlot(slot) 
                            ?? Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId.SteamId64);

                        if (currentPlayer != null && currentPlayer.IsValid && currentPlayer.SteamID == steamId.SteamId64 && currentPlayer.UserId.HasValue)
                        {
                            currentPlayer.Kick(Localizer["ban"]);
                            completion.TrySetResult(true);
                            return;
                        }

                        completion.TrySetResult(false);
                    });

                    if (await completion.Task) return;

                    await Task.Delay(500);
                }
            }
        }
        catch (Exception e)
        {
            PrintLogError(e.ToString());
        }
    }

    [CommandHelper(0, "[#userid or username]", CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_noclip")]
    public void OnCmdNoclip(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null || !CheckingForAdminAndFlag(controller, AdminFlag.Cheats)) return;

        var args = Utils.SplitArgs(command.ArgString, 1);
        var target = controller;

        if (args.Length > 0)
        {
            var found = GetPlayerFromUserIdOrName(args[0]);
            if (!IsPlayerValid(controller, found, true)) return;

            if (found.Index != controller.Index && IsAdmin(found.Index) && !PlayerImmunityComparison(controller, found))
            {
                ReplyToCommand(controller, Localizer["punish.immunity"]);
                return;
            }

            target = found;
        }
        else if (!controller.PawnIsAlive)
        {
            ReplyToCommand(controller, Localizer["player.invalid"]);
            return;
        }

        GiveNoclip(controller, target);
    }

    private void GiveNoclip(CCSPlayerController player, CCSPlayerController target)
    {
        var playerPawn = target.PlayerPawn?.Value;
        if (playerPawn == null || !playerPawn.IsValid) return;

        var newMoveType = playerPawn.MoveType == MoveType_t.MOVETYPE_NOCLIP
            ? MoveType_t.MOVETYPE_WALK
            : MoveType_t.MOVETYPE_NOCLIP;

        playerPawn.MoveType = newMoveType;
        playerPawn.ActualMoveType = newMoveType;
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_MoveType");

        PrintToChatAll(Localizer["noclip.issued", player.PlayerName, target.PlayerName]);
    }

    [CommandHelper(1, "<#userid or username> <ct/t/spec/none> [-k]", CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_team")]
    public void OnCmdChangeTeam(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null || !CheckingForAdminAndFlag(controller, AdminFlag.Kick)) return;

        var args = Utils.SplitArgs(command.ArgString);
        if (args.Length < 2)
        {
            ReplyToCommand(controller, Localizer["team.usage"]);
            return;
        }

        var target = GetPlayerFromUserIdOrName(args[0]);

        if (!IsPlayerValid(controller, target)) return;

        if (IsAdmin(target.Index) && !PlayerImmunityComparison(controller, target))
        {
            ReplyToCommand(controller, Localizer["kick.no_permission"]);
            return;
        }

        var team = GetTeam(args[1]);
        if (team == null)
        {
            ReplyToCommand(controller, Localizer["team.usage"]);
            return;
        }

        if (args.Length >= 3 && args[2].Equals("-k", StringComparison.OrdinalIgnoreCase))
            target.ChangeTeam(team.Value);
        else
            target.SwitchTeam(team.Value);

        PrintToChatAll(Localizer["team.moved", target.PlayerName]);
    }

    private static CsTeam? GetTeam(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "ct" => CsTeam.CounterTerrorist,
            "t" or "tt" => CsTeam.Terrorist,
            "spec" or "spectator" => CsTeam.Spectator,
            "none" => CsTeam.None,
            _ => null
        };
    }


    [CommandHelper(1, "<text>", CommandUsage.CLIENT_AND_SERVER)]
    [ConsoleCommand("css_say")]
    [ConsoleCommand("admin_say")]
    public void OnCmdSay(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return;

        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
        _adminChat.SendAdminSayWithName(adminName, command.ArgString, Localizer);
    }

    [CommandHelper(1, "[color] <text>", CommandUsage.CLIENT_AND_SERVER)]
    [ConsoleCommand("css_csay")]
    [ConsoleCommand("csay")]
    public void OnCmdCSay(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return;

        var raw = command.ArgString;
        if (string.IsNullOrWhiteSpace(raw))
        {
            ReplyToCommand(controller, Localizer["csay.usage"]);
            return;
        }

        var (color, message) = AdminChat.ParseColorMessage(raw, ChatColors.Green);
        if (!string.IsNullOrWhiteSpace(message))
        {
            _adminChat.SendColorSay(message, color, Localizer);
        }
        else
        {
            ReplyToCommand(controller, Localizer["csay.usage"]);
        }
    }

    [CommandHelper(1, "<text>", CommandUsage.CLIENT_AND_SERVER)]
    [ConsoleCommand("css_ssay")]
    [ConsoleCommand("ssay")]
    public void OnCmdSSay(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return;

        var message = command.ArgString;
        if (string.IsNullOrWhiteSpace(message))
        {
            ReplyToCommand(controller, Localizer["ssay.usage"]);
            return;
        }

        _adminChat.SendSilentSay(message, Localizer);
    }

    [CommandHelper(1, "<text>", CommandUsage.CLIENT_AND_SERVER)]
    [ConsoleCommand("css_asay")]
    [ConsoleCommand("asay")]
    public void OnCmdASay(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return;

        var message = command.ArgString;
        if (string.IsNullOrWhiteSpace(message))
        {
            ReplyToCommand(controller, Localizer["asay.usage"]);
            return;
        }

        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
        _adminChat.SendAdminSayWithName(adminName, message, Localizer);
    }

    [CommandHelper(1, "<#userid or username> <message>", CommandUsage.CLIENT_AND_SERVER)]
    [ConsoleCommand("css_psay")]
    [ConsoleCommand("psay")]
    public void OnCmdPSay(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Chat)) return;

        var args = Utils.SplitArgs(command.ArgString, 2);
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            ReplyToCommand(controller, Localizer["psay.usage"]);
            return;
        }

        var target = GetPlayerFromUserIdOrName(args[0]);

        if (target == null)
        {
            ReplyToCommand(controller, Localizer["player.not_found"]);
            return;
        }

        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
        PrintToChat(target, $"{Localizer["psay.from"]} - {ChatColors.Blue}{adminName}{ChatColors.Default}: {args[1]}");
        ReplyToCommand(controller, $"{Localizer["psay.to"]} - {ChatColors.Blue}{target.PlayerName}{ChatColors.Default}: {args[1]}");
    }

    [CommandHelper(usage: "[#userid or name]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_god")]
    public void OnCmdGod(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null || !CheckingForAdminAndFlag(controller, AdminFlag.Cheats)) return;

        var args = Utils.SplitArgs(command.ArgString, 1);

        if (args.Length > 0)
        {
            var target = GetPlayerFromUserIdOrName(args[0]);

            if (target == null)
            {
                ReplyToCommand(controller, Localizer["player.not_found"]);
                return;
            }

            var targetPawn = target.PlayerPawn?.Value;
            if (targetPawn == null || !targetPawn.IsValid) return;

            targetPawn.TakesDamage ^= true;
            Utilities.SetStateChanged(targetPawn, "CBaseEntity", "m_bTakesDamage");
            PrintToChat(target, Localizer["god.given"]);
        }
        else
        {
            var playerPawn = controller.PlayerPawn?.Value;
            if (playerPawn == null || !playerPawn.IsValid) return;
            playerPawn.TakesDamage ^= true;
            Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_bTakesDamage");
            PrintToChat(controller, Localizer["god.established"]);
        }
    }

    [CommandHelper(2, "<cvar> <value>")]
    [ConsoleCommand("css_cvar")]
    public void OnCmdCvar(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Cvar)) return;

        var args = Utils.SplitArgs(command.ArgString, 2);
        if (args.Length < 2) return;

        var cvar = args[0];
        var value = args[1];

        if (BlockedCvars.Contains(cvar, StringComparer.OrdinalIgnoreCase))
        {
            ReplyToCommand(controller, Localizer["no_access"]);
            return;
        }

        var conVar = ConVar.Find(cvar);
        if (conVar == null)
        {
            ReplyToCommand(controller, Localizer["cvar.not_found", cvar]);
            return;
        }

        try
        {
            conVar.SetValue(value);
            var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
            PrintLogInfo("Admin '{admin}' set cvar {cvar} to '{value}'", adminName, cvar, value);
        }
        catch (Exception e)
        {
            PrintLogError(e.ToString());
        }
    }

    [CommandHelper(1, "<command>", CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_rcon")]
    public void OnCmdRcon(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Rcon)) return;

        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
        PrintLogInfo("Admin '{admin}' executed rcon: {command}", adminName, command.ArgString);

        Server.ExecuteCommand(command.ArgString);
    }

    [CommandHelper(1, "<#userid or username> [damage]", CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_slap")]
    public void OnCmdSlap(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null || !CheckingForAdminAndFlag(controller, AdminFlag.Slay)) return;

        var args = Utils.SplitArgs(command.ArgString);
        if (args.Length < 1)
        {
            ReplyToCommand(controller, Localizer["player.not_found"]);
            return;
        }

        var target = GetPlayerFromUserIdOrName(args[0]);

        if (!IsPlayerValid(controller, target, true)) return;

        if (IsAdmin(target.Index) && !PlayerImmunityComparison(controller, target))
        {
            ReplyToCommand(controller, Localizer["slay.no_permission"]);
            return;
        }

        var damage = 0;
        if (args.Length >= 2)
        {
            if (int.TryParse(args[1], out var parsedDmg))
                damage = Math.Max(0, parsedDmg);
        }

        var playerPawn = target.PlayerPawn?.Value;
        if (playerPawn == null) return;

        var velocity = playerPawn.AbsVelocity;
        playerPawn.Teleport(null, null, new Vector(
            velocity.X + Random.Shared.Next(100, 200),
            velocity.Y + Random.Shared.Next(100, 200),
            velocity.Z + Random.Shared.Next(200, 300)));

        playerPawn.Health -= damage;
        Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");

        var random = Random.Shared.Next(3);
        target.ExecuteClientCommand($"play {GetMusicChoice(random)}");

        if (playerPawn.Health <= 0) playerPawn.CommitSuicide(true, true);
    }

    private string GetMusicChoice(int randomNumber)
    {
        return randomNumber switch
        {
            0 => "sounds/player/damage1.vsnd",
            1 => "sounds/player/damage2.vsnd",
            2 => "sounds/player/damage3.vsnd",
            _ => string.Empty
        };
    }

    [CommandHelper(0, "[all | admins | players | <name>]", CommandUsage.CLIENT_ONLY)]
    [ConsoleCommand("css_who")]
    public void OnCmdWho(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller == null || !controller.IsValid) return;

        var filter = command.ArgCount > 1 ? command.GetArg(1).Trim().ToLower() : "all";
        var allPlayers = Utilities.GetPlayers().Where(p => p != null && p.IsValid && !p.IsHLTV).ToList();

        var filtered = allPlayers.Where(p =>
        {
            if (filter == "all") return true;
            if (filter == "admins") return IsAdmin(p.Index);
            if (filter == "players") return !IsAdmin(p.Index) && !p.IsBot;
            return p.PlayerName.ToLower().Contains(filter);
        }).ToList();

        PrintToChat(controller, Localizer["who.count", filtered.Count]);

        foreach (var client in filtered)
        {
            var adminTag = IsAdmin(client.Index) ? $" {ChatColors.Red}[Admin]" : "";
            PrintToChat(controller, $"{Localizer["who.player", client.PlayerName]}{adminTag}");
        }
    }

    [CommandHelper(1, "<map>")]
    [ConsoleCommand("css_map", "change map")]
    public void OnCmdChangeMap(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Changemap)) return;

        var cmdArg = (Utils.SplitArgs(command.ArgString, 1).FirstOrDefault() ?? string.Empty).Trim();
        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;

        if (cmdArg.Length > 0)
        {
            foreach (var t in GetMapFromMaps())
            {
                var isWorkshopMap = t.Trim().StartsWith("ws:", StringComparison.OrdinalIgnoreCase);

                if (!t.Trim().Contains(cmdArg, StringComparison.OrdinalIgnoreCase)) continue;

                var map = isWorkshopMap ? t.Trim().Substring(3) : t.Trim();
                PrintToChatAll(Localizer["map.changing", adminName, map]);
                AddTimer(3.0f, () => ChangeMap(map, isWorkshopMap));
                return;
            }
        }

        ReplyToCommand(controller, Localizer["map.not_exist"]);
    }

    [CommandHelper(1, "<#userid or username>")]
    [ConsoleCommand("css_slay", "kill a player")]
    public void OnCmdSlay(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Slay)) return;

        var args = Utils.SplitArgs(command.ArgString, 1);
        var target = GetPlayerFromUserIdOrName(args.FirstOrDefault() ?? string.Empty);

        if (!IsPlayerValid(controller, target, true)) return;

        if (controller != null && IsAdmin(target.Index) && !PlayerImmunityComparison(controller, target))
        {
            ReplyToCommand(controller, Localizer["slay.no_permission"]);
            return;
        }

        if (target.PlayerPawn.Value != null) target.PlayerPawn.Value.CommitSuicide(true, true);

        Announce(controller,
            Localizer["slay.killed_by", (controller != null ? controller.PlayerName : _config.BanFromConsoleName), target.PlayerName]);
    }

    [CommandHelper(1, "<#userid or username> [reason]")]
    [ConsoleCommand("css_kick", "Kick a player")]
    public void OnCmdKick(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Kick)) return;

        var args = Utils.SplitArgs(command.ArgString, 2);
        if (args.Length < 1) return;

        var target = GetPlayerFromUserIdOrName(args[0]);
        if (!IsPlayerValid(controller, target)) return;

        if (controller != null && IsAdmin(target.Index) && !PlayerImmunityComparison(controller, target))
        {
            ReplyToCommand(controller, Localizer["kick.no_permission"]);
            return;
        }

        var reason = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : Localizer["kick.reason"];
        target.Kick(reason);

        var msg = Localizer["kick.msg", (controller != null ? controller.PlayerName : _config.BanFromConsoleName), target.PlayerName];
        Announce(controller, msg);
    }

    [CommandHelper(2, "<#userid, username or steamid> <time[s/m/h/d/w]> [reason]")]
    [ConsoleCommand("css_ban", "ban")]
    public void OnCmdBan(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Ban)) return;

        var args = Utils.SplitArgs(command.ArgString, 3);
        if (args.Length < 2)
        {
            ReplyToCommand(controller, Localizer["ban.usage"]);
            return;
        }

        var targetArg = args[0];
        if (!Utils.TryParseDuration(args[1], out var durationSec))
        {
            ReplyToCommand(controller, Localizer["invalid_time"]);
            return;
        }

        var reason = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : "none";
        if (reason.Length > 250) reason = reason[..250];

        var startUnix = DateTime.UtcNow.GetUnixEpoch();
        var endUnix = durationSec == 0 ? 0 : DateTime.UtcNow.AddSeconds(durationSec).GetUnixEpoch();

        var adminName = controller?.PlayerName ?? _config.BanFromConsoleName;
        var adminSteamId = controller != null ? new SteamID(controller.SteamID).SteamId2 : _config.BanFromConsoleName;

        var hasSteamId = Utils.TryParseSteamId(targetArg, out var sid64, out var sid2);
        var target = hasSteamId
            ? Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && !p.IsBot && p.SteamID == sid64)
            : GetPlayerFromUserIdOrName(targetArg);

        if (target != null)
        {
            if (target.IsBot)
            {
                ReplyToCommand(controller, Localizer["player.invalid"]);
                return;
            }

            sid64 = target.SteamID;
            sid2 = new SteamID(sid64).SteamId2;
            hasSteamId = true;
        }

        if (!hasSteamId)
        {
            ReplyToCommand(controller, Localizer["player.not_found"]);
            return;
        }

        var targetOnline = target != null;
        var targetName = target?.PlayerName ?? sid2;

        var isCallerRoot = controller == null || HasAdminFlag(controller, AdminFlag.Root);
        var callerImmunity = controller != null && controller.Index < _adminUsers.Length
            ? _adminUsers[controller.Index]?.immunity ?? 0
            : 0;

        if (controller != null)
        {
            if (controller.SteamID == sid64)
            {
                ReplyToCommand(controller, Localizer["punish.self"]);
                return;
            }

            if (target != null && IsAdmin(target.Index) && !PlayerImmunityComparison(controller, target))
            {
                ReplyToCommand(controller, Localizer["ban.no_permission"]);
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!targetOnline && controller != null)
                {
                    var targetAdmin = await Database.GetAdminFromDb(sid2);
                    if (targetAdmin != null && !isCallerRoot && callerImmunity <= targetAdmin.immunity)
                    {
                        Server.NextFrame(() => ReplyToCommand(controller, Localizer["ban.no_permission"]));
                        return;
                    }
                }

                var user = new User
                {
                    admin_username = adminName,
                    admin_steamid = adminSteamId,
                    username = targetName,
                    steamid64 = sid64,
                    steamid = sid2,
                    reason = reason,
                    unban_reason = string.Empty,
                    admin_unlocked_username = string.Empty,
                    admin_unlocked_steamid = string.Empty,
                    start_ban_time = startUnix,
                    end_ban_time = endUnix,
                    ban_active = true
                };

                var result = await Database.AddBan(user);

                Server.NextFrame(() =>
                {
                    if (result.Success)
                        Utilities.GetPlayers()
                            .FirstOrDefault(p => p.IsValid && p.SteamID == sid64)
                            ?.Kick(Localizer["ban"]);

                    ReplyToCommand(controller, result.Message);
                });
            }
            catch (Exception e) { PrintLogError(e.ToString()); }
        });
    }

    private void HandleMute(CCSPlayerController? controller, CommandInfo command, MuteType type, string noPermissionKey)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Generic)) return;

        var args = Utils.SplitArgs(command.ArgString, 3);
        if (args.Length < 2)
        {
            ReplyToCommand(controller, Localizer["mute.usage"]);
            return;
        }

        var target = GetPlayerFromUserIdOrName(args[0]);

        if (!IsPlayerValid(controller, target)) return;

        if (target.IsBot)
        {
            ReplyToCommand(controller, Localizer["player.invalid"]);
            return;
        }

        if (controller != null)
        {
            if (controller.SteamID == target.SteamID)
            {
                ReplyToCommand(controller, Localizer["punish.self"]);
                return;
            }

            if (IsAdmin(target.Index) && !PlayerImmunityComparison(controller, target))
            {
                ReplyToCommand(controller, Localizer[noPermissionKey]);
                return;
            }
        }

        if (!Utils.TryParseDuration(args[1], out var duration))
        {
            ReplyToCommand(controller, Localizer["invalid_time"]);
            return;
        }

        var reason = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2]) ? args[2] : "none";
        if (reason.Length > 250) reason = reason[..250];

        ApplyMute(controller, target, type, reason, duration);
    }

    public void ApplyMute(CCSPlayerController? controller, CCSPlayerController target, MuteType type, string reason, int durationSec)
    {
        var startUnix = DateTime.UtcNow.GetUnixEpoch();
        var endUnix = durationSec == 0 ? 0 : DateTime.UtcNow.AddSeconds(durationSec).GetUnixEpoch();
        var targetIndex = target.Index;
        var targetSteamId64 = target.SteamID;

        var mute = new MuteUser
        {
            mute_type = (int)type,
            admin_username = controller != null ? controller.PlayerName : _config.BanFromConsoleName,
            admin_steamid = controller != null ? new SteamID(controller.SteamID).SteamId2 : _config.BanFromConsoleName,
            username = target.PlayerName,
            steamid64 = targetSteamId64,
            steamid = new SteamID(targetSteamId64).SteamId2,
            reason = reason,
            unmute_reason = string.Empty,
            admin_unlocked_username = string.Empty,
            admin_unlocked_steamid = string.Empty,
            start_mute_time = startUnix,
            end_mute_time = endUnix,
            mute_active = true
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await Database.AddMute(mute, controller);

                Server.NextFrame(() =>
                {
                    var current = Utilities.GetPlayers().FirstOrDefault(p =>
                        p.IsValid && p.Index == targetIndex && p.SteamID == targetSteamId64);

                    if (current != null) RefreshMuteState(current);
                });
            }
            catch (Exception e) { PrintLogError(e.ToString()); }
        });
    }

    private void HandleUnmute(CCSPlayerController? controller, CommandInfo command, MuteType type)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Unban)) return;

        var args = Utils.SplitArgs(command.ArgString, 2);
        if (args.Length < 1)
        {
            ReplyToCommand(controller, Localizer["unmute.usage"]);
            return;
        }

        CCSPlayerController? player = GetPlayerFromUserIdOrName(args[0]);
        string targetSteamId;

        if (player != null && player.IsValid)
        {
            targetSteamId = new SteamID(player.SteamID).SteamId2;
        }
        else if (Utils.TryParseSteamId(args[0], out _, out var parsedSteamId2))
        {
            targetSteamId = parsedSteamId2;
        }
        else
        {
            ReplyToCommand(controller, Localizer["player.not_found"]);
            return;
        }

        var reason = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "none";
        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
        var adminSteamId = controller != null ? new SteamID(controller.SteamID).SteamId2 : _config.BanFromConsoleName;

        _ = Task.Run(async () =>
        {
            try
            {
                var msg = await Database.UnmuteUser((int)type, adminName, adminSteamId, targetSteamId, reason);

                Server.NextFrame(() =>
                {
                    ReplyToCommand(controller, msg);

                    var online = player is { IsValid: true }
                        ? player
                        : Utilities.GetPlayers().FirstOrDefault(p =>
                            p.IsValid && !p.IsBot && p.SteamID != 0 && new SteamID(p.SteamID).SteamId2 == targetSteamId);

                    if (online != null) RefreshMuteState(online);
                });
            }
            catch (Exception e) { PrintLogError(e.ToString()); }
        });
    }

    [CommandHelper(2, "<#userid or username> <time[s/m/h/d/w]> [reason]")]
    [ConsoleCommand("css_mute", "mute")]
    public void OnCmdMute(CCSPlayerController? controller, CommandInfo command)
    {
        HandleMute(controller, command, MuteType.Micro, "mute.no_permission");
    }

    [CommandHelper(1, "<steamid | #userid> [reason]")]
    [ConsoleCommand("css_unmute", "unmute")]
    public void OnCmdUnmute(CCSPlayerController? controller, CommandInfo command)
    {
        HandleUnmute(controller, command, MuteType.Micro);
    }

    [CommandHelper(2, "<#userid or username> <time[s/m/h/d/w]> [reason]")]
    [ConsoleCommand("css_gag", "gag")]
    public void OnCmdGag(CCSPlayerController? controller, CommandInfo command)
    {
        HandleMute(controller, command, MuteType.Chat, "gag.no_permission");
    }

    [CommandHelper(1, "<steamid | #userid> [reason]")]
    [ConsoleCommand("css_ungag", "ungag")]
    public void OnCmdUngag(CCSPlayerController? controller, CommandInfo command)
    {
        HandleUnmute(controller, command, MuteType.Chat);
    }

    [CommandHelper(2, "<#userid or username> <time[s/m/h/d/w]> [reason]")]
    [ConsoleCommand("css_silence")]
    public void OnCmdSilence(CCSPlayerController? controller, CommandInfo command)
    {
        HandleMute(controller, command, MuteType.All, "silence.no_permission");
    }

    [CommandHelper(1, "<#userid or steamid> [reason]")]
    [ConsoleCommand("css_unsilence", "unsilence")]
    public void OnCmdUnSilence(CCSPlayerController? controller, CommandInfo command)
    {
        HandleUnmute(controller, command, MuteType.All);
    }

    [CommandHelper(1, "<steamid> [username] [immunity] [flags] [time]\nif you don't want to update something, don't leave it blank, write `-` or `-s`\nExample of updating flags: css_updateadmin \"STEAM_0:0:123456\" -s -s bcd -s", CommandUsage.SERVER_ONLY)]
    [ConsoleCommand("css_updateadmin")]
    public void OnCmdUpdateAdmin(CCSPlayerController? controller, CommandInfo command)
    {
        var args = Utils.SplitArgs(command.ArgString);
        if (args.Length < 1 || !Utils.TryParseSteamId(args[0], out _, out var steamId2))
        {
            ReplyToCommand(controller, Localizer["invalid_steamid"]);
            return;
        }

        string Arg(int i) => args.Length > i ? args[i] : "-s";

        long endTime = -1;
        if (Utils.TryParseDuration(Arg(4), out var duration))
            endTime = duration == 0 ? 0 : DateTime.UtcNow.AddSeconds(duration).GetUnixEpoch();

        var admin = new Admins
        {
            username = Arg(1),
            steamid = steamId2,
            start_time = -1,
            end_time = endTime,
            immunity = int.TryParse(Arg(2), out var i) ? i : -1,
            flags = Arg(3)
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await Database.UpdateAdmin(admin);
                Server.NextFrame(() => RefreshAdminFor(steamId2));
            }
            catch (Exception e) { PrintLogError(e.ToString()); }
        });
    }

    [CommandHelper(5, "<username> <steamid> <immunity> <flags> <time[s/m/h/d/w]>", CommandUsage.SERVER_ONLY)]
    [ConsoleCommand("css_addadmin")]
    public void OnCmdAddAdmin(CCSPlayerController? controller, CommandInfo command)
    {
        var args = Utils.SplitArgs(command.ArgString);
        if (args.Length < 5 || !Utils.TryParseSteamId(args[1], out _, out var steamId2))
        {
            ReplyToCommand(controller, Localizer["invalid_steamid"]);
            return;
        }

        if (!Utils.TryParseDuration(args[4], out var time))
        {
            ReplyToCommand(controller, Localizer["invalid_time"]);
            return;
        }

        var admin = new Admins
        {
            username = args[0],
            steamid = steamId2,
            start_time = DateTime.UtcNow.GetUnixEpoch(),
            end_time = time == 0 ? 0 : DateTime.UtcNow.AddSeconds(time).GetUnixEpoch(),
            immunity = int.TryParse(args[2], out var i) ? i : 10,
            flags = new string(args[3].Where(char.IsLetter).ToArray())
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await Database.AddAdmin(admin, controller);
                Server.NextFrame(() => RefreshAdminFor(steamId2));
            }
            catch (Exception e) { PrintLogError(e.ToString()); }
        });
    }

    [CommandHelper(1, "<steamid> [reason]")]
    [ConsoleCommand("css_unban", "unban")]
    public void OnCmdUnban(CCSPlayerController? controller, CommandInfo command)
    {
        if (!CheckingForAdminAndFlag(controller, AdminFlag.Unban)) return;

        var args = Utils.SplitArgs(command.ArgString, 2);
        if (args.Length < 1) return;

        var steamId = args[0].Trim();
        var reason = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1] : "none";

        var adminName = controller != null ? controller.PlayerName : _config.BanFromConsoleName;
        var adminSteamId = controller != null ? new SteamID(controller.SteamID).SteamId2 : _config.BanFromConsoleName;

        Task.Run(async () =>
        {
            try
            {
                var msg = await Database.UnbanUser(adminName, adminSteamId, steamId, reason);
                Server.NextFrame(() => ReplyToCommand(controller, msg));
            }
            catch (Exception e)
            {
                PrintLogError(e.ToString());
            }
        });
    }

    [CommandHelper(1, "<steamid>", CommandUsage.SERVER_ONLY)]
    [ConsoleCommand("css_deleteadmin", "delete admin")]
    public void OnCmdDeleteAdmin(CCSPlayerController? controller, CommandInfo command)
    {
        if (controller != null) return;

        var args = Utils.SplitArgs(command.ArgString, 1);
        if (args.Length < 1 || !Utils.TryParseSteamId(args[0], out _, out var steamId2))
        {
            ReplyToCommand(controller, Localizer["invalid_steamid"]);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Database.DeleteAdminAsync(steamId2);
                Server.NextFrame(() => RefreshAdminFor(steamId2));
            }
            catch (Exception e) { PrintLogError(e.ToString()); }
        });
    }

    private CCSPlayerController? GetPlayerFromUserIdOrName(string player)
    {
        if (string.IsNullOrWhiteSpace(player))
            return null;

        player = player.Trim();

        if (player.StartsWith('#'))
        {
            if (!int.TryParse(player[1..], out var userId))
                return null;

            return Utilities.GetPlayerFromUserid(userId);
        }

        if (Utils.TryParseSteamId(player, out var steamId64, out _))
        {
            return Utilities.GetPlayers().FirstOrDefault(u =>
                u != null && u.IsValid && !u.IsBot && u.SteamID == steamId64);
        }

        var all = Utilities.GetPlayers().Where(u => u != null && u.IsValid && !u.IsHLTV).ToList();

        var exact = all.FirstOrDefault(u => u.PlayerName.Equals(player, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var matches = all.Where(u => u.PlayerName.Contains(player, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();

        return matches.Count == 1 ? matches[0] : null;
    }

    public bool HasAdminFlag(CCSPlayerController? controller, AdminFlag flag)
    {
        if (controller == null || !controller.IsValid) return false;
        var entityIndex = controller.Index;

        return IsAdmin(entityIndex) && HasLetterInUserFlags(entityIndex, (char)flag);
    }

    public bool CheckingForAdminAndFlag(CCSPlayerController? controller, AdminFlag flag)
    {
        if (controller == null) return true;

        if (HasAdminFlag(controller, flag)) return true;

        PrintToChat(controller, Localizer["no_access"]);
        return false;
    }

    private string GetTextInsideQuotes(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var startIndex = input.IndexOf('"');
        var endIndex = input.LastIndexOf('"');

        if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
        {
            return input.Substring(startIndex + 1, endIndex - startIndex - 1);
        }

        return input.Trim();
    }

    private bool IsPlayerValid(CCSPlayerController? player,[NotNullWhen(true)] CCSPlayerController? target, bool isKickOrSlap = false)
    {
        if (target == null || !target.IsValid)
        {
            ReplyToCommand(player, Localizer["player.not_found"]);
            return false;
        }

        if (isKickOrSlap)
        {
            if (!target.PawnIsAlive)
            {
                ReplyToCommand(player, Localizer["player.invalid"]);
                return false;
            }
        }

        return true;
    }

    private string BuildConnectionString()
    {
        var dbConfig = _config.Connection;

        Console.WriteLine("Building connection string");
        var builder = new MySqlConnectionStringBuilder
        {
            Database = dbConfig.Database,
            UserID = dbConfig.User,
            Password = dbConfig.Password,
            Server = dbConfig.Host,
            Port = (uint)dbConfig.Port,
        };

        Console.WriteLine("OK!");
        return builder.ConnectionString;
    }

    private Config LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "database.json");
        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;

        return config;
    }

    private Config CreateConfig(string configPath)
    {
        var config = new Config
        {
            BanFromConsoleName = "Console",
            Connection = new BaseAdminDb
            {
                Host = "",
                Database = "",
                User = "",
                Password = "",
                Port = 3306
            }
        };

        File.WriteAllText(configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        return config;
    }

    private IEnumerable<string> ReadLinesOrCreate(string relativePath, string[] defaults)
    {
        var path = Path.Combine(ModuleDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
            File.WriteAllLines(path, defaults);

        return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    public bool TryParseTimeOption(string line, out string label, out int seconds)
    {
        label = line;
        seconds = 0;

        var separator = line.LastIndexOf(':');
        if (separator <= 0) return false;

        label = line[..separator].Trim();
        return int.TryParse(line[(separator + 1)..], out seconds) && seconds >= 0;
    }

    public IEnumerable<string> ReadAllBanReasons()
    {
        return ReadLinesOrCreate("admin/ban_reasons.txt", new[] { "Aim", "Wallhack", "Violation" });
    }

    public IEnumerable<string> ReadAllMuteReasons()
    {
        return ReadLinesOrCreate("admin/mute_reasons.txt", new[] { "Spam Mic/Chat", "Inappropriate behavior", "16+" });
    }

    public IEnumerable<string> ReadAllTime()
    {
        return ReadLinesOrCreate("admin/times.txt", new[] { "permanently:0", "1 hours:3600", "1 day:86400", "1 week:604800" });
    }

    public string[] GetMapFromMaps()
    {
        return ReadLinesOrCreate("maps.txt", new[] { "de_dust2", "ws:awp_lego_2" }).ToArray();
    }

    public IEnumerable<string> ReadAllImmunity()
    {
        return ReadLinesOrCreate("admin/immunity.txt", new[] { "1", "10", "20", "30" });
    }

    public bool HasLetterInUserFlags(uint index, char letter)
    {
        if (index >= _adminUsers.Length || !IsAdmin(index)) return false;

        var adminUser = _adminUsers[(int)index]?.flags;
        if (string.IsNullOrEmpty(adminUser)) return false;

        return adminUser.Contains((char)AdminFlag.Root) || adminUser.Contains(letter);
    }

    public bool PlayerImmunityComparison(CCSPlayerController? player, CCSPlayerController? target)
    {
        if (player == null)
            return true;

        if (!player.IsValid || target == null || !target.IsValid)
            return false;

        if (player.SteamID == target.SteamID)
            return false;

        if (player.Index >= _adminUsers.Length || target.Index >= _adminUsers.Length)
            return false;

        var playerAdmin = _adminUsers[player.Index];
        var targetAdmin = _adminUsers[target.Index];

        if (playerAdmin == null)
            return false;

        if (targetAdmin == null || !IsAdmin(target.Index))
            return true;

        if (HasAdminFlag(player, AdminFlag.Root) && !HasAdminFlag(target, AdminFlag.Root))
            return true;

        return playerAdmin.immunity > targetAdmin.immunity;
    }

    public bool IsAdmin(uint index)
    {
        if (index >= _adminUsers.Length)
            return false;

        var user = _adminUsers[(int)index];
        if (user == null)
            return false;

        if (user.end_time != 0 &&
            DateTime.UtcNow.GetUnixEpoch() >= user.end_time)
            return false;

        return true;
    }

    public void SetAdminInMemory(uint index, Admins admin)
    {
        if (index < _adminUsers.Length)
        {
            _adminUsers[(int)index] = admin;
        }
    }

    public bool IsUserMute(uint index)
    {
        if (index >= _muteUsers.Length) return false;

        var user = _muteUsers[(int)index];
        if (user == null || !user.MuteActive) return false;

        if (user.EndMuteTime == 0) return true;

        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() < user.EndMuteTime;
    }

    private bool IsChatMuted(uint index)
    {
        if (!IsUserMute(index)) return false;

        var user = _muteUsers[(int)index];
        return user != null && user.MuteType is (int)MuteType.Chat or (int)MuteType.All;
    }

    public string ExtractValueInQuotes(string input)
    {
        var match = Regex.Match(input, @"""([^""]*)""");

        return match.Success ? match.Groups[1].Value : input;
    }

    public void ReplyToCommand(CCSPlayerController? controller, string message, params object?[] args)
    {
        if (controller != null)
        {
            if (controller.IsValid) PrintToChat(controller, message);
            return;
        }

        Logger.LogInformation("{Message}", new string(message.Where(c => c > 0x10).ToArray()));
    }

    private void Announce(CCSPlayerController? controller, string message)
    {
        PrintToChatAll(message);

        if (controller == null)
            ReplyToCommand(null, message);
    }

    public void ChangeMap(string mapName, bool isWorkshop = false)
    {
        if (isWorkshop)
        {
            if (ulong.TryParse(mapName.Trim(), out _))
            {
                Server.ExecuteCommand($"host_workshop_map {mapName.Trim()}");
            }
            else
            {
                Server.ExecuteCommand($"ds_workshop_changelevel {mapName.Trim()}");
            }
        }
        else
        {
            Server.ExecuteCommand($"changelevel {mapName.Trim()}");
        }
    }

    public void PrintToChat(CCSPlayerController controller, string msg)
    {
        controller.PrintToChat($"[ {ChatColors.Blue}Admin \x01] {msg}");
    }

    public void PrintToCenterAll(string msg)
    {
        VirtualFunctions.ClientPrintAll(HudDestination.Center, $"Admin: {msg}", 0, 0, 0, 0, 0);
    }

    public void PrintToChatAll(string msg)
    {
        Server.PrintToChatAll($"[ {ChatColors.Blue}Admin \x01] {msg}");
    }

    public void PrintLogInfo(string? message, params object?[] args)
    {
        Logger.LogInformation(message ?? string.Empty, args);
    }

    public void PrintLogError(string? message, params object?[] args)
    {
        Logger.LogError(message ?? string.Empty, args);
    }
}

public class Config
{
    public string BanFromConsoleName { get; init; } = "Console";
    public BaseAdminDb Connection { get; init; } = null!;
}

public class BaseAdminDb
{
    public required string Host { get; init; }
    public required string Database { get; init; }
    public required string User { get; init; }
    public required string Password { get; init; }
    public int Port { get; init; }
}