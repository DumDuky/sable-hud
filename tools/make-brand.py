from PIL import Image, ImageDraw, ImageFont
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "brand")
os.makedirs(OUT, exist_ok=True)

BG = (8, 8, 10)
INK = (244, 244, 246)
DIM = (150, 150, 156)
MUTED = (110, 110, 116)
HAIR = (34, 34, 38)
TILE = (17, 17, 19)

FONT_SB = "C:/Windows/Fonts/seguisb.ttf"
FONT_R = "C:/Windows/Fonts/segoeui.ttf"
FONT_L = "C:/Windows/Fonts/segoeuil.ttf"
FONT_M = "C:/Windows/Fonts/consola.ttf"

def f(path, size):
    return ImageFont.truetype(path, size)

def rounded(draw, box, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)

def mark(size):
    """The Sable mark: a white tile with an ink check."""
    s = size * 4
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rounded(d, (0, 0, s - 1, s - 1), int(s * 0.28), fill=(255, 255, 255, 255))
    w = int(s * 0.075)
    pts = [(s * 0.27, s * 0.52), (s * 0.44, s * 0.68), (s * 0.74, s * 0.34)]
    d.line(pts, fill=(10, 10, 12, 255), width=w, joint="curve")
    r = w / 2
    for x, y in (pts[0], pts[-1]):
        d.ellipse((x - r, y - r, x + r, y + r), fill=(10, 10, 12, 255))
    return img.resize((size, size), Image.LANCZOS)

def keycap(draw, xy, label, font):
    x, y = xy
    pad_x, pad_y = 14, 7
    tw = draw.textlength(label, font=font)
    th = font.size
    box = (x, y, x + tw + pad_x * 2, y + th + pad_y * 2)
    rounded(draw, box, 8, fill=(14, 14, 16), outline=(40, 40, 44), width=1)
    draw.text((x + pad_x, y + pad_y - 1), label, font=font, fill=(190, 190, 196))
    return box[2]

def screenshot_card(path, width, radius=18):
    shot = Image.open(path).convert("RGBA")
    h = round(shot.height * width / shot.width)
    shot = shot.resize((width, h), Image.LANCZOS)
    mask = Image.new("L", (width, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, width - 1, h - 1), radius=radius, fill=255)
    card = Image.new("RGBA", (width + 2, h + 2), (0, 0, 0, 0))
    card.paste(shot, (1, 1), mask)
    d = ImageDraw.Draw(card)
    rounded(d, (0, 0, width + 1, h + 1), radius + 1, outline=(44, 44, 48, 255), width=1)
    return card

def banner(w=1600, h=600, shot_w=640):
    img = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(img)
    rounded(d, (28, 28, w - 28, h - 28), 30, outline=HAIR, width=2)

    logo = mark(72)
    img.paste(logo, (96, 150), logo)
    d.text((190, 158), "Sable", font=f(FONT_SB, 74), fill=INK)
    sable_w = d.textlength("Sable", font=f(FONT_SB, 74))
    d.text((190 + sable_w + 18, 158), "HUD", font=f(FONT_L, 74), fill=DIM)

    d.text((96, 282), "The quiet, controller-first HUD for Windows 11.",
           font=f(FONT_R, 30), fill=(206, 206, 212))
    d.text((96, 334), "No launcher. No Electron. No neon.",
           font=f(FONT_R, 21), fill=MUTED)

    x = 96
    kf = f(FONT_M, 16)
    for label in ("Guide", "Ctrl + Alt + H"):
        x = keycap(d, (x, 414), label, kf) + 12
    d.text((x + 4, 420), "to open and close", font=f(FONT_R, 18), fill=MUTED)

    card = screenshot_card(os.path.join(ROOT, "docs", "screenshots", "hud-music.png"), shot_w)
    img.paste(card, (w - 28 - 44 - card.width, (h - card.height) // 2), card)
    return img

def social(w=1280, h=640):
    img = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(img)
    rounded(d, (24, 24, w - 24, h - 24), 26, outline=HAIR, width=2)

    logo = mark(64)
    img.paste(logo, (76, 92), logo)
    d.text((158, 98), "Sable", font=f(FONT_SB, 64), fill=INK)
    sw = d.textlength("Sable", font=f(FONT_SB, 64))
    d.text((158 + sw + 16, 98), "HUD", font=f(FONT_L, 64), fill=DIM)

    d.text((76, 206), "The quiet, controller-first", font=f(FONT_R, 30), fill=(210, 210, 216))
    d.text((76, 246), "HUD for Windows 11.", font=f(FONT_R, 30), fill=(210, 210, 216))
    d.text((76, 318), "Music, performance, audio and tools.", font=f(FONT_R, 22), fill=MUTED)
    d.text((76, 350), "One press of the Guide button.", font=f(FONT_R, 22), fill=MUTED)

    card = screenshot_card(os.path.join(ROOT, "docs", "screenshots", "hud-music.png"), 700)
    img.paste(card, (w - 24 - card.width - 28, (h - card.height) // 2 + 8), card)
    return img

b = banner()
b.save(os.path.join(OUT, "banner.png"), optimize=True)
s = social()
s.save(os.path.join(OUT, "social-preview.png"), optimize=True)
print("banner", b.size, "social", s.size)
