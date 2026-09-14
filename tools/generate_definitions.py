#!/usr/bin/env python3
import argparse
import json
import re
import sys
from pathlib import Path
from urllib.request import urlopen

ITEMS_GAME_URL = "https://raw.githubusercontent.com/SteamDatabase/GameTracking-CS2/master/game/csgo/pak01_dir/scripts/items/items_game.txt"
CSGO_ENGLISH_URL = "https://raw.githubusercontent.com/SteamDatabase/GameTracking-CS2/master/game/csgo/pak01_dir/resource/csgo_english.txt"
SKINS_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/skins.json"
AGENTS_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/agents.json"
SKINS_ZH_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/zh-CN/skins.json"
AGENTS_ZH_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/zh-CN/agents.json"
MUSIC_KITS_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/music_kits.json"
MUSIC_KITS_ZH_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/zh-CN/music_kits.json"
STICKERS_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/stickers.json"
STICKERS_ZH_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/zh-CN/stickers.json"
KEYCHAINS_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/keychains.json"
KEYCHAINS_ZH_API_URL = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/zh-CN/keychains.json"

WEAPON_DISPLAY = {
    "weapon_ak47": ("rifles", "AK-47"),
    "weapon_aug": ("rifles", "AUG"),
    "weapon_awp": ("rifles", "AWP"),
    "weapon_famas": ("rifles", "FAMAS"),
    "weapon_g3sg1": ("rifles", "G3SG1"),
    "weapon_galilar": ("rifles", "Galil AR"),
    "weapon_m4a1": ("rifles", "M4A4"),
    "weapon_m4a1_silencer": ("rifles", "M4A1-S"),
    "weapon_scar20": ("rifles", "SCAR-20"),
    "weapon_sg556": ("rifles", "SG 553"),
    "weapon_ssg08": ("rifles", "SSG 08"),
    "weapon_bizon": ("smgs", "PP-Bizon"),
    "weapon_mac10": ("smgs", "MAC-10"),
    "weapon_mp5sd": ("smgs", "MP5-SD"),
    "weapon_mp7": ("smgs", "MP7"),
    "weapon_mp9": ("smgs", "MP9"),
    "weapon_p90": ("smgs", "P90"),
    "weapon_ump45": ("smgs", "UMP-45"),
    "weapon_mag7": ("heavy", "MAG-7"),
    "weapon_m249": ("heavy", "M249"),
    "weapon_negev": ("heavy", "Negev"),
    "weapon_nova": ("heavy", "Nova"),
    "weapon_sawedoff": ("heavy", "Sawed-Off"),
    "weapon_xm1014": ("heavy", "XM1014"),
    "weapon_deagle": ("pistols", "Desert Eagle"),
    "weapon_elite": ("pistols", "Dual Berettas"),
    "weapon_fiveseven": ("pistols", "Five-SeveN"),
    "weapon_glock": ("pistols", "Glock-18"),
    "weapon_hkp2000": ("pistols", "P2000"),
    "weapon_usp_silencer": ("pistols", "USP-S"),
    "weapon_p250": ("pistols", "P250"),
    "weapon_cz75a": ("pistols", "CZ75-Auto"),
    "weapon_revolver": ("pistols", "R8 Revolver"),
    "weapon_tec9": ("pistols", "Tec-9"),
}

CATEGORIES = [
    {"id": "pistols", "displayName": "Pistols", "displayNameZh": "手枪", "order": 10, "enabled": True},
    {"id": "smgs", "displayName": "SMGs", "displayNameZh": "微型冲锋枪", "order": 20, "enabled": True},
    {"id": "rifles", "displayName": "Rifles", "displayNameZh": "步枪", "order": 30, "enabled": True},
    {"id": "heavy", "displayName": "Heavy", "displayNameZh": "重型武器", "order": 40, "enabled": True},
]


