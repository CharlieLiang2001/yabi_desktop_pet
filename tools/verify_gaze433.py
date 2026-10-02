"""Verify alpha, fixed lower-body pixels and native desktop evidence."""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT / "bin/validation433"
checks = []
for mode in ("gaze", "gaze-eco"):
    folder = ROOT / mode
    result = (folder / "result.txt").read_text(encoding="utf-8-sig")
    if not result.startswith("Yabi 4.3.3 continuous gaze integration: PASS"):
        raise SystemExit(f"Native integration failed: {mode}\n{result}")
    for name in ("stand-center", "stand-left", "stand-right", "stand-mirrored",
                 "sit-left", "sit-right", "sit-up", "sit-down", "sit-disabled"):
        pixels = np.asarray(Image.open(folder / (name + ".png")).convert("RGBA"))
        alpha = pixels[:, :, 3]
        solid = int(np.count_nonzero(alpha >= 240))
        ghost = int(np.count_nonzero((alpha == 0) & np.any(pixels[:, :, :3] != 0, axis=2)))
        corners = all(alpha[y, x] == 0 for y, x in ((0, 0), (0, -1), (-1, 0), (-1, -1)))
        if solid < alpha.size * .15 or ghost or not corners:
            raise SystemExit(f"Invalid opacity: {mode}/{name}, solid={solid}, ghostRGB={ghost}, corners={corners}")
        checks.append(dict(mode=mode, screenshot=name, opaqueCatPixels=solid, zeroAlphaColorPixels=ghost))
    for first, second in (("stand-left", "stand-right"), ("sit-left", "sit-right"), ("sit-up", "sit-down")):
        a = np.asarray(Image.open(folder / (first + ".png")).convert("RGBA"))
        b = np.asarray(Image.open(folder / (second + ".png")).convert("RGBA"))
        boundary = int(a.shape[0] * .60)
        lower = int(np.count_nonzero(np.any(a[boundary:] != b[boundary:], axis=2)))
        upper = int(np.count_nonzero(np.any(a[:boundary] != b[:boundary], axis=2)))
        if lower != 0 or upper < 100:
            raise SystemExit(f"Gaze deformation invariant failed: {mode}/{first}/{second}: {lower}, {upper}")
        checks.append(dict(mode=mode, pair=[first, second], lowerBodyChangedPixels=lower, headNeckChangedPixels=upper))
    for name in ("desktop-dark", "desktop-light", "desktop-opacity60"):
        if not (folder / (name + ".png")).is_file():
            raise SystemExit(f"Missing actual desktop capture: {mode}/{name}")

with (PROJECT / "assets/generated/yabi-animations-v43.zip").open("rb") as stream:
    digest = hashlib.file_digest(stream, "sha256").hexdigest().upper()
if digest != "4EF33A60956ADD05008C2533A7FD4AF872357D0C02314D1262BC78E67E9013FE":
    raise SystemExit("Original photo/video archive changed")
old = np.asarray(Image.open(PROJECT / "bin/validation432/gaze/stand-center.png").convert("RGBA"))
report = dict(
    date="2026-09-15", assetArchiveSha256=digest, originalAssetsUnchanged=True,
    baseline432=dict(alphaMaximum=int(old[:, :, 3].max()),
                     coloredPixelsWithZeroAlpha=int(np.count_nonzero((old[:, :, 3] == 0) & np.any(old[:, :, :3] != 0, axis=2)))),
    checks=checks,
    limits="Photo deformation remains an approximation, not independent eyeball rotation. Desktop captures only cover this machine."
)
(ROOT / "gaze-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=False, indent=2))
