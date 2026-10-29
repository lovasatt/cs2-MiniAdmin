using System;
using System.Linq;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using Dapper;
using MySqlConnector;

namespace BaseAdmin;

public class Database
{
    private readonly string _dbConnectionString;
    private readonly BaseAdmin _baseAdmin;

    public Database(BaseAdmin baseAdmin, string dbConnectionString)
    {
        _baseAdmin = baseAdmin;
        _dbConnectionString = dbConnectionString;
    }

    public async Task InitializeDatabaseAsync()
    {
        await CreateTable();
        await CreateAdminsTable();
        await CreateMuteTable();
    }

    public async Task CreateTable()
    {
        try
        {
            await using var dbConnection = new MySqlConnection(_dbConnectionString);
            await dbConnection.OpenAsync();

            var createBansTable = @"
                CREATE TABLE IF NOT EXISTS `miniadmin_bans` (
                    `id` INT AUTO_INCREMENT,
                    `admin_username` VARCHAR(255) NOT NULL,
                    `admin_steamid` VARCHAR(255) NOT NULL,
                    `username` VARCHAR(255) NOT NULL,
                    `steamid64` BIGINT NOT NULL,
                    `steamid` VARCHAR(255) NOT NULL,
                    `reason` VARCHAR(255) NOT NULL,
                    `unban_reason` VARCHAR(255) NOT NULL,
                    `admin_unlocked_username` VARCHAR(255) NOT NULL,
                    `admin_unlocked_steamid` VARCHAR(255) NOT NULL,
                    `start_ban_time` BIGINT NOT NULL,
                    `end_ban_time` BIGINT NOT NULL,
                    `ban_active` BOOLEAN NOT NULL,
                    PRIMARY KEY (`id`),
                    KEY `idx_bans_lookup` (`steamid64`, `ban_active`),
                    KEY `idx_bans_steamid_active` (`steamid`, `ban_active`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            await dbConnection.ExecuteAsync(createBansTable);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task CreateAdminsTable()
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var createAdminsTable = @"
                CREATE TABLE IF NOT EXISTS `miniadmin_admins` (
                    `id` INT AUTO_INCREMENT,
                    `username` VARCHAR(255) NOT NULL,
                    `steamid` VARCHAR(255) NOT NULL,
                    `start_time` BIGINT NOT NULL,
                    `end_time` BIGINT NOT NULL,
                    `immunity` INT NOT NULL,
                    `flags` VARCHAR(255) NOT NULL,
                    PRIMARY KEY (`id`),
                    UNIQUE KEY `idx_admin_steamid` (`steamid`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            await connection.ExecuteAsync(createAdminsTable);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task CreateMuteTable()
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var createMuteTable = @"
                CREATE TABLE IF NOT EXISTS `miniadmin_mute` (
                    `id` INT AUTO_INCREMENT,
                    `mute_type` INT NOT NULL,
                    `admin_username` VARCHAR(255) NOT NULL,
                    `admin_steamid` VARCHAR(255) NOT NULL,
                    `username` VARCHAR(255) NOT NULL,
                    `steamid64` BIGINT NOT NULL,
                    `steamid` VARCHAR(255) NOT NULL,
                    `reason` VARCHAR(255) NOT NULL,
                    `unmute_reason` VARCHAR(255) NOT NULL,
                    `admin_unlocked_username` VARCHAR(255) NOT NULL,
                    `admin_unlocked_steamid` VARCHAR(255) NOT NULL,
                    `start_mute_time` BIGINT NOT NULL,
                    `end_mute_time` BIGINT NOT NULL,
                    `mute_active` BOOLEAN NOT NULL,
                    PRIMARY KEY (`id`),
                    KEY `idx_mute_lookup` (`steamid64`, `mute_active`),
                    KEY `idx_mute_steamid_active` (`steamid`, `mute_active`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

            await connection.ExecuteAsync(createMuteTable);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task<(bool Success, string Message)> AddBan(User user)
    {
        try
        {
            var isUserBanned = await IsUserBanned(user.steamid);
            if (isUserBanned)
                return (false, _baseAdmin.Localizer["db.ban.already_banned", user.steamid]);

            await using var connection = new MySqlConnection(_dbConnectionString);

            var affectedRows = await connection.ExecuteAsync(@"
                INSERT INTO miniadmin_bans (admin_username, admin_steamid, username, steamid64, steamid, reason, unban_reason, admin_unlocked_username, admin_unlocked_steamid, start_ban_time, end_ban_time, ban_active)
                VALUES (@admin_username, @admin_steamid, @username, @steamid64, @steamid, @reason, @unban_reason, @admin_unlocked_username, @admin_unlocked_steamid, @start_ban_time, @end_ban_time, @ban_active);
                ", user);

            if (affectedRows != 1)
                return (false, _baseAdmin.Localizer["db.ban.failed"]);

            return (true, _baseAdmin.Localizer["db.ban.success", user.username, user.steamid]);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return (false, _baseAdmin.Localizer["db.ban.error"]);
        }
    }

    public async Task AddMute(MuteUser user, CCSPlayerController? controller)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var activeUserMute = await GetActiveMuteAsync(user.steamid);

            if (activeUserMute != null)
            {
                if (activeUserMute.mute_type == (int)BaseAdmin.MuteType.All)
                {
                    Server.NextFrame(() => _baseAdmin.ReplyToCommand(controller,
                        _baseAdmin.Localizer["db.mute.already_muted_all", user.steamid]));
                    return;
                }

                if (activeUserMute.mute_type == user.mute_type)
                {
                    Server.NextFrame(() =>
                        _baseAdmin.ReplyToCommand(controller, _baseAdmin.Localizer["db.mute.already_disabled"]));
                    return;
                }

                user.id = activeUserMute.id;
                user.mute_type = (int)BaseAdmin.MuteType.All;
                user.end_mute_time = activeUserMute.end_mute_time == 0 || user.end_mute_time == 0
                    ? 0
                    : Math.Max(activeUserMute.end_mute_time, user.end_mute_time);

                await connection.ExecuteAsync(@"
                UPDATE miniadmin_mute
                SET mute_type = @mute_type,
                    admin_username = @admin_username,
                    admin_steamid = @admin_steamid,
                    username = @username,
                    steamid64 = @steamid64,
                    steamid = @steamid,
                    reason = @reason,
                    unmute_reason = @unmute_reason,
                    admin_unlocked_username = @admin_unlocked_username,
                    admin_unlocked_steamid = @admin_unlocked_steamid,
                    start_mute_time = @start_mute_time,
                    end_mute_time = @end_mute_time,
                    mute_active = @mute_active
                WHERE id = @id;", user);

                Server.NextFrame(() => _baseAdmin.ReplyToCommand(controller,
                    _baseAdmin.Localizer["db.mute.updated", user.username, user.steamid]));
                return;
            }

            await connection.ExecuteAsync(@"
            INSERT INTO miniadmin_mute (mute_type, admin_username, admin_steamid, username, steamid64, steamid, reason, unmute_reason, admin_unlocked_username, admin_unlocked_steamid, start_mute_time, end_mute_time, mute_active)
            VALUES (@mute_type, @admin_username, @admin_steamid, @username, @steamid64, @steamid, @reason, @unmute_reason, @admin_unlocked_username, @admin_unlocked_steamid, @start_mute_time, @end_mute_time, @mute_active);
            ", user);

            Server.NextFrame(() =>
                _baseAdmin.ReplyToCommand(controller, _baseAdmin.Localizer["db.mute.success", user.username, user.steamid]));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Server.NextFrame(() => _baseAdmin.ReplyToCommand(controller, _baseAdmin.Localizer["db.error"]));
        }
    }

    public async Task AddAdmin(Admins admin, CCSPlayerController? player = null, bool fromMenu = false)
    {
        try
        {
            var isAdminExist = await GetAdminFromDb(admin.steamid);
            if (isAdminExist != null)
            {
                _baseAdmin.PrintLogError("An administrator with the SteamId identifier {steamid} already exists.",
                    admin.steamid);

                if (fromMenu)
                {
                    if (player != null)
                        Server.NextFrame(() => _baseAdmin.PrintToChat(player,
                            _baseAdmin.Localizer["db.admin.already_exists", admin.steamid]));
                }

                return;
            }

            await using var connection = new MySqlConnection(_dbConnectionString);

            await connection.ExecuteAsync(@"
                INSERT INTO 
                    miniadmin_admins (username, steamid, start_time, end_time, immunity, flags)
                VALUES 
                    (@username, @steamid, @start_time, @end_time, @immunity, @flags);", admin);

            _baseAdmin.PrintLogInfo("Admin '{username}[{steamid}]' successfully added",
                admin.username, admin.steamid);
            if (fromMenu)
            {
                if (player != null)
                {
                    Server.NextFrame(() =>
                        _baseAdmin.PrintToChat(player,
                            _baseAdmin.Localizer["db.admin.added", admin.username, admin.steamid]));
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task UpdateAdmin(Admins admin)
    {
        try
        {
            var existingAdmin = await GetAdminFromDb(admin.steamid);
            if (existingAdmin == null)
            {
                _baseAdmin.PrintLogError("Administrator with SteamId {steamid} does not exist.", admin.steamid);
                return;
            }

            var hasChanges = false;

            if (existingAdmin.username != admin.username && admin.username is not ("-" or "-s"))
            {
                existingAdmin.username = admin.username;
                hasChanges = true;
            }

            if (existingAdmin.end_time != admin.end_time && admin.end_time is not -1)
            {
                existingAdmin.end_time = admin.end_time;
                hasChanges = true;
            }

            if (existingAdmin.immunity != admin.immunity && admin.immunity is not -1)
            {
                existingAdmin.immunity = admin.immunity;
                hasChanges = true;
            }

            if (existingAdmin.flags != admin.flags && admin.flags is not ("-" or "-s"))
            {
                existingAdmin.flags = admin.flags;
                hasChanges = true;
            }

            if (!hasChanges)
            {
                _baseAdmin.PrintLogInfo("No changes detected for administrator '{username}[{steamid}]'",
                    existingAdmin.username, existingAdmin.steamid);
                return;
            }

            await using var connection = new MySqlConnection(_dbConnectionString);

            await connection.ExecuteAsync(@"
            UPDATE miniadmin_admins 
            SET 
                username = @username, 
                end_time = @end_time, 
                immunity = @immunity, 
                flags = @flags
            WHERE 
                steamid = @steamid;", existingAdmin);

            _baseAdmin.PrintLogInfo("Administrator '{username}[{steamid}]' successfully updated",
                existingAdmin.username, existingAdmin.steamid);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public async Task<string> DeleteExpiredAdminsAsync()
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);

            var deleteAdmins = await connection.QueryAsync<Admins>(
                "SELECT * FROM miniadmin_admins WHERE end_time <= @CurrentTime AND end_time > 0",
                new { CurrentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

            var adminsEnumerable = deleteAdmins.ToList();
            if (adminsEnumerable.Any())
            {
                foreach (var deleteAdmin in adminsEnumerable)
                {
                    await connection.ExecuteAsync(@"DELETE FROM miniadmin_admins WHERE steamid = @SteamId;",
                        new { SteamId = deleteAdmin.steamid });

                    _baseAdmin.PrintLogInfo("Admin {steamid} successfully deleted",
                        deleteAdmin.steamid);
                }

                _baseAdmin.PrintLogInfo("Expired admins successfully deleted");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return string.Empty;
    }

    public async Task<string> DeleteAdminAsync(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var raw = steamId.Trim('"', '\'', ' ');
            string s0 = raw;
            string s1 = raw;

            if (Utils.TryParseSteamId(raw, out _, out var parsed2))
            {
                s1 = parsed2;
                s0 = "STEAM_0:" + parsed2.Substring(8);
            }

            await connection.ExecuteAsync(@"
                DELETE FROM miniadmin_admins 
                WHERE (steamid = @Raw OR steamid = @S0 OR steamid = @S1);",
                new { Raw = raw, S0 = s0, S1 = s1 });

            _baseAdmin.PrintLogInfo("Admin {steamId} successfully deleted", steamId);
        }
        catch (Exception e)
        {
            _baseAdmin.PrintLogError(e.ToString());
        }

        return string.Empty;
    }

    public async Task<string> UnbanUser(string adminName, string adminSteamId, string steamId, string reason)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var raw = steamId.Trim('"', '\'', ' ');
            ulong id64 = 0;
            string s0 = raw;
            string s1 = raw;

            if (Utils.TryParseSteamId(raw, out var parsed64, out var parsed2))
            {
                id64 = parsed64;
                s1 = parsed2;
                s0 = "STEAM_0:" + parsed2.Substring(8);
            }
            else if (ulong.TryParse(raw, out var num64))
            {
                id64 = num64;
            }

            var user = await connection.QueryFirstOrDefaultAsync<User>(@"
                SELECT * FROM miniadmin_bans 
                WHERE (steamid = @Raw OR steamid = @S0 OR steamid = @S1 OR (steamid64 != 0 AND steamid64 = @Id64))
                  AND ban_active = 1
                LIMIT 1;",
                new { Raw = raw, S0 = s0, S1 = s1, Id64 = id64 });

            if (user == null) return _baseAdmin.Localizer["db.unban.not_found"];

            user.unban_reason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;
            user.admin_unlocked_username = adminName;
            user.admin_unlocked_steamid = adminSteamId;
            user.ban_active = false;

            await connection.ExecuteAsync(@"
                UPDATE miniadmin_bans
                SET unban_reason = @unban_reason, 
                    admin_unlocked_username = @admin_unlocked_username,
                    admin_unlocked_steamid = @admin_unlocked_steamid, 
                    ban_active = 0
                WHERE id = @id;", user);

            return _baseAdmin.Localizer["db.unban.success", raw, user.unban_reason];
        }
        catch (Exception e)
        {
            _baseAdmin.PrintLogError(e.ToString());
        }

        return _baseAdmin.Localizer["db.error"];
    }

    public async Task<string> UnmuteUser(int unmuteType, string adminName, string adminSteamId, string steamId,
        string reason)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var raw = steamId.Trim('"', '\'', ' ');
            ulong id64 = 0;
            string s0 = raw;
            string s1 = raw;

            if (Utils.TryParseSteamId(raw, out var parsed64, out var parsed2))
            {
                id64 = parsed64;
                s1 = parsed2;
                s0 = "STEAM_0:" + parsed2.Substring(8);
            }
            else if (ulong.TryParse(raw, out var num64))
            {
                id64 = num64;
            }

            var user = await connection.QueryFirstOrDefaultAsync<MuteUser>(@"
                SELECT * FROM miniadmin_mute 
                WHERE (steamid = @Raw OR steamid = @S0 OR steamid = @S1 OR (steamid64 != 0 AND steamid64 = @Id64))
                  AND mute_active = 1
                LIMIT 1;",
                new { Raw = raw, S0 = s0, S1 = s1, Id64 = id64 });

            if (user == null) return _baseAdmin.Localizer["db.unmute.not_found"];

            user.unmute_reason = string.IsNullOrWhiteSpace(reason) ? "none" : reason;
            user.admin_unlocked_username = adminName;
            user.admin_unlocked_steamid = adminSteamId;

            if (unmuteType == -1 || unmuteType == (int)BaseAdmin.MuteType.All)
            {
                user.mute_active = false;
            }
            else if (user.mute_type == (int)BaseAdmin.MuteType.All)
            {
                if (unmuteType == (int)BaseAdmin.MuteType.Micro)
                    user.mute_type = (int)BaseAdmin.MuteType.Chat;
                else if (unmuteType == (int)BaseAdmin.MuteType.Chat)
                    user.mute_type = (int)BaseAdmin.MuteType.Micro;
            }
            else if (user.mute_type == unmuteType)
            {
                user.mute_active = false;
            }
            else
            {
                return _baseAdmin.Localizer["db.unmute.not_found"];
            }

            await connection.ExecuteAsync(@"
                UPDATE miniadmin_mute
                SET 
                    mute_type = @mute_type,
                    unmute_reason = @unmute_reason, 
                    admin_unlocked_username = @admin_unlocked_username,
                    admin_unlocked_steamid = @admin_unlocked_steamid, 
                    mute_active = @mute_active
                WHERE id = @id;", user);

            return _baseAdmin.Localizer["db.unmute.success", raw, user.unmute_reason];
        }
        catch (Exception e)
        {
            _baseAdmin.PrintLogError(e.ToString());
        }

        return _baseAdmin.Localizer["db.error"];
    }

    public async Task<MuteUser?> GetActiveMuteAsync(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var raw = steamId.Trim('"', '\'', ' ');
            ulong id64 = 0;
            string s0 = raw;
            string s1 = raw;

            if (Utils.TryParseSteamId(raw, out var parsed64, out var parsed2))
            {
                id64 = parsed64;
                s1 = parsed2;
                s0 = "STEAM_0:" + parsed2.Substring(8);
            }
            else if (ulong.TryParse(raw, out var num64))
            {
                id64 = num64;
            }

            var muteData = await connection.QueryFirstOrDefaultAsync<MuteUser>(@"
                SELECT * FROM miniadmin_mute 
                WHERE (steamid = @Raw OR steamid = @S0 OR steamid = @S1 OR (steamid64 != 0 AND steamid64 = @Id64)) 
                  AND mute_active = 1
                LIMIT 1;",
                new { Raw = raw, S0 = s0, S1 = s1, Id64 = id64 });

            if (muteData == null) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (muteData.end_mute_time != 0 && muteData.end_mute_time <= now)
            {
                await connection.ExecuteAsync(@"
                    UPDATE miniadmin_mute 
                    SET mute_active = 0, 
                        unmute_reason = @Reason,
                        admin_unlocked_username = 'Console',
                        admin_unlocked_steamid = 'Console'
                    WHERE id = @Id;",
                    new { Reason = _baseAdmin.Localizer["unban.deadline_passed"].Value, Id = muteData.id });

                return null;
            }

            return muteData;
        }
        catch (Exception e)
        {
            _baseAdmin.PrintLogError(e.ToString());
            return null;
        }
    }

    private async Task<bool> IsUserBanned(string steamId)
    {
        await using var connection = new MySqlConnection(_dbConnectionString);
        await connection.OpenAsync();

        var raw = steamId.Trim('"', '\'', ' ');
        ulong id64 = 0;
        string s0 = raw;
        string s1 = raw;

        if (Utils.TryParseSteamId(raw, out var parsed64, out var parsed2))
        {
            id64 = parsed64;
            s1 = parsed2;
            s0 = "STEAM_0:" + parsed2.Substring(8);
        }
        else if (ulong.TryParse(raw, out var num64))
        {
            id64 = num64;
        }

        var existingBan = await connection.QueryFirstOrDefaultAsync<User>(@"
            SELECT * FROM miniadmin_bans 
            WHERE (steamid = @Raw OR steamid = @S0 OR steamid = @S1 OR (steamid64 != 0 AND steamid64 = @Id64))
              AND ban_active = 1
            LIMIT 1;",
            new { Raw = raw, S0 = s0, S1 = s1, Id64 = id64 });

        if (existingBan == null) return false;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (existingBan.end_ban_time != 0 && existingBan.end_ban_time <= now)
        {
            await connection.ExecuteAsync(@"
                UPDATE miniadmin_bans 
                SET ban_active = 0, 
                    unban_reason = @Reason,
                    admin_unlocked_username = 'Console',
                    admin_unlocked_steamid = 'Console'
                WHERE id = @Id;",
                new { Reason = _baseAdmin.Localizer["unban.deadline_passed"].Value, Id = existingBan.id });

            return false;
        }

        return true;
    }

    public async Task<bool> IsUserMuted(string steamId)
    {
        var mute = await GetActiveMuteAsync(steamId);
        return mute != null;
    }

    public async Task<Admins?> GetAdminFromDb(string steamId)
    {
        try
        {
            await using var connection = new MySqlConnection(_dbConnectionString);
            await connection.OpenAsync();

            var raw = steamId.Trim('"', '\'', ' ');
            string s0 = raw;
            string s1 = raw;

            if (Utils.TryParseSteamId(raw, out _, out var parsed2))
            {
                s1 = parsed2;
                s0 = "STEAM_0:" + parsed2.Substring(8);
            }

            var existingAdmin = await connection.QueryFirstOrDefaultAsync<Admins>(@"
                SELECT * FROM miniadmin_admins 
                WHERE (steamid = @Raw OR steamid = @S0 OR steamid = @S1)
                LIMIT 1;",
                new { Raw = raw, S0 = s0, S1 = s1 });

            return existingAdmin;
        }
        catch (Exception e)
        {
            _baseAdmin.PrintLogError(e.ToString());
            return null;
        }
    }
}