class VdfParser:
    def __init__(self, text):
        self.tokens = re.findall(r'"(?:\\.|[^"])*"|[{}]', self._strip_comments(text))
        self.index = 0

    def parse(self):
        result = {}
        while self.index < len(self.tokens):
            key = self._read_string()
            if key is None:
                break
            value = self._read_value()
            result[key] = self._merge(result.get(key), value)
        return result

    def _read_value(self):
        if self.index < len(self.tokens) and self.tokens[self.index] == "{":
            self.index += 1
            obj = {}
            while self.index < len(self.tokens) and self.tokens[self.index] != "}":
                key = self._read_string()
                if key is None:
                    break
                value = self._read_value()
                obj[key] = self._merge(obj.get(key), value)
            if self.index < len(self.tokens) and self.tokens[self.index] == "}":
                self.index += 1
            return obj
        return self._read_string() or ""

    def _read_string(self):
        if self.index >= len(self.tokens):
            return None
        token = self.tokens[self.index]
        self.index += 1
        if token in "{}":
            return None
        return bytes(token[1:-1], "utf-8").decode("unicode_escape")

    @staticmethod
    def _merge(previous, value):
        if previous is None:
            return value
        if isinstance(previous, list):
            previous.append(value)
            return previous
        return [previous, value]

    @staticmethod
    def _strip_comments(text):
        return re.sub(r"//.*", "", text)


def load_text(path_or_url):
    if path_or_url.startswith("http://") or path_or_url.startswith("https://"):
        with urlopen(path_or_url, timeout=60) as response:
            return response.read().decode("utf-8", errors="replace")
    return Path(path_or_url).read_text(encoding="utf-8", errors="replace")


def localize(token, translations):
    if not token:
        return ""
    key = token[1:].lower() if token.startswith("#") else token.lower()
    return translations.get(key, token.lstrip("#"))


def parse_translations(text):
    parsed = VdfParser(text).parse()
    language = parsed.get("lang", {}).get("Tokens", {})
    return {k.lower(): v for k, v in language.items()} if isinstance(language, dict) else {}


def find_items_root(parsed):
    return parsed.get("items_game", parsed)


def as_dict(value):
    if isinstance(value, dict):
        return value
    if isinstance(value, list):
        merged = {}
        for entry in value:
            if isinstance(entry, dict):
                merged.update(entry)
        return merged
    return {}


def collect_weapon_paint_links(root):
    links = {weapon: set() for weapon in WEAPON_DISPLAY}
    item_sets = as_dict(root.get("item_sets", {}))

    for item_set in item_sets.values():
        if not isinstance(item_set, dict):
            continue
        items = item_set.get("items", {})
        if not isinstance(items, dict):
            continue
        for key in items.keys():
            match = re.match(r"\[([^\]]+)\](weapon_[a-z0-9_]+)$", key)
            if not match:
                continue
            paint_name, weapon = match.group(1), match.group(2)
            if weapon in links:
                links[weapon].add(paint_name)
    return links


def build_weapons(root, translations, api_skins=None):
    if api_skins:
        return build_weapons_from_api(api_skins)

    paint_kits = as_dict(root.get("paint_kits", {}))
    rarities = as_dict(root.get("paint_kits_rarity", {}))
    links = collect_weapon_paint_links(root)
    paint_by_name = {}
    for paint_id, paint in paint_kits.items():
        if isinstance(paint, dict) and paint_id.isdigit():
            name = paint.get("name")
            if name:
                paint_by_name[name] = (int(paint_id), paint)

    weapons = []
    for entity, (category, display) in WEAPON_DISPLAY.items():
        skins = []
        for paint_name in sorted(links.get(entity, [])):
            found = paint_by_name.get(paint_name)
            if not found:
                continue
            paint_id, paint = found
            skin_name = localize(paint.get("description_tag", paint_name), translations)
            cosmetic_id = f"{entity}:{paint_id}"
            skins.append({
                "id": cosmetic_id,
                "displayName": skin_name,
                "paintKit": paint_id,
                "seed": 0,
                "wear": 0.0001,
                "enabled": True,
                "rarity": rarities.get(paint_name),
            })
        weapons.append({
            "entityName": entity,
            "displayName": display,
            "category": category,
            "enabled": True,
            "skins": skins,
        })
    return weapons


