"""
SSPTMM's icon, Workshop banner art and README/GitHub images, all made from the mascot.

    python build/branding/make_branding.py        (needs Pillow: pip install pillow)

Source: assets/ssptmm-mascot.png - the mascot on a transparent background, as its author made it.
Everything below is generated from it, so a new mascot is one file swap and one run:

    src/TCFModManager.App/Assets/AppIcon.ico        exe, taskbar, title bar (16 to 256 px)
    src/TCFModManager.App/Assets/WorkshopBanner.png the art at the left of the Workshop banner
    docs/images/ssptmm-icon-256.png                 the icon as a PNG
    docs/images/ssptmm-banner.png                   README header (1280x320)
    docs/images/ssptmm-social.png                   GitHub social preview (1280x640, set by hand
                                                    in the repository's Settings > General)

The icon sits on a square dark Steam-blue tile (boxed, like the mods' thumbnails beside it) because the mascot is mostly white: on its own it would
vanish on a light taskbar or a light GitHub page. At 16-32 px the whole mascot is a smudge, so
those sizes show its helmeted head instead.
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
MASCOT = ROOT / "assets" / "ssptmm-mascot.png"
FONTS = ROOT / "src" / "TCFModManager.App" / "Themes" / "Fonts"

# Steam's own blues (store page header / Workshop): dark top-left, lighter bottom-right.
TILE_TOP = (23, 26, 33)
TILE_BOTTOM = (42, 71, 94)
ACCENT = (102, 192, 244)  # #66C0F4, the app's tertiary accent
WHITE = (255, 255, 255)
GREY = (197, 202, 208)    # #C5CAD0, the banner's body text

# The helmeted head, in the source image's pixels: what the smallest icon sizes show.
HEAD_BOX = (150, -6, 426, 270)


def font(face: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / f"NotoSans-{face}.ttf"), size)


def gradient(size: tuple[int, int]) -> Image.Image:
    """A diagonal TILE_TOP -> TILE_BOTTOM gradient."""
    w, h = size
    small = Image.new("RGB", (2, 2))
    small.putdata([TILE_TOP, _mix(0.5), _mix(0.5), TILE_BOTTOM])
    return small.resize((w, h), Image.BILINEAR).convert("RGBA")


def _mix(t: float) -> tuple[int, int, int]:
    return tuple(round(a + (b - a) * t) for a, b in zip(TILE_TOP, TILE_BOTTOM))


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), radius, fill=255)
    return mask


def fit(image: Image.Image, box: tuple[int, int]) -> Image.Image:
    """Scale to fit inside box, keeping the aspect ratio."""
    scale = min(box[0] / image.width, box[1] / image.height)
    return image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.LANCZOS)


def shadow(image: Image.Image, blur: int, opacity: float) -> Image.Image:
    """A soft dark shadow of image's shape, the same size, to lift the white mascot off the tile."""
    alpha = image.getchannel("A").point(lambda v: round(v * opacity))
    shade = Image.new("RGBA", image.size, (0, 0, 0, 0))
    shade.putalpha(alpha)
    return shade.filter(ImageFilter.GaussianBlur(blur))


