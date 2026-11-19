#!/usr/bin/env python3
"""Generates the Usage Monitor app icons from one design: a dark rounded tile with the
tray icon's usage bars (two bars per account: Claude 5h/7d on top, ChatGPT 5h/7d below).

Requires Pillow (pip install pillow). The generated files are committed, so building the
apps does not need Python:

    windows/app.ico         embedded in UsageMonitor.exe (build.cmd /win32icon)
    macos/AppIcon.icns      copied into UsageMonitor.app by build.sh
    linux/usagemonitor.svg  window and autostart icon for usagemonitor.py

Run from the repository root:  python tools/make_icons.py
The same layout is drawn at run time by AppIcon (windows/src/UpdateForms.cs).
"""
import io
import os
import struct

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

TILE = (44, 44, 44, 255)
TRACK = (80, 80, 80, 255)
GREEN = (63, 185, 80, 255)
ORANGE = (240, 136, 62, 255)
RED = (229, 72, 77, 255)
# Sample usage per bar (Claude 5h, Claude 7d, ChatGPT 5h, ChatGPT 7d) and its level color
BARS = [(0.35, GREEN), (0.62, ORANGE), (0.28, GREEN), (0.85, RED)]
SS = 8  # supersampling: bars stay pixel sharp, the tile corners are smooth


def layout(size):
    """Pixel layout on a 16 px grid scaled by k, rounded so bars land on whole pixels."""
    k = size / 16.0
    m = max(1, round(2 * k))          # side margin
    bh = max(1, round(2 * k))         # bar height
    gap = max(1, round(k))            # between 5h and 7d bar
    group = max(1, round(2 * k))      # between the two accounts
    total = 4 * bh + 2 * gap + group
    y = (size - total) // 2
    return k, m, bh, gap, group, y


def draw(size, inset=0.0):
    """One icon image. inset = transparent margin as a fraction of size (macOS style)."""
    pad = int(round(size * inset))
    inner = size - 2 * pad
    k, m, bh, gap, group, y = layout(inner)
    big = Image.new("RGBA", (inner * SS, inner * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.rounded_rectangle([0, 0, inner * SS - 1, inner * SS - 1], radius=int(3.5 * k * SS), fill=TILE)
    w = inner - 2 * m
    for i, (pct, color) in enumerate(BARS):
        x0, y0 = m * SS, y * SS
        d.rectangle([x0, y0, x0 + w * SS - 1, y0 + bh * SS - 1], fill=TRACK)
        fw = max(1, round(w * pct))
        d.rectangle([x0, y0, x0 + fw * SS - 1, y0 + bh * SS - 1], fill=color)
        y += bh + (group if i == 1 else gap)
    img = big.resize((inner, inner), Image.LANCZOS)
    if pad == 0:
        return img
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(img, (pad, pad))
    return out


def png_bytes(img):
    b = io.BytesIO()
    img.save(b, format="PNG")
    return b.getvalue()


def write_ico(path, sizes):
    """ICO with one PNG-compressed image per size (supported since Windows Vista)."""
    images = [png_bytes(draw(s)) for s in sizes]
    header = struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries, data = b"", b""
    for s, png in zip(sizes, images):
        entries += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(png), offset + len(data))
        data += png
    with open(path, "wb") as f:
        f.write(header + entries + data)


def write_icns(path):
    """ICNS with PNG entries (macOS 10.7+). Apple icons leave ~10% transparent margin."""
    types = [(b"icp4", 16), (b"icp5", 32), (b"icp6", 64), (b"ic07", 128), (b"ic08", 256),
             (b"ic09", 512), (b"ic10", 1024), (b"ic11", 32), (b"ic12", 64), (b"ic13", 256), (b"ic14", 512)]
    body = b""
    for t, s in types:
        png = png_bytes(draw(s, inset=0.1 if s >= 64 else 0.0))
        body += t + struct.pack(">I", len(png) + 8) + png
    with open(path, "wb") as f:
        f.write(b"icns" + struct.pack(">I", len(body) + 8) + body)


def write_svg(path):
    """Scalable version for Linux desktops (same 16 px grid at k = 4)."""
    size = 64
    k, m, bh, gap, group, y = layout(size)
    hexc = lambda c: "#%02x%02x%02x" % c[:3]
    parts = ['<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">',
             '<rect width="64" height="64" rx="%g" fill="%s"/>' % (3.5 * k, hexc(TILE))]
    w = size - 2 * m
    for i, (pct, color) in enumerate(BARS):
        parts.append('<rect x="%d" y="%d" width="%d" height="%d" fill="%s"/>' % (m, y, w, bh, hexc(TRACK)))
        parts.append('<rect x="%d" y="%d" width="%d" height="%d" fill="%s"/>' % (m, y, max(1, round(w * pct)), bh, hexc(color)))
        y += bh + (group if i == 1 else gap)
    parts.append("</svg>")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(parts) + "\n")


if __name__ == "__main__":
    write_ico(os.path.join(ROOT, "windows", "app.ico"), [16, 20, 24, 32, 40, 48, 64, 128, 256])
    write_icns(os.path.join(ROOT, "macos", "AppIcon.icns"))
    write_svg(os.path.join(ROOT, "linux", "usagemonitor.svg"))
    print("OK: windows/app.ico, macos/AppIcon.icns, linux/usagemonitor.svg")
