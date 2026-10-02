"""Read native screenshots and persist reproducible continuous-gaze checks."""
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image

PROJECT = Path(__file__).resolve().parents[1]
ROOT = PROJECT / "bin/validation432"
checks = []
for mode in ("gaze", "gaze-eco"):
    folder = ROOT / mode
    if "PASS" not in (folder / "result.txt").read_text(encoding="utf-8-sig"):
        raise SystemExit(f"Native gaze test did not pass: {mode}")
    for first, second in (("stand-left", "stand-right"), ("sit-left", "sit-right"), ("sit-up", "sit-down")):
        a = np.asarray(Image.open(folder / (first + ".png")).convert("RGBA"))
        b = np.asarray(Image.open(folder / (second + ".png")).convert("RGBA"))
        start = int(a.shape[0] * .60)
        lower = int(np.count_nonzero(np.any(a[start:] != b[start:], axis=2)))
        upper = int(np.count_nonzero(np.any(a[:start] != b[:start], axis=2)))
        corners = all(int(a[y, x, 3]) == int(b[y, x, 3]) == 0 for y, x in ((0, 0), (0, -1), (-1, 0), (-1, -1)))
        if lower or upper < 50 or not corners:
            raise SystemExit(f"Screenshot invariant failed: {mode}/{first}-{second}: {lower}, {upper}, {corners}")
        checks.append(dict(mode=mode, pair=[first, second], lowerBodyChangedPixels=lower, headNeckChangedPixels=upper, transparentCorners=corners))
archive = PROJECT / "assets/generated/yabi-animations-v43.zip"
with archive.open("rb") as stream:
    digest = hashlib.file_digest(stream, "sha256").hexdigest().upper()
expected = "4EF33A60956ADD05008C2533A7FD4AF872357D0C02314D1262BC78E67E9013FE"
if digest != expected:
    raise SystemExit("Original 4.3 asset archive unexpectedly changed")
result = dict(assetArchiveSha256=digest, assetArchiveUnchanged=True, screenshotChecks=checks,
    note="Lower 40% of each native window is pixel-identical across gaze directions. This does not validate real 3D gaze or independently layered eyeballs.")
(ROOT / "continuous-verification.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
