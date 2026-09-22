"""Pickles sprites: visible jar + dir-offset inhands (beer-like), cucumber, barrel ferment."""
from PIL import Image, ImageDraw
from pathlib import Path
import json

ROOT = Path(r"D:\ss14 dev\dead-space-14\Resources\Textures\_DeadSpace\Pickles")
jar = ROOT / "jar.rsi"
cdir = ROOT / "cucumber.rsi"
wood = ROOT / "barrel_wood.rsi"
plastic = ROOT / "barrel_plastic.rsi"

W = H = 32
TRANS = (0, 0, 0, 0)


def blank(w=W, h=H):
    return Image.new("RGBA", (w, h), TRANS)


def save(img, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)


# --- JAR (compact but readable) ---
GLASS = (185, 205, 220, 255)
LID = (105, 80, 50, 255)
LID_HI = (155, 125, 85, 255)
LID_EDGE = (60, 45, 25, 255)
BRINE = (195, 180, 85, 220)
PICKLE = (170, 185, 50, 255)
PICKLE_D = (95, 110, 30, 255)
SHINE = (255, 255, 255, 180)
# Slightly larger world jar than before
BL, BR, BT, BB = 11, 20, 11, 25
NL, NR, NT, NB = 12, 19, 9, 11


def draw_body(d, fill=None):
    d.rectangle([BL, BT, BR, BB], outline=GLASS, fill=fill)
    d.rectangle([NL, NT, NR, NB], outline=GLASS, fill=fill)
    d.point([(BL + 1, BT + 2), (BL + 1, BT + 3)], fill=SHINE)


def draw_lid(d):
    d.rectangle([10, 6, 21, 10], fill=LID, outline=LID_EDGE)
    d.rectangle([11, 5, 20, 8], fill=LID_HI, outline=LID_EDGE)


def draw_open_rim(d):
    d.rectangle([NL, NT - 1, NR, NT + 1], outline=GLASS, fill=(230, 235, 240, 255))


def draw_pickles_n(d, n, style="cucumber"):
    draw_contents(d, style, n)


def draw_contents(d, style, n):
    """Distinct in-jar produce shapes. Spots scale with piece count 1–4."""
    spots = [(13, 15), (15, 18), (13, 21), (16, 16)]
    for i, (x, y) in enumerate(spots[:n]):
        if style == "cucumber":
            d.ellipse([x, y, x + 4, y + 2], fill=PICKLE, outline=PICKLE_D)
        elif style == "cabbage":
            d.arc([x, y, x + 5, y + 4], 200, 340, fill=(90, 150, 60, 255))
            d.point([(x + 2, y + 1)], fill=(140, 180, 90, 255))
        elif style == "onion":
            d.ellipse([x, y, x + 4, y + 4], outline=(200, 180, 120, 255))
            d.ellipse([x + 1, y + 1, x + 3, y + 3], outline=(180, 160, 100, 255))
        elif style == "onionred":
            d.ellipse([x, y, x + 4, y + 4], outline=(160, 50, 90, 255))
            d.ellipse([x + 1, y + 1, x + 3, y + 3], outline=(120, 30, 70, 255))
        elif style == "carrot":
            d.rectangle([x + 1, y, x + 2, y + 4], fill=(220, 120, 30, 255))
            d.point([(x + 1, y)], fill=(60, 140, 40, 255))
        elif style == "garlic":
            d.ellipse([x, y + 1, x + 3, y + 4], fill=(235, 230, 210, 255), outline=(180, 170, 140, 255))
            d.point([(x + 1, y + 1)], fill=(250, 248, 230, 255))
        elif style == "mushroom":
            d.ellipse([x, y, x + 4, y + 2], fill=(160, 120, 70, 255), outline=(100, 70, 40, 255))
            d.rectangle([x + 1, y + 2, x + 2, y + 4], fill=(210, 190, 150, 255))
        elif style == "cactus":
            d.rectangle([x + 1, y, x + 2, y + 4], fill=(50, 130, 50, 255))
            d.point([(x, y + 1), (x + 3, y + 2)], fill=(40, 100, 40, 255))
        elif style == "tomato":
            d.ellipse([x, y, x + 4, y + 4], fill=(200, 50, 40, 255), outline=(140, 30, 25, 255))
            d.point([(x + 1, y + 1)], fill=(240, 100, 80, 255))
        elif style == "watermelon":
            d.polygon([(x, y + 3), (x + 2, y), (x + 4, y + 3)], fill=(220, 70, 90, 255), outline=(40, 110, 50, 255))
            d.point([(x + 2, y + 2)], fill=(240, 200, 200, 255))
        elif style == "pumpkin":
            d.ellipse([x, y, x + 4, y + 3], fill=(220, 130, 30, 255), outline=(160, 80, 20, 255))
            d.line([(x + 2, y), (x + 2, y + 3)], fill=(180, 100, 25, 255))
        elif style == "chili":
            d.ellipse([x, y + 1, x + 2, y + 4], fill=(210, 40, 30, 255), outline=(140, 20, 15, 255))
            d.point([(x + 1, y)], fill=(50, 120, 40, 255))
        elif style == "soy":
            d.ellipse([x, y + 1, x + 2, y + 3], fill=(200, 180, 120, 255), outline=(140, 120, 70, 255))
        else:
            d.ellipse([x, y, x + 4, y + 2], fill=PICKLE, outline=PICKLE_D)