def build_weapons_from_api(api_skins):
    grouped = {entity: [] for entity in WEAPON_DISPLAY}
    for skin in api_skins:
        weapon = skin.get("weapon", {})
        entity = weapon.get("id")
        paint_index = skin.get("paint_index")
        if entity not in grouped or paint_index is None:
            continue
        category = skin.get("category", {})
        pattern = skin.get("pattern", {})
        rarity = skin.get("rarity", {})
        grouped[entity].append({
            "id": f"{entity}:{paint_index}",
            "displayName": pattern.get("name") or skin.get("name", "").split("|")[-1].strip(),
            "paintKit": int(paint_index),
            "seed": 0,
            "wear": float(skin.get("min_float") or 0.0001),
            "legacyModel": bool(skin.get("legacy_model", False)),
            "enabled": True,
            "rarity": rarity.get("id") or category.get("id"),
        })

    weapons = []
    for entity, (category, display) in WEAPON_DISPLAY.items():
        skins = sorted(unique_by_id(grouped[entity]), key=lambda x: x["displayName"])
        weapons.append({
            "entityName": entity,
            "displayName": display,
            "category": category,
            "enabled": True,
            "skins": skins,
        })
    return weapons


def collect_items(root):
    return as_dict(root.get("items", {}))


def build_knives(root, translations, api_skins=None):
    if api_skins:
        return build_knives_from_api(root, translations, api_skins)

    paint_kits = as_dict(root.get("paint_kits", {}))
    knives = []
    for item_id, item in collect_items(root).items():
        if not item_id.isdigit() or not isinstance(item, dict):
            continue
        name = item.get("name", "")
        prefab = item.get("prefab", "")
        if "knife" not in name and "melee" not in prefab:
            continue
        if name == "weapon_knife":
            continue
        display = localize(item.get("item_name", name), translations)
        skins = []
        for paint_id, paint in paint_kits.items():
            if paint_id.isdigit() and isinstance(paint, dict):
                paint_name = localize(paint.get("description_tag", paint.get("name", paint_id)), translations)
                skins.append({
                    "id": f"{name}:{paint_id}",
                    "displayName": paint_name,
                    "paintKit": int(paint_id),
                    "seed": 0,
                    "wear": 0.0001,
                    "itemDefinitionIndex": int(item_id),
                    "enabled": True,
                })
        knives.append({
            "id": name,
            "displayName": display,
            "entityName": name,
            "itemDefinitionIndex": int(item_id),
            "enabled": True,
            "skins": skins,
        })
    return sorted(knives, key=lambda x: x["displayName"])


def build_knives_from_api(root, translations, api_skins):
    items = collect_items(root)
    api_knife_weapon_ids = {
        skin.get("weapon", {}).get("weapon_id")
        for skin in api_skins
        if skin.get("paint_index") is not None
        and (
            str(skin.get("weapon", {}).get("id", "")).startswith("weapon_knife")
            or str(skin.get("weapon", {}).get("id", "")) == "weapon_bayonet"
        )
    }
    knife_items = {
        int(item_id): item
        for item_id, item in items.items()
        if item_id.isdigit()
        and isinstance(item, dict)
        and int(item_id) in api_knife_weapon_ids
        and item.get("name") != "weapon_knife"
    }
    grouped = {item_id: [] for item_id in knife_items}
    for skin in api_skins:
        weapon = skin.get("weapon", {})
        weapon_id = weapon.get("weapon_id")
        paint_index = skin.get("paint_index")
        if weapon_id not in grouped or paint_index is None:
            continue
        pattern = skin.get("pattern", {})
        rarity = skin.get("rarity", {})
        entity = knife_items[weapon_id].get("name")
        grouped[weapon_id].append({
            "id": f"{entity}:{paint_index}",
            "displayName": pattern.get("name") or skin.get("name", "").split("|")[-1].strip(),
            "paintKit": int(paint_index),
            "seed": 0,
            "wear": float(skin.get("min_float") or 0.0001),
            "itemDefinitionIndex": int(weapon_id),
            "legacyModel": bool(skin.get("legacy_model", False)),
            "enabled": True,
            "rarity": rarity.get("id"),
        })

    knives = []
    for item_id, item in knife_items.items():
        name = item.get("name", "")
        display = localize(item.get("item_name", name), translations)
        skins = [{
            "id": f"{name}:0",
            "displayName": "Vanilla",
            "paintKit": 0,
            "seed": 0,
            "wear": 0.0001,
            "itemDefinitionIndex": int(item_id),
            "legacyModel": False,
            "enabled": True,
        }]
        skins.extend(sorted(unique_by_id(grouped.get(item_id, [])), key=lambda x: (x["displayName"], x["paintKit"])))
        knives.append({
            "id": name,
            "displayName": display,
            "entityName": name,
            "itemDefinitionIndex": int(item_id),
            "enabled": True,
            "skins": skins,
        })
    return sorted(knives, key=lambda x: x["displayName"])


