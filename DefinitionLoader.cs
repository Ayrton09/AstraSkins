using System.Text.Json;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class DefinitionLoader
{
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    public DefinitionLoader(ILogger logger)
    {
        _logger = logger;
    }

    public DefinitionCatalog Load(string baseDirectory, PluginConfig config)
    {
        var weaponsPath = Resolve(baseDirectory, config.Definitions.Weapons);
        var knivesPath = Resolve(baseDirectory, config.Definitions.Knives);
        var glovesPath = Resolve(baseDirectory, config.Definitions.Gloves);
        var agentsPath = Resolve(baseDirectory, config.Definitions.Agents);
        var musicKitsPath = Resolve(baseDirectory, config.Definitions.MusicKits);
        var stickersPath = Resolve(baseDirectory, config.Definitions.Stickers);
        var keychainsPath = Resolve(baseDirectory, config.Definitions.Keychains);
        var categoriesPath = string.IsNullOrWhiteSpace(config.Definitions.Categories)
            ? null
            : Resolve(baseDirectory, config.Definitions.Categories!);

        var weapons = LoadRequired<List<WeaponDefinition>>(weaponsPath, "weapons");
        var knives = LoadRequired<List<KnifeDefinition>>(knivesPath, "knives");
        var gloves = LoadRequired<List<GloveDefinition>>(glovesPath, "gloves");
        var agents = LoadRequired<List<AgentDefinition>>(agentsPath, "agents");
        var categories = categoriesPath is not null && File.Exists(categoriesPath)
            ? LoadRequired<List<CategoryDefinition>>(categoriesPath, "categories")
            : new List<CategoryDefinition>();

        // Music kits are an optional category: the feature simply stays hidden
        // when the definition file is absent.
        var musicKits = File.Exists(musicKitsPath)
            ? LoadRequired<List<MusicKitDefinition>>(musicKitsPath, "music kits")
            : new List<MusicKitDefinition>();

        // Same for stickers and charms: no file, no menu entry.
        var stickers = File.Exists(stickersPath)
            ? LoadRequired<List<StickerDefinition>>(stickersPath, "stickers")
            : new List<StickerDefinition>();
        var keychains = File.Exists(keychainsPath)
            ? LoadRequired<List<KeychainDefinition>>(keychainsPath, "keychains")
            : new List<KeychainDefinition>();

        var validation = new DefinitionValidation(_logger);
        return validation.ValidateAndBuild(weapons, knives, gloves, agents, categories, musicKits, stickers, keychains);
    }

    private T LoadRequired<T>(string path, string label)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Missing required {label} definition file: {path}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions);
            if (result is null)
            {
                throw new InvalidOperationException($"{label} definition file is empty: {path}");
            }

            // A literal null in the array deserializes to a null element and
            // would surface as a NullReferenceException past the validation.
            if (result is System.Collections.IList list)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is null)
                    {
                        _logger.LogWarning("Skipping null entry in the {Label} definition file.", label);
                        list.RemoveAt(i);
                    }
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed {label} JSON in {path}: {ex.Message}", ex);
        }
    }

    private static string Resolve(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}

public sealed class DefinitionCatalog
{
    public IReadOnlyList<WeaponDefinition> Weapons { get; init; } = Array.Empty<WeaponDefinition>();
    public IReadOnlyList<KnifeDefinition> Knives { get; init; } = Array.Empty<KnifeDefinition>();
    public IReadOnlyList<GloveDefinition> Gloves { get; init; } = Array.Empty<GloveDefinition>();
    public IReadOnlyList<AgentDefinition> Agents { get; init; } = Array.Empty<AgentDefinition>();
    public IReadOnlyList<CategoryDefinition> Categories { get; init; } = Array.Empty<CategoryDefinition>();
    public IReadOnlyList<MusicKitDefinition> MusicKits { get; init; } = Array.Empty<MusicKitDefinition>();
    // Stickers and keychains keep the file order: the generator sorts them by
    // event, newest first, and the menu shows them that way.
    public IReadOnlyList<StickerDefinition> Stickers { get; init; } = Array.Empty<StickerDefinition>();
    public IReadOnlyList<KeychainDefinition> Keychains { get; init; } = Array.Empty<KeychainDefinition>();
    public IReadOnlyList<StickerGroup> StickerGroups { get; init; } = Array.Empty<StickerGroup>();
    public IReadOnlyList<KeychainGroup> KeychainGroups { get; init; } = Array.Empty<KeychainGroup>();
    public IReadOnlyDictionary<string, WeaponDefinition> WeaponsByEntity { get; init; } = new Dictionary<string, WeaponDefinition>();
    public IReadOnlyDictionary<string, CosmeticEntry> WeaponSkinsById { get; init; } = new Dictionary<string, CosmeticEntry>();
    public IReadOnlyDictionary<string, CosmeticEntry> KnifeSkinsById { get; init; } = new Dictionary<string, CosmeticEntry>();
    public IReadOnlyDictionary<string, CosmeticEntry> GloveSkinsById { get; init; } = new Dictionary<string, CosmeticEntry>();
    public IReadOnlyDictionary<string, AgentDefinition> AgentsById { get; init; } = new Dictionary<string, AgentDefinition>();
    public IReadOnlyDictionary<string, MusicKitDefinition> MusicKitsById { get; init; } = new Dictionary<string, MusicKitDefinition>();
    public IReadOnlyDictionary<string, StickerDefinition> StickersById { get; init; } = new Dictionary<string, StickerDefinition>();
    public IReadOnlyDictionary<string, KeychainDefinition> KeychainsById { get; init; } = new Dictionary<string, KeychainDefinition>();
}