def paste_centred(base: Image.Image, image: Image.Image, centre: tuple[int, int], blur: int = 0) -> None:
    x = round(centre[0] - image.width / 2)
    y = round(centre[1] - image.height / 2)
    if blur:
        pad = blur * 3
        padded = Image.new("RGBA", (image.width + pad * 2, image.height + pad * 2), (0, 0, 0, 0))
        padded.alpha_composite(image, (pad, pad))
        base.alpha_composite(shadow(padded, blur, 0.6), (x - pad, y - pad + blur // 2))
    base.alpha_composite(image, (x, y))


def tile(size: int, art: Image.Image, fill: float, radius_ratio: float = 0.0, border: bool = False) -> Image.Image:
    """The art centred on a square Steam-blue tile, boxed like a mod's thumbnail."""
    base = gradient((size, size))
    if border and size >= 48:
        ImageDraw.Draw(base).rounded_rectangle(
            (0, 0, size - 1, size - 1), round(size * radius_ratio),
            outline=(*ACCENT, 110), width=max(1, size // 128))
    art = fit(art, (round(size * fill), round(size * fill)))
    paste_centred(base, art, (size / 2, size / 2), blur=max(0, size // 64))
    base.putalpha(rounded_mask((size, size), round(size * radius_ratio)))
    return base


def icon(mascot: Image.Image, head: Image.Image) -> list[Image.Image]:
    frames = []
    for size in (16, 20, 24, 32, 40, 48, 64, 96, 128, 256):
        if size <= 32:
            frames.append(tile(size * 4, head, 0.86).resize((size, size), Image.LANCZOS))
        else:
            frames.append(tile(size, mascot, 0.86))
    return frames


def workshop_art(mascot: Image.Image) -> Image.Image:
    """512x512, shown at 203x203 at the left of the Workshop banner: mascot over the name."""
    size = 512
    base = tile(size, Image.new("RGBA", (1, 1)), 0.0)
    art = fit(mascot, (330, 330))
    paste_centred(base, art, (size / 2, 205), blur=8)
    draw = ImageDraw.Draw(base)
    _centred_text(draw, "SSPTMM", font("Bold", 64), size / 2, 410, WHITE)
    _centred_text(draw, "Steamified SPT Mod Manager", font("Regular", 26), size / 2, 462, GREY)
    return base


def wide(mascot: Image.Image, size: tuple[int, int], name_px: int, tag_px: int, line_px: int) -> Image.Image:
    """Mascot on the left, the name and one line beside it, the pair centred - README header and
    social preview. The text shrinks to fit rather than run off the edge."""
    w, h = size
    base = gradient(size)
    ImageDraw.Draw(base).rectangle((0, h - 6, w, h), fill=(*ACCENT, 255))
    art = fit(mascot, (w, round(h * 0.82)))
    draw = ImageDraw.Draw(base)

    lines = [("SSPTMM", "Bold", name_px, WHITE),
             ("Steamified SPT Mod Manager", "Medium", tag_px, ACCENT),
             ("Mods for Single Player Tarkov, the Steam Workshop way", "Regular", line_px, GREY)]
    gap, margin = round(h * 0.10), round(w * 0.05)
    widest = max(draw.textlength(text, font=font(face, px)) for text, face, px, _ in lines)
    scale = min(1.0, (w - 2 * margin - art.width - gap) / widest)
    lines = [(text, face, round(px * scale), colour) for text, face, px, colour in lines]
    text_w = max(draw.textlength(text, font=font(face, px)) for text, face, px, _ in lines)

    left = round((w - (art.width + gap + text_w)) / 2)
    paste_centred(base, art, (left + art.width / 2, h / 2), blur=10)

    spacing = [1.12, 1.6, 0]
    block = sum(px * sp for (_, _, px, _), sp in zip(lines, spacing)) + lines[-1][2]
    y = h / 2 - block / 2 - lines[0][2] * 0.12
    x = left + art.width + gap
    for (text, face, px, colour), sp in zip(lines, spacing):
        draw.text((x, y), text, font=font(face, px), fill=colour)
        y += px * sp
    return base


def _centred_text(draw: ImageDraw.ImageDraw, text: str, f: ImageFont.FreeTypeFont, cx: float, cy: float, fill) -> None:
    left, top, right, bottom = draw.textbbox((0, 0), text, font=f)
    draw.text((cx - (right - left) / 2 - left, cy - (bottom - top) / 2 - top), text, font=f, fill=fill)


def main() -> None:
    mascot = Image.open(MASCOT).convert("RGBA")
    mascot = mascot.crop(mascot.getchannel("A").getbbox())
    head = Image.open(MASCOT).convert("RGBA").crop(HEAD_BOX)

    frames = icon(mascot, head)
    ico = ROOT / "src" / "TCFModManager.App" / "Assets" / "AppIcon.ico"
    frames[-1].save(ico, format="ICO", sizes=[f.size for f in frames], append_images=frames[:-1])

    workshop_art(mascot).save(ROOT / "src" / "TCFModManager.App" / "Assets" / "WorkshopBanner.png", optimize=True)

    images = ROOT / "docs" / "images"
    frames[-1].save(images / "ssptmm-icon-256.png", optimize=True)
    wide(mascot, (1280, 320), 96, 34, 26).save(images / "ssptmm-banner.png", optimize=True)
    wide(mascot, (1280, 640), 132, 44, 32).save(images / "ssptmm-social.png", optimize=True)
    print("written:", ico.name, "WorkshopBanner.png, ssptmm-icon-256.png, ssptmm-banner.png, ssptmm-social.png")


if __name__ == "__main__":
    main()
