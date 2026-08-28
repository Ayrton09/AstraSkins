CREATE TABLE IF NOT EXISTS astra_player_skin_selections (
    steam_id BIGINT UNSIGNED NOT NULL,
    selection_type VARCHAR(16) NOT NULL,
    target VARCHAR(64) NOT NULL,
    cosmetic_id VARCHAR(128) NOT NULL,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (steam_id, selection_type, target),
    INDEX idx_astra_player_skin_selections_steam_id (steam_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