CONTENTS_STYLES = [
    "cucumber", "cabbage", "onion", "onionred", "carrot", "garlic", "mushroom",
    "cactus", "tomato", "watermelon", "pumpkin", "chili", "soy",
]

img = blank(); d = ImageDraw.Draw(img)
draw_body(d, fill=(35, 40, 50, 40)); draw_lid(d)
save(img, jar / "icon_empty.png")

img = blank(); d = ImageDraw.Draw(img)
draw_body(d, fill=BRINE); draw_contents(d, "cucumber", 4); draw_lid(d)
save(img, jar / "icon.png")

img = blank(); d = ImageDraw.Draw(img)
draw_body(d, fill=(35, 40, 50, 40)); draw_open_rim(d)
save(img, jar / "icon_open.png")

img = blank(); d = ImageDraw.Draw(img)
d.point([(BL + 1, BT + 2)], fill=SHINE)
save(img, jar / "icon-front.png")

for style in CONTENTS_STYLES:
    for n in range(1, 5):
        img = blank(); d = ImageDraw.Draw(img); draw_contents(d, style, n)
        save(img, jar / f"{style}-{n}.png")
        # Legacy alias used by older YAML / fallbacks
        if style == "cucumber":
            save(img, jar / f"contents-{n}.png")

for i in range(1, 10):
    img = blank(); d = ImageDraw.Draw(img)
    top = max(BB - 1 - int(i * 1.2), BT + 1)
    d.rectangle([BL + 1, top, BR - 1, BB - 1], fill=BRINE)
    save(img, jar / f"fill-{i}.png")


# Glass/tomato grip positions (S/N/E/W) — match glass_clear inhand offsets.
DIR_POS = {
    "S": (18, 15),
    "N": (9, 15),
    "E": (20, 14),
    "W": (14, 15),
}
DIR_ORDER = ["S", "N", "E", "W"]


def mirror_dir_sheet(sheet):
    """Mirror each 32x32 direction tile for the opposite hand."""
    out = blank(64, 64)
    positions = [(0, 0), (32, 0), (0, 32), (32, 32)]
    for ox, oy in positions:
        tile = sheet.crop((ox, oy, ox + 32, oy + 32)).transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        # No mask: paste-with-alpha-mask drops semi-transparent glass fills.
        out.paste(tile, (ox, oy))
    return out


def make_dir_sheet(draw_fn):
    sheet = blank(64, 64)
    positions = [(0, 0), (32, 0), (0, 32), (32, 32)]
    for (ox, oy), dname in zip(positions, DIR_ORDER):
        tile = blank()
        x, y = DIR_POS[dname]
        draw_fn(ImageDraw.Draw(tile), x, y, dname)
        out_tile = blank()
        out_tile.paste(tile, (0, 0))
        sheet.paste(out_tile, (ox, oy))
    return sheet


