# cs2-MiniAdmin
Modernized, high-performance administration plugin for Counter-Strike 2 powered by [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

---

## What's New in v1.2.0
* **High-Performance Database Engine:** Added composite indexes (`idx_bans_lookup`, `idx_mute_lookup`, `idx_admin_steamid`) in MariaDB/MySQL, replacing slow Full Table Scans with instant sub-millisecond lookups on player connect.
* **Y2038-Proof 64-bit Timestamps:** Upgraded all database and internal epoch timestamps to `long` (64-bit `BIGINT`).
* **Automated Punishment Expiration (Solution "A"):** Expired temporary bans and mutes are automatically archived in-place upon inspection, preventing ghost records from blocking new punishments on offline players.
* **Multi-Admin Safe Menu Engine:** Menus are instantiated dynamically per-player, preventing staff members from clearing or overwriting each other's menu views.
* **Dedicated In-Game Unmute Menu:** Added an interactive menu to view and selectively lift active voice mutes and chat gags.
* **Reconnection Ban Enforcement:** Added an asynchronous 5-attempt retry loop on connection to guarantee banned players are cleanly disconnected even on slow map loads.
* **Universal Target Resolution:** Commands accept `#userid`, exact name, partial name, legacy SteamID (`STEAM_0`/`STEAM_1`), and 64-bit SteamID64.
* **In-Chat Color Say (`csay`):** Replaced HUD center printing with rich, highlighted in-chat messages supporting 14 native CS2 colors (e.g. `!csay red <msg>`, `!csay green <msg>`).
* **Full In-Chat Admin Suite:** Added anonymous broadcast (`ssay`), named broadcast (`asay`), and dual-channel private messaging (`psay`).
* **Zero Hardcoded Strings:** 100% localization parity (79 keys) across 17 supported languages.

---

## Installation
1. Install [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master) and [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (v1.0.364 or higher).
2. Download the latest MiniAdmin [Release](https://github.com/lovasatt/cs2-MiniAdmin/releases/).
3. Extract the archive into your server's `game/csgo/addons/counterstrikesharp/plugins/` directory.
4. Configure your database credentials in `database.json`.

---

## Commands

### Chat Commands (Prefixes)
* All commands prefixed with `css_` can also be triggered directly from in-game chat using `!` or `/` (e.g. `!admin`, `!slay`, `!ban`, `!mute`).
* Chat commands (`csay`, `ssay`, `asay`, `psay`) can also be typed into chat by administrators without any prefix.

### Administrator Commands
| Command | Description | Admin Flag |
| :--- | :--- | :---: |
| `css_admin`, `!admin` | Opens the administrative menu | Any Admin |
| `css_ban <target> <duration> [reason]` | Bans a player (online or offline) | `d` (Ban) |
| `css_unban <steamid or name> [reason]` | Unbans a player | `e` (Unban) |
| `css_mute <target> <duration> [reason]` | Mutes player's microphone | `b` (Generic) |
| `css_unmute <target> [reason]` | Unmutes player's microphone | `e` (Unban) |
| `css_gag <target> <duration> [reason]` | Blocks player's text chat | `b` (Generic) |
| `css_ungag <target> [reason]` | Unblocks player's text chat | `e` (Unban) |
| `css_silence <target> <duration> [reason]` | Silences both voice and chat | `b` (Generic) |
| `css_unsilence <target> [reason]` | Lifts both voice and chat silences | `e` (Unban) |
| `css_kick <target> [reason]` | Kicks a player from the server | `c` (Kick) |
| `css_slay <target>` | Kills a player instantly | `f` (Slay) |
| `css_slap <target> [damage]` | Slaps a player with velocity and optional damage | `f` (Slay) |
| `css_team <target> <ct/t/spec/none> [-k]` | Transfers a player (`-k` kills them upon transfer) | `c` (Kick) |
| `css_map <mapname>` | Changes map (`changelevel` for standard, workshop IDs supported) | `g` (Changemap) |
| `css_god [target]` | Toggles player immortality | `n` (Cheats) |
| `css_noclip [target]` | Toggles noclip mode for a player | `n` (Cheats) |
| `css_cvar <cvar> <value>` | Changes a ConVar value (sensitive cvars are blocked) | `h` (Cvar) |
| `css_rcon <command>` | Executes a command on the server console | `m` (Rcon) |
| `css_say <message>` | Broadcasts a highlighted admin message to all players | `j` (Chat) |
| `css_csay [color] <message>` | Broadcasts a colored in-chat message (14 colors supported) | `j` (Chat) |
| `css_ssay <message>` | Broadcasts an anonymous admin announcement (`[ADMIN]: <msg>`) | `j` (Chat) |
| `css_asay <message>` | Broadcasts an admin message with sender name (`[ADMIN - Name]`) | `j` (Chat) |
| `css_psay <target> <message>` | Sends a private message to a player | `j` (Chat) |
| `css_who [all/admins/players/name]` | Lists online players, administrative status, and filters | `-` (All) |

> **Target identifiers:** `<target>` can be a `#userid` (e.g. `#12`), exact/partial name (e.g. `"John Doe"`), SteamID (`STEAM_0:0:...` / `STEAM_1:0:...`), or SteamID64 (`76561198...`).  
> **Duration format:** `<duration>` accepts `s` (seconds), `m` (minutes), `h` (hours), `d` (days), `w` (weeks), `mo` (months), `y` (years), or `0`/`perm` for permanent punishments (e.g. `30m`, `2h`, `1d`, `0`).

---

### Admin Chat (`@`)
* **Team Chat (`say_team @<message>`):** Sends a private message to all online administrators. Regular players can use this to report rule breakers.
* **Public Chat (`say @<message>`):** If typed by an administrator with the `j` (Chat) flag, broadcasts the announcement to all players on the server.

### Chief Administrator Console Commands (Server Console Only)
* **Add Administrator:**
  ```text
  css_addadmin "<username>" "<steamid>" "<immunity>" "<flags>" "<duration>"
  ```
  *Example:* `css_addadmin "AdminName" "STEAM_0:1:123456" "10" "bcdefghjmn" "0"` (0 = permanent)
* **Update Administrator:**
  ```text
  css_updateadmin "<steamid>" [username] [immunity] [flags] [duration]
  ```
  *(Use `-` or `-s` to skip a parameter without changing it)*  
  *Example updating flags only:* `css_updateadmin "STEAM_0:1:123456" -s -s bcdfj -s`
* **Delete Administrator:**
  ```text
  css_deleteadmin "<steamid>"
  ```

---

## Admin Flags & Immunity

| Flag | Name | Function |
| :---: | :--- | :--- |
| `a` | Reservation | Reserved slot access *(reserved for future use)* |
| `b` | Generic | Voice and chat mute (`css_mute`, `css_gag`, `css_silence`) |
| `c` | Kick | Kick players and transfer teams (`css_kick`, `css_team`) |
| `d` | Ban | Ban players (`css_ban`) |
| `e` | Unban | Unban and unmute players (`css_unban`, `css_unmute`, `css_ungag`, `css_unsilence`) |
| `f` | Slay | Slay and slap players (`css_slay`, `css_slap`) |
| `g` | Changemap | Change server maps (`css_map`) |
| `h` | Cvar | Modify non-restricted server ConVars (`css_cvar`) |
| `i` | Config | Execute configuration files *(reserved for future use)* |
| `j` | Chat | Administrative chat broadcasts (`css_say`, `css_csay`, `css_ssay`, `css_asay`, `css_psay`, `@`) |
| `k` | Vote | Start votes *(reserved for future use)* |
| `l` | Password | Set server password *(reserved for future use)* |
| `m` | Rcon | Execute RCON commands (`css_rcon`) |
| `n` | Cheats | Toggle cheat commands (`css_god`, `css_noclip`) |
| `o` | Custom / VIP | Custom privilege level *(reserved for future use)* |
| `z` | Root | Full access to all commands and menus, overrides immunity |

> **Immunity Rules:** Subordinate administrators cannot punish or transfer administrators with equal or higher immunity (`>`). Staff with the `z` (Root) flag possess absolute authority over all non-root users.

---

## Configuration Files

The plugin automatically generates its configuration files next to the plugin DLL upon startup:

* **`database.json`** - MySQL/MariaDB database connection credentials:
  ```json
  {
    "BanFromConsoleName": "Console",
    "Connection": {
      "Host": "localhost",
      "Database": "admin",
      "User": "admin_user",
      "Password": "your_password",
      "Port": 3306
    }
  }
  ```
* **`maps.txt`** - Defines the list of available maps for the map change menu:
  ```text
  de_dust2
  de_mirage
  de_inferno
  ws:awp_lego_2
  ws:3070290869
  ```
  *(Workshop maps can be prefixed with `ws:` using either collection names or numeric Workshop IDs).*
* **`admin/ban_reasons.txt`** - Preset ban reasons displayed in the `!admin` menu.
* **`admin/mute_reasons.txt`** - Preset mute reasons displayed in the `!admin` menu.
* **`admin/times.txt`** - Preset punishment durations formatted as `Label:Seconds` (e.g. `1 hour:3600`, `permanently:0`).
* **`admin/immunity.txt`** - Preset immunity values available when adding administrators via the menu.

---

## Supported Languages
Full localization parity (79 keys) across all 17 catalogs:
* English (`en`)
* Hungarian (`hu`)
* German (`de`)
* Russian (`ru`)
* Czech (`cs`)
* Polish (`pl`)
* Spanish (`es`)
* French (`fr`)
* Portuguese (`pt`)
* Brazilian Portuguese (`pt-BR`)
* Romanian (`ro`)
* Turkish (`tr`)
* Ukrainian (`uk`)
* Danish (`da`)
* Swedish (`sv`)
* Korean (`ko`)
* Simplified Chinese (`zh-Hans`)

---

## Credits & License
* **Original author:** [partiusfabaa](https://github.com/partiusfabaa/cs2-MiniAdmin)
* **Modernization & Maintenance:** [lovasatt](https://github.com/lovasatt)