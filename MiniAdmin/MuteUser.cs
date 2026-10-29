namespace BaseAdmin;

public class MuteUser
{
    public int id { get; set; }
    public int mute_type { get; set; }
    public required string admin_username { get; set; }
    public required string admin_steamid { get; set; }
    public required string username { get; set; }
    public ulong steamid64 { get; set; }
    public required string steamid { get; set; }
    public required string reason { get; set; }
    public string? unmute_reason { get; set; }
    public string? admin_unlocked_username { get; set; }
    public string? admin_unlocked_steamid { get; set; }
    public long start_mute_time { get; set; }
    public long end_mute_time { get; set; }
    public bool mute_active { get; set; }
}

public class MuteUserLocal
{
    public int MuteType { get; set; }
    public required string SteamId { get; set; }
    public long EndMuteTime { get; set; }
    public bool MuteActive { get; set; }
}