def jar_closed_draw(d, x, y, dname):
    # ~half previous size: compact sealed jar at grip.
    d.rectangle([x, y + 1, x + 2, y + 4], outline=GLASS, fill=(40, 48, 58, 100))
    d.rectangle([x - 1, y, x + 3, y + 1], fill=LID, outline=LID_EDGE)
    d.point([(x, y + 2)], fill=SHINE)


def jar_open_draw(d, x, y, dname):
    d.rectangle([x, y + 1, x + 2, y + 4], outline=GLASS, fill=(40, 48, 58, 60))
    d.rectangle([x, y, x + 2, y + 1], outline=GLASS, fill=(235, 240, 245, 230))
    d.point([(x, y + 2)], fill=SHINE)


def jar_fill_draw(d, x, y, dname, level):
    top = y + 4 - level
    d.rectangle([x + 1, max(top, y + 1), x + 1, y + 3], fill=BRINE)


closed_left = make_dir_sheet(jar_closed_draw)
open_left = make_dir_sheet(jar_open_draw)
save(closed_left, jar / "closed-inhand-left.png")
save(mirror_dir_sheet(closed_left), jar / "closed-inhand-right.png")
save(open_left, jar / "open-inhand-left.png")
save(mirror_dir_sheet(open_left), jar / "open-inhand-right.png")

for f in (1, 2, 3):
    fill_left = make_dir_sheet(lambda d, x, y, n, level=f: jar_fill_draw(d, x, y, n, level))
    fill_right = mirror_dir_sheet(fill_left)
    save(fill_left, jar / f"open-inhand-left-fill-{f}.png")
    save(fill_right, jar / f"open-inhand-right-fill-{f}.png")
    save(fill_left, jar / f"closed-inhand-left-fill-{f}.png")
    save(fill_right, jar / f"closed-inhand-right-fill-{f}.png")

_content_states = []
for style in CONTENTS_STYLES:
    for n in range(1, 5):
        _content_states.append(f'        {{ "name": "{style}-{n}" }},')
_content_states_str = "\n".join(_content_states)

(jar / "meta.json").write_text(f"""{{
    "version": 1,
    "size": {{ "x": 32, "y": 32 }},
    "license": "CC-BY-SA-3.0",
    "copyright": "Procedural mason jar for Dead Space Pickles overlay.",
    "states": [
        {{ "name": "icon" }},
        {{ "name": "icon-front" }},
        {{ "name": "contents-1" }}, {{ "name": "contents-2" }},
        {{ "name": "contents-3" }}, {{ "name": "contents-4" }},
{_content_states_str}
        {{ "name": "fill-1" }}, {{ "name": "fill-2" }}, {{ "name": "fill-3" }},
        {{ "name": "fill-4" }}, {{ "name": "fill-5" }}, {{ "name": "fill-6" }},
        {{ "name": "fill-7" }}, {{ "name": "fill-8" }}, {{ "name": "fill-9" }},
        {{ "name": "open-inhand-left", "directions": 4 }},
        {{ "name": "open-inhand-left-fill-1", "directions": 4 }},
        {{ "name": "open-inhand-left-fill-2", "directions": 4 }},
        {{ "name": "open-inhand-left-fill-3", "directions": 4 }},
        {{ "name": "open-inhand-right", "directions": 4 }},
        {{ "name": "open-inhand-right-fill-1", "directions": 4 }},
        {{ "name": "open-inhand-right-fill-2", "directions": 4 }},
        {{ "name": "open-inhand-right-fill-3", "directions": 4 }},
        {{ "name": "closed-inhand-left", "directions": 4 }},
        {{ "name": "closed-inhand-left-fill-1", "directions": 4 }},
        {{ "name": "closed-inhand-left-fill-2", "directions": 4 }},
        {{ "name": "closed-inhand-left-fill-3", "directions": 4 }},
        {{ "name": "closed-inhand-right", "directions": 4 }},
        {{ "name": "closed-inhand-right-fill-1", "directions": 4 }},
        {{ "name": "closed-inhand-right-fill-2", "directions": 4 }},
        {{ "name": "closed-inhand-right-fill-3", "directions": 4 }},
        {{ "name": "icon_empty" }},
        {{ "name": "icon_open" }}
    ]
}}
""", encoding="utf-8")


