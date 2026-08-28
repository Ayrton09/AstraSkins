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
