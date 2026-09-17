using AstraSkins.Models;

namespace AstraSkins;

public enum MenuView
{
    Closed,
    Main,
    Categories,
    Weapons,
    WeaponSkins,
    KnifeTypes,
    KnifeSkins,
    GloveTypes,
    GloveSkins,
    AgentTeams,
    Agents,
    MusicKits,
    Search,
    AttachmentWeapons,
    StickerSlots,
    StickerGroups,
    StickerCapsules,
    Stickers,
    StickerSearch,
    KeychainGroups,
    Keychains,
    KeychainSearch
}

// What a weapon pick leads to: its skins, its sticker slots, or its charm.
public enum MenuPurpose
{
    Skins,
    Stickers,
    Keychain
}

public sealed class PlayerMenuState
{
    public bool PreferZh { get; set; }
    public int Slot { get; init; }
    public MenuView View { get; set; }
    public Stack<MenuSnapshot> BackStack { get; } = new();
    public int Cursor { get; set; }
    public DateTime LastInteractionUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastInputUtc { get; set; } = DateTime.MinValue;
    public DateTime LastSelectionUtc { get; set; } = DateTime.MinValue;
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public string? LastSelectionKey { get; set; }
    public string PreviousButtonsSnapshot { get; set; } = string.Empty;
    public IReadOnlyList<MenuOption>? CachedOptions { get; set; }
    public DateTime CachedOptionsAtUtc { get; set; }
    public string? SearchQuery { get; set; }
    public string? CategoryId { get; set; }
    public string? AgentTeam { get; set; }
    public WeaponDefinition? Weapon { get; set; }
    public KnifeDefinition? Knife { get; set; }
    public GloveDefinition? Glove { get; set; }
    public MenuPurpose Purpose { get; set; }
    public int StickerSlot { get; set; }
    public StickerGroup? StickerGroup { get; set; }
    public StickerCapsule? StickerCapsule { get; set; }
    public KeychainGroup? KeychainGroup { get; set; }
    // A sticker picked from a search result, waiting for the slot choice.
    public string? PendingStickerId { get; set; }
    public string? PendingStickerName { get; set; }
    public bool IsOpen => View != MenuView.Closed;
}

public sealed record MenuSnapshot(
    MenuView View,
    int Cursor,
    string? CategoryId,
    string? AgentTeam,
    WeaponDefinition? Weapon,
    KnifeDefinition? Knife,
    GloveDefinition? Glove,
    MenuPurpose Purpose = MenuPurpose.Skins,
    int StickerSlot = 0,
    StickerGroup? StickerGroup = null,
    StickerCapsule? StickerCapsule = null,
    KeychainGroup? KeychainGroup = null);

// SelectionKey tells two rows with the same label apart for the repeat
// throttle (two stickers can share a name); it defaults to the label.
public sealed record MenuOption(string Label, Action Action, bool IsSelected = false, bool ThrottleSelection = false, string? LabelColor = null, string? SelectionKey = null);
