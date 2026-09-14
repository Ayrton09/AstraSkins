using CounterStrikeSharp.API.Core;
using System.Text.Json.Serialization;

namespace AstraSkins.Models;

public sealed class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 1;
    public string DatabaseMode { get; set; } = "mysql";
    public SqliteConfig Sqlite { get; set; } = new();
    public MySqlConfig MySql { get; set; } = new();
    public MenuConfig Menu { get; set; } = new();
    public CustomizationConfig Customization { get; set; } = new();
    public StickersConfig Stickers { get; set; } = new();
    public KeychainsConfig Keychains { get; set; } = new();
    // Off by default so a possessed bot keeps whatever cosmetics its pawn has
    // (for example from a bot randomizer plugin). Opt in to see your own instead.
    public bool ApplyPlayerCosmeticsOnBotTakeover { get; set; } = false;
    // Every weapon or knife with a selected skin starts with a StatTrak counter
    // at 0. Players can still turn it off per item with !stattrak reset.
    public bool EnableStatTrakByDefault { get; set; } = false;
    public bool EnableMusicKitMvpCounter { get; set; } = false;
    public DefinitionPathConfig Definitions { get; set; } = new();
    public bool EnableAdminReloadCommand { get; set; } = true;
    public string AdminReloadPermission { get; set; } = "@css/config";
    public bool EnableAdminDebugCommand { get; set; } = true;
    public string AdminDebugPermission { get; set; } = "@css/config";
    public bool EnableAdminResetCommand { get; set; } = true;
    public string AdminResetPermission { get; set; } = "@css/config";
}

public sealed class SqliteConfig
{
    public string Path { get; set; } = "data/astra_skins.sqlite";
}

public sealed class MySqlConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "astra_skins";
    public string Username { get; set; } = "astra_skins";
    public string Password { get; set; } = "change-me";
    public string SslMode { get; set; } = "required";
}

public sealed class MenuConfig
{
    public int ItemsPerPage { get; set; } = 6;
    public int TimeoutSeconds { get; set; } = 25;
    public int CooldownMilliseconds { get; set; } = 180;
    public int SelectionCooldownMilliseconds { get; set; } = 900;
    public bool AllowWhileDead { get; set; } = true;
}

public sealed class CustomizationConfig
{
    public bool Enabled { get; set; } = true;
    public string Permission { get; set; } = string.Empty;
    // 20 matches the name tag length the real game allows.
    public int MaxNameTagLength { get; set; } = 20;
    // Case-insensitive substrings a name tag may not contain. Empty by
    // default; each server adds its own. Avoid words that occur inside
    // normal words, since the match is by substring.
    public List<string> BlockedNameTagWords { get; set; } = new();
}

// Stickers and charms each have their own switch and flag, separate from the
// Customization commands, so a server can hand them out to different ranks.
public sealed class StickersConfig
{
    public bool Enabled { get; set; } = true;
    public string Permission { get; set; } = string.Empty;
}

public sealed class KeychainsConfig
{
    public bool Enabled { get; set; } = true;
    public string Permission { get; set; } = string.Empty;
}

public sealed class DefinitionPathConfig
{
    public string Weapons { get; set; } = "data/weapons.json";
    public string Knives { get; set; } = "data/knives.json";
    public string Gloves { get; set; } = "data/gloves.json";
    public string Agents { get; set; } = "data/agents.json";
    public string MusicKits { get; set; } = "data/music_kits.json";
    public string Stickers { get; set; } = "data/stickers.json";
    public string Keychains { get; set; } = "data/keychains.json";
    public string? Categories { get; set; } = "data/categories.json";
}
