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

        var validation = new DefinitionValidation(_logger);
        return validation.ValidateAndBuild(weapons, knives, gloves, agents, categories);
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
    public IReadOnlyDictionary<string, WeaponDefinition> WeaponsByEntity { get; init; } = new Dictionary<string, WeaponDefinition>();
    public IReadOnlyDictionary<string, CosmeticEntry> WeaponSkinsById { get; init; } = new Dictionary<string, CosmeticEntry>();
    public IReadOnlyDictionary<string, CosmeticEntry> KnifeSkinsById { get; init; } = new Dictionary<string, CosmeticEntry>();
    public IReadOnlyDictionary<string, CosmeticEntry> GloveSkinsById { get; init; } = new Dictionary<string, CosmeticEntry>();
    public IReadOnlyDictionary<string, AgentDefinition> AgentsById { get; init; } = new Dictionary<string, AgentDefinition>();
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
        List<CategoryDefinition> categories)
    {
        var categoryIds = categories.Where(c => c.Enabled).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validWeapons = new List<WeaponDefinition>();
        var validKnives = new List<KnifeDefinition>();
        var validGloves = new List<GloveDefinition>();
        var validAgents = new List<AgentDefinition>();
        var weaponsByEntity = new Dictionary<string, WeaponDefinition>(StringComparer.OrdinalIgnoreCase);
        var weaponSkinsById = new Dictionary<string, CosmeticEntry>(StringComparer.OrdinalIgnoreCase);
        var knifeSkinsById = new Dictionary<string, CosmeticEntry>(StringComparer.OrdinalIgnoreCase);
        var gloveSkinsById = new Dictionary<string, CosmeticEntry>(StringComparer.OrdinalIgnoreCase);
        var agentsById = new Dictionary<string, AgentDefinition>(StringComparer.OrdinalIgnoreCase);

        ValidateCategories(categories);

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
            Categories = categories.Where(c => c.Enabled).OrderBy(c => c.Order).ThenBy(c => c.DisplayName).ToList(),
            WeaponsByEntity = weaponsByEntity,
            WeaponSkinsById = weaponSkinsById,
            KnifeSkinsById = knifeSkinsById,
            GloveSkinsById = gloveSkinsById,
            AgentsById = agentsById
        };
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

        foreach (var entry in entries)
        {
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
