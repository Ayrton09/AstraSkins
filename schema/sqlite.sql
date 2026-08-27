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

-- Created by the plugin only when EnableStatTrak is true.
CREATE TABLE IF NOT EXISTS astra_music_kit_mvp_counts (
    steam_id INTEGER NOT NULL,
    music_kit_id INTEGER NOT NULL,
    mvp_count INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (steam_id, music_kit_id)
);

CREATE INDEX IF NOT EXISTS idx_astra_music_kit_mvp_counts_steam_id
    ON astra_music_kit_mvp_counts (steam_id);
