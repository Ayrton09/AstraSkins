<div align="center">

# Astra Skins

**Weapon skins, knives, gloves, and agents for Counter-Strike 2 — with a built-in WASD menu, per-player customization, and database-backed persistence.**

[![CS2](https://img.shields.io/badge/game-Counter--Strike%202-orange)](https://www.counter-strike.net/)
[![CounterStrikeSharp](https://img.shields.io/badge/CounterStrikeSharp-%E2%89%A5%201.0.369-blue)](https://github.com/roflmuffin/CounterStrikeSharp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/Ayrton09/AstraSkins/actions/workflows/ci.yml/badge.svg)](https://github.com/Ayrton09/AstraSkins/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

</div>

---

## Features

- 🎨 **1,400+ weapon skins, 20 knives with 576 finishes, 8 glove types, 63 agents** — all data-driven from JSON, no datasets baked into the code.
- 🕹️ **Built-in WASD menu** — navigate with `W`/`S`, select with `E`. No external menu plugin required.
- 🔧 **Per-player customization** — custom paint seed, wear/float, name tags, and StatTrak counters via `!seed`, `!wear`, `!nametag`, and `!stattrak`.
- 🔎 **Search** — `!ws <text>` finds any skin, knife, glove, or agent without scrolling through pages.
- 💾 **Persistent selections** — SQLite or MySQL, keyed by SteamID64. Selections survive reconnects, map changes, and restarts.
- 🌍 **7 languages** — per-player localization (English, Spanish, Chinese, Portuguese, German, French, Russian).
- 🗣️ **Agent radio voices** — agents keep their voice lines where the CS2 schema exposes the voice data.
- 🛡️ **Permission gating** — restrict individual skins, knives, gloves, agents, or the whole customization feature to admin flags.
- ⚙️ **Admin tooling** — hot reload of definitions and a diagnostics command.

## Requirements

- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) `1.0.369` or newer (with Metamod:Source), running on `.NET 10`.
- SQLite (zero setup) or a MySQL server, selected explicitly in the config.

## Installation

1. Install [Metamod:Source](https://www.sourcemm.net/) and [CounterStrikeSharp](https://docs.cssharp.dev/docs/guides/getting-started.html) on your CS2 dedicated server.
2. **Required:** edit `addons/counterstrikesharp/configs/core.json` and set:

   ```json
   "FollowCS2ServerGuidelines": false
   ```

   Without this, CounterStrikeSharp blocks the item-property writes this plugin needs and **no skins will apply**.
3. Copy the plugin files using this layout:

   ```text
   addons/
     counterstrikesharp/
       plugins/
         AstraSkins/
           AstraSkins.dll
           AstraSkins.deps.json
           data/            ← weapons, knives, gloves, agents, categories JSON
           lang/            ← translations
           schema/          ← reference SQL (the plugin creates tables itself)
       gamedata/
         astra_skins.json   ← required, see Gamedata below
       configs/
         plugins/
           AstraSkins/
             AstraSkins.json
   ```

4. Configure the database in `configs/plugins/AstraSkins/AstraSkins.json` (see [Configuration](#configuration)). SQLite works out of the box; MySQL needs an existing database and user.
5. Restart the server (or `css_plugins load AstraSkins`). The startup log reports how many skins, knives, gloves, and agents were loaded.

## Commands

### Players

| Command | Description |
| --- | --- |
| `!ws` | Open the main skins menu |
| `!ws <search>` | Search every skin, knife, glove, and agent at once |
| `!knife` | Open the knife menu |
| `!gloves` | Open the gloves menu |
| `!agents` | Open the agents menu |
| `!wsrefresh` | Reapply saved selections |
| `!wsreset [all\|weapons\|knife\|gloves\|agents]` | Reset saved selections, all or per category |

### Customization

| Command | Description |
| --- | --- |
| `!seed <0-1000>` | Custom paint seed for the held weapon · `!seed gloves <n>` for gloves · `!seed reset` to clear |
| `!wear <0.00-1.00>` | Custom wear/float for the held weapon · `!wear gloves <n>` for gloves · `!wear reset` to clear |
| `!nametag <text>` | Name tag for the held weapon · `!nametag reset` to remove |
| `!stattrak` | Toggle StatTrak on the held weapon · `!stattrak <count>` sets the counter · `!stattrak reset` removes it |

Overrides apply on top of the selected skin, take effect instantly, and persist in the database. They target the weapon currently held (knife included); pass `gloves` as the first argument to target equipped gloves instead. A skin must be selected for the item first.

StatTrak works the same way: enable it on a weapon or knife and the counter goes up with every kill you get with that item, persisting across reconnects and map changes.

> **Tip:** seeds only change finishes whose pattern placement varies — Case Hardened, Crimson Web, Marble Fade, Fade. Most other skins look identical on every seed.

### Admin

| Command | Default permission | Description |
| --- | --- | --- |
| `!wsreload` | `@css/config` | Reload the JSON definitions and reapply skins to everyone |
| `!wsdebug` | `@css/config` | Diagnostics: load counts, database mode, and the caller's selections |

Both can be disabled entirely in the config.

## Menu Controls

| Key | Action |
| --- | --- |
| `W` / `S` | Move up / down |
| `E` | Select |
| `Shift` | Back |
| `R` | Close |

The menu items are numbered as a visual guide for orientation; navigation is by keys, not numbers. While the menu is open the player is held in place. Heads up: `E` still performs its normal in-world action (open doors, pick up weapons, defuse), so avoid confirming a selection while standing on the bomb.

## Search

With 1,449 weapon skins alone, scrolling is not always the fastest way in. `!ws <search>` opens a flat result list spanning weapon skins, knife finishes, glove finishes, and agents.

Every whitespace-separated term has to appear in the entry, so you can narrow down quickly:

```text
!ws redline        → Redline on every weapon that has it
!ws ak redline     → straight to the AK-47 Redline
!ws marble fade    → every Marble Fade knife
!ws ct mccoy       → the CT agent
```

Results respect permissions and are capped at 64 entries; already-equipped items are marked with `*`.

## Configuration

`configs/plugins/AstraSkins/AstraSkins.json` — the defaults are safe to publish and use placeholder credentials:

```json
{
  "ConfigVersion": 1,
  "DatabaseMode": "mysql",
  "Sqlite": {
    "Path": "data/astra_skins.sqlite"
  },
  "MySql": {
    "Host": "127.0.0.1",
    "Port": 3306,
    "Database": "astra_skins",
    "Username": "astra_skins",
    "Password": "change-me",
    "SslMode": "required"
  },
  "Menu": {
    "ItemsPerPage": 6,
    "TimeoutSeconds": 25,
    "CooldownMilliseconds": 180,
    "SelectionCooldownMilliseconds": 900,
    "AllowWhileDead": true
  },
  "Customization": {
    "Enabled": true,
    "Permission": "",
    "MaxNameTagLength": 20
  },
  "Definitions": {
    "Weapons": "data/weapons.json",
    "Knives": "data/knives.json",
    "Gloves": "data/gloves.json",
    "Agents": "data/agents.json",
    "Categories": "data/categories.json"
  },
  "EnableAdminReloadCommand": true,
  "AdminReloadPermission": "@css/config",
  "EnableAdminDebugCommand": true,
  "AdminDebugPermission": "@css/config"
}
```

| Key | What it does |
| --- | --- |
| `DatabaseMode` | `"sqlite"` or `"mysql"` — required, validated at startup |
| `Menu.ItemsPerPage` | Visible menu rows (3–6) |
| `Menu.TimeoutSeconds` | Menu auto-closes after this many idle seconds |
| `Menu.CooldownMilliseconds` | Minimum delay between menu key presses |
| `Menu.SelectionCooldownMilliseconds` | Minimum delay between skin selections |
| `Menu.AllowWhileDead` | Allow opening the menu while dead |
| `Customization.Enabled` | Master switch for `!seed` / `!wear` / `!nametag` |
| `Customization.Permission` | Restrict customization to a flag; empty = everyone |
| `Customization.MaxNameTagLength` | Name tag cap, 4–32 (default 20 matches the real game) |

### SQLite

```json
{ "DatabaseMode": "sqlite", "Sqlite": { "Path": "data/astra_skins.sqlite" } }
```

The plugin creates the schema on startup — nothing to install. Note the default path lives inside the plugin folder: **back up the `.sqlite` file before redeploying the plugin directory**, or point `Path` somewhere outside it.

### MySQL

```json
{ "DatabaseMode": "mysql", "MySql": { "Host": "…", "Port": 3306, "Database": "astra_skins", "Username": "astra_skins", "Password": "…", "SslMode": "required" } }
```

The database and user must already exist; the plugin creates its table on startup. `SslMode` accepts `none`, `preferred`, `required` (default), `verifyca`, or `verifyfull` — keep `required` for remote databases so credentials travel encrypted; use `preferred` or `none` only if your MySQL server has TLS disabled.

## Localization

Every player-facing message and menu label is localized per player through CounterStrikeSharp's language system. Players pick their language with `css_lang <language>` (e.g. `css_lang es`); missing translations fall back to the server language.

Shipped: `en` English · `es` Spanish · `zh` Chinese (Simplified) · `pt` Portuguese · `de` German · `fr` French · `ru` Russian — flat key/value JSON files in `lang/`.

Wrong or missing translation? PRs welcome — edit the matching `lang/*.json`. To add a language, copy `en.json` to `<culture>.json` and translate the values.

## Cosmetic Data

All cosmetic content lives in `data/*.json` and is validated at startup and on `!wsreload` — malformed JSON, duplicate IDs, unknown weapon entities, missing fields, and broken category references are skipped with clear log messages.

Currently packaged:

| Type | Count |
| --- | ---: |
| Weapons | 34 |
| Weapon skins | 1,449 |
| Knives | 20 |
| Knife skins | 576 |
| Glove types | 8 |
| Glove skins | 94 |
| Agents | 63 |

To regenerate the data after a CS2 update, run the included generator — it pulls the latest `items_game.txt` and translation data automatically:

```bash
python tools/generate_definitions.py --output data
```

## Gamedata

Copy `gamedata/astra_skins.json` to `addons/counterstrikesharp/gamedata/`. It contains the single memory signature used to apply paint attributes visually.

CS2 updates can break this signature. When that happens the plugin keeps running and logs a clear error instead of crashing — update the signature (or grab an updated release) to restore skin rendering.

## Building from Source

```bash
dotnet build -c Release
```

Requires the .NET 10 SDK. Deployable output lands in `bin/Release/net10.0/`.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Skins never apply, no errors | Set `FollowCS2ServerGuidelines: false` in `configs/core.json` and restart |
| "gamedata signature is missing" in logs | Copy `gamedata/astra_skins.json` to `addons/counterstrikesharp/gamedata/` |
| Skins stopped working after a CS2 update | The gamedata signature broke — see [Gamedata](#gamedata) |
| `!wsreload` / `!wsdebug` say no permission | Add your SteamID to `configs/admins.json` with the `@css/config` flag |
| `!seed` looks like it does nothing | The held skin's pattern doesn't vary by seed — try Case Hardened or Crimson Web |

## Disclaimer

Server-side skin plugins conflict with Valve's [server guidelines](https://blog.counter-strike.net/index.php/server_guidelines/). Running this on a public server with a GSLT carries a token-ban risk that you accept as the operator. Use at your own discretion.

## License

[MIT](LICENSE) © Ayrton
