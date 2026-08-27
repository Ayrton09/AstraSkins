CREATE TABLE IF NOT EXISTS astra_player_skin_selections (
    steam_id BIGINT UNSIGNED NOT NULL,
    selection_type VARCHAR(16) NOT NULL,
    target VARCHAR(64) NOT NULL,
    cosmetic_id VARCHAR(128) NOT NULL,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (steam_id, selection_type, target),
    INDEX idx_astra_player_skin_selections_steam_id (steam_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Created by the plugin only when EnableStatTrak is true.
CREATE TABLE IF NOT EXISTS astra_music_kit_mvp_counts (
    steam_id BIGINT UNSIGNED NOT NULL,
    music_kit_id INT UNSIGNED NOT NULL,
    mvp_count BIGINT UNSIGNED NOT NULL DEFAULT 0,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (steam_id, music_kit_id),
    INDEX idx_astra_music_kit_mvp_counts_steam_id (steam_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
