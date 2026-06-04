using System.Text.Json;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class ConfigManager
{
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ConfigManager(ILogger logger)
    {
        _logger = logger;
    }

    public PluginConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Missing config file: {path}");
        }

        PluginConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(path), _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed config JSON in {path}: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new InvalidOperationException($"Config file is empty or invalid: {path}");
        }

        Validate(config);
        return config;
    }

    public void WriteExample(string path)
    {
        if (File.Exists(path))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var example = new PluginConfig
        {
            DatabaseMode = "mysql",
            Sqlite = new SqliteConfig { Path = "data/astra_skins.sqlite" },
            MySql = new MySqlConfig
            {
                Host = "127.0.0.1",
                Port = 3306,
                Database = "astra_skins",
                Username = "astra_skins",
                Password = "change-me"
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(example, _jsonOptions));
    }

    public void Validate(PluginConfig config)
    {
        if (config.Sqlite is null)
        {
            throw new InvalidOperationException("Sqlite config section is required.");
        }

        if (config.MySql is null)
        {
            throw new InvalidOperationException("MySql config section is required.");
        }

        if (config.Menu is null)
        {
            throw new InvalidOperationException("Menu config section is required.");
        }

        if (config.Definitions is null)
        {
            throw new InvalidOperationException("Definitions config section is required.");
        }

        var mode = config.DatabaseMode?.Trim().ToLowerInvariant();
        if (mode is not ("mysql" or "sqlite"))
        {
            throw new InvalidOperationException("DatabaseMode is required and must be exactly \"mysql\" or \"sqlite\".");
        }

        config.DatabaseMode = mode;

        if (mode == "sqlite" && string.IsNullOrWhiteSpace(config.Sqlite.Path))
        {
            throw new InvalidOperationException("Sqlite.Path is required when DatabaseMode is \"sqlite\".");
        }

        if (mode == "mysql")
        {
            if (string.IsNullOrWhiteSpace(config.MySql.Host) ||
                string.IsNullOrWhiteSpace(config.MySql.Database) ||
                string.IsNullOrWhiteSpace(config.MySql.Username))
            {
                throw new InvalidOperationException("MySql.Host, MySql.Database, and MySql.Username are required when DatabaseMode is \"mysql\".");
            }

            if (config.MySql.Port is < 1 or > 65535)
            {
                throw new InvalidOperationException("MySql.Port must be between 1 and 65535.");
            }
        }

        if (config.Menu.ItemsPerPage is < 3 or > 10)
        {
            throw new InvalidOperationException("Menu.ItemsPerPage must be between 3 and 10.");
        }

        if (config.Menu.TimeoutSeconds < 5)
        {
            throw new InvalidOperationException("Menu.TimeoutSeconds must be at least 5.");
        }

        if (config.Menu.CooldownMilliseconds < 50)
        {
            throw new InvalidOperationException("Menu.CooldownMilliseconds must be at least 50.");
        }

        if (config.Menu.SelectionCooldownMilliseconds is < 0 or > 5000)
        {
            throw new InvalidOperationException("Menu.SelectionCooldownMilliseconds must be between 0 and 5000.");
        }

        if (string.IsNullOrWhiteSpace(config.Definitions.Weapons) ||
            string.IsNullOrWhiteSpace(config.Definitions.Knives) ||
            string.IsNullOrWhiteSpace(config.Definitions.Gloves) ||
            string.IsNullOrWhiteSpace(config.Definitions.Agents))
        {
            throw new InvalidOperationException("Definitions.Weapons, Definitions.Knives, Definitions.Gloves, and Definitions.Agents are required.");
        }

        _logger.LogInformation("Astra Skins config validated with DatabaseMode={DatabaseMode}", config.DatabaseMode);
    }
}