def build_gloves(root, translations, api_skins=None):
    if api_skins:
        return build_gloves_from_api(root, translations, api_skins)

    paint_kits = as_dict(root.get("paint_kits", {}))
    gloves = []
    for item_id, item in collect_items(root).items():
        if not item_id.isdigit() or not isinstance(item, dict):
            continue
        name = item.get("name", "")
        prefab = item.get("prefab", "")
        if "glove" not in name and "hands" not in prefab:
            continue
        display = localize(item.get("item_name", name), translations)
        skins = []
        if item.get("prefab") != "hands_paintable":
            continue
        family = glove_family_for_item(name)
        if not family:
            continue
        for paint_id, paint in paint_kits.items():
            if not paint_id.isdigit() or not isinstance(paint, dict):
                continue
            if glove_family_for_paint(paint) != family:
                continue
            paint_name = localize(paint.get("description_tag", paint.get("name", paint_id)), translations)
            skins.append({
                "id": f"{name}:{paint_id}",
                "displayName": paint_name,
                "paintKit": int(paint_id),
                "seed": 0,
                "wear": 0.0001,
                "itemDefinitionIndex": int(item_id),
                "enabled": True,
            })
        gloves.append({
            "id": name,
            "displayName": display,
            "itemDefinitionIndex": int(item_id),
            "enabled": True,
            "skins": skins,
        })
    return sorted(gloves, key=lambda x: x["displayName"])


def build_gloves_from_api(root, translations, api_skins):
    items = collect_items(root)
    glove_items = {
        int(item_id): item
        for item_id, item in items.items()
        if item_id.isdigit()
        and isinstance(item, dict)
        and item.get("prefab") == "hands_paintable"
    }
    grouped = {item_id: [] for item_id in glove_items}
    for skin in api_skins:
        weapon = skin.get("weapon", {})
        weapon_id = weapon.get("weapon_id")
        paint_index = skin.get("paint_index")
        if weapon_id not in grouped or paint_index is None:
            continue
        pattern = skin.get("pattern", {})
        rarity = skin.get("rarity", {})
        entity = glove_items[weapon_id].get("name")
        grouped[weapon_id].append({
            "id": f"{entity}:{paint_index}",
            "displayName": pattern.get("name") or skin.get("name", "").split("|")[-1].strip(),
            "paintKit": int(paint_index),
            "seed": 0,
            "wear": float(skin.get("min_float") or 0.0001),
            "itemDefinitionIndex": int(weapon_id),
            "legacyModel": bool(skin.get("legacy_model", False)),
            "enabled": True,
            "rarity": rarity.get("id"),
        })

    gloves = []
    for item_id, item in glove_items.items():
        name = item.get("name", "")
        skins = sorted(unique_by_id(grouped.get(item_id, [])), key=lambda x: (x["displayName"], x["paintKit"]))
        if not skins:
            continue
        gloves.append({
            "id": name,
            "displayName": localize(item.get("item_name", name), translations),
            "itemDefinitionIndex": int(item_id),
            "enabled": True,
            "skins": skins,
        })
    return sorted(gloves, key=lambda x: x["displayName"])


