"""Generate polished installer banner BMPs for Inno Setup.

Uses PIL to render:
 - WizardImage: tall left-side banner (164x314 + 2x). Vertical gradient,
   centered logo, product name, and tagline.
 - WizardSmallImage: header thumbnail (55x55 + 2x). Just the logo on a card.

Inno Setup expects 24-bit BMP with alpha-flat (BMP has no alpha, use flat bg).
"""
from PIL import Image, ImageDraw, ImageFont
import os

ROOT = r"C:\Users\shahi\Downloads\Compressed\windows"
LOGO = os.path.join(ROOT, "logo.png")
OUT = os.path.join(ROOT, "installer")

# Brand colors - deep bluish-purple gradient (BanglaHost feel)
TOP    = (78,  46,  185)   # deep purple
BOTTOM = (33,  20,  90)    # near-navy
ACCENT = (255, 205, 90)    # warm gold accent line

def vertical_gradient(w, h, top, bottom):
    img = Image.new("RGB", (w, h), top)
    draw = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        r = int(top[0] * (1 - t) + bottom[0] * t)
        g = int(top[1] * (1 - t) + bottom[1] * t)
        b = int(top[2] * (1 - t) + bottom[2] * t)
        draw.line([(0, y), (w, y)], fill=(r, g, b))
    return img

def load_font(size):
    for name in ("segoeuisemibold.ttf", "segoeui.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            pass
    return ImageFont.load_default()

def render_wizard(w, h, out_path):
    img = vertical_gradient(w, h, TOP, BOTTOM)
    draw = ImageDraw.Draw(img, "RGBA")

    # subtle radial highlight top-left
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for r in range(w, 0, -8):
        alpha = max(0, 40 - r // 6)
        gd.ellipse([-r // 2, -r // 2, r // 2, r // 2], fill=(255, 255, 255, alpha))
    img.paste(Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB"))

    # thin accent line lower third
    draw = ImageDraw.Draw(img)
    yaccent = int(h * 0.62)
    draw.rectangle([int(w * 0.18), yaccent, int(w * 0.82), yaccent + 2], fill=ACCENT)

    # logo (centered in upper half)
    logo = Image.open(LOGO).convert("RGBA")
    target = int(w * 0.62)
    lw, lh = logo.size
    scale = target / max(lw, lh)
    logo = logo.resize((int(lw * scale), int(lh * scale)), Image.LANCZOS)
    lx = (w - logo.size[0]) // 2
    ly = int(h * 0.14)
    img.paste(logo, (lx, ly), logo)

    # product name
    title_size = int(w * 0.15)
    tag_size   = int(w * 0.075)
    tf = load_font(title_size)
    sf = load_font(tag_size)

    draw = ImageDraw.Draw(img)
    title = "BanglaHost"
    tw = draw.textlength(title, font=tf)
    draw.text(((w - tw) / 2 + 1, int(h * 0.66) + 1), title, font=tf, fill=(0, 0, 0, 180))
    draw.text(((w - tw) / 2,     int(h * 0.66)),     title, font=tf, fill=(255, 255, 255))

    tagline = "Local Web Stack for Windows"
    sw = draw.textlength(tagline, font=sf)
    draw.text(((w - sw) / 2, int(h * 0.78)), tagline, font=sf, fill=(220, 214, 240))

    # footer credit
    ff = load_font(int(w * 0.05))
    cred = "by Mohammad Sheikh Shahinur Rahman"
    cw = draw.textlength(cred, font=ff)
    draw.text(((w - cw) / 2, h - int(w * 0.13)), cred, font=ff, fill=(180, 175, 210))

    img.convert("RGB").save(out_path, "BMP")
    print(f"wrote {out_path} {w}x{h}")

def render_small(sz, out_path):
    # Card-style: soft rounded square with logo
    img = Image.new("RGB", (sz, sz), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    # subtle purple tint bottom
    for y in range(sz):
        t = y / max(1, sz - 1)
        r = int(255 * (1 - t * 0.06))
        g = int(255 * (1 - t * 0.07))
        b = int(255 * (1 - t * 0.02))
        draw.line([(0, y), (sz, y)], fill=(r, g, b))
    logo = Image.open(LOGO).convert("RGBA")
    pad = max(2, sz // 12)
    target = sz - pad * 2
    lw, lh = logo.size
    scale = target / max(lw, lh)
    logo = logo.resize((int(lw * scale), int(lh * scale)), Image.LANCZOS)
    img.paste(logo, ((sz - logo.size[0]) // 2, (sz - logo.size[1]) // 2), logo)
    img.save(out_path, "BMP")
    print(f"wrote {out_path} {sz}x{sz}")

render_wizard(164, 314, os.path.join(OUT, "WizardImage.bmp"))
render_wizard(192, 386, os.path.join(OUT, "WizardImage-2x.bmp"))
render_small(55,  os.path.join(OUT, "WizardSmallImage.bmp"))
render_small(64,  os.path.join(OUT, "WizardSmallImage-2x.bmp"))
print("done")
