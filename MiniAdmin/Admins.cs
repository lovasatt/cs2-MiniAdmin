namespace BaseAdmin;

public class Admins
{
    public required string username { get; set; }
    public required string steamid { get; set; }
    public long start_time { get; set; }
    public long end_time { get; set; }
    public int immunity { get; set; }
    public required string flags { get; set; }
}