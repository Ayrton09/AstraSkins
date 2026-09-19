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

        foreach (var (name, module) in new (string, ModuleConfig?)[]
                 {
                     ("Weapons", config.Weapons), ("Knives", config.Knives), ("Gloves", config.Gloves), ("Agents", config.Agents),
                     ("MusicKits", config.MusicKits), ("Stickers", config.Stickers), ("Keychains", config.Keychains)
                 })
        {
            if (module is null)
            {
                throw new InvalidOperationException($"{name} config section is required.");
            }

            module.Permission ??= string.Empty;
        }

        if (config.Commands is null)
        {
            throw new InvalidOperationException("Commands config section is required.");
        }

        NormalizeCommands(config.Commands);

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

        var backKey = config.Menu.BackKey?.Trim().ToLowerInvariant();
        config.Menu.BackKey = backKey switch
        {
            "shift" => "Shift",
            "a" => "A",
            "both" => "Both",
            _ => throw new InvalidOperationException("Menu.BackKey must be \"Shift\", \"A\" or \"Both\".")
        };

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

    // Command names end up as CounterStrikeSharp console commands, so they are
    // lower-cased, prefixed with "css_" when written bare ("kch" or "!kch"
    // both mean css_kch) and checked for characters the console accepts. A
    // name used by two commands is a configuration error, not a coin toss.
    private static void NormalizeCommands(CommandsConfig commands)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, aliases) in commands.Entries())
        {
            if (aliases is null)
            {
                throw new InvalidOperationException($"Commands.{name} must be a list of command names (it may be empty).");
            }

            var normalized = new List<string>();
            foreach (var raw in aliases)
            {
                var alias = (raw ?? string.Empty).Trim().TrimStart('!', '/').ToLowerInvariant();
                if (alias.Length > 0 && !alias.StartsWith("css_", StringComparison.Ordinal))
                {
                    alias = "css_" + alias;
                }

                if (alias.Length <= 4 || alias.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                {
                    throw new InvalidOperationException($"Commands.{name} contains an invalid command name \"{raw}\": use letters, digits and underscores.");
                }

                if (normalized.Contains(alias))
                {
                    continue;
                }

                if (seen.TryGetValue(alias, out var owner) && owner != name)
                {
                    throw new InvalidOperationException($"Command name \"{alias}\" is used by both Commands.{owner} and Commands.{name}.");
                }

                seen[alias] = name;
                normalized.Add(alias);
            }

            aliases.Clear();
            aliases.AddRange(normalized);
        }
    }
}
