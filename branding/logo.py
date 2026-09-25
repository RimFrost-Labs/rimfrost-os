#!/usr/bin/env python3
# The RimFrost mark: a pointy-top hexagon of ice with an R cut out of it.
# Three cuts make the R: the counter (hole in the bowl), the notch between
# bowl and leg (from the right edge), and the notch between stem and leg
# (from the bottom). All cut edges run at 0, 60 or 120 degrees, like facets.
# Writes branding/rimfrost-mark.svg (one path) and a preview sheet.
#   python3 branding/logo.py
import math, os
from PIL import Image, ImageDraw
from shapely.geometry import Polygon
from shapely.ops import unary_union

HERE = os.path.dirname(os.path.abspath(__file__))
ICE, NAVY = '#AAD6F0', '#080E1C'
T60 = math.tan(math.radians(60))

C, R = 32.0, 30.0
hexagon = Polygon([(C + R * math.cos(math.radians(d)), C + R * math.sin(math.radians(d)))
                   for d in (-90, -30, 30, 90, 150, 210)])

STEM = 22.0          # right edge of the stem
TOP, WAIST = 13.0, 31.0

# Counter: flat left side on the stem, pointed right side at 60 degrees.
h = (WAIST - TOP) - 8.0          # height of the hole
cy = TOP + 4.0 + h / 2
counter = Polygon([(STEM + 4.0, TOP + 4.0), (38.0, TOP + 4.0),
                   (38.0 + (h / 2) / T60 * 1.0 + 0.0, cy),
                   (38.0, TOP + 4.0 + h), (STEM + 4.0, TOP + 4.0 + h)])
# Notch from the right edge into the waist.
notch_r = Polygon([(70.0, WAIST - 10.0), (44.0, WAIST + 3.0), (70.0, WAIST + 16.0)])
# Notch from the bottom between stem and leg, pointing up to the waist.
apex = (STEM + 4.0, WAIST + 8.0)
notch_b = Polygon([(STEM, 70.0), (STEM, apex[1] + 2.0), apex,
                   (apex[0] + 40.0 / T60, apex[1] + 40.0)])

SLIT = 1.8
# Facet gaps: a vertical one from the top vertex into the counter.
slit_top = Polygon([(C - SLIT / 2, -5.0), (C + SLIT / 2, -5.0),
                    (C + SLIT / 2, TOP + 5.0), (C - SLIT / 2, TOP + 5.0)])
solid = hexagon.difference(unary_union([counter, notch_r, notch_b]))
faceted = solid.difference(slit_top)
import sys
mark = faceted if '--solid' not in sys.argv else solid


def to_path(geom):
    polys = getattr(geom, 'geoms', [geom])
    out = []
    for p in polys:
        for ring in [p.exterior, *p.interiors]:
            xy = list(ring.coords)[:-1]
            out.append('M' + ' L'.join(f'{x:.2f} {y:.2f}' for x, y in xy) + ' Z')
    return ' '.join(out)


def svg(fill=ICE):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">\n'
            f'  <path fill="{fill}" fill-rule="evenodd" d="{to_path(mark)}"/>\n</svg>\n')


def render(size, bg=NAVY, fg=ICE):
    k = 8
    big = Image.new('RGBA', (size * k, size * k), bg)
    d = ImageDraw.Draw(big)
    f = size * k / 64
    for p in getattr(mark, 'geoms', [mark]):
        d.polygon([(x * f, y * f) for x, y in p.exterior.coords], fill=fg)
        for ring in p.interiors:
            d.polygon([(x * f, y * f) for x, y in ring.coords], fill=bg)
    return big.resize((size, size), Image.LANCZOS)


SPLASH = os.path.join(HERE, '..', 'system_files/usr/share/plasma/look-and-feel/org.rimfrost.splash/contents/splash/images')


def glow(size=512, rgb=(22, 50, 79)):
    # soft radial glow for the splash: full colour in the middle, gone at the rim
    img = Image.new('RGBA', (size, size), rgb + (0,))
    px = img.load()
    c = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            r = math.hypot(x - c, y - c) / c
            a = max(0.0, 1.0 - r) ** 2.2
            px[x, y] = rgb + (int(255 * a),)
    return img


if __name__ == '__main__':
    open(os.path.join(HERE, 'rimfrost-mark.svg'), 'w').write(svg())
    os.makedirs(SPLASH, exist_ok=True)
    open(os.path.join(SPLASH, 'rimfrost-mark.svg'), 'w').write(svg())
    glow().save(os.path.join(SPLASH, 'glow.png'))
    sheet = Image.new('RGBA', (1000, 512), '#FFFFFF')
    sheet.paste(render(512), (0, 0))
    x = 530
    for n in (128, 64, 32, 16):
        sheet.paste(render(n), (x, 40))
        sheet.paste(render(n, '#FFFFFF', NAVY), (x, 300))
        x += n + 24
    sheet.save(os.path.join(HERE, 'mark-preview.png'))
    cmp = Image.new('RGBA', (1060, 560), NAVY)
    for i, m in enumerate((solid, faceted)):
        mark = m
        cmp.paste(render(400), (40 + i * 520, 20))
        cmp.paste(render(32), (40 + i * 520 + 150, 480))
        cmp.paste(render(16), (40 + i * 520 + 210, 488))
    cmp.save(os.path.join(HERE, 'mark-compare.png'))
    mark = faceted
    print('ok')