# --- CUCUMBER ---
def draw_cucumber(d, ox=0, oy=0):
    d.ellipse([9 + ox, 16 + oy, 24 + ox, 21 + oy], fill=(25, 55, 20, 180))
    d.ellipse([8 + ox, 12 + oy, 23 + ox, 19 + oy], fill=(45, 120, 45, 255), outline=(18, 45, 18, 255))
    d.ellipse([9 + ox, 14 + oy, 22 + ox, 19 + oy], fill=(38, 100, 38, 255))
    d.ellipse([10 + ox, 12 + oy, 16 + ox, 16 + oy], fill=(95, 170, 75, 255))
    d.line([(11 + ox, 13 + oy), (19 + ox, 13 + oy)], fill=(110, 185, 90, 255))
    for x, y in [(12, 15), (15, 16), (18, 15), (14, 17), (17, 17)]:
        d.point([(x + ox, y + oy)], fill=(28, 70, 25, 255))
        d.point([(x + 1 + ox, y + oy)], fill=(55, 110, 40, 255))
    d.ellipse([21 + ox, 14 + oy, 24 + ox, 17 + oy], fill=(190, 175, 55, 255), outline=(120, 100, 30, 255))
    d.point([(22 + ox, 15 + oy)], fill=(220, 200, 80, 255))
    d.rectangle([7 + ox, 14 + oy, 9 + ox, 17 + oy], fill=(55, 80, 25, 255), outline=(30, 50, 15, 255))
    d.point([(8 + ox, 13 + oy)], fill=(70, 100, 35, 255))


img = blank(); draw_cucumber(ImageDraw.Draw(img))
save(img, cdir / "produce.png")


def cuk_inhand(d, x, y, dname):
    # Tomato-scale produce inhand: tiny oval at grip point
    d.ellipse([x - 1, y + 1, x + 3, y + 4], fill=(25, 55, 20, 160))
    d.ellipse([x - 1, y, x + 3, y + 3], fill=(45, 120, 45, 255), outline=(18, 45, 18, 255))
    d.point([(x, y + 1)], fill=(95, 170, 75, 255))


cuk_left = make_dir_sheet(cuk_inhand)
save(cuk_left, cdir / "produce-inhand-left.png")
save(mirror_dir_sheet(cuk_left), cdir / "produce-inhand-right.png")

meta_c = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Cucumber produce redrawn for Dead Space Pickles; plant stages from vgstation lineage.",
    "size": {"x": 32, "y": 32},
    "states": [
        {"name": "dead"},
        {"name": "harvest"},
        {"name": "produce"},
        {"name": "seed"},
        {"name": "stage-1"},
        {"name": "produce-inhand-left", "directions": 4},
        {"name": "produce-inhand-right", "directions": 4},
    ],
}
(cdir / "meta.json").write_text(json.dumps(meta_c, indent=4), encoding="utf-8")


# --- BARREL ferment animation ---
def animate_ferment(base_path: Path):
    idle = Image.open(base_path / "idle.png").convert("RGBA")
    frames = []
    paths = [
        [(10, 12), (16, 10), (22, 13)],
        [(11, 11), (17, 12), (21, 11)],
        [(12, 10), (15, 11), (20, 12)],
        [(10, 11), (16, 10), (22, 12)],
    ]
    for pts in paths:
        fr = idle.copy()
        d = ImageDraw.Draw(fr)
        for x, y in pts:
            d.ellipse([x, y, x + 3, y + 3], fill=(255, 220, 70, 230), outline=(200, 160, 30, 255))
            d.point([(x + 1, y + 1)], fill=(255, 250, 180, 255))
        frames.append(fr)
    strip = blank(32 * len(frames), 32)
    for i, fr in enumerate(frames):
        strip.paste(fr, (i * 32, 0), fr)
    save(strip, base_path / "fermenting.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Adapted from vgstation barrel sprites for Dead Space Pickles overlay.",
        "size": {"x": 32, "y": 32},
        "states": [
            {"name": "idle"},
            {"name": "fermenting", "delays": [[0.2, 0.2, 0.2, 0.2]]},
        ],
    }
    (base_path / "meta.json").write_text(json.dumps(meta, indent=4), encoding="utf-8")


animate_ferment(wood)
if (plastic / "idle.png").exists():
    animate_ferment(plastic)

print("sprites ok")
