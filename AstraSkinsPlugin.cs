using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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

public sealed class AstraSkinsPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
    private PluginConfig? _config;
    private ISkinStorage? _storage;
    private SkinManager? _skinManager;
    private MenuManager? _menuManager;
    private readonly Dictionary<int, ulong> _steamIdsBySlot = new();
    private bool _ready;
    private bool _giveNamedItemHooked;

    public PluginConfig Config { get; set; } = new();

    public override string ModuleName => "Astra Skins";
    public override string ModuleVersion => "1.0.1";
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
        AddCommand("css_wsrefresh", "Reapply selected skins.", CommandRefresh);
        AddCommand("css_wsreset", "Reset all selected skins.", CommandReset);
        AddCommand("css_wsreload", "Reload Astra Skins definitions.", CommandReload);
        AddCommand("css_wsdebug", "Show Astra Skins diagnostic information.", CommandDebug);

        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnPre, HookMode.Pre);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawnPost, HookMode.Post);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEndPre, HookMode.Pre);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        HookGiveNamedItem();

        if (hotReload && _ready)
        {
            foreach (var player in Utilities.GetPlayers().Where(IsLiveHuman))
            {
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
        _skinManager = new SkinManager(storage, catalog, Logger);
        _menuManager = new MenuManager(_skinManager, config, Logger);
        _ready = true;

        Logger.LogInformation(
            "Astra Skins loaded: {Weapons} weapons, {KnifeSkins} knife skins, {GloveSkins} glove skins, {Agents} agents, DB={DatabaseMode}",
            catalog.Weapons.Count,
            catalog.KnifeSkinsById.Count,
            catalog.GloveSkinsById.Count,
            catalog.Agents.Count,
            config.DatabaseMode);
    }

    private ISkinStorage CreateStorage(PluginConfig config)
    {
        return config.DatabaseMode switch
        {
            "sqlite" => new SqliteSkinStorage(Resolve(ModuleDirectory, config.Sqlite.Path), Logger),
            "mysql" => new MySqlSkinStorage(config.MySql, Logger),
            _ => throw new InvalidOperationException("Invalid DatabaseMode after validation.")
        };
    }

    private void CommandOpenWeapons(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command))
        {
            return;
        }

        _menuManager!.OpenMain(player!);
    }

    private void CommandOpenKnives(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command))
        {
            return;
        }

        _menuManager!.OpenKnives(player!);
    }

    private void CommandOpenGloves(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command))
        {
            return;
        }

        _menuManager!.OpenGloves(player!);
    }

    private void CommandOpenAgents(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command))
        {
            return;
        }

        _menuManager!.OpenAgents(player!);
    }

    private void CommandRefresh(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command))
        {
            return;
        }

        _skinManager!.ApplyToPlayer(player!, logFailures: true);
        command.ReplyToCommand($"{FormatPrefix()} Selections reapplied.");
    }

    private void CommandReset(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireReadyPlayer(player, command))
        {
            return;
        }

        _menuManager!.Close(player!, clearScreen: true);
        var category = command.ArgCount > 1 ? command.GetArg(1).Trim().ToLowerInvariant() : "all";
        if (category is "all" or "*")
        {
            _skinManager!.Reset(player!);
            command.ReplyToCommand($"{FormatPrefix()} All selections reset. Default agent returns on next spawn.");
            return;
        }

        if (!_skinManager!.ResetCategory(player!, category))
        {
            command.ReplyToCommand($"{FormatPrefix()} Usage: !wsreset [all|weapons|knife|gloves|agents]");
            return;
        }

        var label = category switch
        {
            "weapon" or "weapons" or "guns" => "Weapon selections",
            "knife" or "knives" => "Knife selection",
            "glove" or "gloves" => "Glove selection",
            "agent" or "agents" => "Agent selections",
            _ => "Selections"
        };
        var suffix = label.Equals("Agent selections", StringComparison.Ordinal)
            ? " Default agent returns on next spawn."
            : string.Empty;
        command.ReplyToCommand($"{FormatPrefix()} {label} reset.{suffix}");
    }

    private void CommandReload(CCSPlayerController? player, CommandInfo command)
    {
        if (_config is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} Plugin is not initialized.");
            return;
        }

        if (!_config.EnableAdminReloadCommand)
        {
            command.ReplyToCommand($"{FormatPrefix()} Reload command is disabled.");
            return;
        }

        if (player is not null && !AdminManager.PlayerHasPermissions(player, _config.AdminReloadPermission))
        {
            command.ReplyToCommand($"{FormatPrefix()} You do not have permission to reload definitions.");
            return;
        }

        try
        {
            var catalog = new DefinitionLoader(Logger).Load(ModuleDirectory, _config);
            _skinManager?.ReplaceCatalog(catalog);
            foreach (var livePlayer in Utilities.GetPlayers().Where(IsLiveHuman))
            {
                _skinManager?.ApplyToPlayer(livePlayer);
            }

            command.ReplyToCommand($"{FormatPrefix()} Definitions reloaded.");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Astra Skins definition reload failed.");
            command.ReplyToCommand($"{FormatPrefix()} Reload failed. Check server logs.");
        }
    }

    private void CommandDebug(CCSPlayerController? player, CommandInfo command)
    {
        if (_config is null || _skinManager is null)
        {
            command.ReplyToCommand($"{FormatPrefix()} Plugin is not initialized.");
            return;
        }

        if (!_config.EnableAdminDebugCommand)
        {
            command.ReplyToCommand($"{FormatPrefix()} Debug command is disabled.");
            return;
        }

        if (player is not null && !AdminManager.PlayerHasPermissions(player, _config.AdminDebugPermission))
        {
            command.ReplyToCommand($"{FormatPrefix()} You do not have permission to use debug.");
            return;
        }

        var catalog = _skinManager.Catalog;
        var weaponSkinCount = catalog.Weapons.Sum(w => w.Skins.Count);
        var knifeSkinCount = catalog.Knives.Sum(k => k.Skins.Count);
        var gloveSkinCount = catalog.Gloves.Sum(g => g.Skins.Count);
        var agentVoiceCount = catalog.Agents.Count(a => !string.IsNullOrWhiteSpace(a.VoicePrefix));
        command.ReplyToCommand($"{FormatPrefix()} Debug: ready={_ready}, db={_config.DatabaseMode}, inputCooldown={_config.Menu.CooldownMilliseconds}ms, selectionCooldown={_config.Menu.SelectionCooldownMilliseconds}ms");
        command.ReplyToCommand($"{FormatPrefix()} Data: weapons={catalog.Weapons.Count}/{weaponSkinCount}, knives={catalog.Knives.Count}/{knifeSkinCount}, gloves={catalog.Gloves.Count}/{gloveSkinCount}, agents={catalog.Agents.Count} voices={agentVoiceCount}");

        if (player is null || !IsLiveHuman(player))
        {
            return;
        }

        var profile = _skinManager.GetProfile(player);
        var agentT = profile.AgentIdsByTeam.TryGetValue("t", out var tAgent) ? tAgent : "none";
        var agentCt = profile.AgentIdsByTeam.TryGetValue("ct", out var ctAgent) ? ctAgent : "none";
        command.ReplyToCommand($"{FormatPrefix()} Player: steam={player.SteamID}, team={player.Team}, ownedWeapons={_skinManager.GetOwnedWeaponDefinitions(player).Count}");
        command.ReplyToCommand($"{FormatPrefix()} Selections: weapons={profile.WeaponSkins.Count}, knifeType={profile.KnifeId ?? "none"}, knifeSkin={profile.KnifeSkinId ?? "none"}, glove={profile.GloveSkinId ?? "none"}, agentT={agentT}, agentCT={agentCt}");
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

    private HookResult OnRoundFreezeEndPre(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_ready || _skinManager is null)
        {
            return HookResult.Continue;
        }

        foreach (var player in Utilities.GetPlayers().Where(IsLiveHuman))
        {
            _skinManager.ApplyAgentToPlayer(player, logFailures: false, loadIfMissing: false);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null && player.IsValid)
        {
            _menuManager?.Close(player);
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null)
        {
            _menuManager?.CloseSlot(player.Slot);
            _skinManager?.Forget(player);
            if (_steamIdsBySlot.Remove(player.Slot, out var steamId))
            {
                _skinManager?.Forget(steamId);
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
            if (_ready && IsLiveHuman(player))
            {
                _skinManager?.PreloadProfile(player);
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
            _skinManager.PreloadProfile(player!);
            return;
        }

        if (steamId.SteamId64 != 0)
        {
            _skinManager.PreloadProfile(steamId.SteamId64);
        }
    }

    private void OnTick()
    {
        if (!_ready)
        {
            return;
        }

        _menuManager?.OnTick();
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
            command.ReplyToCommand($"{FormatPrefix()} Plugin is not ready. Check server logs.");
            return false;
        }

        if (player is null || !IsLiveHuman(player))
        {
            command.ReplyToCommand($"{FormatPrefix()} This command can only be used by a connected human player.");
            return false;
        }

        return true;
    }

    private static bool IsLiveHuman(CCSPlayerController? player)
    {
        return player is not null && player.IsValid && !player.IsBot && player.SteamID != 0;
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
