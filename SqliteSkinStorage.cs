using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class SqliteSkinStorage : ISkinStorage
{
    private readonly string _databasePath;
    private readonly ILogger _logger;

    public SqliteSkinStorage(string databasePath, ILogger logger)
    {
        _databasePath = databasePath;
        _logger = logger;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databasePath)) ?? ".");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS astra_player_skin_selections (
            steam_id INTEGER NOT NULL,
            selection_type TEXT NOT NULL,
            target TEXT NOT NULL,
            cosmetic_id TEXT NOT NULL,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY (steam_id, selection_type, target)
        );
        CREATE INDEX IF NOT EXISTS idx_astra_player_skin_selections_steam_id
            ON astra_player_skin_selections (steam_id);
        """;
        command.ExecuteNonQuery();
        _logger.LogInformation("SQLite storage initialized at {Path}", _databasePath);
    }

    public PlayerSkinProfile LoadProfile(ulong steamId64)
    {
        var profile = new PlayerSkinProfile { SteamId64 = steamId64 };
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT selection_type, target, cosmetic_id FROM astra_player_skin_selections WHERE steam_id = $steam_id";
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ApplyRow(profile, reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }

        return profile;
    }

    public void SaveWeaponSkin(ulong steamId64, string weaponEntity, string cosmeticId)
    {
        Upsert(steamId64, "weapon", weaponEntity, cosmeticId);
    }

    public void SaveKnifeType(ulong steamId64, string knifeId)
    {
        Upsert(steamId64, "knife_type", "knife", knifeId);
    }

    public void SaveKnifeSkin(ulong steamId64, string cosmeticId)
    {
        Upsert(steamId64, "knife", "knife", cosmeticId);
    }

    public void SaveGloveSkin(ulong steamId64, string cosmeticId)
    {
        Upsert(steamId64, "glove", "glove", cosmeticId);
    }

    public void SaveAgent(ulong steamId64, string team, string agentId)
    {
        Upsert(steamId64, "agent", team, agentId);
    }

    public void ResetProfile(ulong steamId64)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id";
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));
        command.ExecuteNonQuery();
    }

    public void ResetCategory(ulong steamId64, string category)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = category switch
        {
            "weapons" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type = 'weapon'",
            "knife" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type IN ('knife', 'knife_type')",
            "gloves" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type = 'glove'",
            "agents" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type = 'agent'",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Invalid reset category.")
        };
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
    }

    private SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private void Upsert(ulong steamId64, string type, string target, string cosmeticId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO astra_player_skin_selections (steam_id, selection_type, target, cosmetic_id, updated_at)
        VALUES ($steam_id, $selection_type, $target, $cosmetic_id, CURRENT_TIMESTAMP)
        ON CONFLICT(steam_id, selection_type, target)
        DO UPDATE SET cosmetic_id = excluded.cosmetic_id, updated_at = CURRENT_TIMESTAMP;
        """;
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));
        command.Parameters.AddWithValue("$selection_type", type);
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$cosmetic_id", cosmeticId);
        command.ExecuteNonQuery();
    }

    private static void ApplyRow(PlayerSkinProfile profile, string type, string target, string cosmeticId)
    {
        if (type.Equals("weapon", StringComparison.OrdinalIgnoreCase))
        {
            profile.WeaponSkins[target] = cosmeticId;
        }
        else if (type.Equals("knife", StringComparison.OrdinalIgnoreCase))
        {
            profile.KnifeSkinId = cosmeticId;
        }
        else if (type.Equals("knife_type", StringComparison.OrdinalIgnoreCase))
        {
            profile.KnifeId = cosmeticId;
        }
        else if (type.Equals("glove", StringComparison.OrdinalIgnoreCase))
        {
            profile.GloveSkinId = cosmeticId;
        }
        else if (type.Equals("agent", StringComparison.OrdinalIgnoreCase))
        {
            profile.AgentIdsByTeam[target] = cosmeticId;
        }
    }
}
