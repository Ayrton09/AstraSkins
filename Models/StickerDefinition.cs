namespace AstraSkins.Models;

public sealed class StickerDefinition
{
    public string Id { get; set; } = string.Empty;
    // The sticker kit id the game renders ("sticker slot N id").
    public int StickerId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameZh { get; set; }
    // Menu grouping: the tournament, capsule or collection the sticker came
    // from, and for tournament stickers the capsule inside that event.
    public string? Group { get; set; }
    public string? GroupZh { get; set; }
    public string? Capsule { get; set; }
    public string? CapsuleZh { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Permission { get; set; }
    public string? Rarity { get; set; }
}