def build_agents(api_agents, root):
    schema_metadata = build_agent_schema_metadata(root)
    agents = []
    for agent in api_agents or []:
        agent_id = agent.get("id")
        name = agent.get("name")
        model = agent.get("model_player")
        team = normalize_agent_team(agent.get("team", {}).get("id"))
        def_index = agent.get("def_index")
        rarity = agent.get("rarity", {})
        collections = agent.get("collections") or []
        group = collections[0].get("name") if collections and isinstance(collections[0], dict) else None
        metadata = schema_metadata.get(str(def_index), {})
        voice_prefix = metadata.get("voicePrefix")
        if not agent_id or not name or not model or not team:
            continue
        display_name = str(name).split("|", 1)[0].strip()
        agents.append({
            "id": str(agent_id),
            "displayName": display_name,
            "team": team,
            "model": str(model),
            "itemDefinitionIndex": int(def_index) if str(def_index).isdigit() else None,
            "voicePrefix": voice_prefix,
            "hasFemaleVoice": bool(metadata.get("hasFemaleVoice", False)),
            "enabled": True,
            "rarity": rarity.get("id") if isinstance(rarity, dict) else None,
            "group": group,
        })
    return sorted(unique_by_id(agents), key=lambda x: (x["team"], x["displayName"]))


def build_agent_schema_metadata(root):
    metadata = {}
    for item_id, item in collect_items(root).items():
        if not item_id.isdigit() or not isinstance(item, dict):
            continue
        if not item.get("model_player") or "customplayer" not in str(item.get("prefab", "")):
            continue
        voice_prefix = item.get("vo_prefix")
        if not voice_prefix:
            continue
        text = " ".join(str(item.get(key, "")) for key in ("default_cheer", "default_defeat", "vo_prefix"))
        inventory_data = item.get("inventory_image_data", {})
        if isinstance(inventory_data, dict):
            text = f"{text} {inventory_data.get('pose_sequence', '')}"
        metadata[str(item_id)] = {
            "voicePrefix": str(voice_prefix),
            "hasFemaleVoice": "fem" in text.lower() or "female" in text.lower(),
        }
    return metadata


def normalize_agent_team(team):
    if not team:
        return None
    value = str(team).strip().lower()
    if value in {"terrorist", "terrorists", "t"}:
        return "t"
    if value in {"counter-terrorist", "counter-terrorists", "counterterrorist", "counterterrorists", "ct"}:
        return "ct"
    return None


def glove_family_for_item(item_name):
    if item_name == "studded_bloodhound_gloves":
        return "bloodhound"
    if item_name == "studded_hydra_gloves":
        return "hydra"
    if item_name == "studded_brokenfang_gloves":
        return "brokenfang"
    if item_name == "slick_gloves":
        return "driver"
    if item_name == "sporty_gloves":
        return "sport"
    if item_name == "leather_handwraps":
        return "handwrap"
    if item_name == "motorcycle_gloves":
        return "motorcycle"
    if item_name == "specialist_gloves":
        return "specialist"
    return None


def glove_family_for_paint(paint):
    name = paint.get("name", "")
    path = paint.get("vmt_path", "")
    if not name and "paints_gloves" not in path:
        return None
    if name.startswith("bloodhound_hydra_"):
        return "hydra"
    if name.startswith("bloodhound_"):
        return "bloodhound"
    if name.startswith("operation10_"):
        return "brokenfang"
    if name.startswith("slick_") or name.startswith("glove_driver_"):
        return "driver"
    if name.startswith("sporty_") or name.startswith("glove_sport_"):
        return "sport"
    if name.startswith("handwrap_"):
        return "handwrap"
    if name.startswith("motorcycle_"):
        return "motorcycle"
    if name.startswith("specialist_") or name.startswith("glove_specialist_"):
        return "specialist"
    return None


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    # newline="\n" keeps the output byte-identical across platforms; without it
    # Python rewrites every "\n" as CRLF on Windows.
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def strip_music_kit_prefix(name):
    # "Music Kit | Feed Me, High Noon" / "\u97f3\u4e50\u76d2 | ..." -> keep only the kit name.
    if name and " | " in name:
        return name.split(" | ", 1)[1].strip()
    return (name or "").strip()


