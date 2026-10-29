namespace BaseAdmin;

public class User
{
    public int id { get; set; }
    public required string admin_username { get; set; }
    public required string admin_steamid { get; set; }
    public required string username { get; set; }
    public ulong steamid64 { get; set; }
    public required string steamid { get; set; }
    public required string reason { get; set; }
    public string? unban_reason { get; set; }
    public string? admin_unlocked_username { get; set; }
    public string? admin_unlocked_steamid { get; set; }
    public long start_ban_time { get; set; }
    public long end_ban_time { get; set; }
    public bool ban_active { get; set; }
}