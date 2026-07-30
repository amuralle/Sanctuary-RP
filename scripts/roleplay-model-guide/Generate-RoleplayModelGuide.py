#!/usr/bin/env python3

import argparse
import csv
import datetime
import html
import json
import math
import pathlib
import subprocess

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import letter, landscape
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (
    KeepTogether,
    LongTable,
    PageBreak,
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


CATEGORY_ORDER = [
    "People and Humanoid NPCs",
    "Creatures and Monsters",
    "Collections, Loot and Rewards",
    "Vehicles and Mounts",
    "Props and Interactables",
    "Housing and Furnishings",
    "Environment and Scenery",
    "Jobs, Farming and Minigames",
    "Effects and Technical Assets",
    "Miscellaneous - Needs Testing",
]

CATEGORY_GUIDANCE = {
    "People and Humanoid NPCs": (
        "Usually the best miniature candidates. Search by race, job, costume, or a known NPC name."
    ),
    "Creatures and Monsters": (
        "Animals, enemies, pets, and fantasy creatures. Animation quality varies by model."
    ),
    "Collections, Loot and Rewards": (
        "Collection piles, coins, chests, and reward props. Useful for objectives and scene markers."
    ),
    "Vehicles and Mounts": (
        "Mounts, hoverboards, carts, boats, and racing assets. Some expect rider or vehicle systems."
    ),
    "Props and Interactables": (
        "Portable scene dressing such as crates, signs, food, tools, and furniture-like props."
    ),
    "Housing and Furnishings": (
        "Housing assets and architectural pieces. Many are useful, but some are very large."
    ),
    "Environment and Scenery": (
        "Trees, rocks, buildings, terrain, and regional scenery. Test scale before using these."
    ),
    "Jobs, Farming and Minigames": (
        "Job equipment, farm stages, race parts, balls, and activity-specific models."
    ),
    "Effects and Technical Assets": (
        "Particles, anchors, triggers, invisible helpers, and test assets. Expect unusual behavior."
    ),
    "Miscellaneous - Needs Testing": (
        "Assets that could not be confidently classified from client metadata alone."
    ),
}

HUMANOID_PREFIXES = (
    "human",
    "hum_",
    "fairy",
    "dwarf",
    "chugawug",
    "robgoblin",
    "troll",
    "bixie",
    "changeling",
    "floren",
    "tikihead",
    "ghostdwarf",
    "freestyle",
    "merchant",
)

CREATURE_TERMS = (
    "animal",
    "banshee",
    "bat_",
    "bear",
    "bird",
    "boar",
    "cakenstein",
    "cat_",
    "chicken",
    "cow",
    "crab",
    "deer",
    "dog_",
    "dragon",
    "duck",
    "fish",
    "frog",
    "ghost_",
    "goat",
    "golem",
    "monster",
    "penguin",
    "pet_",
    "pony",
    "rat_",
    "scorpion",
    "screecher",
    "sheep",
    "skeleton",
    "snake",
    "spider",
    "swooper",
    "trex",
    "t_rex",
    "turtle",
    "werewolf",
    "wolf",
    "yeti",
    "zombie",
)

COLLECTION_TERMS = (
    "collection_node",
    "exploration_coin",
    "loot_",
    "lootcoin",
    "loot_coin",
    "mystery_chest",
    "treasure_chest",
    "reward_",
    "gold_coin",
)

VEHICLE_TERMS = (
    "_mount",
    "mount_",
    "hoverboard",
    "vehicle",
    "racecar",
    "race_car",
    "kart_",
    "mkt_",
    "boat",
    "broom",
    "horse",
    "pony_mount",
    "carriage",
)

HOUSING_TERMS = (
    "hsg_",
    "housing",
    "house_",
    "furniture",
    "apartment",
    "chair",
    "couch",
    "sofa",
    "bed_",
    "bookshelf",
    "cabinet",
    "chandelier",
    "fireplace",
    "pictureframe",
    "picture_frame",
    "wallpaper",
)

MINIGAME_TERMS = (
    "farming_",
    "seedpacket",
    "minigame",
    "soccer",
    "ball_",
    "kt_",
    "ktdm_",
    "dm_",
    "derby",
    "racetrack",
    "race_track",
    "job_",
    "fishing_",
)

TECHNICAL_TERMS = (
    "invisible",
    "anchor",
    "particle",
    "emitter",
    "effect",
    "_fx",
    "fx_",
    "trigger",
    "collision",
    "volume",
    "locator",
    "placeholder",
    "dummy",
    "debug",
    "test_",
    "target_",
    "decal",
    "powerup_",
    "lightbeam",
)

ENVIRONMENT_TERMS = (
    "overworld",
    "environment",
    "env_",
    "terrain",
    "ground_",
    "road_",
    "bridge",
    "building",
    "castle",
    "tower",
    "cave",
    "tree",
    "rock",
    "boulder",
    "bush",
    "shrub",
    "plant",
    "flower",
    "mushroom",
    "waterfall",
    "water_",
    "cloud",
    "sky_",
    "archway",
    "fence",
    "gate_",
    "sg_",
    "bw_",
    "sh_",
    "ss_",
    "gw_",
    "sv_",
)

PROP_TERMS = (
    "barrel",
    "basket",
    "bench",
    "book",
    "bottle",
    "box_",
    "bucket",
    "campfire",
    "candle",
    "chest",
    "crate",
    "door_",
    "food",
    "lamp",
    "mail_",
    "present",
    "pushbutton",
    "rope",
    "sign_",
    "statue",
    "table",
    "tool",
    "torch",
    "weapon",
    "window",
)

STARTER_MODEL_IDS = [
    4,
    14,
    37,
    46,
    65,
    134,
    151,
    176,
    189,
    248,
    249,
    317,
    333,
    503,
    674,
    720,
    724,
    728,
    815,
]


class CatalogDocument(SimpleDocTemplate):
    def afterFlowable(self, flowable):
        if not isinstance(flowable, Paragraph):
            return

        if flowable.style.name not in ("GuideHeading", "CatalogHeading"):
            return

        title = flowable.getPlainText()
        key = f"section-{self.seq.nextf('section')}"
        self.canv.bookmarkPage(key)
        self.canv.addOutlineEntry(title, key, level=0, closed=False)


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", type=pathlib.Path, required=True)
    parser.add_argument("--npcs", type=pathlib.Path, required=True)
    parser.add_argument("--pdf", type=pathlib.Path, required=True)
    parser.add_argument("--csv", type=pathlib.Path, required=True)
    return parser.parse_args()


def parse_int(value, default=0):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def parse_float(value, default=0.0):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def contains_any(value, terms):
    return any(term in value for term in terms)


def categorize(model):
    filename = model["file"].lower()

    if contains_any(filename, TECHNICAL_TERMS):
        return "Effects and Technical Assets"
    if contains_any(filename, COLLECTION_TERMS):
        return "Collections, Loot and Rewards"
    if contains_any(filename, VEHICLE_TERMS):
        return "Vehicles and Mounts"
    if contains_any(filename, HOUSING_TERMS):
        return "Housing and Furnishings"
    if contains_any(filename, MINIGAME_TERMS):
        return "Jobs, Farming and Minigames"
    if filename.startswith(HUMANOID_PREFIXES):
        return "People and Humanoid NPCs"
    if contains_any(filename, CREATURE_TERMS):
        return "Creatures and Monsters"
    if contains_any(filename, PROP_TERMS):
        return "Props and Interactables"
    if contains_any(filename, ENVIRONMENT_TERMS):
        return "Environment and Scenery"
    return "Miscellaneous - Needs Testing"


def friendly_label(filename):
    stem = pathlib.Path(filename.strip()).stem
    words = [word for word in stem.replace("-", "_").split("_") if word]
    return " ".join(word.upper() if len(word) <= 2 else word.capitalize() for word in words)


def get_revision(root):
    try:
        return subprocess.run(
            ["git", "-C", str(root), "rev-parse", "--short", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def load_models(path):
    fields = [
        "id",
        "file",
        "race_id",
        "scale",
        "description",
        "gender",
        "age",
        "descriptor",
        "material_type",
        "water_displacement",
        "teleport_effect_id",
        "camera_distance",
        "camera_angle",
        "valid_for_pc",
        "nameplate_offset",
        "capsule_height",
        "radius",
        "extra",
    ]

    models = []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, fieldnames=fields, delimiter="^")
        next(reader)

        for row in reader:
            model_id = parse_int(row["id"], -1)
            filename = (row["file"] or "").strip()
            if model_id <= 0 or not filename:
                continue

            model = {
                "id": model_id,
                "file": filename,
                "friendly_label": friendly_label(filename),
                "race_id": parse_int(row["race_id"]),
                "scale": parse_float(row["scale"]),
                "description": (row["description"] or "").strip(),
                "gender": parse_int(row["gender"]),
                "age": parse_int(row["age"]),
                "descriptor": (row["descriptor"] or "").strip(),
                "material_type": parse_int(row["material_type"]),
                "water_displacement": parse_float(row["water_displacement"]),
                "teleport_effect_id": parse_int(row["teleport_effect_id"]),
                "camera_distance": parse_float(row["camera_distance"]),
                "camera_angle": parse_float(row["camera_angle"]),
                "valid_for_pc": parse_int(row["valid_for_pc"]) != 0,
                "nameplate_offset": parse_float(row["nameplate_offset"]),
                "capsule_height": parse_float(row["capsule_height"]),
                "radius": parse_float(row["radius"]),
            }
            model["category"] = categorize(model)
            models.append(model)

    return sorted(models, key=lambda model: model["id"])


def load_npc_aliases(path):
    with path.open("r", encoding="utf-8-sig") as handle:
        definitions = json.load(handle)

    aliases = {}
    definition_ids = {}

    for definition in definitions:
        model_id = parse_int(definition.get("ModelId"))
        if model_id <= 0:
            continue

        name = (definition.get("Name") or "").strip()
        if name:
            aliases.setdefault(model_id, set()).add(name)

        definition_id = parse_int(definition.get("Id"))
        if definition_id > 0:
            definition_ids.setdefault(model_id, set()).add(definition_id)

    return aliases, definition_ids


def roleplay_fit(category):
    if category in (
        "People and Humanoid NPCs",
        "Creatures and Monsters",
        "Collections, Loot and Rewards",
        "Props and Interactables",
    ):
        return "Good candidate"
    if category in (
        "Vehicles and Mounts",
        "Housing and Furnishings",
        "Environment and Scenery",
        "Jobs, Farming and Minigames",
    ):
        return "Test scale/behavior"
    return "Experimental"


def alias_summary(names, limit=3):
    values = sorted(names)
    if not values:
        return ""
    if len(values) <= limit:
        return ", ".join(values)
    return f"{', '.join(values[:limit])} (+{len(values) - limit})"


def model_notes(model):
    notes = []
    if model["valid_for_pc"]:
        notes.append("PC-valid")
    if model["scale"] not in (0.0, 1.0):
        notes.append(f"scale {model['scale']:g}")
    if model["category"] == "Effects and Technical Assets":
        notes.append("technical")
    if any(term in model["file"].lower() for term in ("building", "terrain", "overworld", "wall_")):
        notes.append("may be large")
    return ", ".join(notes)


def enrich_models(models, aliases, definition_ids):
    for model in models:
        model["known_names"] = sorted(aliases.get(model["id"], set()))
        model["npc_definition_ids"] = sorted(definition_ids.get(model["id"], set()))

        known_names = " ".join(model["known_names"]).lower()
        if "exploration coin" in known_names:
            model["category"] = "Collections, Loot and Rewards"

        model["roleplay_fit"] = roleplay_fit(model["category"])
        model["notes"] = model_notes(model)


def write_catalog_csv(path, models):
    path.parent.mkdir(parents=True, exist_ok=True)
    fieldnames = [
        "model_id",
        "asset_file",
        "friendly_label",
        "inferred_category",
        "roleplay_fit",
        "known_npc_names",
        "npc_definition_ids",
        "description",
        "descriptor",
        "scale",
        "race_id",
        "gender",
        "age",
        "valid_for_pc",
        "nameplate_offset",
        "capsule_height",
        "radius",
        "material_type",
        "water_displacement",
        "teleport_effect_id",
        "camera_distance",
        "camera_angle",
    ]

    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        writer.writeheader()

        for model in models:
            writer.writerow(
                {
                    "model_id": model["id"],
                    "asset_file": model["file"],
                    "friendly_label": model["friendly_label"],
                    "inferred_category": model["category"],
                    "roleplay_fit": model["roleplay_fit"],
                    "known_npc_names": " | ".join(model["known_names"]),
                    "npc_definition_ids": " | ".join(
                        str(value) for value in model["npc_definition_ids"]
                    ),
                    "description": model["description"],
                    "descriptor": model["descriptor"],
                    "scale": model["scale"],
                    "race_id": model["race_id"],
                    "gender": model["gender"],
                    "age": model["age"],
                    "valid_for_pc": model["valid_for_pc"],
                    "nameplate_offset": model["nameplate_offset"],
                    "capsule_height": model["capsule_height"],
                    "radius": model["radius"],
                    "material_type": model["material_type"],
                    "water_displacement": model["water_displacement"],
                    "teleport_effect_id": model["teleport_effect_id"],
                    "camera_distance": model["camera_distance"],
                    "camera_angle": model["camera_angle"],
                }
            )


def escaped(value):
    return html.escape(str(value))


def make_styles():
    base = getSampleStyleSheet()
    return {
        "cover_title": ParagraphStyle(
            "CoverTitle",
            parent=base["Title"],
            fontName="Helvetica-Bold",
            fontSize=28,
            leading=31,
            textColor=colors.HexColor("#18324B"),
            alignment=TA_LEFT,
            spaceAfter=12,
        ),
        "cover_subtitle": ParagraphStyle(
            "CoverSubtitle",
            parent=base["Normal"],
            fontName="Helvetica",
            fontSize=13,
            leading=18,
            textColor=colors.HexColor("#397C79"),
            alignment=TA_LEFT,
        ),
        "guide_heading": ParagraphStyle(
            "GuideHeading",
            parent=base["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=18,
            leading=22,
            textColor=colors.HexColor("#18324B"),
            spaceBefore=4,
            spaceAfter=10,
        ),
        "catalog_heading": ParagraphStyle(
            "CatalogHeading",
            parent=base["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=15,
            leading=18,
            textColor=colors.HexColor("#18324B"),
            spaceBefore=4,
            spaceAfter=5,
        ),
        "subheading": ParagraphStyle(
            "Subheading",
            parent=base["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=11,
            leading=14,
            textColor=colors.HexColor("#397C79"),
            spaceBefore=8,
            spaceAfter=5,
        ),
        "body": ParagraphStyle(
            "GuideBody",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9,
            leading=13,
            textColor=colors.HexColor("#26343F"),
            spaceAfter=6,
        ),
        "small": ParagraphStyle(
            "GuideSmall",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=7.2,
            leading=9.2,
            textColor=colors.HexColor("#26343F"),
        ),
        "tiny": ParagraphStyle(
            "GuideTiny",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=6.4,
            leading=8,
            textColor=colors.HexColor("#26343F"),
        ),
        "table_header": ParagraphStyle(
            "TableHeader",
            parent=base["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=7,
            leading=8,
            textColor=colors.white,
            alignment=TA_LEFT,
        ),
        "command": ParagraphStyle(
            "Command",
            parent=base["Code"],
            fontName="Courier",
            fontSize=8.2,
            leading=11,
            textColor=colors.HexColor("#18324B"),
        ),
        "center": ParagraphStyle(
            "CenterSmall",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=7,
            leading=9,
            alignment=TA_CENTER,
        ),
    }


def paragraph(value, style):
    return Paragraph(escaped(value), style)


def page_footer(canvas, document):
    canvas.saveState()
    width, _ = landscape(letter)
    canvas.setStrokeColor(colors.HexColor("#D9E2E7"))
    canvas.line(0.55 * inch, 0.42 * inch, width - 0.55 * inch, 0.42 * inch)
    canvas.setFont("Helvetica", 7)
    canvas.setFillColor(colors.HexColor("#607480"))
    canvas.drawString(0.55 * inch, 0.25 * inch, "OSFR Roleplay Model Field Guide")
    canvas.drawRightString(width - 0.55 * inch, 0.25 * inch, f"Page {document.page}")
    canvas.restoreState()


def add_cover(story, styles, models, revision):
    category_count = len({model["category"] for model in models})
    named_count = sum(1 for model in models if model["known_names"])

    story.extend(
        [
            Spacer(1, 0.65 * inch),
            Paragraph("OSFR Roleplay", styles["cover_subtitle"]),
            Paragraph("Model Field Guide", styles["cover_title"]),
            Paragraph(
                "A searchable tabletop catalog for player-spawned Free Realms models",
                styles["cover_subtitle"],
            ),
            Spacer(1, 0.4 * inch),
        ]
    )

    stat_data = [
        [
            paragraph(f"{len(models):,}", styles["guide_heading"]),
            paragraph(f"{named_count:,}", styles["guide_heading"]),
            paragraph(str(category_count), styles["guide_heading"]),
        ],
        [
            paragraph("client models", styles["center"]),
            paragraph("models with known NPC aliases", styles["center"]),
            paragraph("inferred roleplay categories", styles["center"]),
        ],
    ]
    stats = Table(stat_data, colWidths=[2.1 * inch] * 3)
    stats.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), colors.HexColor("#EDF5F3")),
                ("BOX", (0, 0), (-1, -1), 0.7, colors.HexColor("#AFC8C3")),
                ("INNERGRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#CADAD7")),
                ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
                ("TOPPADDING", (0, 0), (-1, 0), 12),
                ("BOTTOMPADDING", (0, 1), (-1, 1), 10),
            ]
        )
    )
    story.append(stats)
    story.append(Spacer(1, 0.35 * inch))
    story.append(
        Paragraph(
            "Generated from <b>src/Resources/Models.txt</b> and "
            "<b>src/Resources/Npcs.json</b>. Categories are inferred from asset names and metadata; "
            "they are navigation aids, not authoritative client classifications.",
            styles["body"],
        )
    )
    story.append(
        Paragraph(
            f"Catalog revision: <b>{escaped(revision)}</b> | Generated "
            f"{datetime.date.today().isoformat()}",
            styles["body"],
        )
    )
    story.append(PageBreak())


