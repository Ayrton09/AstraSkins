using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class ConfigManager
{
    private readonly ILogger _logger;

    public ConfigManager(ILogger logger)
    {
        _logger = logger;
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

        if (config.Customization is null)
        {
            throw new InvalidOperationException("Customization config section is required.");
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

            var sslMode = config.MySql.SslMode?.Trim().ToLowerInvariant();
            if (sslMode is not ("none" or "preferred" or "required" or "verifyca" or "verifyfull"))
            {
                throw new InvalidOperationException("MySql.SslMode must be one of: none, preferred, required, verifyca, verifyfull.");
            }

            config.MySql.SslMode = sslMode;
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

        if (config.Customization.MaxNameTagLength is < 4 or > 32)
        {
            throw new InvalidOperationException("Customization.MaxNameTagLength must be between 4 and 32.");
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