def build_music_kits(api_music_kits, api_music_kits_zh):
    zh_names = {}
    for entry in api_music_kits_zh or []:
        zh_names[entry.get("id")] = strip_music_kit_prefix(entry.get("name"))

    # The kit number is what the game plays; StatTrak is only a variant of the
    # same kit. Prefer the plain entry, but keep the "_st" one when it is the
    # only listing (the StatTrak-only kits 32-38 never had a plain version).
    by_number = {}
    for entry in api_music_kits or []:
        kit_id = entry.get("id") or ""
        match = re.fullmatch(r"music_kit-(\d+)(_st)?", kit_id)
        if not match:
            continue
        number = int(match.group(1))
        if number <= 2:
            continue  # engine default kits, not selectable cosmetics
        is_stattrak = match.group(2) is not None
        if number in by_number and not by_number[number][0]:
            continue  # plain entry already kept
        by_number[number] = (is_stattrak, kit_id, entry)

    kits = []
    for number, (_, kit_id, entry) in by_number.items():
        name = strip_music_kit_prefix(entry.get("name"))
        if not name:
            continue
        kit = {"id": str(number), "musicKit": number, "displayName": name}
        zh = zh_names.get(kit_id)
        if zh:
            kit["displayNameZh"] = zh
        kits.append(kit)

    kits.sort(key=lambda k: k["musicKit"])
    return kits


def split_item_name(name):
    """"Sticker | ropz (Gold) | Cologne 2026" -> ("ropz (Gold)", "Cologne 2026");
    "Charm | Lil' Ava" -> ("Lil' Ava", None). The first segment is the item
    type, which the menu already shows as the category."""
    # The zh-CN names put no space before the pipe after a full-width
    # parenthesis ("Natus Vincere（全息）| 2026年科隆锦标赛"), so split on the
    # pipe itself.
    parts = [part.strip() for part in re.split(r"\s*\|\s*", name or "")]
    if len(parts) >= 3:
        return parts[1], parts[2]
    if len(parts) == 2:
        return parts[1], None
    return parts[0], None


def first_name(entries):
    if entries and isinstance(entries[0], dict):
        return entries[0].get("name") or None
    return None


def sticker_group(entry):
    """(group, capsule): tournament stickers group by event and then by the
    capsule they came from; the rest group by capsule or collection; anything
    left over has no group and lands in the menu's "Other" entry."""
    tournament = entry.get("tournament") or {}
    crate = first_name(entry.get("crates"))
    collection = first_name(entry.get("collections"))
    if isinstance(tournament, dict) and tournament.get("name"):
        return tournament["name"], crate
    if crate:
        return crate, None
    if collection:
        return collection, None
    return None, None


def add_zh_field(item, key, english, chinese):
    if chinese and chinese != english:
        item[key] = chinese


def build_stickers(api_stickers, api_stickers_zh):
    zh_by_id = {entry.get("id"): entry for entry in api_stickers_zh or []}
    stickers = []
    tournament_groups = set()
    for entry in api_stickers or []:
        def_index = str(entry.get("def_index", ""))
        if not def_index.isdigit():
            continue
        name, _ = split_item_name(entry.get("name"))
        if not name:
            continue
        group, capsule = sticker_group(entry)
        if group and isinstance(entry.get("tournament"), dict) and entry["tournament"].get("name"):
            tournament_groups.add(group)
        zh = zh_by_id.get(entry.get("id")) or {}
        zh_name, _ = split_item_name(zh.get("name"))
        zh_group, zh_capsule = sticker_group(zh) if zh else (None, None)
        rarity = entry.get("rarity") or {}
        item = {"id": f"sticker-{def_index}", "stickerId": int(def_index), "displayName": name}
        add_zh_field(item, "displayNameZh", name, zh_name)
        if group:
            item["group"] = group
            add_zh_field(item, "groupZh", group, zh_group)
        if capsule:
            item["capsule"] = capsule
            add_zh_field(item, "capsuleZh", capsule, zh_capsule)
        if isinstance(rarity, dict) and rarity.get("id"):
            item["rarity"] = rarity["id"]
        stickers.append(item)

    stickers = unique_by_id(stickers)
    # Tournaments first, newest event first (the highest sticker id in the
    # group), then the capsules and collections the same way, ungrouped
    # stickers last; inside a group the capsules keep their release order and
    # the stickers are alphabetical.
    group_rank = {}
    capsule_rank = {}
    for item in stickers:
        group = item.get("group")
        group_rank[group] = max(group_rank.get(group, -1), item["stickerId"])
        key = (group, item.get("capsule"))
        capsule_rank[key] = min(capsule_rank.get(key, 1 << 30), item["stickerId"])
    stickers.sort(key=lambda item: (
        item.get("group") is None,
        item.get("group") not in tournament_groups,
        -group_rank[item.get("group")],
        item.get("capsule") is None,
        capsule_rank[(item.get("group"), item.get("capsule"))],
        item["displayName"].lower(),
        item["stickerId"],
    ))
    return stickers