def add_quick_start(story, styles):
    story.append(Paragraph("Quick Start", styles["guide_heading"]))
    story.append(
        Paragraph(
            "Use model search to discover IDs, then spawn a named piece at your current position "
            "and facing. The optional @group label keeps scenes organized.",
            styles["body"],
        )
    )

    commands = [
        ("Find likely matches", "!mini models wolf"),
        ("Browse the complete catalog", "!mini models * 2"),
        ("Place a miniature", "!mini spawn 176 @enemies Briar Wolf"),
        ("List a scene group", "!mini list @enemies"),
        ("Inspect the closest piece", "!mini info nearest"),
        ("Remove by short ID", "!mini remove #1"),
        ("Clean a scene group", "!mini clear @enemies"),
    ]
    command_data = [
        [
            paragraph("Task", styles["table_header"]),
            paragraph("Command", styles["table_header"]),
        ]
    ]
    command_data.extend(
        [paragraph(label, styles["small"]), Paragraph(command, styles["command"])]
        for label, command in commands
    )
    command_table = Table(command_data, colWidths=[2.1 * inch, 4.9 * inch], repeatRows=1)
    command_table.setStyle(default_table_style())
    story.append(command_table)
    story.append(Spacer(1, 0.15 * inch))

    story.append(Paragraph("What the catalog can and cannot tell us", styles["subheading"]))
    cautions = [
        "The model ID and asset filename are authoritative for this server revision.",
        "Known NPC names are aliases from Npcs.json. One generic model can have many aliases.",
        "A valid catalog entry can still be invisible, effect-only, incomplete, or extremely large.",
        "The server uses scale 1.0 when the catalog scale is zero. Test scenery before a live scene.",
        "Miniatures are session-only and disappear when the zone or server restarts.",
    ]
    for item in cautions:
        story.append(Paragraph(f"- {escaped(item)}", styles["body"]))


