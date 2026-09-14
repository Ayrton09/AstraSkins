namespace AstraSkins.Models;

// Charms in the game's own words; the schema still calls them keychains.
public sealed class KeychainDefinition
{
    public string Id { get; set; } = string.Empty;
    // The charm definition id the game renders ("keychain slot 0 id").
    public int KeychainId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameZh { get; set; }
    public string? Group { get; set; }
    public string? GroupZh { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Permission { get; set; }
    public string? Rarity { get; set; }
}