// Menu structure for stickers: a group is a tournament, capsule or
// collection; tournament groups split further into the capsules of that
// event. A null name is the "Other" bucket for entries without a group.
public sealed class StickerGroup
{
    public string? Name { get; init; }
    public string? NameZh { get; init; }
    public IReadOnlyList<StickerCapsule> Capsules { get; init; } = Array.Empty<StickerCapsule>();
    // Stickers of the group that belong to no capsule.
    public IReadOnlyList<StickerDefinition> Stickers { get; init; } = Array.Empty<StickerDefinition>();
}

public sealed class StickerCapsule
{
    public string Name { get; init; } = string.Empty;
    public string? NameZh { get; init; }
    public IReadOnlyList<StickerDefinition> Stickers { get; init; } = Array.Empty<StickerDefinition>();
}

public sealed class KeychainGroup
{
    public string? Name { get; init; }
    public string? NameZh { get; init; }
    public IReadOnlyList<KeychainDefinition> Keychains { get; init; } = Array.Empty<KeychainDefinition>();
}

internal sealed class DefinitionValidation
{
    private static readonly HashSet<string> KnownWeaponEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapon_ak47", "weapon_aug", "weapon_awp", "weapon_bizon", "weapon_deagle", "weapon_elite",
        "weapon_famas", "weapon_fiveseven", "weapon_g3sg1", "weapon_galilar", "weapon_glock",
        "weapon_hkp2000", "weapon_usp_silencer", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_m249",
        "weapon_mac10", "weapon_mag7", "weapon_mp5sd", "weapon_mp7", "weapon_mp9", "weapon_negev",
        "weapon_nova", "weapon_p250", "weapon_cz75a", "weapon_p90", "weapon_revolver", "weapon_sawedoff", "weapon_scar20",
        "weapon_sg556", "weapon_ssg08", "weapon_tec9", "weapon_ump45", "weapon_xm1014"
    };

    private readonly ILogger _logger;

    public DefinitionValidation(ILogger logger)
    {
        _logger = logger;
    }

    public DefinitionCatalog ValidateAndBuild(
        List<WeaponDefinition> weapons,
        List<KnifeDefinition> knives,
        List<GloveDefinition> gloves,
        List<AgentDefinition> agents,
        List<CategoryDefinition> categories,
        List<MusicKitDefinition> musicKits,
        List<StickerDefinition>? stickers = null,
        List<KeychainDefinition>? keychains = null)
    {
        // Validate first so a category dropped here is also unknown to weapons.
        ValidateCategories(categories);
        var categoryIds = categories.Where(c => c.Enabled).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validWeapons = new List<WeaponDefinition>();
        var validKnives = new List<KnifeDefinition>();
        var validGloves = new List<GloveDefinition>();
        var validAgents = new List<AgentDefinition>();
        var validMusicKits = new List<MusicKitDefinition>();
        var weaponsByEntity = new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase);
        var weaponSkinsById = new Dictionary<string, CosmeticEntry>(StringComparer.OrdinalIgnoreCase);
        var knifeSkinsById = new Dictionary<string, CosmeticEntry>(StringComparer.OrdinalIgnoreCase);
        var gloveSkinsById = new Dictionary<string, CosmeticEntry>(StringComparer.OrdinalIgnoreCase);
        var agentsById = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase);
        var musicKitsById = new Dictionary<string, MusicKitDefinition>(StringComparer.OrdinalIgnoreCase);
        var validStickers = new List<StickerDefinition>();
        var validKeychains = new List<KeychainDefinition>();
        var stickersById = new Dictionary<string, StickerDefinition>(StringComparer.OrdinalIgnoreCase);
        var keychainsById = new Dictionary<string, KeychainDefinition>(StringComparer.OrdinalIgnoreCase);
        var knifeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gloveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var weapon in weapons)
        {
            if (!weapon.Enabled)
            {
                continue;
            }

            if (!ValidateWeapon(weapon, categoryIds, weaponsByEntity))
            {
                continue;
            }

            weapon.Skins = ValidateCosmetics(weapon.Skins, $"weapon {weapon.EntityName}", weaponSkinsById, requireItemDefinition: false);
            if (weapon.Skins.Count == 0)
            {
                _logger.LogWarning("Skipping weapon {Weapon}: it has no valid skins.", weapon.EntityName);
                continue;
            }

            validWeapons.Add(weapon);
            weaponsByEntity[weapon.EntityName] = weapon;
        }

        foreach (var knife in knives)
        {
            if (!knife.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(knife.Id) || string.IsNullOrWhiteSpace(knife.DisplayName) || knife.ItemDefinitionIndex == 0)
            {
                _logger.LogWarning("Skipping invalid knife definition with Id={Id}. Id, DisplayName, and ItemDefinitionIndex are required.", knife.Id);
                continue;
            }

            if (!knifeIds.Add(knife.Id))
            {
                _logger.LogWarning("Skipping duplicate knife definition Id={Id}.", knife.Id);
                continue;
            }

            knife.Skins = ValidateCosmetics(knife.Skins, $"knife {knife.Id}", knifeSkinsById, requireItemDefinition: false, defaultItemDefinition: knife.ItemDefinitionIndex);
            if (knife.Skins.Count > 0)
            {
                validKnives.Add(knife);
            }
        }

        foreach (var glove in gloves)
        {
            if (!glove.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(glove.Id) || string.IsNullOrWhiteSpace(glove.DisplayName) || glove.ItemDefinitionIndex == 0)
            {
                _logger.LogWarning("Skipping invalid glove definition with Id={Id}. Id, DisplayName, and ItemDefinitionIndex are required.", glove.Id);
                continue;
            }

            if (!gloveIds.Add(glove.Id))
            {
                _logger.LogWarning("Skipping duplicate glove definition Id={Id}.", glove.Id);
                continue;
            }

            glove.Skins = ValidateCosmetics(glove.Skins, $"glove {glove.Id}", gloveSkinsById, requireItemDefinition: false, defaultItemDefinition: glove.ItemDefinitionIndex);
            if (glove.Skins.Count > 0)
            {
                validGloves.Add(glove);
            }
        }

        foreach (var agent in agents)
        {
            if (!agent.Enabled)
            {
                continue;
            }

            if (!ValidateAgent(agent, agentsById))
            {
                continue;
            }

            validAgents.Add(agent);
            agentsById[agent.Id] = agent;
        }

        foreach (var kit in musicKits)
        {
            if (!kit.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(kit.Id) || string.IsNullOrWhiteSpace(kit.DisplayName) || kit.MusicKit <= 0)
            {
                _logger.LogWarning("Skipping invalid music kit definition with Id={Id}. Id, DisplayName, and a positive MusicKit value are required.", kit.Id);
                continue;
            }

            if (!musicKitsById.TryAdd(kit.Id, kit))
            {
                _logger.LogWarning("Skipping duplicate music kit definition Id={Id}.", kit.Id);
                continue;
            }

            validMusicKits.Add(kit);
        }

        foreach (var sticker in stickers ?? new List<StickerDefinition>())
        {
            if (sticker is null || !sticker.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(sticker.Id) || string.IsNullOrWhiteSpace(sticker.DisplayName) || sticker.StickerId <= 0)
            {
                _logger.LogWarning("Skipping invalid sticker definition with Id={Id}. Id, DisplayName, and a positive StickerId value are required.", sticker.Id);
                continue;
            }

            if (!stickersById.TryAdd(sticker.Id, sticker))
            {
                _logger.LogWarning("Skipping duplicate sticker definition Id={Id}.", sticker.Id);
                continue;
            }

            validStickers.Add(sticker);
        }

        foreach (var keychain in keychains ?? new List<KeychainDefinition>())
        {
            if (keychain is null || !keychain.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(keychain.Id) || string.IsNullOrWhiteSpace(keychain.DisplayName) || keychain.KeychainId <= 0)
            {
                _logger.LogWarning("Skipping invalid keychain definition with Id={Id}. Id, DisplayName, and a positive KeychainId value are required.", keychain.Id);
                continue;
            }

            if (!keychainsById.TryAdd(keychain.Id, keychain))
            {
                _logger.LogWarning("Skipping duplicate keychain definition Id={Id}.", keychain.Id);
                continue;
            }

            validKeychains.Add(keychain);
        }

        if (validWeapons.Count == 0 && validKnives.Count == 0 && validGloves.Count == 0 && validAgents.Count == 0)
        {
            throw new InvalidOperationException("No valid cosmetic definitions were loaded. Check data JSON files.");
        }

        return new DefinitionCatalog
        {
            Weapons = validWeapons.OrderBy(w => w.Category).ThenBy(w => w.DisplayName).ToList(),
            Knives = validKnives.OrderBy(k => k.DisplayName).ToList(),
            Gloves = validGloves.OrderBy(g => g.DisplayName).ToList(),
            Agents = validAgents.OrderBy(a => a.Team).ThenBy(a => a.DisplayName).ToList(),
            MusicKits = validMusicKits.OrderBy(m => m.MusicKit).ToList(),
            Categories = categories.Where(c => c.Enabled).OrderBy(c => c.Order).ThenBy(c => c.DisplayName).ToList(),
            WeaponsByEntity = weaponsByEntity,
            WeaponSkinsById = weaponSkinsById,
            KnifeSkinsById = knifeSkinsById,
            GloveSkinsById = gloveSkinsById,
            AgentsById = agentsById,
            MusicKitsById = musicKitsById,
            Stickers = validStickers,
            Keychains = validKeychains,
            StickerGroups = BuildStickerGroups(validStickers),
            KeychainGroups = BuildKeychainGroups(validKeychains),
            StickersById = stickersById,
            KeychainsById = keychainsById
        };
    }

    // Groups and capsules come out in file order (first appearance), which is
    // the order the generator chose; entries without a group go last.
    private static List<StickerGroup> BuildStickerGroups(List<StickerDefinition> stickers)
    {
        var groups = new List<StickerGroup>();
        foreach (var byGroup in GroupInOrder(stickers, s => s.Group))
        {
            var capsules = new List<StickerCapsule>();
            var loose = new List<StickerDefinition>();
            foreach (var byCapsule in GroupInOrder(byGroup, s => s.Capsule))
            {
                if (byCapsule.Key is null)
                {
                    loose.AddRange(byCapsule);
                    continue;
                }

                capsules.Add(new StickerCapsule
                {
                    Name = byCapsule.Key,
                    NameZh = byCapsule.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.CapsuleZh))?.CapsuleZh,
                    Stickers = byCapsule.ToList()
                });
            }

            groups.Add(new StickerGroup
            {
                Name = byGroup.Key,
                NameZh = byGroup.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.GroupZh))?.GroupZh,
                Capsules = capsules,
                Stickers = loose
            });
        }

        return groups.OrderBy(g => g.Name is null ? 1 : 0).ToList();
    }

    private static List<KeychainGroup> BuildKeychainGroups(List<KeychainDefinition> keychains)
    {
        return GroupInOrder(keychains, k => k.Group)
            .Select(byGroup => new KeychainGroup
            {
                Name = byGroup.Key,
                NameZh = byGroup.FirstOrDefault(k => !string.IsNullOrWhiteSpace(k.GroupZh))?.GroupZh,
                Keychains = byGroup.ToList()
            })
            .OrderBy(g => g.Name is null ? 1 : 0)
            .ToList();
    }

    private static IEnumerable<IGrouping<string?, T>> GroupInOrder<T>(IEnumerable<T> items, Func<T, string?> key)
    {
        // GroupBy keeps the groups in order of first appearance; the key is
        // normalized so an empty string and null land in the same bucket.
        return items.GroupBy(item => string.IsNullOrWhiteSpace(key(item)) ? null : key(item), StringComparer.OrdinalIgnoreCase);
    }

    private void ValidateCategories(List<CategoryDefinition> categories)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            if (string.IsNullOrWhiteSpace(category.Id) || string.IsNullOrWhiteSpace(category.DisplayName))
            {
                _logger.LogWarning("Invalid category definition skipped: Id and DisplayName are required.");
                category.Enabled = false;
                continue;
            }

            if (!seen.Add(category.Id))
            {
                _logger.LogWarning("Duplicate category id {CategoryId}; later duplicate disabled.", category.Id);
                category.Enabled = false;
            }
        }
    }

    private bool ValidateWeapon(WeaponDefinition weapon, HashSet<string> categoryIds, Dictionary<string, WeaponDefinition> weaponsByEntity)
    {
        if (string.IsNullOrWhiteSpace(weapon.EntityName) ||
            string.IsNullOrWhiteSpace(weapon.DisplayName) ||
            string.IsNullOrWhiteSpace(weapon.Category))
        {
            _logger.LogWarning("Skipping invalid weapon definition. EntityName, DisplayName, and Category are required.");
            return false;
        }

        if (!KnownWeaponEntities.Contains(weapon.EntityName))
        {
            _logger.LogWarning("Skipping weapon {Weapon}: entity name is not a known CS2 weapon mapping.", weapon.EntityName);
            return false;
        }

        if (categoryIds.Count > 0 && !categoryIds.Contains(weapon.Category))
        {
            _logger.LogWarning("Skipping weapon {Weapon}: category {Category} is not present in categories.json.", weapon.EntityName, weapon.Category);
            return false;
        }

        if (weaponsByEntity.ContainsKey(weapon.EntityName))
        {
            _logger.LogWarning("Skipping duplicate weapon entity {Weapon}.", weapon.EntityName);
            return false;
        }

        return true;
    }

    private bool ValidateAgent(AgentDefinition agent, Dictionary<string, AgentDefinition> agentsById)
    {
        if (string.IsNullOrWhiteSpace(agent.Id) ||
            string.IsNullOrWhiteSpace(agent.DisplayName) ||
            string.IsNullOrWhiteSpace(agent.Team) ||
            string.IsNullOrWhiteSpace(agent.Model))
        {
            _logger.LogWarning("Skipping invalid agent definition with Id={Id}. Id, DisplayName, Team, and Model are required.", agent.Id);
            return false;
        }

        agent.Team = NormalizeAgentTeam(agent.Team);
        if (agent.Team is not "t" and not "ct")
        {
            _logger.LogWarning("Skipping agent {AgentId}: Team must be t or ct.", agent.Id);
            return false;
        }

        if (!agent.Model.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Skipping agent {AgentId}: Model must be a .vmdl path.", agent.Id);
            return false;
        }

        if (agent.ItemDefinitionIndex is null or 0)
        {
            _logger.LogWarning("Skipping agent {AgentId}: ItemDefinitionIndex is required for agent radio support.", agent.Id);
            return false;
        }

        if (agentsById.ContainsKey(agent.Id))
        {
            _logger.LogWarning("Skipping duplicate agent id {AgentId}.", agent.Id);
            return false;
        }

        return true;
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

    private List<CosmeticEntry> ValidateCosmetics(
        List<CosmeticEntry> entries,
        string owner,
        Dictionary<string, CosmeticEntry> globalIds,
        bool requireItemDefinition,
        ushort? defaultItemDefinition = null)
    {
        var valid = new List<CosmeticEntry>();
        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (entries is null)
        {
            _logger.LogWarning("Skipping {Owner}: its skins list is null.", owner);
            return valid;
        }

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                _logger.LogWarning("Skipping null cosmetic entry in {Owner}.", owner);
                continue;
            }

            if (!entry.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.DisplayName))
            {
                _logger.LogWarning("Skipping cosmetic entry in {Owner}: Id and DisplayName are required.", owner);
                continue;
            }

            if (!localIds.Add(entry.Id) || globalIds.ContainsKey(entry.Id))
            {
                _logger.LogWarning("Skipping duplicate cosmetic id {CosmeticId} in {Owner}.", entry.Id, owner);
                continue;
            }

            if (entry.PaintKit < 0 || entry.Seed < 0 || entry.Wear is < 0f or > 1f)
            {
                _logger.LogWarning("Skipping cosmetic {CosmeticId} in {Owner}: PaintKit, Seed, or Wear is invalid.", entry.Id, owner);
                continue;
            }

            if (requireItemDefinition && entry.ItemDefinitionIndex is null or 0)
            {
                _logger.LogWarning("Skipping cosmetic {CosmeticId} in {Owner}: ItemDefinitionIndex is required.", entry.Id, owner);
                continue;
            }

            if (entry.ItemDefinitionIndex is null && defaultItemDefinition.HasValue)
            {
                entry.ItemDefinitionIndex = defaultItemDefinition.Value;
            }

            valid.Add(entry);
            globalIds[entry.Id] = entry;
        }

        return valid.OrderBy(e => e.DisplayName).ToList();
    }
}