def add_category_summary(story, styles, models):
    story.append(PageBreak())
    story.append(Paragraph("Category Map", styles["guide_heading"]))
    story.append(
        Paragraph(
            "The guide groups models by likely tabletop use. Search terms are often more useful "
            "than scrolling: try a race, creature, object, region prefix, or NPC name.",
            styles["body"],
        )
    )

    counts = {
        category: sum(1 for model in models if model["category"] == category)
        for category in CATEGORY_ORDER
    }
    rows = [
        [
            paragraph("Inferred category", styles["table_header"]),
            paragraph("Models", styles["table_header"]),
            paragraph("How to use it", styles["table_header"]),
        ]
    ]
    for category in CATEGORY_ORDER:
        rows.append(
            [
                paragraph(category, styles["small"]),
                paragraph(f"{counts[category]:,}", styles["small"]),
                paragraph(CATEGORY_GUIDANCE[category], styles["small"]),
            ]
        )

    table = Table(rows, colWidths=[2.2 * inch, 0.65 * inch, 6.75 * inch], repeatRows=1)
    table.setStyle(default_table_style())
    story.append(table)


def add_starter_models(story, styles, models):
    story.append(PageBreak())
    story.append(Paragraph("Starter Models", styles["guide_heading"]))
    story.append(
        Paragraph(
            "These IDs are recognizable anchors for testing the command flow. They are not a "
            "quality ranking; use the complete catalog and in-game search to branch out.",
            styles["body"],
        )
    )

    by_id = {model["id"]: model for model in models}
    rows = [
        [
            paragraph("ID", styles["table_header"]),
            paragraph("Asset", styles["table_header"]),
            paragraph("Known name or description", styles["table_header"]),
            paragraph("Category", styles["table_header"]),
        ]
    ]
    for model_id in STARTER_MODEL_IDS:
        model = by_id.get(model_id)
        if not model:
            continue
        label = alias_summary(model["known_names"], 2) or model["description"] or model["friendly_label"]
        rows.append(
            [
                paragraph(model["id"], styles["small"]),
                paragraph(model["file"], styles["small"]),
                paragraph(label, styles["small"]),
                paragraph(model["category"], styles["small"]),
            ]
        )

    table = LongTable(rows, colWidths=[0.45 * inch, 3.1 * inch, 3.1 * inch, 2.95 * inch], repeatRows=1)
    table.setStyle(default_table_style())
    story.append(table)


