"""Regenerate IV drip RSI: hose, folded stand, icon (no bags)."""
from PIL import Image, ImageDraw
import os

ROOT = os.path.normpath(
    os.path.join(os.path.dirname(__file__), "..", "Resources", "Textures", "_DeadSpace", "Medical", "iv_drip.rsi")
)


def clean_red_blob(name: str) -> None:
    path = os.path.join(ROOT, name)
    if not os.path.exists(path):
        return
    im = Image.open(path).convert("RGBA")
    px = im.load()
    for y in range(im.size[1]):
        for x in range(im.size[0]):
            r, g, b, a = px[x, y]
            if a > 10 and r > 80 and g < 60 and b < 60:
                px[x, y] = (0, 0, 0, 0)
    im.save(path)


def strip_bag_and_tube(im: Image.Image) -> Image.Image:
    """Remove IV bag outline / hanging tube on the right of unfolded."""
    out = im.copy()
    px = out.load()
    for y in range(32):
        for x in range(32):
            # Right of pole (~x>=19): bag + tube area — clear bright outline and thin hose
            if x >= 19:
                r, g, b, a = px[x, y]
                if a < 10:
                    continue
                # Keep only very dark bits if any; clear bag whites/grays and thin hose
                if r + g + b > 200 or (a > 10 and x >= 19 and y <= 18):
                    # Keep pole tip arm pixels that are part of metal (x 16-18 handled separately)
                    if x >= 19:
                        px[x, y] = (0, 0, 0, 0)
    return out


def make_icon_and_unfolded_clean() -> Image.Image:
    """Unfolded stand without hanging bag — used for icon + base for folded."""
    path = os.path.join(ROOT, "unfolded.png")
    im = Image.open(path).convert("RGBA")
    clean = strip_bag_and_tube(im)
    # Also clear leftover tube coils mid-pole (dark squiggles left of bag)
    px = clean.load()
    for y in range(8, 20):
        for x in range(12, 19):
            r, g, b, a = px[x, y]
            if a < 10:
                continue
            # thin dark hose-like pixels near pole but not the pole column (~15-17)
            if x in (12, 13, 14) and r < 100 and g < 100 and b < 110:
                px[x, y] = (0, 0, 0, 0)
    return clean


def make_folded(base: Image.Image) -> None:
    """Collapse arm + shorten stand into a packed folded look."""
    folded = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    src = base.load()
    dst = folded.load()

    # Copy vertical pole (x 15-17) compressed slightly toward center
    for y in range(4, 28):
        for x in range(15, 18):
            c = src[x, y]
            if c[3] > 10:
                # squash height toward mid
                ny = 8 + int((y - 4) * 0.75)
                if 0 <= ny < 32:
                    dst[x, ny] = c

    # Fold top arm downward along the pole (was horizontal to the right)
    for y in range(1, 5):
        for x in range(16, 24):
            c = src[x, y]
            if c[3] < 10:
                continue
            # map arm into downward fold on right of pole
            ny = 6 + (x - 16)
            nx = 17 + (y - 1)
            if 0 <= nx < 32 and 0 <= ny < 32:
                dst[nx, ny] = c

    # Compact wheeled base
    for y in range(26, 31):
        for x in range(11, 24):
            c = src[x, y]
            if c[3] > 10:
                dst[x, min(30, y)] = c

    # Ensure a solid small base block if sparse
    for x in range(13, 20):
        if dst[x, 29][3] < 10:
            dst[x, 29] = (45, 43, 54, 255)
        if dst[x, 28][3] < 10:
            dst[x, 28] = (68, 72, 87, 255)

    folded.save(os.path.join(ROOT, "folded.png"))
    print("wrote folded.png")


def make_bag_fill() -> None:
    bag = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = bag.load()
    fill = (255, 255, 255, 255)
    px[21, 6] = fill
    for y in range(7, 11):
        for x in range(20, 23):
            px[x, y] = fill
    px[21, 11] = fill
    bag.save(os.path.join(ROOT, "bag.png"))
    print("wrote bag.png")


def make_needle() -> None:
    """Clear translucent hose loop + small tip — not a cobweb."""
    needle = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(needle)

    # Soft plastic tubing (semi-transparent cyan-gray)
    tube = (180, 200, 210, 140)
    tube_hi = (220, 235, 240, 180)
    # Outer U-loop
    d.arc([10, 8, 22, 20], start=200, end=340, fill=tube, width=2)
    d.arc([11, 9, 21, 19], start=200, end=340, fill=tube_hi, width=1)
    # Drop to needle
    d.line([(16, 19), (16, 24)], fill=tube, width=2)
    d.line([(16, 24), (14, 28)], fill=(160, 170, 180, 200), width=1)
    # Metal tip
    d.point((13, 29), fill=(200, 205, 210, 255))
    d.point((14, 29), fill=(230, 230, 235, 255))

    needle.save(os.path.join(ROOT, "needle.png"))

    for side in ("left", "right"):
        ih = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        di = ImageDraw.Draw(ih)
        # Compact in-hand: small loop only (Tiny item)
        di.arc([12, 10, 20, 18], start=210, end=330, fill=tube, width=2)
        di.line([(16, 17), (15, 22)], fill=tube, width=1)
        di.point((14, 23), fill=(210, 215, 220, 255))
        ih.save(os.path.join(ROOT, f"needle-inhand-{side}.png"))

    print("wrote needle sprites")


if __name__ == "__main__":
    clean_red_blob("unfolded.png")
    clean = make_icon_and_unfolded_clean()
    # Keep unfolded WITH bag outline for gameplay — restore from current unfolded then only clean red
    # Re-open original unfolded for gameplay bag hook
    unfolded = Image.open(os.path.join(ROOT, "unfolded.png")).convert("RGBA")
    # ensure no red blob
    px = unfolded.load()
    for y in range(32):
        for x in range(32):
            r, g, b, a = px[x, y]
            if a > 10 and r > 80 and g < 60 and b < 60:
                px[x, y] = (0, 0, 0, 0)
    unfolded.save(os.path.join(ROOT, "unfolded.png"))

    # icon = clean stand no bag (vendor / inventory)
    clean.save(os.path.join(ROOT, "icon.png"))
    print("wrote icon.png")

    make_folded(clean)
    make_bag_fill()
    make_needle()
