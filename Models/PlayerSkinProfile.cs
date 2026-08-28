namespace AstraSkins.Models;

public sealed class PlayerSkinProfile
{
    public ulong SteamId64 { get; set; }
    public Dictionary<string, string> WeaponSkins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? KnifeId { get; set; }
    public string? KnifeSkinId { get; set; }
    public string? GloveSkinId { get; set; }
    public string? MusicKitId { get; set; }
    public Dictionary<int, int> MusicKitMvpCounts { get; set; } = new();
    public Dictionary<string, string> AgentIdsByTeam { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Per-player overrides on top of the selected skin, keyed by weapon entity
    // name, "knife", or "glove". A null field means "use the skin's value".
    public Dictionary<string, WeaponCustomization> Customizations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WeaponCustomization
{
    public int? Seed { get; set; }
    public float? Wear { get; set; }
    public string? NameTag { get; set; }
    public int? StatTrak { get; set; }
    public bool IsEmpty => Seed is null && Wear is null && NameTag is null && StatTrak is null;
}
