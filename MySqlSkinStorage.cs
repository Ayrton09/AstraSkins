using Microsoft.Extensions.Logging;
using MySqlConnector;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class MySqlSkinStorage : ISkinStorage
{
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public MySqlSkinStorage(Models.MySqlConfig config, ILogger logger)
    {
        _logger = logger;
        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)config.Port,
            Database = config.Database,
            UserID = config.Username,
            Password = config.Password,
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true,
            TreatTinyAsBoolean = true
        };
        _connectionString = builder.ConnectionString;
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        CREATE TABLE IF NOT EXISTS astra_player_skin_selections (
            steam_id BIGINT UNSIGNED NOT NULL,
            selection_type VARCHAR(16) NOT NULL,
            target VARCHAR(64) NOT NULL,
            cosmetic_id VARCHAR(128) NOT NULL,
            updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (steam_id, selection_type, target),
            INDEX idx_astra_player_skin_selections_steam_id (steam_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
        """;
        command.ExecuteNonQuery();
        _logger.LogInformation("MySQL storage initialized.");
    }

    public PlayerSkinProfile LoadProfile(ulong steamId64)
    {
        var profile = new PlayerSkinProfile { SteamId64 = steamId64 };
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT selection_type, target, cosmetic_id FROM astra_player_skin_selections WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@steam_id", steamId64);

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
        command.CommandText = "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id";
        command.Parameters.AddWithValue("@steam_id", steamId64);
        command.ExecuteNonQuery();
    }

    public void ResetCategory(ulong steamId64, string category)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = category switch
        {
            "weapons" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type = 'weapon'",
            "knife" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type IN ('knife', 'knife_type')",
            "gloves" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type = 'glove'",
            "agents" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type = 'agent'",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Invalid reset category.")
        };
        command.Parameters.AddWithValue("@steam_id", steamId64);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
    }

    private MySqlConnection Open()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Upsert(ulong steamId64, string type, string target, string cosmeticId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO astra_player_skin_selections (steam_id, selection_type, target, cosmetic_id)
        VALUES (@steam_id, @selection_type, @target, @cosmetic_id)
        ON DUPLICATE KEY UPDATE cosmetic_id = VALUES(cosmetic_id), updated_at = CURRENT_TIMESTAMP;
        """;
        command.Parameters.AddWithValue("@steam_id", steamId64);
        command.Parameters.AddWithValue("@selection_type", type);
        command.Parameters.AddWithValue("@target", target);
        command.Parameters.AddWithValue("@cosmetic_id", cosmeticId);
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
