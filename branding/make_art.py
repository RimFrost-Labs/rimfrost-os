#!/usr/bin/env python3
# Draws RimFrost OS artwork: the wallpaper (rime crystals growing in from the
# edges of a dark ice-blue sky) and the logo mark (a six-armed rime crystal).
#   python3 branding/make_art.py   -> writes into system_files/usr/share/...
import math, os, random
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
WALL_DIR = os.path.join(ROOT, 'system_files/usr/share/wallpapers/RimFrost/contents/images')
LOGO_DIR = os.path.join(ROOT, 'system_files/usr/share/pixmaps')

DEEP = (8, 14, 28)
MID = (18, 42, 74)
ICE = (170, 214, 240)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def branch(draw, x, y, ang, length, width, depth, rng, color):
    """One crystal arm: a straight spine with side branches at 60 degrees."""
    if depth == 0 or length < 3:
        return
    x2 = x + math.cos(ang) * length
    y2 = y + math.sin(ang) * length
    draw.line((x, y, x2, y2), fill=color, width=max(1, int(width)))
    steps = rng.randint(3, 5)
    for i in range(1, steps + 1):
        t = i / (steps + 1)
        bx, by = x + (x2 - x) * t, y + (y2 - y) * t
        sub = length * (0.45 - 0.25 * t) * rng.uniform(0.8, 1.1)
        for side in (-1, 1):
            branch(draw, bx, by, ang + side * math.pi / 3, sub, width * 0.6, depth - 1, rng, color)


def crystal(draw, cx, cy, radius, width, rng, color, rot=0.0, depth=3):
    for k in range(6):
        branch(draw, cx, cy, rot + k * math.pi / 3, radius, width, depth, rng, color)


def wallpaper(w, h, seed=7):
    rng = random.Random(seed)
    img = Image.new('RGB', (w, h))
    px = img.load()
    # vertical sky gradient, a little lighter towards the lower edge
    for y in range(h):
        t = y / (h - 1)
        c = lerp(DEEP, MID, t ** 1.6)
        for x in range(w):
            px[x, y] = c
    glow = Image.new('L', (w, h), 0)
    gd = ImageDraw.Draw(glow)
    gd.ellipse((-w * 0.2, h * 0.55, w * 1.2, h * 1.6), fill=90)
    glow = glow.filter(ImageFilter.GaussianBlur(w // 12))
    img = Image.composite(Image.new('RGB', (w, h), (40, 88, 130)), img, glow)

    frost = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    fd = ImageDraw.Draw(frost)
    s = w / 3840
    # rime creeping in from the lower edge and the corners
    for _ in range(26):
        x = rng.uniform(-0.05, 1.05) * w
        y = h + rng.uniform(-0.04, 0.02) * h
        ang = -math.pi / 2 + rng.uniform(-0.7, 0.7)
        a = rng.randint(60, 150)
        branch(fd, x, y, ang, rng.uniform(260, 620) * s, 5 * s, 4, rng, ICE + (a,))
    for cx, cy in ((0, 0), (w, 0)):
        for _ in range(9):
            ang = math.atan2(h / 2 - cy, w / 2 - cx) + rng.uniform(-0.6, 0.6)
            branch(fd, cx, cy, ang, rng.uniform(200, 460) * s, 4 * s, 4, rng, ICE + (rng.randint(40, 90),))
    # a few free crystals, soft and far away
    for _ in range(14):
        crystal(fd, rng.uniform(0, w), rng.uniform(0, h * 0.8), rng.uniform(18, 60) * s, 2 * s, rng,
                ICE + (rng.randint(25, 60),), rng.uniform(0, math.pi))
    soft = frost.filter(ImageFilter.GaussianBlur(6 * s))
    img = img.convert('RGBA')
    img.alpha_composite(soft)
    img.alpha_composite(frost)
    return img.convert('RGB')


def logo(size, seed=3):
    rng = random.Random(seed)
    big = size * 4
    img = Image.new('RGBA', (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = big * 0.46
    crystal(d, big / 2, big / 2, r, big * 0.035, rng, ICE + (255,), -math.pi / 2, depth=2)
    return img.resize((size, size), Image.LANCZOS)


if __name__ == '__main__':
    os.makedirs(WALL_DIR, exist_ok=True)
    os.makedirs(LOGO_DIR, exist_ok=True)
    for w, h in ((3840, 2160), (2560, 1440), (1920, 1080), (3440, 1440)):
        wallpaper(w, h).save(os.path.join(WALL_DIR, f'{w}x{h}.png'), optimize=True)
        print('wallpaper', w, h)
    for n in (256, 64):
        logo(n).save(os.path.join(LOGO_DIR, f'rimfrost-logo-{n}.png'))
    logo(256).save(os.path.join(LOGO_DIR, 'rimfrost-logo.png'))
    print('logo done')