def build_keychains(api_keychains, api_keychains_zh):
    zh_by_id = {entry.get("id"): entry for entry in api_keychains_zh or []}
    keychains = []
    for entry in api_keychains or []:
        def_index = str(entry.get("def_index", ""))
        if not def_index.isdigit():
            continue
        name, _ = split_item_name(entry.get("name"))
        if not name:
            continue
        group = first_name(entry.get("collections"))
        zh = zh_by_id.get(entry.get("id")) or {}
        zh_name, _ = split_item_name(zh.get("name"))
        rarity = entry.get("rarity") or {}
        item = {"id": f"keychain-{def_index}", "keychainId": int(def_index), "displayName": name}
        add_zh_field(item, "displayNameZh", name, zh_name)
        if group:
            item["group"] = group
            add_zh_field(item, "groupZh", group, first_name(zh.get("collections")))
        if isinstance(rarity, dict) and rarity.get("id"):
            item["rarity"] = rarity["id"]
        keychains.append(item)

    keychains = unique_by_id(keychains)
    group_order = {}
    for item in keychains:
        group_order.setdefault(item.get("group"), len(group_order))
    keychains.sort(key=lambda item: (
        item.get("group") is None,
        group_order[item.get("group")],
        item["displayName"].lower(),
        item["keychainId"],
    ))
    return keychains


def with_zh(entry, zh_name):
    """Return a copy with displayNameZh right after displayName, or the entry itself when there is no translation."""
    if not zh_name or zh_name == entry.get("displayName"):
        return entry
    result = {}
    for key, value in entry.items():
        result[key] = value
        if key == "displayName":
            result["displayNameZh"] = zh_name
    return result


def attach_zh_names(weapons, knives, gloves, agents, api_skins_zh, api_agents_zh):
    """Add displayNameZh from the zh-CN copies of the same upstream lists.

    Skins are matched by (weapon id, paint index) and knives/gloves by
    (item definition index, paint index), which is how the English entries
    were keyed in the first place. Item names come from the zh weapon field.
    """
    skin_zh = {}
    weapon_zh = {}
    for skin in api_skins_zh or []:
        weapon = skin.get("weapon") or {}
        paint_index = skin.get("paint_index")
        pattern = skin.get("pattern") or {}
        name = (pattern.get("name") or (skin.get("name") or "").split("|")[-1]).strip()
        if paint_index is None or not name:
            continue
        skin_zh[(weapon.get("id"), int(paint_index))] = name
        skin_zh[(weapon.get("weapon_id"), int(paint_index))] = name
        if weapon.get("name"):
            weapon_zh[weapon.get("id")] = weapon["name"]
            weapon_zh[weapon.get("weapon_id")] = weapon["name"]

    def enrich(containers, container_key):
        for index, container in enumerate(containers):
            key = container.get(container_key)
            skins = [
                with_zh(skin, "原版" if skin.get("paintKit") == 0 else skin_zh.get((key, skin.get("paintKit"))))
                for skin in container.get("skins", [])
            ]
            enriched = with_zh(container, weapon_zh.get(key))
            enriched["skins"] = skins
            containers[index] = enriched

    enrich(weapons, "entityName")
    enrich(knives, "itemDefinitionIndex")
    enrich(gloves, "itemDefinitionIndex")

    agent_zh = {
        str(agent.get("id")): str(agent.get("name", "")).split("|", 1)[0].strip()
        for agent in api_agents_zh or []
        if agent.get("id") and agent.get("name")
    }
    for index, agent in enumerate(agents):
        agents[index] = with_zh(agent, agent_zh.get(agent["id"]))