def add_complete_catalog(story, styles, models):
    story.append(PageBreak())
    story.append(Paragraph("Complete Model Catalog", styles["guide_heading"]))
    story.append(
        Paragraph(
            "Every model loaded by this server revision appears below. Use PDF search for an asset "
            "term, known NPC name, or numeric ID. The companion CSV contains every raw metadata field.",
            styles["body"],
        )
    )
    story.append(Spacer(1, 0.1 * inch))

    for category_index, category in enumerate(CATEGORY_ORDER):
        category_models = [model for model in models if model["category"] == category]
        if not category_models:
            continue

        story.append(
            KeepTogether(
                [
                    Paragraph(
                        f"{category} ({len(category_models):,})",
                        styles["catalog_heading"],
                    ),
                    Paragraph(CATEGORY_GUIDANCE[category], styles["small"]),
                    Spacer(1, 0.06 * inch),
                ]
            )
        )

        rows = [
            [
                paragraph("ID", styles["table_header"]),
                paragraph("Asset file", styles["table_header"]),
                paragraph("Known NPC aliases / description", styles["table_header"]),
                paragraph("Notes", styles["table_header"]),
            ]
        ]

        for model in category_models:
            aliases = alias_summary(model["known_names"])
            context = aliases or model["description"] or model["friendly_label"]
            rows.append(
                [
                    paragraph(model["id"], styles["tiny"]),
                    paragraph(model["file"], styles["tiny"]),
                    paragraph(context, styles["tiny"]),
                    paragraph(model["notes"], styles["tiny"]),
                ]
            )

        table = LongTable(
            rows,
            colWidths=[0.42 * inch, 3.35 * inch, 4.35 * inch, 1.5 * inch],
            repeatRows=1,
            splitByRow=1,
        )
        table.setStyle(default_table_style(font_size=6.4))
        story.append(table)

        if category_index < len(CATEGORY_ORDER) - 1:
            story.append(PageBreak())


