<div align="center">

# Astra Skins

**Weapon skins, knives, gloves, agents, stickers and charms for Counter-Strike 2 — with a built-in WASD menu, per-player customization, and database-backed persistence.**

[![CS2](https://img.shields.io/badge/game-Counter--Strike%202-orange)](https://www.counter-strike.net/)
[![CounterStrikeSharp](https://img.shields.io/badge/CounterStrikeSharp-%E2%89%A5%201.0.369-blue)](https://github.com/roflmuffin/CounterStrikeSharp)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![CI](https://github.com/Ayrton09/AstraSkins/actions/workflows/ci.yml/badge.svg)](https://github.com/Ayrton09/AstraSkins/actions/workflows/ci.yml)
[![Downloads](https://img.shields.io/github/downloads/Ayrton09/AstraSkins/total?label=downloads&color=brightgreen)](https://github.com/Ayrton09/AstraSkins/releases)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

</div>

---

## Features

- 🎨 **1,400+ weapon skins, 20 knives with 576 finishes, 8 glove types, 63 agents, 99 music kits, 11,000+ stickers, 78 charms** — all data-driven from JSON, no datasets baked into the code.
- 🕹️ **Built-in WASD menu** — navigate with `W`/`S`, select with `E`. No external menu plugin required.
- 🔧 **Per-player customization** — custom paint seed, wear/float, name tags, and StatTrak counters via `!seed`, `!wear`, `!nametag`, and `!stattrak`; name tags and StatTrak work on the default skin too.
- 🔎 **Search** — `!ws <text>` finds any skin, knife, glove, agent, or music kit without scrolling through pages.
- 🏷️ **Stickers and charms** — up to five stickers and a charm on every gun, with or without a skin, browsed by tournament and capsule or found with `!stickers <text>` and `!charms <text>`.
- 🎵 **Music kits** — pick any of 99 kits from the menu, with an optional per-kit MVP counter shown on the scoreboard.
- 💾 **Persistent selections** — SQLite or MySQL, keyed by SteamID64. Selections survive reconnects, map changes, and restarts.
- 🌍 **7 languages** — per-player localization (English, Spanish, Chinese, Portuguese, German, French, Russian). Chinese players also get skin, knife, glove, agent, category, and music kit names in Chinese, and can search in either language.
- 🎬 **Team intro** shows your agent, gloves, and weapon skins on the match intro and on the team select screen once you are on a team (the very first team select after connecting has no owner assigned by the engine, so it keeps the defaults).
- 🗣️ **Agent radio voices** — agents keep their voice lines where the CS2 schema exposes the voice data.
- 🤖 **Bot takeover aware** leaves a possessed bot's loadout alone by default, so bot cosmetics plugins keep working. Opt in to see your own skins on the bot instead.
- 🛡️ **Permission gating** — restrict individual skins, knives, gloves, agents, stickers, charms, or a whole module (weapons, knives, gloves, agents, music kits, stickers, charms, customization) to admin flags.
- 🧩 **Modules and commands** — every module has its own on/off switch, so a server can run only the parts it wants, and every chat command name is configurable, with any number of aliases.
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

The names below are the defaults. Every command can be renamed or given extra aliases in the `Commands` section of the config; usage messages show the configured name.

### Players

| Command | Description |
| --- | --- |
| `!ws` | Open the main skins menu |
| `!ws <search>` | Search every skin, knife, glove, and agent at once |
| `!knife` | Open the knife menu |
| `!gloves` | Open the gloves menu |
| `!agents` | Open the agents menu |
| `!stickers` | Sticker slots of the held gun (weapon picker when nothing usable is held) |
| `!stickers <search>` | Search every sticker for the held gun, then pick the slot |
| `!charms` | Charm of the held gun · `!keychains` works too |
| `!charms <search>` | Search every charm for the held gun |
| `!wsrefresh` | Reapply saved selections |
| `!wsreset [all\|weapons\|knife\|gloves\|agents\|music\|stickers\|charms]` | Reset saved selections, all or per category |

### Customization

| Command | Description |
| --- | --- |
| `!seed <0-1000>` | Custom paint seed for the held weapon · `!seed gloves <n>` for gloves · `!seed reset` to clear |
| `!wear <0.00-1.00>` | Custom wear/float for the held weapon · `!wear gloves <n>` for gloves · `!wear reset` to clear |
| `!nametag <text>` | Name tag for the held weapon · `!nametag reset` to remove |
| `!stattrak` | Toggle StatTrak on the held weapon · `!stattrak <count>` sets the counter · `!stattrak reset` removes it |

Overrides apply on top of the selected skin, take effect instantly, and persist in the database. They target the weapon currently held (knife included); pass `gloves` as the first argument to target equipped gloves instead. Seed and wear need a skin selected for the item; name tags and StatTrak also work on the default skin of any gun or the knife.

StatTrak works the same way: enable it on a weapon or knife and the counter goes up with every kill you get with that item, persisting across reconnects and map changes. With `EnableStatTrakByDefault` on, every weapon and knife with a selected skin starts counting at 0 without asking, and `!stattrak reset` turns it off for that item.

> **Tip:** seeds only change finishes whose pattern placement varies — Case Hardened, Crimson Web, Marble Fade, Fade. Most other skins look identical on every seed.

### Admin

| Command | Default permission | Description |
| --- | --- | --- |
| `!wsreload` | `@css/config` | Reload the JSON definitions and reapply skins to everyone |
| `!wsdebug` | `@css/config` | Diagnostics: load counts, database mode, and the caller's selections |
| `css_wsresetplayer <steamid64> [all\|weapons\|knife\|gloves\|agents\|music\|stickers\|charms]` | `@css/config` | Reset a player's selections by SteamID64, connected or not (server console or admin) |

Both can be disabled entirely in the config.

## Menu Controls

| Key | Action |
| --- | --- |
| `W` / `S` | Move up / down |
| `E` | Select |
| `Shift` | Back |
| `R` | Close |

The menu items are numbered as a visual guide for orientation; navigation is by keys, not numbers. While the menu is open the player is held in place. With `Menu.CloseOnRoundStart` on, every open menu closes when a new round starts. Heads up: `E` still performs its normal in-world action (open doors, pick up weapons, defuse), so avoid confirming a selection while standing on the bomb.

Every skin, knife, glove, agent and music kit list starts with a **Default** row that removes the selection and is marked when nothing is selected: the stock finish of a gun or knife (stickers, charms, name tags and counters saved for it stay on), the default knife, the default gloves, the default agent (back on the next spawn) and the default music kit.

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

## Music Kits

The main menu has a **Music Kit** category with every selectable kit and a "Default Music Kit" entry to go back to the stock music. The selected kit plays at the usual cues (round start, MVP anthem, bomb planted) and persists like any other selection.

With `EnableMusicKitMvpCounter` set to `true`, the plugin also tracks how many MVPs each player earns with their selected kit and shows the count on the MVP scoreboard panel, like StatTrak music kits in the official game. The counter persists in the database and is cleared by `!wsreset music` or a full reset.

If `data/music_kits.json` is missing, the category simply stays hidden.

## Stickers and Charms

Every gun takes up to five stickers and one charm, on top of its skin or on the stock finish. The main menu has **Stickers** and **Charms** entries that start with the guns in hand and continue with the full catalog by category; `!stickers` and `!charms` jump straight to the held gun. Stickers are grouped by tournament, newest first, and then by the capsule of that event, with the other capsules and collections after them; charms are grouped by collection. Each slot row shows what is on it, and a slot that has a sticker offers a remove entry.

With 11,000 stickers the search is the fast way in: `!stickers <text>` lists the matching stickers for the held gun and asks for the slot, `!charms <text>` does the same for charms. Every whitespace-separated term has to match, on the sticker name or its event:

```text
!stickers natus holo    → Natus Vincere holo stickers from every event
!stickers ropz gold     → ropz gold autographs
!stickers donk holo     → donk holo autographs
!charms lil ava         → the Lil' Ava charm
```

Both features have their own `Enabled` switch and `Permission` flag in the config (the `Stickers` and `Keychains` sections), separate from the customization commands, and every entry in `data/stickers.json` and `data/keychains.json` accepts the usual `permission` field. Selections persist like everything else and are cleared with `!wsreset stickers`, `!wsreset charms`, or `!wsreset weapons`. If a data file is missing, its menu entry stays hidden.

Stickers use the game's stock placement, size and wear for each slot; there is no scraping or moving. The game only rebuilds a gun's first-person finish when its wear changes, so a gun that has had stickers gets its wear shifted by a small amount (0.006 per change, 0.06 at most), invisible on the finish; guns that never had stickers keep their exact wear.

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
    "AllowWhileDead": true,
    "CloseOnRoundStart": false
  },
  "Customization": {
    "Enabled": true,
    "Permission": "",
    "MaxNameTagLength": 20,
    "BlockedNameTagWords": []
  },
  "Weapons": {
    "Enabled": true,
    "Permission": ""
  },
  "Knives": {
    "Enabled": true,
    "Permission": ""
  },
  "Gloves": {
    "Enabled": true,
    "Permission": ""
  },
  "Agents": {
    "Enabled": true,
    "Permission": ""
  },
  "MusicKits": {
    "Enabled": true,
    "Permission": ""
  },
  "Stickers": {
    "Enabled": true,
    "Permission": ""
  },
  "Keychains": {
    "Enabled": true,
    "Permission": ""
  },
  "Commands": {
    "Menu": ["css_ws"],
    "Knife": ["css_knife"],
    "Gloves": ["css_gloves"],
    "Agents": ["css_agents"],
    "Stickers": ["css_stickers"],
    "Charms": ["css_charms", "css_keychains", "css_keychain"],
    "Refresh": ["css_wsrefresh"],
    "Reset": ["css_wsreset"],
    "Seed": ["css_seed"],
    "Wear": ["css_wear"],
    "NameTag": ["css_nametag"],
    "StatTrak": ["css_stattrak"],
    "Reload": ["css_wsreload"],
    "Debug": ["css_wsdebug"],
    "ResetPlayer": ["css_wsresetplayer"]
  },
  "ApplyPlayerCosmeticsOnBotTakeover": false,
  "EnableStatTrakByDefault": false,
  "EnableMusicKitMvpCounter": false,
  "Definitions": {
    "Weapons": "data/weapons.json",
    "Knives": "data/knives.json",
    "Gloves": "data/gloves.json",
    "Agents": "data/agents.json",
    "MusicKits": "data/music_kits.json",
    "Stickers": "data/stickers.json",
    "Keychains": "data/keychains.json",
    "Categories": "data/categories.json"
  },
  "EnableAdminReloadCommand": true,
  "AdminReloadPermission": "@css/config",
  "EnableAdminDebugCommand": true,
  "AdminDebugPermission": "@css/config",
  "EnableAdminResetCommand": true,
  "AdminResetPermission": "@css/config"
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
| `Menu.CloseOnRoundStart` | Close every open menu when a new round starts, so the overlay does not sit over the round start. Off by default, since the end of a round is when most players open the menu |
| `Customization.Enabled` | Master switch for `!seed` / `!wear` / `!nametag` |
| `Customization.Permission` | Restrict customization to a flag; empty = everyone |
| `Customization.MaxNameTagLength` | Name tag cap, 4–32 (default 20 matches the real game) |
| `Customization.BlockedNameTagWords` | Words a name tag may not contain, matched as case-insensitive substrings. Empty by default, each server adds its own, for example `["badword", "slur"]`. Avoid short or common words (`puta` would also block `computadora`) |
| `Weapons`, `Knives`, `Gloves`, `Agents`, `MusicKits`, `Stickers`, `Keychains` | One section per module, each with `Enabled` and `Permission`. Off hides the module from the menu and the search, its commands answer that it is unavailable, and saved selections stop applying (they stay in the database and come back when it is switched on). The flag does the same for players who do not hold it; empty = everyone |
| `Commands` | Chat command names, a list per command; the first name is the one shown in usage messages. Names are lower-cased and get the `css_` prefix when it is missing, so `"kch"` registers `css_kch` and answers to `!kch` and `/kch`. An empty list leaves that command unregistered; the same name on two commands is rejected at startup |
| `ApplyPlayerCosmeticsOnBotTakeover` | Off by default: a bot you take over keeps its own loadout. Set to `true` to apply your knife, agent and music kit to the possessed bot (guns and gloves already in hand keep their look) |
| `EnableStatTrakByDefault` | Every weapon and knife with a selected skin starts with a StatTrak counter at 0; players can still turn it off per item with `!stattrak reset`. That counter belongs to the selection: while the selection is not applied (module off, flag lost) it stays hidden, and it comes back with it |
| `EnableMusicKitMvpCounter` | Track per-player MVP counts for selected music kits |

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
| Music kits | 99 |
| Stickers | 11,134 |
| Charms | 78 |

To regenerate the data after a CS2 update, run the included generator — it pulls the latest `items_game.txt` and translation data automatically:

```bash
python tools/generate_definitions.py --output data
```

### Permissions

Every entry in the data files accepts an optional `permission` field: weapon, knife and glove skins (the `skins` arrays), knife and glove types, agents, music kits, stickers and charms. The shipped files do not include it, so everything is open by default. Add it to the entries you want to restrict:

```json
{
  "id": "weapon_ak47:801",
  "displayName": "Asiimov",
  "paintKit": 801,
  "permission": "@css/vip"
}
```

Any CounterStrikeSharp flag (`@css/vip`) or group (`#css/vip`) works, and `@css/root` passes everything. The flag is per entry, so different entries can require different flags, and a flag on a knife or glove type covers all of its skins. Players without the flag do not see the entry in the menu or in the `!ws` search and cannot select it. The same check runs when cosmetics are applied, so a saved selection whose flag expired stops applying at the next spawn (music kits within a tick) and comes back as soon as the flag does. Flags are read live, nothing is cached per player.

`Customization.Permission` in `config.json` is separate: it gates the `!seed`, `!wear`, `!nametag` and `!stattrak` commands. The module sections (`Weapons`, `Knives`, `Gloves`, `Agents`, `MusicKits`, `Stickers`, `Keychains`) each carry their own `Permission` that gates the whole module the same way, menu and apply side alike, and an `Enabled` switch that turns it off for everyone. The plugin does not grant flags itself; use `configs/admins.json` or any VIP/rank plugin that assigns flags.

The generator rewrites the data files, so re-apply your permissions after regenerating.

## Gamedata

Copy `gamedata/astra_skins.json` to `addons/counterstrikesharp/gamedata/`. It contains the single memory signature used to apply paint attributes visually.

CS2 updates can break this signature. When that happens the plugin keeps running and logs a clear error instead of crashing — update the signature (or grab an updated release) to restore skin rendering.

## Building from Source

```bash
dotnet build -c Release
```

Requires the .NET 10 SDK. Deployable output lands in `bin/Release/net10.0/`.

The release zip does not ship the Linux SQLite library from the NuGet package, which needs glibc 2.34 and fails to load on older host images (Debian 11, Ubuntu 20.04). `scripts/build_sqlite_linux.sh` builds the same SQLite, with the same options, against glibc 2.28 using zig, and `scripts/package.sh` puts that build in the zip; CI runs both.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Skins never apply, no errors | Set `FollowCS2ServerGuidelines: false` in `configs/core.json` and restart |
| "gamedata signature is missing" in logs | Copy `gamedata/astra_skins.json` to `addons/counterstrikesharp/gamedata/` |
| Skins stopped working after a CS2 update | The gamedata signature broke — see [Gamedata](#gamedata) |
| `!wsreload` / `!wsdebug` say no permission | Add your SteamID to `configs/admins.json` with the `@css/config` flag |
| `!seed` looks like it does nothing | The held skin's pattern doesn't vary by seed — try Case Hardened or Crimson Web |
| `GLIBC_2.3x not found` when loading in sqlite mode | Releases before 1.1.2 shipped a SQLite library that needs glibc 2.34; update, the current one loads on any host that can run CS2 |
| A menu entry or a command is missing | Its module is switched off or flag-gated in the config (`Weapons`, `Knives`, `Gloves`, `Agents`, `MusicKits`, `Stickers`, `Keychains`), or the command was renamed in `Commands`; `!wsdebug` lists the module states |

## Disclaimer

Server-side skin plugins conflict with Valve's [server guidelines](https://blog.counter-strike.net/index.php/server_guidelines/). Running this on a public server with a GSLT carries a token-ban risk that you accept as the operator. Use at your own discretion.

## License

[MIT](LICENSE) © Ayrton
