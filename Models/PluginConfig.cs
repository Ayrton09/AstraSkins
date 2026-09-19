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
    public ModuleConfig Weapons { get; set; } = new();
    public ModuleConfig Knives { get; set; } = new();
    public ModuleConfig Gloves { get; set; } = new();
    public ModuleConfig Agents { get; set; } = new();
    public ModuleConfig MusicKits { get; set; } = new();
    public ModuleConfig Stickers { get; set; } = new();
    public ModuleConfig Keychains { get; set; } = new();
    public CommandsConfig Commands { get; set; } = new();
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
    public int ItemsPerPage { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 25;
    public int CooldownMilliseconds { get; set; } = 180;
    public int SelectionCooldownMilliseconds { get; set; } = 900;
    public bool AllowWhileDead { get; set; } = true;
    // The key that goes back one view: "Shift", "A" (strafe left, next to
    // W/S) or "Both", the default. A live player is held in place while the
    // menu is open, so A does nothing else in the meantime; from a root view
    // only Shift and R close the menu.
    public string BackKey { get; set; } = "Both";
    // Off by default: the end of a round is when most players open the menu,
    // and closing it at round start makes them open it again in freeze time.
    // On, for servers where the overlay over the round start bothers more.
    public bool CloseOnRoundStart { get; set; } = false;
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
// One switch per feature: off hides it from the menu and the search, its
// commands answer that it is unavailable, and saved selections stop applying.
// The flag does the same for players who do not hold it.
public sealed class ModuleConfig
{
    public bool Enabled { get; set; } = true;
    public string Permission { get; set; } = string.Empty;
}

// The seven switches gathered for SkinManager, which does not read the
// plugin config itself.
public sealed class ModuleSwitches
{
    public ModuleConfig Weapons { get; init; } = new();
    public ModuleConfig Knives { get; init; } = new();
    public ModuleConfig Gloves { get; init; } = new();
    public ModuleConfig Agents { get; init; } = new();
    public ModuleConfig MusicKits { get; init; } = new();
    public ModuleConfig Stickers { get; init; } = new();
    public ModuleConfig Keychains { get; init; } = new();

    public static ModuleSwitches From(PluginConfig config)
    {
        return new ModuleSwitches
        {
            Weapons = config.Weapons,
            Knives = config.Knives,
            Gloves = config.Gloves,
            Agents = config.Agents,
            MusicKits = config.MusicKits,
            Stickers = config.Stickers,
            Keychains = config.Keychains
        };
    }
}

// Chat command names, any number per command. Names are registered as
// CounterStrikeSharp commands ("css_ws" answers to "!ws" and "/ws" in chat);
// the validator adds the "css_" prefix when it is missing. An empty list
// leaves that command unregistered.
public sealed class CommandsConfig
{
    public List<string> Menu { get; set; } = new() { "css_ws" };
    public List<string> Knife { get; set; } = new() { "css_knife" };
    public List<string> Gloves { get; set; } = new() { "css_gloves" };
    public List<string> Agents { get; set; } = new() { "css_agents" };
    public List<string> Stickers { get; set; } = new() { "css_stickers" };
    public List<string> Charms { get; set; } = new() { "css_charms", "css_keychains", "css_keychain" };
    public List<string> Refresh { get; set; } = new() { "css_wsrefresh" };
    public List<string> Reset { get; set; } = new() { "css_wsreset" };
    public List<string> Seed { get; set; } = new() { "css_seed" };
    public List<string> Wear { get; set; } = new() { "css_wear" };
    public List<string> NameTag { get; set; } = new() { "css_nametag" };
    public List<string> StatTrak { get; set; } = new() { "css_stattrak" };
    public List<string> Reload { get; set; } = new() { "css_wsreload" };
    public List<string> Debug { get; set; } = new() { "css_wsdebug" };
    public List<string> ResetPlayer { get; set; } = new() { "css_wsresetplayer" };

    public IEnumerable<(string Name, List<string> Aliases)> Entries()
    {
        yield return (nameof(Menu), Menu);
        yield return (nameof(Knife), Knife);
        yield return (nameof(Gloves), Gloves);
        yield return (nameof(Agents), Agents);
        yield return (nameof(Stickers), Stickers);
        yield return (nameof(Charms), Charms);
        yield return (nameof(Refresh), Refresh);
        yield return (nameof(Reset), Reset);
        yield return (nameof(Seed), Seed);
        yield return (nameof(Wear), Wear);
        yield return (nameof(NameTag), NameTag);
        yield return (nameof(StatTrak), StatTrak);
        yield return (nameof(Reload), Reload);
        yield return (nameof(Debug), Debug);
        yield return (nameof(ResetPlayer), ResetPlayer);
    }
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