def unique_by_id(entries):
    result = []
    seen = set()
    for entry in entries:
        if entry["id"] in seen:
            continue
        seen.add(entry["id"])
        result.append(entry)
    return result


def main():
    parser = argparse.ArgumentParser(description="Generate WeaponSkins JSON definitions from CS2 item schema data.")
    parser.add_argument("--items-game", default=ITEMS_GAME_URL)
    parser.add_argument("--language", default=CSGO_ENGLISH_URL)
    parser.add_argument("--skins-api", default=SKINS_API_URL)
    parser.add_argument("--agents-api", default=AGENTS_API_URL)
    parser.add_argument("--skins-zh-api", default=SKINS_ZH_API_URL)
    parser.add_argument("--agents-zh-api", default=AGENTS_ZH_API_URL)
    parser.add_argument("--music-kits-api", default=MUSIC_KITS_API_URL)
    parser.add_argument("--music-kits-zh-api", default=MUSIC_KITS_ZH_API_URL)
    parser.add_argument("--stickers-api", default=STICKERS_API_URL)
    parser.add_argument("--stickers-zh-api", default=STICKERS_ZH_API_URL)
    parser.add_argument("--keychains-api", default=KEYCHAINS_API_URL)
    parser.add_argument("--keychains-zh-api", default=KEYCHAINS_ZH_API_URL)
    parser.add_argument("--output", default="data")
    args = parser.parse_args()

    root = find_items_root(VdfParser(load_text(args.items_game)).parse())
    translations = parse_translations(load_text(args.language))
    api_skins = json.loads(load_text(args.skins_api)) if args.skins_api else None
    api_agents = json.loads(load_text(args.agents_api)) if args.agents_api else None
    api_skins_zh = json.loads(load_text(args.skins_zh_api)) if args.skins_zh_api else None
    api_agents_zh = json.loads(load_text(args.agents_zh_api)) if args.agents_zh_api else None
    api_music_kits = json.loads(load_text(args.music_kits_api)) if args.music_kits_api else None
    api_music_kits_zh = json.loads(load_text(args.music_kits_zh_api)) if args.music_kits_zh_api else None
    api_stickers = json.loads(load_text(args.stickers_api)) if args.stickers_api else None
    api_stickers_zh = json.loads(load_text(args.stickers_zh_api)) if args.stickers_zh_api else None
    api_keychains = json.loads(load_text(args.keychains_api)) if args.keychains_api else None
    api_keychains_zh = json.loads(load_text(args.keychains_zh_api)) if args.keychains_zh_api else None
    output = Path(args.output)

    weapons = build_weapons(root, translations, api_skins)
    knives = build_knives(root, translations, api_skins)
    gloves = build_gloves(root, translations, api_skins)
    agents = build_agents(api_agents, root)
    music_kits = build_music_kits(api_music_kits, api_music_kits_zh)
    stickers = build_stickers(api_stickers, api_stickers_zh)
    keychains = build_keychains(api_keychains, api_keychains_zh)
    attach_zh_names(weapons, knives, gloves, agents, api_skins_zh, api_agents_zh)

    if not any(w["skins"] for w in weapons):
        print("No weapon skins were generated; check item_sets and paint_kits in the input schema.", file=sys.stderr)
        return 2

    write_json(output / "weapons.json", weapons)
    write_json(output / "knives.json", knives)
    write_json(output / "gloves.json", gloves)
    write_json(output / "agents.json", agents)
    write_json(output / "music_kits.json", music_kits)
    write_json(output / "stickers.json", stickers)
    write_json(output / "keychains.json", keychains)
    write_json(output / "categories.json", CATEGORIES)
    print(f"Generated {sum(len(w['skins']) for w in weapons)} weapon skins, {sum(len(k['skins']) for k in knives)} knife skins, {sum(len(g['skins']) for g in gloves)} glove skins, {len(agents)} agents, {len(music_kits)} music kits, {len(stickers)} stickers and {len(keychains)} keychains into {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