def default_table_style(font_size=7.2):
    return TableStyle(
        [
            ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#18324B")),
            ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
            ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
            ("FONTNAME", (0, 1), (-1, -1), "Helvetica"),
            ("FONTSIZE", (0, 1), (-1, -1), font_size),
            ("GRID", (0, 0), (-1, -1), 0.35, colors.HexColor("#C8D3D9")),
            ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F4F7F8")]),
            ("VALIGN", (0, 0), (-1, -1), "TOP"),
            ("LEFTPADDING", (0, 0), (-1, -1), 4),
            ("RIGHTPADDING", (0, 0), (-1, -1), 4),
            ("TOPPADDING", (0, 0), (-1, -1), 3),
            ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
        ]
    )


def write_pdf(path, models, revision):
    path.parent.mkdir(parents=True, exist_ok=True)
    styles = make_styles()
    document = CatalogDocument(
        str(path),
        pagesize=landscape(letter),
        leftMargin=0.55 * inch,
        rightMargin=0.55 * inch,
        topMargin=0.52 * inch,
        bottomMargin=0.55 * inch,
        title="OSFR Roleplay Model Field Guide",
        author="OSFR Roleplay",
        subject="Searchable client model catalog for tabletop miniatures",
    )

    story = []
    add_cover(story, styles, models, revision)
    add_quick_start(story, styles)
    add_category_summary(story, styles, models)
    add_starter_models(story, styles, models)
    add_complete_catalog(story, styles, models)
    document.build(story, onFirstPage=page_footer, onLaterPages=page_footer)


def main():
    args = parse_args()
    models_path = args.models.resolve()
    npcs_path = args.npcs.resolve()
    repository_root = models_path.parents[2]

    models = load_models(models_path)
    aliases, definition_ids = load_npc_aliases(npcs_path)
    enrich_models(models, aliases, definition_ids)

    write_catalog_csv(args.csv.resolve(), models)
    write_pdf(args.pdf.resolve(), models, get_revision(repository_root))

    category_counts = {
        category: sum(1 for model in models if model["category"] == category)
        for category in CATEGORY_ORDER
    }
    print(f"Generated {args.pdf} and {args.csv} for {len(models)} models.")
    for category in CATEGORY_ORDER:
        print(f"{category}: {category_counts[category]}")


if __name__ == "__main__":
    main()
