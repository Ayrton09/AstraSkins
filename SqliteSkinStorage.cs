using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using AstraSkins.Models;

namespace AstraSkins;

public sealed class SqliteSkinStorage : ISkinStorage
{
    private readonly string _databasePath;
    private readonly ILogger _logger;
    private readonly bool _musicKitMvpCounterEnabled;

    public SqliteSkinStorage(string databasePath, ILogger logger, bool musicKitMvpCounterEnabled = false)
    {
        _databasePath = databasePath;
        _logger = logger;
        _musicKitMvpCounterEnabled = musicKitMvpCounterEnabled;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databasePath)) ?? ".");
        using var connection = Open();
        using var command = connection.CreateCommand();
        const string schema = """
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
        command.CommandText = schema;
        command.ExecuteNonQuery();
        _logger.LogInformation("SQLite storage initialized at {Path}, MusicKitMvpCounter={MusicKitMvpCounter}", _databasePath, _musicKitMvpCounterEnabled);
    }

    public PlayerSkinProfile LoadProfile(ulong steamId64)
    {
        var profile = new PlayerSkinProfile { SteamId64 = steamId64 };
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT selection_type, target, cosmetic_id FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type <> 'music_kit_mvp'";
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));

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
            countCommand.CommandText = "SELECT target, cosmetic_id FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type = 'music_kit_mvp'";
            countCommand.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));
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
        INSERT INTO astra_player_skin_selections (steam_id, selection_type, target, cosmetic_id, updated_at)
        VALUES ($steam_id, 'music_kit_mvp', $target, '1', CURRENT_TIMESTAMP)
        ON CONFLICT(steam_id, selection_type, target)
        DO UPDATE SET cosmetic_id = CAST(MIN(CAST(cosmetic_id AS INTEGER) + 1, 2147483647) AS TEXT), updated_at = CURRENT_TIMESTAMP;
        """;
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));
        command.Parameters.AddWithValue("$target", musicKitId.ToString(CultureInfo.InvariantCulture));
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
        command.CommandText = "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type = $selection_type AND target = $target";
        command.Parameters.AddWithValue("$steam_id", unchecked((long)steamId64));
        command.Parameters.AddWithValue("$selection_type", field);
        command.Parameters.AddWithValue("$target", target);
        command.ExecuteNonQuery();
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
            "weapons" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND (selection_type = 'weapon' OR (selection_type IN ('seed', 'wear', 'nametag', 'stattrak') AND target NOT IN ('knife', 'glove')))",
            "knife" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND (selection_type IN ('knife', 'knife_type') OR (selection_type IN ('seed', 'wear', 'nametag', 'stattrak') AND target = 'knife'))",
            "gloves" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND (selection_type = 'glove' OR (selection_type IN ('seed', 'wear', 'nametag', 'stattrak') AND target = 'glove'))",
            "agents" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type = 'agent'",
            "music" => "DELETE FROM astra_player_skin_selections WHERE steam_id = $steam_id AND selection_type IN ('music_kit', 'music_kit_mvp')",
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
