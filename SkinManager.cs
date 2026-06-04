using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class SkinManager : IDisposable
{
    private const ulong MinimumCustomItemId = 65578;

    private readonly ISkinStorage _storage;
    private readonly ILogger _logger;
    private readonly EconAttributeApplicator _econAttributes;
    private readonly Dictionary<ulong, PlayerSkinProfile> _profiles = new();
    private readonly HashSet<ulong> _loadingProfiles = new();
    private readonly HashSet<ulong> _activeSteamIds = new();
    private ulong _nextItemId = MinimumCustomItemId;
    private bool _disposed;

    private static readonly Dictionary<int, string> WeaponEntityByDefinitionIndex = new()
    {
        [1] = "weapon_deagle",
        [2] = "weapon_elite",
        [3] = "weapon_fiveseven",
        [4] = "weapon_glock",
        [7] = "weapon_ak47",
        [8] = "weapon_aug",
        [9] = "weapon_awp",
        [10] = "weapon_famas",
        [11] = "weapon_g3sg1",
        [13] = "weapon_galilar",
        [14] = "weapon_m249",
        [16] = "weapon_m4a1",
        [17] = "weapon_mac10",
        [19] = "weapon_p90",
        [23] = "weapon_mp5sd",
        [24] = "weapon_ump45",
        [25] = "weapon_xm1014",
        [26] = "weapon_bizon",
        [27] = "weapon_mag7",
        [28] = "weapon_negev",
        [29] = "weapon_sawedoff",
        [30] = "weapon_tec9",
        [31] = "weapon_taser",
        [32] = "weapon_hkp2000",
        [33] = "weapon_mp7",
        [34] = "weapon_mp9",
        [35] = "weapon_nova",
        [36] = "weapon_p250",
        [38] = "weapon_scar20",
        [39] = "weapon_sg556",
        [40] = "weapon_ssg08",
        [60] = "weapon_m4a1_silencer",
        [61] = "weapon_usp_silencer",
        [63] = "weapon_cz75a",
        [64] = "weapon_revolver",
        [500] = "weapon_bayonet",
        [503] = "weapon_knife_css",
        [505] = "weapon_knife_flip",
        [506] = "weapon_knife_gut",
        [507] = "weapon_knife_karambit",
        [508] = "weapon_knife_m9_bayonet",
        [509] = "weapon_knife_tactical",
        [512] = "weapon_knife_falchion",
        [514] = "weapon_knife_survival_bowie",
        [515] = "weapon_knife_butterfly",
        [516] = "weapon_knife_push",
        [517] = "weapon_knife_cord",
        [518] = "weapon_knife_canis",
        [519] = "weapon_knife_ursus",
        [520] = "weapon_knife_gypsy_jackknife",
        [521] = "weapon_knife_outdoor",
        [522] = "weapon_knife_stiletto",
        [523] = "weapon_knife_widowmaker",
        [525] = "weapon_knife_skeleton",
        [526] = "weapon_knife_kukri"
    };

    public DefinitionCatalog Catalog { get; private set; }

    public SkinManager(ISkinStorage storage, DefinitionCatalog catalog, ILogger logger)
    {
        _storage = storage;
        Catalog = catalog;
        _logger = logger;
        _econAttributes = new EconAttributeApplicator(logger);
    }

    public void ReplaceCatalog(DefinitionCatalog catalog)
    {
        Catalog = catalog;
        RemoveInvalidCachedSelections();
    }

    public void Dispose()
    {
        _disposed = true;
        _activeSteamIds.Clear();
        _loadingProfiles.Clear();
        _profiles.Clear();
    }

    public PlayerSkinProfile GetProfile(CCSPlayerController player)
    {
        var steamId = GetSteamId64(player);
        if (!_profiles.TryGetValue(steamId, out var profile))
        {
            profile = _storage.LoadProfile(steamId);
            _profiles[steamId] = profile;
        }

        _activeSteamIds.Add(steamId);
        return profile;
    }

    public void Forget(CCSPlayerController player)
    {
        if (TryGetSteamId64(player, out var steamId))
        {
            _profiles.Remove(steamId);
            _loadingProfiles.Remove(steamId);
            _activeSteamIds.Remove(steamId);
        }
    }

    public void Forget(ulong steamId64)
    {
        _profiles.Remove(steamId64);
        _loadingProfiles.Remove(steamId64);
        _activeSteamIds.Remove(steamId64);
    }

    public void ApplyToPlayerWhenProfileReady(CCSPlayerController player, bool logFailures = false)
    {
        if (_disposed)
        {
            return;
        }

        if (!TryGetSteamId64(player, out var steamId))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply cosmetics: player SteamID64 is invalid.");
            }

            return;
        }

        if (_profiles.ContainsKey(steamId))
        {
            ApplyToPlayer(player, logFailures);
            return;
        }

        _activeSteamIds.Add(steamId);
        LoadProfileInBackground(steamId, applyAfterLoad: true, logFailures);
    }

    public void PreloadProfile(CCSPlayerController player)
    {
        if (_disposed || !TryGetSteamId64(player, out var steamId) || _profiles.ContainsKey(steamId))
        {
            return;
        }

        _activeSteamIds.Add(steamId);
        LoadProfileInBackground(steamId, applyAfterLoad: false, logFailures: false);
    }

    public void PreloadProfile(ulong steamId64)
    {
        if (_disposed || steamId64 == 0 || _profiles.ContainsKey(steamId64))
        {
            return;
        }

        _activeSteamIds.Add(steamId64);
        LoadProfileInBackground(steamId64, applyAfterLoad: false, logFailures: false);
    }

    public bool SetWeaponSkin(CCSPlayerController player, string weaponEntity, string cosmeticId)
    {
        if (!Catalog.WeaponSkinsById.TryGetValue(cosmeticId, out var skin))
        {
            return false;
        }

        var steamId = GetSteamId64(player);
        var profile = GetProfile(player);
        profile.WeaponSkins[weaponEntity] = cosmeticId;
        _storage.SaveWeaponSkin(steamId, weaponEntity, cosmeticId);
        ApplyWeaponSelection(player, weaponEntity, skin, logFailures: true);
        return true;
    }

    public bool SetKnifeSkin(CCSPlayerController player, string cosmeticId)
    {
        if (!Catalog.KnifeSkinsById.TryGetValue(cosmeticId, out var skin))
        {
            return false;
        }

        var steamId = GetSteamId64(player);
        var profile = GetProfile(player);
        profile.KnifeSkinId = cosmeticId;
        _storage.SaveKnifeSkin(steamId, cosmeticId);
        var knife = Catalog.Knives.FirstOrDefault(k => k.ItemDefinitionIndex == skin.ItemDefinitionIndex);
        if (knife is not null)
        {
            profile.KnifeId = knife.Id;
            _storage.SaveKnifeType(steamId, knife.Id);
        }

        ApplyKnifeSelection(player, skin, logFailures: true);
        return true;
    }

    public bool SetKnifeType(CCSPlayerController player, string knifeId)
    {
        var knife = Catalog.Knives.FirstOrDefault(k => k.Id.Equals(knifeId, StringComparison.OrdinalIgnoreCase));
        if (knife is null)
        {
            return false;
        }

        var steamId = GetSteamId64(player);
        var profile = GetProfile(player);
        profile.KnifeId = knife.Id;
        _storage.SaveKnifeType(steamId, knife.Id);
        ApplyKnifeTypeSelection(player, knife, logFailures: true);
        return true;
    }

    public bool SetGloveSkin(CCSPlayerController player, string cosmeticId)
    {
        if (!Catalog.GloveSkinsById.TryGetValue(cosmeticId, out var glove))
        {
            return false;
        }

        var steamId = GetSteamId64(player);
        var profile = GetProfile(player);
        profile.GloveSkinId = cosmeticId;
        _storage.SaveGloveSkin(steamId, cosmeticId);
        ApplyGloveSelection(player, glove, logFailures: true);
        return true;
    }

    public bool SetAgent(CCSPlayerController player, string team, string agentId)
    {
        var normalizedTeam = NormalizeAgentTeam(team);
        if (normalizedTeam is not "t" and not "ct" ||
            !Catalog.AgentsById.TryGetValue(agentId, out var agent) ||
            !agent.Team.Equals(normalizedTeam, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var steamId = GetSteamId64(player);
        var profile = GetProfile(player);
        profile.AgentIdsByTeam[normalizedTeam] = agent.Id;
        _storage.SaveAgent(steamId, normalizedTeam, agent.Id);
        ApplyAgentSelection(player, agent, logFailures: true);
        return true;
    }

    public void Reset(CCSPlayerController player)
    {
        var steamId = GetSteamId64(player);
        _storage.ResetProfile(steamId);
        _profiles.Remove(steamId);
        ClearPlayerCosmetics(player, logFailures: true);
    }

    public bool ResetCategory(CCSPlayerController player, string category)
    {
        var normalized = NormalizeResetCategory(category);
        if (normalized is null)
        {
            return false;
        }

        var steamId = GetSteamId64(player);
        _storage.ResetCategory(steamId, normalized);

        var profile = GetProfile(player);
        switch (normalized)
        {
            case "weapons":
                profile.WeaponSkins.Clear();
                ClearWeaponCosmetics(player, includeKnives: false, logFailures: true);
                break;
            case "knife":
                profile.KnifeId = null;
                profile.KnifeSkinId = null;
                ClearWeaponCosmetics(player, includeKnives: true, onlyKnives: true, logFailures: true);
                break;
            case "gloves":
                profile.GloveSkinId = null;
                if (TryGetPawn(player, out var glovePawn, logFailures: true))
                {
                    ClearGloveCosmetic(player, glovePawn!);
                }
                break;
            case "agents":
                profile.AgentIdsByTeam.Clear();
                break;
        }

        return true;
    }

    public void ApplyToPlayer(CCSPlayerController player, bool logFailures = false)
    {
        if (!IsUsablePlayer(player))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply cosmetics: player is invalid.");
            }

            return;
        }

        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return;
        }

        var profile = GetProfile(player);
        ApplyWeapons(player, pawn!, profile, logFailures);
        ApplyGloves(player, pawn!, profile, logFailures);
        ApplyAgent(player, pawn!, profile, logFailures);
    }

    public void ApplyAgentToPlayer(CCSPlayerController player, bool logFailures = false, bool loadIfMissing = true)
    {
        if (!IsUsablePlayer(player) || !TryGetPawn(player, out var pawn, logFailures))
        {
            return;
        }

        if (!TryGetSteamId64(player, out var steamId))
        {
            return;
        }

        if (!_profiles.TryGetValue(steamId, out var profile))
        {
            if (loadIfMissing)
            {
                _activeSteamIds.Add(steamId);
                LoadProfileInBackground(steamId, applyAfterLoad: false, logFailures);
            }

            return;
        }

        ApplyAgent(player, pawn!, profile, logFailures);
    }

    public bool ApplyToWeapon(CCSPlayerController player, CBasePlayerWeapon weapon, bool logFailures = false)
    {
        if (_disposed)
        {
            return false;
        }

        if (!IsUsablePlayer(player))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply weapon cosmetic: player is invalid.");
            }

            return false;
        }

        if (weapon is null || !weapon.IsValid)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply weapon cosmetic for player {SteamId}: weapon is invalid.", player.SteamID);
            }

            return false;
        }

        if (!TryGetSteamId64(player, out var steamId) || !_profiles.ContainsKey(steamId))
        {
            if (TryGetSteamId64(player, out steamId))
            {
                _activeSteamIds.Add(steamId);
                LoadProfileInBackground(steamId, applyAfterLoad: true, logFailures);
            }

            return false;
        }

        return ApplyMatchingWeapon(player, weapon, GetProfile(player), logFailures);
    }

    public bool CanUse(CCSPlayerController player, CosmeticEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Permission) || AdminManager.PlayerHasPermissions(player, entry.Permission);
    }

    public bool CanUse(CCSPlayerController player, KnifeDefinition entry)
    {
        return string.IsNullOrWhiteSpace(entry.Permission) || AdminManager.PlayerHasPermissions(player, entry.Permission);
    }

    public bool CanUse(CCSPlayerController player, GloveDefinition entry)
    {
        return string.IsNullOrWhiteSpace(entry.Permission) || AdminManager.PlayerHasPermissions(player, entry.Permission);
    }

    public bool CanUse(CCSPlayerController player, AgentDefinition entry)
    {
        return string.IsNullOrWhiteSpace(entry.Permission) || AdminManager.PlayerHasPermissions(player, entry.Permission);
    }

    public IReadOnlyList<WeaponDefinition> GetOwnedWeaponDefinitions(CCSPlayerController player)
    {
        if (!TryGetPawn(player, out var pawn, logFailures: false) || pawn?.WeaponServices is null)
        {
            return Array.Empty<WeaponDefinition>();
        }

        var owned = new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var weaponHandle in pawn.WeaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon is null || !weapon.IsValid)
            {
                continue;
            }

            var entityName = ResolveWeaponEntityName(weapon);
            if (IsKnife(entityName))
            {
                continue;
            }

            if (Catalog.WeaponsByEntity.TryGetValue(entityName, out var definition))
            {
                owned.TryAdd(definition.EntityName, definition);
            }
        }

        return owned.Values
            .OrderBy(w => IsPistol(w.EntityName) ? 1 : 0)
            .ThenBy(w => w.DisplayName)
            .ToList();
    }

    public KnifeDefinition? GetCurrentKnifeDefinition(CCSPlayerController player)
    {
        var profile = GetProfile(player);
        if (profile.KnifeId is not null)
        {
            var selectedKnife = Catalog.Knives.FirstOrDefault(k => k.Id.Equals(profile.KnifeId, StringComparison.OrdinalIgnoreCase));
            if (selectedKnife is not null)
            {
                return selectedKnife;
            }
        }

        if (profile.KnifeSkinId is not null &&
            Catalog.KnifeSkinsById.TryGetValue(profile.KnifeSkinId, out var selectedKnifeSkin) &&
            selectedKnifeSkin.ItemDefinitionIndex.HasValue)
        {
            var selectedKnife = Catalog.Knives.FirstOrDefault(k => k.ItemDefinitionIndex == selectedKnifeSkin.ItemDefinitionIndex.Value);
            if (selectedKnife is not null)
            {
                return selectedKnife;
            }
        }

        if (!TryGetPawn(player, out var pawn, logFailures: false) || pawn?.WeaponServices is null)
        {
            return null;
        }

        foreach (var weaponHandle in pawn.WeaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon is null || !weapon.IsValid)
            {
                continue;
            }

            var entityName = ResolveWeaponEntityName(weapon);
            if (!IsKnife(entityName))
            {
                continue;
            }

            var itemDefinitionIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
            return Catalog.Knives.FirstOrDefault(k =>
                k.ItemDefinitionIndex == itemDefinitionIndex ||
                k.EntityName.Equals(entityName, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private void ApplyWeapons(CCSPlayerController player, CCSPlayerPawn pawn, PlayerSkinProfile profile, bool logFailures)
    {
        var weaponServices = pawn.WeaponServices;
        if (weaponServices is null)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply weapon cosmetics for player {SteamId}: weapon services are unavailable.", player.SteamID);
            }

            return;
        }

        var appliedWeaponEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedKnife = false;

        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon is null || !weapon.IsValid)
            {
                continue;
            }

            var applied = ApplyMatchingWeapon(player, weapon, profile, logFailures: false);
            if (!applied)
            {
                continue;
            }

            var weaponName = ResolveWeaponEntityName(weapon);
            if (IsKnife(weaponName))
            {
                appliedKnife = true;
            }
            else
            {
                appliedWeaponEntities.Add(weaponName);
            }
        }

        if (!logFailures)
        {
            return;
        }

        foreach (var weaponEntity in profile.WeaponSkins.Keys)
        {
            if (!appliedWeaponEntities.Contains(weaponEntity))
            {
                _logger.LogWarning("Astra Skins weapon not found for player {SteamId}: {WeaponEntity}. Selection remains saved.", player.SteamID, weaponEntity);
            }
        }

        if (profile.KnifeSkinId is not null && !appliedKnife)
        {
            _logger.LogWarning("Astra Skins knife weapon not found for player {SteamId}. Selection remains saved.", player.SteamID);
        }
        else if (profile.KnifeId is not null && !appliedKnife)
        {
            _logger.LogWarning("Astra Skins knife weapon not found for player {SteamId}. Knife type selection remains saved.", player.SteamID);
        }
    }

    private bool ApplyMatchingWeapon(CCSPlayerController player, CBasePlayerWeapon weapon, PlayerSkinProfile profile, bool logFailures)
    {
        var weaponName = ResolveWeaponEntityName(weapon);
        if (string.IsNullOrWhiteSpace(weaponName))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply weapon cosmetic for player {SteamId}: weapon name is unavailable.", player.SteamID);
            }

            return false;
        }

        if (IsKnife(weaponName))
        {
            if (profile.KnifeSkinId is not null &&
                Catalog.KnifeSkinsById.TryGetValue(profile.KnifeSkinId, out var knifeSkin) &&
                KnifeSkinMatchesSelectedKnife(profile, knifeSkin))
            {
                return ApplyCosmeticToWeapon(player, weapon, knifeSkin, isKnife: true, logFailures);
            }

            if (profile.KnifeId is not null)
            {
                var knife = Catalog.Knives.FirstOrDefault(k => k.Id.Equals(profile.KnifeId, StringComparison.OrdinalIgnoreCase));
                return knife is not null && ApplyKnifeTypeToWeapon(player, weapon, knife, logFailures);
            }

            return false;
        }

        return profile.WeaponSkins.TryGetValue(weaponName, out var cosmeticId) &&
               Catalog.WeaponSkinsById.TryGetValue(cosmeticId, out var skin) &&
               ApplyCosmeticToWeapon(player, weapon, skin, isKnife: false, logFailures);
    }

    private bool ApplyWeaponSelection(CCSPlayerController player, string weaponEntity, CosmeticEntry skin, bool logFailures)
    {
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return false;
        }

        var weaponServices = pawn!.WeaponServices;
        if (weaponServices is null)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply {CosmeticId}: weapon services are unavailable for player {SteamId}.", skin.Id, player.SteamID);
            }

            return false;
        }

        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon is null || !weapon.IsValid)
            {
                continue;
            }

            var weaponName = ResolveWeaponEntityName(weapon);
            if (!string.IsNullOrWhiteSpace(weaponName) &&
                weaponName.Equals(weaponEntity, StringComparison.OrdinalIgnoreCase))
            {
                return RefreshOwnedWeaponWithSelection(player, weapon, weaponEntity, skin, logFailures);
            }
        }

        if (logFailures)
        {
            _logger.LogWarning("Astra Skins weapon not found for player {SteamId}: {WeaponEntity}. Selection was saved and will apply when owned.", player.SteamID, weaponEntity);
        }

        return false;
    }

    private void LoadProfileInBackground(ulong steamId64, bool applyAfterLoad, bool logFailures)
    {
        if (_disposed)
        {
            return;
        }

        if (_profiles.ContainsKey(steamId64) || !_loadingProfiles.Add(steamId64))
        {
            return;
        }

        Task.Run(() => _storage.LoadProfile(steamId64)).ContinueWith(task =>
        {
            Server.NextFrame(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _loadingProfiles.Remove(steamId64);

                if (task.IsFaulted)
                {
                    if (logFailures)
                    {
                        _logger.LogWarning(task.Exception, "Astra Skins failed to load profile for player {SteamId}.", steamId64);
                    }

                    return;
                }

                if (task.Result is null)
                {
                    if (logFailures)
                    {
                        _logger.LogWarning("Astra Skins storage returned no profile for player {SteamId}.", steamId64);
                    }

                    return;
                }

                if (!_activeSteamIds.Contains(steamId64))
                {
                    return;
                }

                _profiles[steamId64] = task.Result;
                if (!applyAfterLoad)
                {
                    return;
                }

                var player = Utilities.GetPlayers().FirstOrDefault(p => IsUsablePlayer(p) && p.SteamID == steamId64);
                if (player is not null)
                {
                    ApplyToPlayer(player, logFailures);
                }
            });
        });
    }

    private bool ApplyKnifeSelection(CCSPlayerController player, CosmeticEntry skin, bool logFailures)
    {
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return false;
        }

        var weaponServices = pawn!.WeaponServices;
        if (weaponServices is null)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply knife {CosmeticId}: weapon services are unavailable for player {SteamId}.", skin.Id, player.SteamID);
            }

            return false;
        }

        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            var weaponName = weapon is null ? null : ResolveWeaponEntityName(weapon);
            if (weapon is null || !weapon.IsValid || string.IsNullOrWhiteSpace(weaponName) || !IsKnife(weaponName))
            {
                continue;
            }

            var knife = Catalog.Knives.FirstOrDefault(k => k.ItemDefinitionIndex == skin.ItemDefinitionIndex);
            if (knife is null)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins cannot apply knife skin {CosmeticId}: knife item definition index {ItemDefinitionIndex} is not loaded.", skin.Id, skin.ItemDefinitionIndex);
                }

                return false;
            }

            return RefreshOwnedKnifeWithSelection(player, weapon, knife, skin, logFailures);
        }

        if (logFailures)
        {
            _logger.LogWarning("Astra Skins knife weapon not found for player {SteamId}. Selection was saved and will apply when owned.", player.SteamID);
        }

        return false;
    }

    private bool ApplyKnifeTypeSelection(CCSPlayerController player, KnifeDefinition knife, bool logFailures)
    {
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return false;
        }

        var weaponServices = pawn!.WeaponServices;
        if (weaponServices is null)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply knife type {KnifeId}: weapon services are unavailable for player {SteamId}.", knife.Id, player.SteamID);
            }

            return false;
        }

        foreach (var weaponHandle in weaponServices.MyWeapons)
        {
            var weapon = weaponHandle.Value;
            var weaponName = weapon is null ? null : ResolveWeaponEntityName(weapon);
            if (weapon is null || !weapon.IsValid || string.IsNullOrWhiteSpace(weaponName) || !IsKnife(weaponName))
            {
                continue;
            }

            return RefreshOwnedKnifeWithSelection(player, weapon, knife, skin: null, logFailures);
        }

        if (logFailures)
        {
            _logger.LogWarning("Astra Skins knife weapon not found for player {SteamId}. Knife type selection was saved and will apply when owned.", player.SteamID);
        }

        return false;
    }

    private bool ApplyGloveSelection(CCSPlayerController player, CosmeticEntry glove, bool logFailures)
    {
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return false;
        }

        return ApplyGloveCosmetic(player, pawn!, glove, logFailures);
    }

    private bool ApplyGloves(CCSPlayerController player, CCSPlayerPawn pawn, PlayerSkinProfile profile, bool logFailures)
    {
        if (profile.GloveSkinId is null)
        {
            return false;
        }

        if (!Catalog.GloveSkinsById.TryGetValue(profile.GloveSkinId, out var glove))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins glove selection {CosmeticId} is not present in loaded definitions for player {SteamId}.", profile.GloveSkinId, player.SteamID);
            }

            return false;
        }

        return ApplyGloveCosmetic(player, pawn, glove, logFailures);
    }

    private bool ApplyGloveCosmetic(CCSPlayerController player, CCSPlayerPawn pawn, CosmeticEntry glove, bool logFailures)
    {
        try
        {
            var econGloves = pawn.EconGloves;
            if (econGloves.Handle == IntPtr.Zero)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins econ item invalid while applying gloves {CosmeticId} for player {SteamId}.", glove.Id, player.SteamID);
                }

                return false;
            }

            if (!glove.ItemDefinitionIndex.HasValue)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins glove {CosmeticId} has no ItemDefinitionIndex.", glove.Id);
                }

                return false;
            }

            econGloves.ItemDefinitionIndex = glove.ItemDefinitionIndex.Value;
            econGloves.EntityQuality = 3;
            UpdateEconItemIdentity(econGloves, player);
            ApplyCustomName(econGloves, glove);

            var attributesApplied = _econAttributes.ApplyPaintAttributes(econGloves, glove, $"gloves player {player.SteamID}");
            pawn.EconGlovesChanged++;
            MarkGlovesStateChanged(pawn);
            RefreshGloves(player, pawn);
            return attributesApplied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply gloves for player {SteamId}.", player.SteamID);
            return false;
        }
    }

    private bool ApplyAgentSelection(CCSPlayerController player, AgentDefinition agent, bool logFailures)
    {
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return false;
        }

        return ApplyAgentModel(player, pawn!, agent, logFailures);
    }

    private bool ApplyAgent(CCSPlayerController player, CCSPlayerPawn pawn, PlayerSkinProfile profile, bool logFailures)
    {
        var team = GetPlayerTeamKey(player);
        if (team is null)
        {
            return false;
        }

        if (!profile.AgentIdsByTeam.TryGetValue(team, out var agentId))
        {
            return false;
        }

        if (!Catalog.AgentsById.TryGetValue(agentId, out var agent))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins agent selection {AgentId} is not present in loaded definitions for player {SteamId}.", agentId, player.SteamID);
            }

            return false;
        }

        if (!agent.Team.Equals(team, StringComparison.OrdinalIgnoreCase))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins agent {AgentId} does not match player team {Team} for player {SteamId}.", agent.Id, team, player.SteamID);
            }

            return false;
        }

        return ApplyAgentModel(player, pawn, agent, logFailures);
    }

    private bool ApplyAgentModel(CCSPlayerController player, CCSPlayerPawn pawn, AgentDefinition agent, bool logFailures)
    {
        if (pawn is null || !pawn.IsValid)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply agent {AgentId} for player {SteamId}: pawn is invalid.", agent.Id, player.SteamID);
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(agent.Model))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply agent {AgentId} for player {SteamId}: model path is empty.", agent.Id, player.SteamID);
            }

            return false;
        }

        var playerTeam = GetPlayerTeamKey(player);
        if (playerTeam is null || !agent.Team.Equals(playerTeam, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var itemDefinitionIndex = agent.ItemDefinitionIndex.GetValueOrDefault();
            pawn.SetModel(agent.Model);
            pawn.CharacterDefIndex = itemDefinitionIndex;
            pawn.StrVOPrefix = agent.VoicePrefix ?? string.Empty;
            pawn.HasFemaleVoice = agent.HasFemaleVoice ?? IsFemaleVoicePrefix(agent.VoicePrefix);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to apply agent {AgentId} model {Model} for player {SteamId}.", agent.Id, agent.Model, player.SteamID);
            return false;
        }
    }

    private bool ApplyCosmeticToWeapon(CCSPlayerController player, CBasePlayerWeapon weapon, CosmeticEntry cosmetic, bool isKnife, bool logFailures)
    {
        try
        {
            if (weapon.AttributeManager.Handle == IntPtr.Zero)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins econ attribute manager invalid while applying {CosmeticId} to weapon entity {EntityIndex}.", cosmetic.Id, weapon.Index);
                }

                return false;
            }

            var item = weapon.AttributeManager.Item;
            if (item.Handle == IntPtr.Zero)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins econ item invalid while applying {CosmeticId} to weapon entity {EntityIndex}.", cosmetic.Id, weapon.Index);
                }

                return false;
            }

            if (isKnife && cosmetic.ItemDefinitionIndex.HasValue)
            {
                TryChangeKnifeSubclass(weapon, cosmetic.ItemDefinitionIndex.Value);
            }

            weapon.FallbackPaintKit = cosmetic.PaintKit;
            weapon.FallbackSeed = cosmetic.Seed;
            weapon.FallbackWear = cosmetic.Wear;
            weapon.FallbackStatTrak = -1;
            weapon.OriginalOwnerXuidLow = (uint)(player.SteamID & 0xFFFFFFFF);
            weapon.OriginalOwnerXuidHigh = (uint)(player.SteamID >> 32);

            if (cosmetic.ItemDefinitionIndex.HasValue)
            {
                item.ItemDefinitionIndex = cosmetic.ItemDefinitionIndex.Value;
            }

            item.EntityQuality = isKnife ? 3 : 0;
            UpdateEconItemIdentity(item, player);
            ApplyCustomName(item, cosmetic);

            var attributesApplied = _econAttributes.ApplyPaintAttributes(item, cosmetic, $"{ResolveWeaponEntityName(weapon)} entity {weapon.Index}");
            ApplyWeaponBodyGroup(weapon, cosmetic);
            MarkWeaponStateChanged(weapon);
            RefreshActiveWeapon(player, weapon);

            if (!attributesApplied && logFailures)
            {
                _logger.LogWarning("Astra Skins applied CS2 paint netprops but dynamic attribute update failed for {CosmeticId} on weapon entity {EntityIndex}.", cosmetic.Id, weapon.Index);
            }

            return attributesApplied;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply cosmetic {CosmeticId} to weapon entity {EntityIndex}.", cosmetic.Id, weapon.Index);
            return false;
        }
    }

    private void ClearPlayerCosmetics(CCSPlayerController player, bool logFailures)
    {
        ClearWeaponCosmetics(player, includeKnives: true, logFailures: logFailures);
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return;
        }

        ClearGloveCosmetic(player, pawn!);
    }

    private void ClearWeaponCosmetics(CCSPlayerController player, bool includeKnives, bool onlyKnives = false, bool logFailures = false)
    {
        if (!TryGetPawn(player, out var pawn, logFailures))
        {
            return;
        }

        var weaponServices = pawn!.WeaponServices;
        if (weaponServices is not null)
        {
            foreach (var weaponHandle in weaponServices.MyWeapons)
            {
                var weapon = weaponHandle.Value;
                if (weapon is null || !weapon.IsValid)
                {
                    continue;
                }

                var weaponName = ResolveWeaponEntityName(weapon);
                var isKnife = IsKnife(weaponName);
                if ((onlyKnives && !isKnife) || (!includeKnives && isKnife))
                {
                    continue;
                }

                ClearWeaponCosmetic(player, weapon);
            }
        }
        else if (logFailures)
        {
            _logger.LogWarning("Astra Skins cannot clear weapon cosmetics for player {SteamId}: weapon services are unavailable.", player.SteamID);
        }
    }

    private void ClearWeaponCosmetic(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        try
        {
            weapon.FallbackPaintKit = 0;
            weapon.FallbackSeed = 0;
            weapon.FallbackWear = 0f;
            weapon.FallbackStatTrak = -1;

            var item = weapon.AttributeManager.Item;
            if (item.Handle != IntPtr.Zero)
            {
                _econAttributes.ClearPaintAttributes(item, $"{ResolveWeaponEntityName(weapon)} entity {weapon.Index}");
                UpdateEconItemIdentity(item, player);
            }

            MarkWeaponStateChanged(weapon);
            RefreshActiveWeapon(player, weapon);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to clear weapon cosmetic on entity {EntityIndex}.", weapon.Index);
        }
    }

    private void ClearGloveCosmetic(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        try
        {
            var econGloves = pawn.EconGloves;
            if (econGloves.Handle != IntPtr.Zero)
            {
                _econAttributes.ClearPaintAttributes(econGloves, $"gloves player {player.SteamID}");
                UpdateEconItemIdentity(econGloves, player);
            }

            pawn.EconGlovesChanged++;
            MarkGlovesStateChanged(pawn);
            RefreshGloves(player, pawn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to clear glove cosmetic for player {SteamId}.", player.SteamID);
        }
    }

    private bool RefreshOwnedWeaponWithSelection(CCSPlayerController player, CBasePlayerWeapon oldWeapon, string weaponEntity, CosmeticEntry skin, bool logFailures)
    {
        try
        {
            if (oldWeapon is null || !oldWeapon.IsValid)
            {
                return false;
            }

            var oldClip = 0;
            var oldReserve = 0;
            oldClip = Math.Max(0, oldWeapon.Clip1);
            oldReserve = oldWeapon.ReserveAmmo.Length > 0 ? Math.Max(0, oldWeapon.ReserveAmmo[0]) : 0;

            ApplyCosmeticToWeapon(player, oldWeapon, skin, isKnife: false, logFailures);
            oldWeapon.AddEntityIOEvent("Kill", oldWeapon, null, string.Empty, 0.01f);

            var newHandle = player.GiveNamedItem(weaponEntity);
            var newWeapon = new CBasePlayerWeapon(newHandle);
            Server.NextFrame(() =>
            {
                if (!IsUsablePlayer(player) || !newWeapon.IsValid)
                {
                    return;
                }

                try
                {
                    newWeapon.Clip1 = oldClip;
                    if (newWeapon.ReserveAmmo.Length > 0)
                    {
                        newWeapon.ReserveAmmo[0] = oldReserve;
                    }

                    ApplyCosmeticToWeapon(player, newWeapon, skin, isKnife: false, logFailures);
                    player.ExecuteClientCommand(IsPistol(weaponEntity) ? "slot2" : "slot1");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Astra Skins failed to finish weapon replacement for {WeaponEntity} on player {SteamId}.", weaponEntity, player.SteamID);
                }
            });

            if (logFailures)
            {
                _logger.LogInformation("Astra Skins refreshed {WeaponEntity} for player {SteamId} to apply {CosmeticId}.", weaponEntity, player.SteamID, skin.Id);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to refresh weapon {WeaponEntity} for player {SteamId}.", weaponEntity, player.SteamID);
            return false;
        }
    }

    private bool RefreshOwnedKnifeWithSelection(CCSPlayerController player, CBasePlayerWeapon oldKnife, KnifeDefinition knife, CosmeticEntry? skin, bool logFailures)
    {
        try
        {
            if (oldKnife is null || !oldKnife.IsValid)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins cannot refresh knife for player {SteamId}: current knife entity is invalid.", player.SteamID);
                }

                return false;
            }

            if (!WeaponEntityByDefinitionIndex.TryGetValue(knife.ItemDefinitionIndex, out var knifeEntity) ||
                !IsKnife(knifeEntity))
            {
                if (logFailures)
                {
                    _logger.LogWarning(
                        "Astra Skins cannot refresh knife {KnifeId} for player {SteamId}: item definition index is unmapped.",
                        knife.Id,
                        player.SteamID);
                }

                return false;
            }

            if (skin is null)
            {
                ApplyKnifeTypeToWeapon(player, oldKnife, knife, logFailures);
            }
            else
            {
                ApplyCosmeticToWeapon(player, oldKnife, skin, isKnife: true, logFailures);
            }

            oldKnife.AddEntityIOEvent("Kill", oldKnife, null, string.Empty, 0.01f);

            var newHandle = player.GiveNamedItem("weapon_knife");
            var newKnife = new CBasePlayerWeapon(newHandle);
            Server.NextFrame(() =>
            {
                if (!IsUsablePlayer(player) || !newKnife.IsValid)
                {
                    if (logFailures)
                    {
                        _logger.LogWarning("Astra Skins failed to create refreshed knife {KnifeEntity} for player {SteamId}.", knifeEntity, player.SteamID);
                    }

                    return;
                }

                try
                {
                    if (skin is null)
                    {
                        ApplyKnifeTypeToWeapon(player, newKnife, knife, logFailures);
                    }
                    else
                    {
                        ApplyCosmeticToWeapon(player, newKnife, skin, isKnife: true, logFailures);
                    }

                    player.ExecuteClientCommand("slot3");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Astra Skins failed to finish knife replacement for {KnifeEntity} on player {SteamId}.", knifeEntity, player.SteamID);
                }
            });

            if (logFailures)
            {
                _logger.LogInformation(
                    "Astra Skins refreshed {KnifeEntity} for player {SteamId} to apply {SelectionId}.",
                    knifeEntity,
                    player.SteamID,
                    skin?.Id ?? knife.Id);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to refresh knife for player {SteamId}.", player.SteamID);
            return false;
        }
    }

    private bool ApplyKnifeTypeToWeapon(CCSPlayerController player, CBasePlayerWeapon weapon, KnifeDefinition knife, bool logFailures)
    {
        try
        {
            if (weapon.AttributeManager.Handle == IntPtr.Zero)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins econ attribute manager invalid while applying knife type {KnifeId} to weapon entity {EntityIndex}.", knife.Id, weapon.Index);
                }

                return false;
            }

            var item = weapon.AttributeManager.Item;
            if (item.Handle == IntPtr.Zero)
            {
                if (logFailures)
                {
                    _logger.LogWarning("Astra Skins econ item invalid while applying knife type {KnifeId} to weapon entity {EntityIndex}.", knife.Id, weapon.Index);
                }

                return false;
            }

            TryChangeKnifeSubclass(weapon, knife.ItemDefinitionIndex);
            weapon.FallbackPaintKit = 0;
            weapon.FallbackSeed = 0;
            weapon.FallbackWear = 0f;
            weapon.FallbackStatTrak = -1;
            weapon.OriginalOwnerXuidLow = (uint)(player.SteamID & 0xFFFFFFFF);
            weapon.OriginalOwnerXuidHigh = (uint)(player.SteamID >> 32);
            item.ItemDefinitionIndex = knife.ItemDefinitionIndex;
            item.EntityQuality = 3;
            UpdateEconItemIdentity(item, player);
            _econAttributes.ClearPaintAttributes(item, $"{knife.EntityName} entity {weapon.Index}");
            MarkWeaponStateChanged(weapon);
            RefreshActiveWeapon(player, weapon);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Astra Skins failed to apply knife type {KnifeId} to weapon entity {EntityIndex}.", knife.Id, weapon.Index);
            return false;
        }
    }

    private void RemoveInvalidCachedSelections()
    {
        foreach (var profile in _profiles.Values)
        {
            foreach (var weapon in profile.WeaponSkins.Keys.ToArray())
            {
                if (!Catalog.WeaponSkinsById.ContainsKey(profile.WeaponSkins[weapon]))
                {
                    profile.WeaponSkins.Remove(weapon);
                }
            }

            if (profile.KnifeSkinId is not null && !Catalog.KnifeSkinsById.ContainsKey(profile.KnifeSkinId))
            {
                profile.KnifeSkinId = null;
            }

            if (profile.KnifeId is not null && !Catalog.Knives.Any(k => k.Id.Equals(profile.KnifeId, StringComparison.OrdinalIgnoreCase)))
            {
                profile.KnifeId = null;
            }

            if (profile.GloveSkinId is not null && !Catalog.GloveSkinsById.ContainsKey(profile.GloveSkinId))
            {
                profile.GloveSkinId = null;
            }

            foreach (var team in profile.AgentIdsByTeam.Keys.ToArray())
            {
                if (!Catalog.AgentsById.ContainsKey(profile.AgentIdsByTeam[team]))
                {
                    profile.AgentIdsByTeam.Remove(team);
                }
            }
        }
    }

    private static bool IsKnife(string weaponName)
    {
        return weaponName.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
               weaponName.Equals("weapon_bayonet", StringComparison.OrdinalIgnoreCase);
    }

    private bool KnifeSkinMatchesSelectedKnife(PlayerSkinProfile profile, CosmeticEntry knifeSkin)
    {
        if (profile.KnifeId is null)
        {
            return true;
        }

        var knife = Catalog.Knives.FirstOrDefault(k => k.Id.Equals(profile.KnifeId, StringComparison.OrdinalIgnoreCase));
        return knife is not null && knifeSkin.ItemDefinitionIndex == knife.ItemDefinitionIndex;
    }

    private static string ResolveWeaponEntityName(CBasePlayerWeapon weapon)
    {
        if (!weapon.IsValid)
        {
            return string.Empty;
        }

        try
        {
            var itemDefinitionIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
            if (WeaponEntityByDefinitionIndex.TryGetValue(itemDefinitionIndex, out var byDefinitionIndex))
            {
                return byDefinitionIndex;
            }
        }
        catch
        {
            // Secondary lookup covers weapons whose econ item is not ready yet.
        }

        var byExtension = weapon.GetWeaponName();
        if (!string.IsNullOrWhiteSpace(byExtension))
        {
            return byExtension;
        }

        return weapon.DesignerName ?? string.Empty;
    }

    private static bool IsPistol(string weaponName)
    {
        return weaponName is "weapon_deagle" or "weapon_elite" or "weapon_fiveseven" or "weapon_glock" or
            "weapon_hkp2000" or "weapon_usp_silencer" or "weapon_p250" or "weapon_tec9" or
            "weapon_cz75a" or "weapon_revolver";
    }

    private static string? GetPlayerTeamKey(CCSPlayerController player)
    {
        return player.Team switch
        {
            CsTeam.Terrorist => "t",
            CsTeam.CounterTerrorist => "ct",
            _ => null
        };
    }

    private static string NormalizeAgentTeam(string team)
    {
        return team.Trim().ToLowerInvariant() switch
        {
            "terrorist" or "terrorists" or "t" => "t",
            "counter-terrorist" or "counter-terrorists" or "counterterrorist" or "counterterrorists" or "ct" => "ct",
            var value => value
        };
    }

    private static string? NormalizeResetCategory(string category)
    {
        return category.Trim().ToLowerInvariant() switch
        {
            "weapon" or "weapons" or "guns" => "weapons",
            "knife" or "knives" => "knife",
            "glove" or "gloves" => "gloves",
            "agent" or "agents" => "agents",
            _ => null
        };
    }

    private static bool IsFemaleVoicePrefix(string? voicePrefix)
    {
        return !string.IsNullOrWhiteSpace(voicePrefix) &&
            voicePrefix.Contains("fem", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetPawn(CCSPlayerController player, out CCSPlayerPawn? pawn, bool logFailures)
    {
        pawn = null;
        if (!IsUsablePlayer(player))
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply cosmetics: player is invalid.");
            }

            return false;
        }

        pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            if (logFailures)
            {
                _logger.LogWarning("Astra Skins cannot apply cosmetics for player {SteamId}: player pawn is invalid.", player.SteamID);
            }

            return false;
        }

        return true;
    }

    private void UpdateEconItemIdentity(CEconItemView item, CCSPlayerController player)
    {
        var itemId = _nextItemId++;
        if (_nextItemId == ulong.MaxValue)
        {
            _nextItemId = MinimumCustomItemId;
        }

        item.ItemID = itemId;
        item.ItemIDLow = (uint)(itemId & 0xFFFFFFFF);
        item.ItemIDHigh = (uint)(itemId >> 32);
        item.AccountID = GetAccountId(player);
        item.Initialized = true;
    }

    private static void ApplyCustomName(CEconItemView item, CosmeticEntry cosmetic)
    {
        if (string.IsNullOrWhiteSpace(cosmetic.CustomName))
        {
            return;
        }

        item.CustomName = cosmetic.CustomName;
        item.CustomNameOverride = cosmetic.CustomName;
    }

    private void TryChangeKnifeSubclass(CBasePlayerWeapon weapon, ushort itemDefinitionIndex)
    {
        try
        {
            weapon.AcceptInput("ChangeSubclass", value: itemDefinitionIndex.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Astra Skins failed to change knife subclass for weapon entity {EntityIndex}.", weapon.Index);
        }
    }

    private void ApplyWeaponBodyGroup(CBasePlayerWeapon weapon, CosmeticEntry cosmetic)
    {
        if (!cosmetic.LegacyModel.HasValue)
        {
            _logger.LogWarning(
                "Astra Skins cosmetic {CosmeticId} has no legacyModel metadata. Regenerate and upload data JSON files.",
                cosmetic.Id);
            return;
        }

        try
        {
            weapon.AcceptInput("SetBodygroup", value: $"body,{(cosmetic.LegacyModel.Value ? 1 : 0)}");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Astra Skins failed to set weapon bodygroup for {CosmeticId} on entity {EntityIndex}.", cosmetic.Id, weapon.Index);
        }
    }

    private void MarkWeaponStateChanged(CBasePlayerWeapon weapon)
    {
        TrySetStateChanged(weapon, "CEconEntity", "m_AttributeManager");
        TrySetStateChanged(weapon, "CEconEntity", "m_nFallbackPaintKit");
        TrySetStateChanged(weapon, "CEconEntity", "m_nFallbackSeed");
        TrySetStateChanged(weapon, "CEconEntity", "m_flFallbackWear");
        TrySetStateChanged(weapon, "CEconEntity", "m_nFallbackStatTrak");
    }

    private void MarkGlovesStateChanged(CCSPlayerPawn pawn)
    {
        TrySetStateChanged(pawn, "CCSPlayerPawn", "m_EconGloves");
        TrySetStateChanged(pawn, "CCSPlayerPawn", "m_nEconGlovesChanged");
    }

    private void TrySetStateChanged(CBaseEntity entity, string className, string fieldName)
    {
        try
        {
            Utilities.SetStateChanged(entity, className, fieldName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Astra Skins failed to mark state changed for {ClassName}.{FieldName}.", className, fieldName);
        }
    }

    private void RefreshActiveWeapon(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        try
        {
            var pawn = player.PlayerPawn.Value;
            var activeWeapon = pawn?.WeaponServices?.ActiveWeapon.Value;
            if (pawn is null || !pawn.IsValid || activeWeapon is null || !activeWeapon.IsValid || activeWeapon.Index != weapon.Index)
            {
                return;
            }

            var weaponName = ResolveWeaponEntityName(weapon);
            var slotCommand = IsKnife(weaponName)
                ? "slot3"
                : weaponName.Equals("weapon_taser", StringComparison.OrdinalIgnoreCase)
                    ? "slot5"
                    : IsPistol(weaponName)
                        ? "slot2"
                        : "slot1";

            Server.NextFrame(() =>
            {
                if (!IsUsablePlayer(player))
                {
                    return;
                }

                player.ExecuteClientCommand(slotCommand.Equals("slot3", StringComparison.Ordinal) ? "lastinv" : "slot3");
                Server.NextFrame(() =>
                {
                    if (IsUsablePlayer(player))
                    {
                        player.ExecuteClientCommand(slotCommand);
                    }
                });
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Astra Skins failed to refresh active weapon for player {SteamId}.", player.SteamID);
        }
    }

    private void RefreshGloves(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        try
        {
            Server.NextFrame(() =>
            {
                if (!IsUsablePlayer(player) || !pawn.IsValid)
                {
                    return;
                }

                player.ExecuteClientCommand("lastinv");
                pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,0");
                Server.NextFrame(() =>
                {
                    if (pawn.IsValid)
                    {
                        pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,1");
                    }
                });
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Astra Skins failed to refresh gloves for player {SteamId}.", player.SteamID);
        }
    }

    private static uint GetAccountId(CCSPlayerController player)
    {
        return (uint)(player.SteamID & 0xFFFFFFFF);
    }

    private static ulong GetSteamId64(CCSPlayerController player)
    {
        if (!TryGetSteamId64(player, out var steamId))
        {
            throw new InvalidOperationException("Player does not have a valid SteamID64.");
        }

        return steamId;
    }

    private static bool TryGetSteamId64(CCSPlayerController player, out ulong steamId)
    {
        steamId = 0;
        if (!player.IsValid || player.IsBot || player.SteamID == 0)
        {
            return false;
        }

        steamId = player.SteamID;
        return true;
    }

    private static bool IsUsablePlayer(CCSPlayerController player)
    {
        return player.IsValid && !player.IsBot && player.SteamID != 0;
    }
}
