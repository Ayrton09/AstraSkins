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
    public DefinitionPathConfig Definitions { get; set; } = new();
    public bool EnableAdminReloadCommand { get; set; } = true;
    public string AdminReloadPermission { get; set; } = "@css/config";
    public bool EnableAdminDebugCommand { get; set; } = true;
    public string AdminDebugPermission { get; set; } = "@css/config";
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
}

public sealed class MenuConfig
{
    public int ItemsPerPage { get; set; } = 6;
    public int TimeoutSeconds { get; set; } = 25;
    public int CooldownMilliseconds { get; set; } = 180;
    public int SelectionCooldownMilliseconds { get; set; } = 900;
}

public sealed class DefinitionPathConfig
{
    public string Weapons { get; set; } = "data/weapons.json";
    public string Knives { get; set; } = "data/knives.json";
    public string Gloves { get; set; } = "data/gloves.json";
    public string Agents { get; set; } = "data/agents.json";
    public string? Categories { get; set; } = "data/categories.json";
}
