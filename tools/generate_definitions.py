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
    {"id": "pistols", "displayName": "Pistols", "order": 10, "enabled": True},
    {"id": "smgs", "displayName": "SMGs", "order": 20, "enabled": True},
    {"id": "rifles", "displayName": "Rifles", "order": 30, "enabled": True},
    {"id": "heavy", "displayName": "Heavy", "order": 40, "enabled": True},
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
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


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
    parser.add_argument("--output", default="data")
    args = parser.parse_args()

    root = find_items_root(VdfParser(load_text(args.items_game)).parse())
    translations = parse_translations(load_text(args.language))
    api_skins = json.loads(load_text(args.skins_api)) if args.skins_api else None
    api_agents = json.loads(load_text(args.agents_api)) if args.agents_api else None
    output = Path(args.output)

    weapons = build_weapons(root, translations, api_skins)
    knives = build_knives(root, translations, api_skins)
    gloves = build_gloves(root, translations, api_skins)
    agents = build_agents(api_agents, root)

    if not any(w["skins"] for w in weapons):
        print("No weapon skins were generated; check item_sets and paint_kits in the input schema.", file=sys.stderr)
        return 2

    write_json(output / "weapons.json", weapons)
    write_json(output / "knives.json", knives)
    write_json(output / "gloves.json", gloves)
    write_json(output / "agents.json", agents)
    write_json(output / "categories.json", CATEGORIES)
    print(f"Generated {sum(len(w['skins']) for w in weapons)} weapon skins, {sum(len(k['skins']) for k in knives)} knife skins, {sum(len(g['skins']) for g in gloves)} glove skins, and {len(agents)} agents into {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
