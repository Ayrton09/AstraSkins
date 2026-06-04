namespace AstraSkins.Models;

public sealed class PlayerSkinProfile
{
    public ulong SteamId64 { get; set; }
    public Dictionary<string, string> WeaponSkins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? KnifeId { get; set; }
    public string? KnifeSkinId { get; set; }
    public string? GloveSkinId { get; set; }
    public Dictionary<string, string> AgentIdsByTeam { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
