using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

[MinimumApiVersion(369)]
public sealed class AstraSkinsPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
    private const int MaintenanceCommandCooldownMilliseconds = 2000;

    private PluginConfig? _config;
    private ISkinStorage? _storage;
    private SkinManager? _skinManager;
    private MenuManager? _menuManager;
    private readonly Dictionary<int, ulong> _steamIdsBySlot = new();
    // Humans dropped by the level shutdown (player_disconnect with reason
    // SHUTDOWN fires for everyone before OnMapEnd) and, once the map has
    // ended, the ones expected back on the next map: the only clients that
    // can carry a looping track across the load.
    private readonly HashSet<ulong> _shutdownSteamIds = new();
    private readonly HashSet<ulong> _carriedOverSteamIds = new();
    private readonly Dictionary<int, DateTime> _maintenanceCooldownsBySlot = new();
    // Agent models handed to the engine at map load; a catalog reloaded
    // mid-map can list models the engine does not have.
    private readonly HashSet<string> _precachedModels = new(StringComparer.OrdinalIgnoreCase);
    // Set on round_mvp, consumed if that same player then dies to planted_c4.
    // DeathCam on the MVP's own client replaces the anthem; the 1s kit
    // reconcile does not replay the cue.
    private PendingMvpCue? _pendingMvpCue;
    private bool _ready;
    private bool _giveNamedItemHooked;

    private readonly record struct PendingMvpCue(
        int UserId,
        ulong SteamId,
        long MusicKitId,
        long MusicKitMvps,
        int Reason,
        long Value);

    public PluginConfig Config { get; set; } = new();

    public override string ModuleName => "Astra Skins";
    public override string ModuleVersion => "1.0.15";
    public override string ModuleAuthor => "Ayrton09";
    public override string ModuleDescription => string.Empty;

    public override void Load(bool hotReload)
    {
        try
        {
            InitializeRuntime();
        }
        catch (Exception ex)
        {
            _ready = false;
            Logger.LogCritical(ex, "Astra Skins failed to load. No fallback mode will be used.");
        }

        AddCommand("css_ws", "Open Astra Skins menu.", CommandOpenWeapons);
        AddCommand("css_knife", "Open knife skins menu.", CommandOpenKnives);
        AddCommand("css_gloves", "Open glove skins menu.", CommandOpenGloves);
        AddCommand("css_agents", "Open agents menu.", CommandOpenAgents);
        AddCommand("css_stickers", "Open the sticker menu for the held weapon.", CommandOpenStickers);
        AddCommand("css_charms", "Open the charm menu for the held weapon.", CommandOpenCharms);
        AddCommand("css_keychains", "Open the charm menu for the held weapon.", CommandOpenCharms);
        AddCommand("css_keychain", "Open the charm menu for the held weapon.", CommandOpenCharms);
        AddCommand("css_wsrefresh", "Reapply selected skins.", CommandRefresh);
        AddCommand("css_wsreset", "Reset all selected skins.", CommandReset);
        AddCommand("css_wsreload", "Reload Astra Skins definitions.", CommandReload);
        AddCommand("css_wsresetplayer", "Reset a player's selections by SteamID64.", CommandResetPlayer);
        AddCommand("css_wsdebug", "Show Astra Skins diagnostic information.", CommandDebug);
        AddCommand("css_seed", "Set a custom paint seed for the held weapon.", CommandSeed);
        AddCommand("css_wear", "Set a custom wear value for the held weapon.", CommandWear);
        AddCommand("css_nametag", "Set a custom name tag for the held weapon.", CommandNameTag);
        AddCommand("css_stattrak", "Toggle StatTrak on the held weapon.", CommandStatTrak);

        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        RegisterListener<Listeners.OnServerPrecacheResources>(OnServerPrecacheResources);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnPre, HookMode.Pre);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnPost, HookMode.Post);
        RegisterEventHandler<EventBotTakeover>(OnBotTakeover, HookMode.Post);
        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        RegisterEventHandler<EventTeamIntroStart>(OnTeamIntroStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEndPre, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventRoundMvp>(OnRoundMvp, HookMode.Pre);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        HookGiveNamedItem();

        if (hotReload && _ready)
        {
            foreach (var player in Utilities.GetPlayers().Where(IsLiveHuman))
            {
                // OnClientAuthorized does not fire again for players who were
                // already in, so the slot table has to be rebuilt here.
                _steamIdsBySlot[player.Slot] = player.SteamID;
                _skinManager?.ApplyToPlayer(player);
            }
        }
    }

    public override void Unload(bool hotReload)
    {
        UnhookGiveNamedItem();
        _skinManager?.Dispose();
        _storage?.Dispose();
        _storage = null;
        _skinManager = null;
        _menuManager = null;
        _steamIdsBySlot.Clear();
        _pendingMvpCue = null;
        _ready = false;
    }

    public void OnConfigParsed(PluginConfig config)
    {
        var configManager = new ConfigManager(Logger);
        configManager.Validate(config);
        Config = config;
        _config = config;
    }

    private void InitializeRuntime()
    {
        var config = Config;

        var catalog = new DefinitionLoader(Logger).Load(ModuleDirectory, config);
        var storage = CreateStorage(config);
        storage.Initialize();

        _config = config;
        _storage = storage;
        _skinManager = new SkinManager(storage, catalog, Logger,
            (delay, action) => AddTimer(delay, () => action(), TimerFlags.STOP_ON_MAPCHANGE),
            config.EnableStatTrakByDefault,
            // No precache pass seen yet (plugin loaded mid-map): nothing to check against.
            model => _precachedModels.Count == 0 || _precachedModels.Contains(model),
            config.Stickers,
            config.Keychains);
        _menuManager = new MenuManager(_skinManager, config, Localizer, Logger);
        _ready = true;

        Logger.LogInformation(
            "Astra Skins loaded: {Weapons} weapons, {KnifeSkins} knife skins, {GloveSkins} glove skins, {Agents} agents, {MusicKits} music kits, {Stickers} stickers, {Keychains} keychains, DB={DatabaseMode}, StatTrakByDefault={StatTrakByDefault}, MusicKitMvpCounter={MusicKitMvpCounter}",
            catalog.Weapons.Count,
            catalog.KnifeSkinsById.Count,
            catalog.GloveSkinsById.Count,
            catalog.Agents.Count,
            catalog.MusicKits.Count,
            catalog.Stickers.Count,
            catalog.Keychains.Count,
            config.DatabaseMode,
            config.EnableStatTrakByDefault,
            config.EnableMusicKitMvpCounter);
    }

    private ISkinStorage CreateStorage(PluginConfig config)
    {
        return config.DatabaseMode switch
        {
            "sqlite" => new SqliteSkinStorage(Resolve(ModuleDirectory, config.Sqlite.Path), Logger, config.EnableMusicKitMvpCounter),
            "mysql" => new MySqlSkinStorage(config.MySql, Logger, config.EnableMusicKitMvpCounter),
            _ => throw new InvalidOperationException("Invalid DatabaseMode after validation.")
        };
    }

    private void CommandOpenWeapons(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMenuAllowed(player!, command))
        {
            return;
        }

        var query = command.ArgCount > 1 ? command.ArgString.Trim() : string.Empty;
        if (query.Length == 0)
        {
            _menuManager!.OpenMain(player!);
            return;
        }

        _menuManager!.OpenSearch(player!, query);
        if (!_menuManager.HasSearchResults(player!))
        {
            _menuManager.Close(player!, clearScreen: true);
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.search_no_results", query)}");
        }
    }

    private void CommandStatTrak(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireCustomization(player!, command))
        {
            return;
        }

        var target = _skinManager!.GetHeldCustomizationTarget(player!);
        if (target is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_weapon")}");
            return;
        }

        var current = _skinManager.GetStatTrak(player!, target);
        int? next;
        if (command.ArgCount <= 1)
        {
            next = current is null ? 0 : null;
        }
        else
        {
            var token = command.GetArg(1).Trim();
            if (IsResetToken(token))
            {
                next = null;
            }
            else if (token.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                next = current ?? 0;
            }
            else if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                     parsed is >= 0 and <= 999999)
            {
                next = parsed;
            }
            else
            {
                command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.stattrak_usage")}");
                return;
            }
        }

        if (!RequireMaintenanceCooldown(player!, command))
        {
            return;
        }

        if (!_skinManager.SetStatTrak(player!, target, next))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_skin")}");
            return;
        }

        command.ReplyToCommand(next is null
            ? $"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.stattrak_off")}"
            : $"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.stattrak_on", next.Value)}");
    }

    // !stickers opens the held gun's slots (or the weapon picker when nothing
    // usable is in hand); !stickers <text> searches the catalog for that gun.
    private void CommandOpenStickers(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMenuAllowed(player!, command) || !RequireStickers(player!, command))
        {
            return;
        }

        var target = _skinManager!.GetHeldAttachmentTarget(player!);
        var query = command.ArgCount > 1 ? command.ArgString.Trim() : string.Empty;
        if (query.Length == 0)
        {
            _menuManager!.OpenStickers(player!, target);
            return;
        }

        if (target is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.attach_no_weapon")}");
            return;
        }

        _menuManager!.OpenStickerSearch(player!, target, query);
        if (!_menuManager.HasSearchResults(player!))
        {
            _menuManager.Close(player!, clearScreen: true);
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.search_no_results", query)}");
        }
    }

    private void CommandOpenCharms(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMenuAllowed(player!, command) || !RequireKeychains(player!, command))
        {
            return;
        }

        var target = _skinManager!.GetHeldAttachmentTarget(player!);
        var query = command.ArgCount > 1 ? command.ArgString.Trim() : string.Empty;
        if (query.Length == 0)
        {
            _menuManager!.OpenKeychains(player!, target);
            return;
        }

        if (target is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.attach_no_weapon")}");
            return;
        }

        _menuManager!.OpenKeychainSearch(player!, target, query);
        if (!_menuManager.HasSearchResults(player!))
        {
            _menuManager.Close(player!, clearScreen: true);
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.search_no_results", query)}");
        }
    }

    private bool RequireStickers(CCSPlayerController player, CommandInfo command)
    {
        if (!_skinManager!.StickersAvailable)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.stickers_disabled")}");
            return false;
        }

        if (!_skinManager.CanUseStickers(player))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.stickers_no_permission")}");
            return false;
        }

        return true;
    }

    private bool RequireKeychains(CCSPlayerController player, CommandInfo command)
    {
        if (!_skinManager!.KeychainsAvailable)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.charms_disabled")}");
            return false;
        }

        if (!_skinManager.CanUseKeychains(player))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.charms_no_permission")}");
            return false;
        }

        return true;
    }

    private void CommandOpenKnives(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMenuAllowed(player!, command))
        {
            return;
        }

        _menuManager!.OpenKnives(player!);
    }

    private void CommandOpenGloves(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMenuAllowed(player!, command))
        {
            return;
        }

        _menuManager!.OpenGloves(player!);
    }

    private void CommandOpenAgents(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMenuAllowed(player!, command))
        {
            return;
        }

        _menuManager!.OpenAgents(player!);
    }

    private void CommandRefresh(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMaintenanceCooldown(player!, command))
        {
            return;
        }

        _skinManager!.ApplyToPlayer(player!, logFailures: true);
        command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.refresh_done")}");
    }

    private void CommandReset(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireMaintenanceCooldown(player!, command))
        {
            return;
        }

        _menuManager!.Close(player!, clearScreen: true);
        var category = command.ArgCount > 1 ? command.GetArg(1).Trim().ToLowerInvariant() : "all";
        if (category is "all" or "*")
        {
            _skinManager!.Reset(player!);
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.reset_all")}");
            return;
        }

        if (!_skinManager!.ResetCategory(player!, category))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.reset_usage")}");
            return;
        }

        var messageKey = category switch
        {
            "weapon" or "weapons" or "guns" => "astra.reset_weapons",
            "knife" or "knives" => "astra.reset_knife",
            "glove" or "gloves" => "astra.reset_gloves",
            "agent" or "agents" => "astra.reset_agents",
            "music" or "musickit" or "musickits" => "astra.reset_music",
            "sticker" or "stickers" => "astra.reset_stickers",
            "keychain" or "keychains" or "charm" or "charms" => "astra.reset_charms",
            _ => "astra.reset_all"
        };
        command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, messageKey)}");
    }

    // Admin reset by SteamID64: works whether the target is connected or not,
    // so an offensive name tag can be cleared without waiting for the player.
    private void CommandResetPlayer(CCSPlayerController? player, CommandInfo command)
    {
        if (_config is null || _skinManager is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.not_initialized")}");
            return;
        }

        if (!_config.EnableAdminResetCommand)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.adminreset_disabled")}");
            return;
        }

        if (player is not null && !AdminManager.PlayerHasPermissions(player, _config.AdminResetPermission))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.adminreset_no_permission")}");
            return;
        }

        if (command.ArgCount < 2 || !ulong.TryParse(command.GetArg(1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var steamId) || steamId == 0)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.adminreset_usage")}");
            return;
        }

        var category = command.ArgCount > 2 ? command.GetArg(2).Trim().ToLowerInvariant() : "all";
        var target = Utilities.GetPlayers().FirstOrDefault(p => IsLiveHuman(p) && p.SteamID == steamId);
        bool done;
        if (target is null)
        {
            done = _skinManager.ResetStored(steamId, category);
        }
        else if (category is "all" or "*")
        {
            _menuManager?.Close(target, clearScreen: true);
            _skinManager.Reset(target);
            done = true;
        }
        else
        {
            _menuManager?.Close(target, clearScreen: true);
            done = _skinManager.ResetCategory(target, category);
        }

        if (!done)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.adminreset_usage")}");
            return;
        }

        command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.adminreset_done", category, steamId, target is null ? "offline" : "online")}");
        Logger.LogInformation("Astra Skins admin reset: {Category} for {SteamId} ({State}) by {Admin}", category, steamId, target is null ? "offline" : "online", player?.SteamID.ToString() ?? "console");
    }

    private bool ContainsBlockedWord(string nameTag)
    {
        var words = _config?.Customization.BlockedNameTagWords;
        if (words is null || words.Count == 0)
        {
            return false;
        }

        foreach (var word in words)
        {
            if (!string.IsNullOrWhiteSpace(word) && nameTag.Contains(word.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void CommandReload(CCSPlayerController? player, CommandInfo command)
    {
        if (_config is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.not_initialized")}");
            return;
        }

        if (!_ready || _skinManager is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.not_ready")}");
            return;
        }

        if (!_config.EnableAdminReloadCommand)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.reload_disabled")}");
            return;
        }

        if (player is not null && !AdminManager.PlayerHasPermissions(player, _config.AdminReloadPermission))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.reload_no_permission")}");
            return;
        }

        try
        {
            var catalog = new DefinitionLoader(Logger).Load(ModuleDirectory, _config);
            _skinManager?.ReplaceCatalog(catalog);
            _menuManager?.InvalidateAll();
            foreach (var livePlayer in Utilities.GetPlayers().Where(IsLiveHuman))
            {
                _skinManager?.ApplyToPlayer(livePlayer);
            }

            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.reload_done")}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Astra Skins definition reload failed.");
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.reload_failed")}");
        }
    }

    private void CommandDebug(CCSPlayerController? player, CommandInfo command)
    {
        if (_config is null || _skinManager is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.not_initialized")}");
            return;
        }

        if (!_config.EnableAdminDebugCommand)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.debug_disabled")}");
            return;
        }

        if (player is not null && !AdminManager.PlayerHasPermissions(player, _config.AdminDebugPermission))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.debug_no_permission")}");
            return;
        }

        var catalog = _skinManager.Catalog;
        var weaponSkinCount = catalog.Weapons.Sum(w => w.Skins.Count);
        var knifeSkinCount = catalog.Knives.Sum(k => k.Skins.Count);
        var gloveSkinCount = catalog.Gloves.Sum(g => g.Skins.Count);
        var agentVoiceCount = catalog.Agents.Count(a => !string.IsNullOrWhiteSpace(a.VoicePrefix));
        command.ReplyToCommand($"{FormatPrefix()} Debug: ready={_ready}, db={_config.DatabaseMode}, inputCooldown={_config.Menu.CooldownMilliseconds}ms, selectionCooldown={_config.Menu.SelectionCooldownMilliseconds}ms");
        command.ReplyToCommand($"{FormatPrefix()} Data: weapons={catalog.Weapons.Count}/{weaponSkinCount}, knives={catalog.Knives.Count}/{knifeSkinCount}, gloves={catalog.Gloves.Count}/{gloveSkinCount}, agents={catalog.Agents.Count} voices={agentVoiceCount}, musicKits={catalog.MusicKits.Count}, stickers={catalog.Stickers.Count}, keychains={catalog.Keychains.Count}");

        if (player is null || !IsLiveHuman(player))
        {
            return;
        }

        var profile = _skinManager.GetProfile(player);
        var agentT = profile.AgentIdsByTeam.TryGetValue("t", out var tAgent) ? tAgent : "none";
        var agentCt = profile.AgentIdsByTeam.TryGetValue("ct", out var ctAgent) ? ctAgent : "none";
        command.ReplyToCommand($"{FormatPrefix()} Player: steam={player.SteamID}, team={player.Team}, ownedWeapons={_skinManager.GetOwnedWeaponDefinitions(player).Count}");
        var musicMvps = _skinManager.TryGetSelectedMusicKitId(player, out var selectedKitId) &&
                        profile.MusicKitMvpCounts.TryGetValue(selectedKitId, out var mvps)
            ? mvps
            : 0;
        command.ReplyToCommand($"{FormatPrefix()} Selections: weapons={profile.WeaponSkins.Count}, knifeType={profile.KnifeId ?? "none"}, knifeSkin={profile.KnifeSkinId ?? "none"}, glove={profile.GloveSkinId ?? "none"}, agentT={agentT}, agentCT={agentCt}, musicKit={profile.MusicKitId ?? "none"} mvps={musicMvps}, stickers={profile.Stickers.Sum(s => s.Value.Count)}, keychains={profile.Keychains.Count}");
    }

    private void CommandSeed(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireCustomization(player!, command))
        {
            return;
        }

        var (target, valueToken) = ResolveCustomizationArgs(player!, command);
        if (valueToken is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.seed_usage")}");
            return;
        }

        if (target is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_weapon")}");
            return;
        }

        int? seed;
        if (IsResetToken(valueToken))
        {
            seed = null;
        }
        else if (int.TryParse(valueToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0 and <= 1000)
        {
            seed = parsed;
        }
        else
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.seed_usage")}");
            return;
        }

        if (!RequireMaintenanceCooldown(player!, command))
        {
            return;
        }

        if (!_skinManager!.SetSeed(player!, target, seed))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_skin")}");
            return;
        }

        command.ReplyToCommand($"{FormatPrefix()} {(seed is null
            ? Localizer.ForPlayer(player, "astra.seed_reset")
            : Localizer.ForPlayer(player, "astra.seed_set", seed.Value))}");
    }

    private void CommandWear(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireCustomization(player!, command))
        {
            return;
        }

        var (target, valueToken) = ResolveCustomizationArgs(player!, command);
        if (valueToken is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.wear_usage")}");
            return;
        }

        if (target is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_weapon")}");
            return;
        }

        float? wear;
        if (IsResetToken(valueToken))
        {
            wear = null;
        }
        else if (float.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0f and <= 1f)
        {
            // A wear of exactly 0 renders as the default finish on some skins.
            wear = Math.Max(parsed, 0.000001f);
        }
        else
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.wear_usage")}");
            return;
        }

        if (!RequireMaintenanceCooldown(player!, command))
        {
            return;
        }

        if (!_skinManager!.SetWear(player!, target, wear))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_skin")}");
            return;
        }

        command.ReplyToCommand($"{FormatPrefix()} {(wear is null
            ? Localizer.ForPlayer(player, "astra.wear_reset")
            : Localizer.ForPlayer(player, "astra.wear_set", wear.Value.ToString("0.######", CultureInfo.InvariantCulture)))}");
    }

    private void CommandNameTag(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command) || !RequireCustomization(player!, command))
        {
            return;
        }

        var text = command.ArgString.Trim();
        if (text.Length == 0)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.nametag_usage")}");
            return;
        }

        var target = _skinManager!.GetHeldCustomizationTarget(player!);
        if (target is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_weapon")}");
            return;
        }

        string? nameTag;
        if (IsResetToken(text))
        {
            nameTag = null;
        }
        else
        {
            nameTag = SanitizeNameTag(text);
            if (nameTag.Length == 0)
            {
                command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.nametag_usage")}");
                return;
            }

            if (ContainsBlockedWord(nameTag))
            {
                command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.nametag_blocked")}");
                return;
            }
        }

        if (!RequireMaintenanceCooldown(player!, command))
        {
            return;
        }

        if (!_skinManager.SetNameTag(player!, target, nameTag))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_skin")}");
            return;
        }

        command.ReplyToCommand($"{FormatPrefix()} {(nameTag is null
            ? Localizer.ForPlayer(player, "astra.nametag_reset")
            : Localizer.ForPlayer(player, "astra.nametag_set", nameTag))}");
    }

    private bool RequireCustomization(CCSPlayerController player, CommandInfo command)
    {
        if (!_config!.Customization.Enabled)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_disabled")}");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_config.Customization.Permission) &&
            !AdminManager.PlayerHasPermissions(player, _config.Customization.Permission))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.custom_no_permission")}");
            return false;
        }

        return true;
    }

    private (string? Target, string? Value) ResolveCustomizationArgs(CCSPlayerController player, CommandInfo command)
    {
        if (command.ArgCount >= 3 && command.GetArg(1).Trim().ToLowerInvariant() is "gloves" or "glove")
        {
            return (SkinManager.GloveTarget, command.GetArg(2).Trim());
        }

        if (command.ArgCount >= 2)
        {
            return (_skinManager!.GetHeldCustomizationTarget(player), command.GetArg(1).Trim());
        }

        return (null, null);
    }

    private static bool IsResetToken(string token)
    {
        return token.Trim().ToLowerInvariant() is "reset" or "default" or "none" or "off";
    }

    private string SanitizeNameTag(string text)
    {
        var cleaned = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        var maxLength = _config!.Customization.MaxNameTagLength;
        return cleaned.Length > maxLength ? cleaned[..maxLength].Trim() : cleaned;
    }

    private HookResult OnPlayerSpawnPre(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (_ready && IsLiveHuman(player))
        {
            _skinManager?.ApplyAgentToPlayer(player!, logFailures: false, loadIfMissing: false);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawnPost(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (_ready && IsLiveHuman(player))
        {
            AddTimer(0.25f, () =>
            {
                if (IsLiveHuman(player))
                {
                    _skinManager?.ApplyToPlayerWhenProfileReady(player!);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        return HookResult.Continue;
    }

    // Taking over a bot hands the player the bot's pawn, which has none of
    // their cosmetics on it. The possessed body needs its own settle time, so
    // apply shortly after and once more for slower handovers. Off by default
    // so bot cosmetics plugins keep their loadout on the possessed body.
    private HookResult OnBotTakeover(EventBotTakeover @event, GameEventInfo info)
    {
        var player = @event.Userid;
        // Button input keeps coming from the player's own pawn during the
        // possession, so an open menu would sit there unresponsive.
        if (player is { IsValid: true })
        {
            _menuManager?.Close(player);
        }

        if (!_ready || _config is null || !_config.ApplyPlayerCosmeticsOnBotTakeover || !IsLiveHuman(player))
        {
            return HookResult.Continue;
        }

        var slot = player!.Slot;
        var userId = player.UserId;
        foreach (var delay in new[] { 0.25f, 1.0f })
        {
            AddTimer(delay, () =>
            {
                var current = Utilities.GetPlayerFromSlot(slot);
                if (_ready && IsLiveHuman(current) && current!.UserId == userId)
                {
                    _skinManager?.ApplyToPossessedPawnWhenProfileReady(current);
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
    {
        _pendingMvpCue = null;
        // Valve fills team_intro Xuid on this event; write after the assignment lands.
        ScheduleTeamPreviewApply();
        return HookResult.Continue;
    }

    private HookResult OnTeamIntroStart(EventTeamIntroStart @event, GameEventInfo info)
    {
        ScheduleTeamPreviewApply();
        return HookResult.Continue;
    }

    // Team-intro / team-select Xuid is assigned a frame or two after the event.
    private void ScheduleTeamPreviewApply(CCSPlayerController? player = null)
    {
        if (!_ready || _skinManager is null)
        {
            return;
        }

        var slot = player?.Slot;
        var userId = player?.UserId;
        void apply()
        {
            if (!_ready || _skinManager is null)
            {
                return;
            }

            if (slot is null)
            {
                _skinManager.ApplyTeamPreviewCosmetics();
                return;
            }

            var current = Utilities.GetPlayerFromSlot(slot.Value);
            if (IsLiveHuman(current) && current!.UserId == userId)
            {
                _skinManager.ApplyTeamPreviewCosmetics(current);
            }
        }

        Server.NextFrame(apply);
        AddTimer(0.25f, apply, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private HookResult OnRoundFreezeEndPre(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_ready || _skinManager is null)
        {
            return HookResult.Continue;
        }

        foreach (var player in Utilities.GetPlayers().Where(IsLiveHuman))
        {
            // loadIfMissing keeps a slow first profile read from leaving the
            // player on the default agent for the whole round.
            _skinManager.ApplyAgentToPlayer(player, logFailures: false, loadIfMissing: true);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null && player.IsValid)
        {
            _menuManager?.Close(player);

            // Solo T plant: round_mvp fires, then planted_c4 kills the MVP.
            // Other clients still hear the anthem; the dead MVP's client plays
            // DeathCam instead. Replay the cue to that one client.
            if (_ready &&
                IsLiveHuman(player) &&
                string.Equals(@event.Weapon, "planted_c4", StringComparison.OrdinalIgnoreCase) &&
                _pendingMvpCue is { } pendingMvp &&
                pendingMvp.UserId == player.UserId &&
                pendingMvp.SteamId == player.SteamID)
            {
                var slot = player.Slot;
                var userId = player.UserId;
                AddTimer(0.1f, () =>
                {
                    var current = Utilities.GetPlayerFromSlot(slot);
                    if (_ready && current is { IsValid: true } &&
                        !current.IsBot && current.SteamID == pendingMvp.SteamId && current.UserId == userId)
                    {
                        ReplayMvpCueToClient(current, pendingMvp);
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
        }

        var attacker = @event.Attacker;
        if (_ready && IsLiveHuman(attacker) && (player is null || attacker!.Slot != player.Slot))
        {
            var weapon = attacker!.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
            if (weapon is not null && weapon.IsValid)
            {
                _skinManager?.IncrementStatTrak(attacker, weapon, @event.Weapon);
            }
        }

        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (_ready && _skinManager is not null && player is { IsValid: true } && IsLiveHuman(player))
        {
            var hasSelectedMusicKit = _skinManager.TryGetSelectedMusicKitId(player, out var musicKitId);
            if (hasSelectedMusicKit)
            {
                // Keep the scoreboard MVP music consistent with the selected kit.
                @event.Musickitid = musicKitId;
                @event.Nomusic = 0;
            }
            else
            {
                musicKitId = checked((int)@event.Musickitid);
            }

            if (_config?.EnableMusicKitMvpCounter == true)
            {
                @event.Musickitmvps = _skinManager.RecordMusicKitMvp(player, musicKitId);
            }

            if (@event.Nomusic == 0 && @event.Musickitid > 0)
            {
                _pendingMvpCue = new PendingMvpCue(
                    player.UserId ?? -1,
                    player.SteamID,
                    @event.Musickitid,
                    @event.Musickitmvps,
                    @event.Reason,
                    @event.Value);
            }
        }

        return HookResult.Continue;
    }

    private void ReplayMvpCueToClient(CCSPlayerController player, PendingMvpCue pendingMvp)
    {
        EventRoundMvp? replay = null;
        try
        {
            replay = new EventRoundMvp(force: true)
            {
                Userid = player,
                Musickitid = pendingMvp.MusicKitId,
                Musickitmvps = pendingMvp.MusicKitMvps,
                Nomusic = 0,
                Reason = pendingMvp.Reason,
                Value = pendingMvp.Value
            };

            replay.FireEventToClient(player);
            Logger.LogInformation(
                "Astra Skins replayed round_mvp cue to C4-killed MVP: steam={SteamId}, slot={Slot}, kit={MusicKitId}, mvpCount={MusicKitMvps}",
                player.SteamID,
                player.Slot,
                pendingMvp.MusicKitId,
                pendingMvp.MusicKitMvps);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Astra Skins failed to replay round_mvp cue to C4-killed MVP {SteamId}.", player.SteamID);
        }
        finally
        {
            if (replay is not null)
            {
                try
                {
                    replay.Free();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Astra Skins failed to free replayed round_mvp event.");
                }
            }
        }
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null)
        {
            _menuManager?.CloseSlot(player.Slot);
            _maintenanceCooldownsBySlot.Remove(player.Slot);
            _skinManager?.Forget(player);
            if (!_steamIdsBySlot.Remove(player.Slot, out var steamId))
            {
                steamId = player.IsBot ? 0 : player.SteamID;
            }

            if (steamId != 0)
            {
                _skinManager?.Forget(steamId);
                if (@event.Reason == (int)CounterStrikeSharp.API.ValveConstants.Protobuf.NetworkDisconnectionReason.NETWORK_DISCONNECT_SHUTDOWN)
                {
                    _shutdownSteamIds.Add(steamId);
                }
            }
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null && player.IsValid)
        {
            _menuManager?.Close(player);
            // Safe point for the leftover stop: the menu just closed and took
            // the selection music with it, so only the old loop can be playing.
            if (_ready && !player.IsBot && @event.Oldteam == 0 && @event.Team is 2 or 3 && _carriedOverSteamIds.Remove(player.SteamID))
            {
                EmitToClient(player, "StopSoundEvents.StopAllMusic", "first team join");
            }
            if (_ready && IsLiveHuman(player))
            {
                _skinManager?.ApplyMusicKitWhenProfileReady(player, logFailures: false);
                ScheduleTeamPreviewApply(player);
            }
        }

        return HookResult.Continue;
    }

    private void OnClientAuthorized(int playerSlot, SteamID steamId)
    {
        if (!_ready || _skinManager is null)
        {
            return;
        }

        if (steamId.SteamId64 != 0)
        {
            _steamIdsBySlot[playerSlot] = steamId.SteamId64;
        }

        var player = Utilities.GetPlayerFromSlot(playerSlot);
        if (IsLiveHuman(player))
        {
            _skinManager.ApplyMusicKitWhenProfileReady(player!, logFailures: false);
            return;
        }

    }

    private void EmitToClient(CCSPlayerController player, string soundEvent, string context)
    {
        try
        {
            NativeAPI.EmitSoundFilter(1UL << player.Slot, (uint)player.Index, soundEvent, 1f, 0f);
            Logger.LogDebug("Astra Skins emitted {Sound} to slot {Slot} ({Context}).", soundEvent, player.Slot, context);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Astra Skins could not emit {Sound} to slot {Slot}.", soundEvent, player.Slot);
        }
    }

    private void OnMapStart(string mapName)
    {
        _skinManager?.ResetTeamPreviewTracking();
    }

    // The client keeps music alive across map loads, and the round start
    // track loops until the player first moves. A player who never moved
    // carries that loop into the next map, where nothing replaces it until
    // the first team intro (round start music is deferred during warmup).
    // Remember who was in when the map ended: on the next map, the first
    // time each of them joins a team the menu has just closed and stopped
    // the selection music, so only the old loop can still be playing and a
    // plain StopAllMusic is safe. Earlier moments are not: before the client
    // is in the game it ignores sound messages, and at put-in-server the
    // stop lands after the team select music started. A fresh connection has
    // nothing to stop and is left alone.
    private void OnMapEnd()
    {
        _carriedOverSteamIds.Clear();
        _carriedOverSteamIds.UnionWith(_shutdownSteamIds);
        _shutdownSteamIds.Clear();
        Logger.LogDebug("Astra Skins map end: {Count} humans carried over.", _carriedOverSteamIds.Count);
    }

    private void OnTick()
    {
        if (!_ready)
        {
            return;
        }

        _menuManager?.OnTick();
        _skinManager?.ReconcileTeamPreview();
    }

    // Music kits are reconciled right before the server builds each tick's
    // snapshot, after all game logic and client packet processing of the
    // tick. Valve rewrites the kit on its own schedule (once the client is
    // fully connected, on spawns, on bot joins, at the round boundary), and
    // a client that sees the stock kit for even one snapshot starts the
    // default team select track and never stops it when the kit switches
    // back. Doing the compare here means the client only ever sees the
    // selected kit. Reads a few fields per player, writes only on a mismatch.
    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        EnsureMusicKitForLivePlayers();
    }

    private void EnsureMusicKitForLivePlayers()
    {
        if (!_ready || _skinManager is null)
        {
            return;
        }

        try
        {
            foreach (var player in Utilities.GetPlayers().Where(IsLiveHuman))
            {
                _skinManager.EnsureMusicKitWhenProfileReady(player);
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Astra Skins failed to reconcile music kits.");
        }
    }

    private void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if (_ready && player.IsValid)
        {
            _menuManager?.OnButtonsChanged(player, pressed);
        }
    }

    // Agent models must be in each map's resource manifest before SetModel;
    // applying a model the map never precached can crash the server.
    private void OnServerPrecacheResources(ResourceManifest manifest)
    {
        var catalog = _skinManager?.Catalog;
        if (catalog is null)
        {
            return;
        }

        _precachedModels.Clear();
        foreach (var agent in catalog.Agents)
        {
            if (!string.IsNullOrWhiteSpace(agent.Model))
            {
                manifest.AddResource(agent.Model);
                _precachedModels.Add(agent.Model);
            }
        }
    }

    private HookResult OnGiveNamedItemPost(DynamicHook hook)
    {
        try
        {
            if (!_ready || _skinManager is null)
            {
                return HookResult.Continue;
            }

            var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
            var weapon = hook.GetReturn<CBasePlayerWeapon>();
            if (weapon is null || !weapon.IsValid || !weapon.DesignerName.Contains("weapon", StringComparison.OrdinalIgnoreCase))
            {
                return HookResult.Continue;
            }

            var player = GetPlayerFromItemServices(itemServices);
            if (!IsLiveHuman(player))
            {
                return HookResult.Continue;
            }

            // A possessed bot pawn reports the human as its controller, so
            // weapons bought or picked up during possession land here too.
            if (_config is not null && !_config.ApplyPlayerCosmeticsOnBotTakeover && IsPossessedPawn(player!, itemServices.Pawn.Value))
            {
                return HookResult.Continue;
            }

            Server.NextFrame(() =>
            {
                if (IsLiveHuman(player) && weapon.IsValid)
                {
                    _skinManager?.ApplyToWeapon(player!, weapon);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Astra Skins failed to apply cosmetics from GiveNamedItem hook.");
        }

        return HookResult.Continue;
    }

    private void HookGiveNamedItem()
    {
        if (_giveNamedItemHooked)
        {
            return;
        }

        try
        {
            VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
            _giveNamedItemHooked = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Astra Skins could not hook GiveNamedItem. Pickup/spawn/manual refresh application will still run.");
        }
    }

    private void UnhookGiveNamedItem()
    {
        if (!_giveNamedItemHooked)
        {
            return;
        }

        try
        {
            VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Astra Skins failed to unhook GiveNamedItem.");
        }
        finally
        {
            _giveNamedItemHooked = false;
        }
    }

    private bool RequireReadyPlayer(CCSPlayerController? player, CommandInfo command)
    {
        if (!_ready || _config is null || _skinManager is null || _menuManager is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.not_ready")}");
            return false;
        }

        if (player is null || !IsLiveHuman(player))
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.players_only")}");
            return false;
        }

        return true;
    }

    private bool RequireMenuAllowed(CCSPlayerController player, CommandInfo command)
    {
        // While possessing a bot, button input still comes from the player's
        // own (dead) pawn, so the menu would open but never respond to keys.
        // Spectating also detaches Pawn from PlayerPawn (observer pawn), but
        // there the player is dead; possession is the only alive-and-detached
        // state.
        var possessed = player.Pawn.Value;
        var ownPawn = player.PlayerPawn.Value;
        if (player.PawnIsAlive && possessed is not null && ownPawn is not null && possessed.Handle != ownPawn.Handle)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.menu_while_bot")}");
            return false;
        }

        if (_config!.Menu.AllowWhileDead || player.PawnIsAlive)
        {
            return true;
        }

        command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.menu_disabled_dead")}");
        return false;
    }

    // Refresh/reset trigger weapon kill+give cycles and database writes; the
    // cooldown keeps command spam from queueing that work repeatedly.
    private bool RequireMaintenanceCooldown(CCSPlayerController player, CommandInfo command)
    {
        var now = DateTime.UtcNow;
        if (_maintenanceCooldownsBySlot.TryGetValue(player.Slot, out var last) &&
            (now - last).TotalMilliseconds < MaintenanceCommandCooldownMilliseconds)
        {
            command.ReplyToCommand($"{FormatPrefix()} {Localizer.ForPlayer(player, "astra.command_cooldown")}");
            return false;
        }

        _maintenanceCooldownsBySlot[player.Slot] = now;
        return true;
    }

    private static bool IsLiveHuman(CCSPlayerController? player)
    {
        return player is not null && player.IsValid && !player.IsBot && player.SteamID != 0;
    }

    // The controller's PlayerPawn stays on the player's own body while they
    // possess a bot, so any other pawn attributed to them is a possessed one.
    private static bool IsPossessedPawn(CCSPlayerController player, CBasePlayerPawn? pawn)
    {
        var ownPawn = player.PlayerPawn.Value;
        return pawn is not null && pawn.IsValid && ownPawn is not null && ownPawn.IsValid && ownPawn.Handle != pawn.Handle;
    }

    private static CCSPlayerController? GetPlayerFromItemServices(CCSPlayer_ItemServices itemServices)
    {
        var pawn = itemServices.Pawn.Value;
        if (pawn is null || !pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value is null)
        {
            return null;
        }

        var player = new CCSPlayerController(pawn.Controller.Value.Handle);
        return IsLiveHuman(player) ? player : null;
    }

    private static string Resolve(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    internal static string FormatPrefix()
    {
        return $" {ChatColors.DarkRed}[Astra Skins]{ChatColors.Default}";
    }
}
