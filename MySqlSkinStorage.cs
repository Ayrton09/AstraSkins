using System.Globalization;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class MySqlSkinStorage : ISkinStorage
{
    private readonly string _connectionString;
    private readonly ILogger _logger;
    private readonly bool _musicKitMvpCounterEnabled;

    public MySqlSkinStorage(Models.MySqlConfig config, ILogger logger, bool musicKitMvpCounterEnabled = false)
    {
        _logger = logger;
        _musicKitMvpCounterEnabled = musicKitMvpCounterEnabled;
        var sslMode = ParseSslMode(config.SslMode);
        var builder = new MySqlConnectionStringBuilder
        {
            Server = config.Host,
            Port = (uint)config.Port,
            Database = config.Database,
            UserID = config.Username,
            Password = config.Password,
            SslMode = sslMode,
            // RSA public key retrieval over an unencrypted channel can be
            // intercepted; only allow it when the connection may be plaintext.
            AllowPublicKeyRetrieval = sslMode is MySqlSslMode.None or MySqlSslMode.Preferred,
            TreatTinyAsBoolean = true
        };
        _connectionString = builder.ConnectionString;
    }

    private static MySqlSslMode ParseSslMode(string sslMode)
    {
        return sslMode.Trim().ToLowerInvariant() switch
        {
            "none" => MySqlSslMode.None,
            "preferred" => MySqlSslMode.Preferred,
            "verifyca" => MySqlSslMode.VerifyCA,
            "verifyfull" => MySqlSslMode.VerifyFull,
            _ => MySqlSslMode.Required
        };
    }

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        const string schema = """
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
        command.CommandText = schema;
        command.ExecuteNonQuery();
        _logger.LogInformation("MySQL storage initialized, MusicKitMvpCounter={MusicKitMvpCounter}.", _musicKitMvpCounterEnabled);
    }

    public PlayerSkinProfile LoadProfile(ulong steamId64)
    {
        var profile = new PlayerSkinProfile { SteamId64 = steamId64 };
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT selection_type, target, cosmetic_id FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type <> 'music_kit_mvp'";
        command.Parameters.AddWithValue("@steam_id", steamId64);

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                ApplyRow(profile, reader.GetString(0), reader.GetString(1), reader.GetString(2));
            }
        }

        if (_musicKitMvpCounterEnabled)
        {
            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT target, cosmetic_id FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type = 'music_kit_mvp'";
            countCommand.Parameters.AddWithValue("@steam_id", steamId64);
            using var countReader = countCommand.ExecuteReader();
            while (countReader.Read())
            {
                if (int.TryParse(countReader.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var musicKitId) &&
                    int.TryParse(countReader.GetString(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                {
                    profile.MusicKitMvpCounts[musicKitId] = Math.Max(0, count);
                }
            }
        }

        return profile;
    }

    public void IncrementMusicKitMvp(ulong steamId64, int musicKitId)
    {
        if (!_musicKitMvpCounterEnabled)
        {
            return;
        }

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO astra_player_skin_selections (steam_id, selection_type, target, cosmetic_id)
        VALUES (@steam_id, 'music_kit_mvp', @target, '1')
        ON DUPLICATE KEY UPDATE cosmetic_id = CAST(LEAST(CAST(cosmetic_id AS UNSIGNED) + 1, 2147483647) AS CHAR), updated_at = CURRENT_TIMESTAMP;
        """;
        command.Parameters.AddWithValue("@steam_id", steamId64);
        command.Parameters.AddWithValue("@target", musicKitId.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
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

    public void SaveCustomization(ulong steamId64, string field, string target, string value)
    {
        Upsert(steamId64, field, target, value);
    }

    public void ClearCustomization(ulong steamId64, string field, string target)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type = @selection_type AND target = @target";
        command.Parameters.AddWithValue("@steam_id", steamId64);
        command.Parameters.AddWithValue("@selection_type", field);
        command.Parameters.AddWithValue("@target", target);
        command.ExecuteNonQuery();
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
            "weapons" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND (selection_type = 'weapon' OR (selection_type IN ('seed', 'wear', 'nametag', 'stattrak') AND target NOT IN ('knife', 'glove')))",
            "knife" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND (selection_type IN ('knife', 'knife_type') OR (selection_type IN ('seed', 'wear', 'nametag', 'stattrak') AND target = 'knife'))",
            "gloves" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND (selection_type = 'glove' OR (selection_type IN ('seed', 'wear', 'nametag', 'stattrak') AND target = 'glove'))",
            "agents" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type = 'agent'",
            "music" => "DELETE FROM astra_player_skin_selections WHERE steam_id = @steam_id AND selection_type IN ('music_kit', 'music_kit_mvp')",
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
        // Referencing the bound parameter instead of VALUES(cosmetic_id) avoids
        // the MySQL 8.0.20+ deprecation while staying compatible with MariaDB,
        // which does not support the row-alias (AS new) replacement syntax.
        command.CommandText = """
        INSERT INTO astra_player_skin_selections (steam_id, selection_type, target, cosmetic_id)
        VALUES (@steam_id, @selection_type, @target, @cosmetic_id)
        ON DUPLICATE KEY UPDATE cosmetic_id = @cosmetic_id, updated_at = CURRENT_TIMESTAMP;
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
        else if (type.Equals("music_kit", StringComparison.OrdinalIgnoreCase))
        {
            profile.MusicKitId = cosmeticId;
        }
        else if (type.Equals("music_kit_mvp", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var musicKitId) &&
                 int.TryParse(cosmeticId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mvpCount))
        {
            profile.MusicKitMvpCounts[musicKitId] = Math.Max(0, mvpCount);
        }
        else if (type.Equals("seed", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(cosmeticId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            GetOrAddCustomization(profile, target).Seed = seed;
        }
        else if (type.Equals("wear", StringComparison.OrdinalIgnoreCase) &&
                 float.TryParse(cosmeticId, NumberStyles.Float, CultureInfo.InvariantCulture, out var wear))
        {
            GetOrAddCustomization(profile, target).Wear = wear;
        }
        else if (type.Equals("nametag", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddCustomization(profile, target).NameTag = cosmeticId;
        }
        else if (type.Equals("stattrak", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(cosmeticId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var statTrak))
        {
            GetOrAddCustomization(profile, target).StatTrak = statTrak;
        }
    }

    private static WeaponCustomization GetOrAddCustomization(PlayerSkinProfile profile, string target)
    {
        if (!profile.Customizations.TryGetValue(target, out var customization))
        {
            customization = new WeaponCustomization();
            profile.Customizations[target] = customization;
        }

        return customization;
    }

}
