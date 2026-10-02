#!/usr/bin/env python3
"""Build the deterministic transparent asset pack used by Yabi Desktop Pet 4.2.

The source media is intentionally never modified.  This script extracts video
frames at 12 fps, estimates each pale studio background from border samples,
builds a soft connected foreground matte, normalises all portrait poses to one
320 x 480 stage, and writes a self-describing ZIP for the WPF renderer.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import subprocess
import sys
import zipfile
from collections import deque
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple
from xml.etree import ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps

try:
    import onnxruntime as ort
except ImportError:  # pragma: no cover - reported as a clear build error in main().
    ort = None


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "source"
GENERATED = ROOT / "assets" / "generated"
RAW = GENERATED / "raw"
PACK = GENERATED / "pack"
PREVIEWS = GENERATED / "previews"
PORTRAIT_SIZE = (320, 480)
SLEEP_SIZE = (480, 320)
TRANSITION_SIZE = (480, 480)
FPS = 24
VIDEO_MATTE_SIZE = (480, 720)
GREEN_VIDEO_HEIGHT = 720
PORTRAIT_SUBJECT_OFFSET_X = -34
DEFAULT_SEGMENTATION_MODEL = ROOT.parents[1] / "work" / "models" / "u2net.onnx"
U2NET_MODEL_MD5 = "60024c5c889badc19c04ad937298a77b"
SEGMENTATION_SESSION = None

# The studio shadows below the curled tails contain brown AI/compression bleed,
# so colour segmentation alone cannot distinguish them from dark tail stripes.
# These small, final-stage guide masks trace only the affected lower-right area;
# the rest of every subject remains fully data-driven.
SIT_TAIL_REGION = (188, 340, 286, 426)
SIT_TAIL_POLYGON = [
    (184, 334), (286, 334), (286, 382), (278, 389), (266, 393),
    (250, 396), (232, 397), (214, 398), (200, 399), (190, 402),
]
LOOK_TAIL_REGION = (200, 340, 292, 420)
LOOK_TAIL_POLYGON = [
    (196, 334), (292, 334), (292, 382), (282, 390), (268, 395),
    (250, 398), (232, 399), (216, 401), (204, 405), (200, 409),
]
SITTING_VIDEO_LOWER_REGION = (54, 295, 300, 438)
SITTING_VIDEO_LOWER_POLYGON = [
    (54, 292), (300, 292), (300, 382), (292, 391), (278, 397),
    (260, 401), (240, 403), (220, 404), (202, 407), (182, 411),
    (160, 414), (132, 416), (104, 416), (80, 414), (64, 409), (54, 402),
]
SITTING_VIDEO_CONTACT_REGION = (100, 388, 282, 430)
SITTING_VIDEO_CONTACT_POLYGON = [
    (96, 384), (282, 384), (282, 397), (266, 400), (248, 400),
    (232, 399), (218, 397), (204, 398), (190, 405), (176, 412),
    (162, 416), (146, 417), (130, 416), (116, 413), (104, 408),
    (100, 402),
]
LOOK_STATIC_TAIL_POLYGON = [
    (165, 372), (195, 354), (235, 354), (270, 365), (282, 385),
    (282, 425), (165, 425),
]
SLEEP_TAIL_REGION = (242, 190, 472, 282)
SLEEP_TAIL_POLYGON = [
    (238, 186), (464, 186), (464, 228), (458, 237), (446, 244),
    (430, 248), (406, 251), (378, 253), (346, 253), (314, 252),
    (284, 251), (260, 248), (246, 243), (242, 238),
]

SIT_SOURCE_TAIL_REGION = (545, 1110, 925, 1320)
SIT_SOURCE_TAIL_POLYGON = [
    (520, 1060), (930, 1060), (930, 1208), (914, 1234), (890, 1254),
    (854, 1270), (812, 1282), (766, 1290), (718, 1295), (670, 1295),
    (626, 1291), (590, 1284), (556, 1274), (530, 1260), (520, 1240),
]
SLEEP_SOURCE_TAIL_REGION = (510, 840, 1024, 1110)
SLEEP_SOURCE_TAIL_POLYGON = [
    (480, 790), (960, 790), (988, 814), (1002, 850), (1008, 900),
    (1008, 950), (1002, 1000), (984, 1032), (958, 1052), (928, 1064),
    (902, 1070), (854, 1079), (802, 1086), (750, 1090), (698, 1092),
    (646, 1090), (598, 1086), (558, 1078), (530, 1066), (514, 1048),
]
SIT_SOURCE_TAIL_BLEND = [
    (520, 1060), (930, 1060), (930, 1215), (910, 1248), (860, 1278),
    (790, 1298), (700, 1304), (620, 1298), (550, 1280), (520, 1255),
]
SLEEP_SOURCE_TAIL_BLEND = [
    (480, 790), (960, 790), (994, 820), (1014, 880), (1014, 970),
    (998, 1026), (958, 1066), (870, 1088), (760, 1100), (650, 1100),
    (560, 1088), (500, 1060),
]


CLIPS = {
    "stand_blink": SOURCE / "stand_blink.mp4",
    "stand_to_sit": SOURCE / "stand_to_sit.mp4",
    "sit_lookaround": SOURCE / "sit_head_turn.mp4",
    "sit_to_sleep": SOURCE / "sit_to_sleep.mp4",
    "sleep_to_sit": SOURCE / "sleep_to_sit.mp4",
}


def run(command: Sequence[str]) -> None:
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if result.returncode:
        raise RuntimeError(
            "Command failed ({0}):\n{1}\n{2}".format(result.returncode, " ".join(command), result.stderr)
        )


def reset_generated() -> None:
    resolved = GENERATED.resolve()
    if resolved.parent != (ROOT / "assets").resolve():
        raise RuntimeError("Refusing to clean an unexpected generated directory: {0}".format(resolved))
    if GENERATED.exists():
        shutil.rmtree(GENERATED)
    RAW.mkdir(parents=True)
    PACK.mkdir(parents=True)
    PREVIEWS.mkdir(parents=True)


def extract_video_frames(ffmpeg: str, clip_id: str, source: Path) -> List[Path]:
    target = RAW / clip_id
    target.mkdir(parents=True)
    run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source),
            "-vf",
            "fps={0}".format(FPS),
            "-vsync",
            "0",
            str(target / "%04d.png"),
            "-y",
        ]
    )
    frames = sorted(target.glob("*.png"))
    if not frames:
        raise RuntimeError("No frames extracted for {0}".format(clip_id))
    return frames


def fit_stage(image: Image.Image, size: Tuple[int, int]) -> Image.Image:
    """Centre-crop to the stage ratio, then downsample with a high quality filter."""
    return ImageOps.fit(image.convert("RGB"), size, Image.Resampling.LANCZOS, centering=(0.5, 0.5))


def configure_segmentation_model(path: Path) -> None:
    """Load the offline U2Net builder model once; it is never embedded in the EXE."""
    global SEGMENTATION_SESSION
    if ort is None:
        raise RuntimeError("onnxruntime is required to rebuild the transparent assets")
    if not path.exists():
        raise RuntimeError("offline segmentation model is missing: {0}".format(path))
    digest = hashlib.md5(path.read_bytes()).hexdigest()
    if digest != U2NET_MODEL_MD5:
        raise RuntimeError(
            "unexpected U2Net model checksum: {0} (expected {1})".format(
                digest, U2NET_MODEL_MD5
            )
        )
    SEGMENTATION_SESSION = ort.InferenceSession(
        str(path), providers=["CPUExecutionProvider"]
    )


def neural_foreground_alpha(image: Image.Image) -> np.ndarray:
    """Return U2Net's full-size soft subject alpha for one RGB image."""
    if SEGMENTATION_SESSION is None:
        raise RuntimeError("segmentation model has not been configured")
    source = image.convert("RGB")
    small = source.resize((320, 320), Image.Resampling.LANCZOS)
    tensor = np.asarray(small, dtype=np.float32) / 255.0
    tensor = (
        tensor - np.array([0.485, 0.456, 0.406], dtype=np.float32)
    ) / np.array([0.229, 0.224, 0.225], dtype=np.float32)
    tensor = tensor.transpose(2, 0, 1)[None]
    input_name = SEGMENTATION_SESSION.get_inputs()[0].name
    prediction = SEGMENTATION_SESSION.run(None, {input_name: tensor})[0][0, 0]
    minimum = float(prediction.min())
    maximum = float(prediction.max())
    prediction = (prediction - minimum) / max(1e-6, maximum - minimum)
    alpha = Image.fromarray(
        np.uint8(np.clip(prediction, 0.0, 1.0) * 255.0), "L"
    ).resize(source.size, Image.Resampling.LANCZOS)
    result = np.asarray(alpha, dtype=np.uint8).copy()
    result[result < 3] = 0
    result[result > 251] = 255
    return result


def polynomial_background(rgb: np.ndarray) -> np.ndarray:
    """Estimate a smooth studio backdrop from an outer border using a quadratic surface."""
    height, width, _ = rgb.shape
    band = max(5, int(min(width, height) * 0.035))
    yy, xx = np.mgrid[0:height, 0:width]
    border = (xx < band) | (xx >= width - band) | (yy < band) | (yy >= height - band)

    # A sparse sample is enough and keeps the per-frame solve cheap.
    sample = border & ((xx % 2 == 0) & (yy % 2 == 0))
    x = (xx[sample].astype(np.float32) / max(1, width - 1)) * 2.0 - 1.0
    y = (yy[sample].astype(np.float32) / max(1, height - 1)) * 2.0 - 1.0
    design = np.stack([np.ones_like(x), x, y, x * x, y * y, x * y], axis=1)
    values = rgb[sample].astype(np.float32)

    # Iteratively reject border outliers such as a watermark.
    keep = np.ones(len(x), dtype=bool)
    coefficients = None
    for _ in range(3):
        coefficients, _, _, _ = np.linalg.lstsq(design[keep], values[keep], rcond=None)
        residual = np.sqrt(np.sum((design @ coefficients - values) ** 2, axis=1))
        cutoff = max(7.0, float(np.percentile(residual[keep], 88)))
        keep = residual <= cutoff

    gx = (xx.astype(np.float32) / max(1, width - 1)) * 2.0 - 1.0
    gy = (yy.astype(np.float32) / max(1, height - 1)) * 2.0 - 1.0
    full = np.stack([np.ones_like(gx), gx, gy, gx * gx, gy * gy, gx * gy], axis=2)
    return np.clip(full @ coefficients, 0, 255)


def mask_filter(mask: np.ndarray, filter_object: ImageFilter.Filter) -> np.ndarray:
    image = Image.fromarray((mask.astype(np.uint8) * 255), "L")
    return np.asarray(image.filter(filter_object), dtype=np.uint8) > 127


def component_from_seed(mask: np.ndarray, seed: Tuple[int, int]) -> np.ndarray:
    """Return the eight-connected component containing seed."""
    height, width = mask.shape
    sy, sx = seed
    if not mask[sy, sx]:
        return np.zeros_like(mask, dtype=bool)
    result = np.zeros_like(mask, dtype=bool)
    result[sy, sx] = True
    queue: deque[Tuple[int, int]] = deque([(sy, sx)])
    while queue:
        y, x = queue.popleft()
        for ny in range(max(0, y - 1), min(height, y + 2)):
            for nx in range(max(0, x - 1), min(width, x + 2)):
                if mask[ny, nx] and not result[ny, nx]:
                    result[ny, nx] = True
                    queue.append((ny, nx))
    return result


def fill_holes(mask: np.ndarray) -> np.ndarray:
    """Fill holes while leaving the exterior untouched."""
    height, width = mask.shape
    exterior = np.zeros_like(mask, dtype=bool)
    queue: deque[Tuple[int, int]] = deque()
    for x in range(width):
        if not mask[0, x]:
            exterior[0, x] = True
            queue.append((0, x))
        if not mask[height - 1, x]:
            exterior[height - 1, x] = True
            queue.append((height - 1, x))
    for y in range(height):
        if not mask[y, 0] and not exterior[y, 0]:
            exterior[y, 0] = True
            queue.append((y, 0))
        if not mask[y, width - 1] and not exterior[y, width - 1]:
            exterior[y, width - 1] = True
            queue.append((y, width - 1))
    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < height and 0 <= nx < width and not mask[ny, nx] and not exterior[ny, nx]:
                exterior[ny, nx] = True
                queue.append((ny, nx))
    return mask | (~exterior)


def despill(rgb: np.ndarray, background: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """Reduce pale backdrop contamination only on semi-transparent edge pixels."""
    fraction = (alpha.astype(np.float32) / 255.0)[..., None]
    edge = ((alpha >= 32) & (alpha < 245))[..., None]
    safe = np.maximum(fraction, 0.18)
    estimated = (rgb.astype(np.float32) - background * (1.0 - fraction)) / safe
    # Very faint floor-shadow pixels must not be colour-unmixed as if they were
    # fur.  That turns a soft grey contact shadow into an opaque-looking black
    # fringe on the desktop.  Ramp despill in only as the matte becomes solid.
    strength = np.clip((fraction - 0.12) / 0.55, 0.0, 1.0) * 0.16
    corrected = rgb.astype(np.float32) * (1.0 - strength) + estimated * strength
    mixed = np.where(edge, corrected, rgb.astype(np.float32))
    return np.clip(mixed, 0, 255).astype(np.uint8)


def trim_lower_contact_matte(
    alpha: np.ndarray,
    component: np.ndarray,
    chroma_excess: np.ndarray,
    colour_distance: np.ndarray,
    darker: np.ndarray,
) -> np.ndarray:
    """Remove attached floor shadow below paws and tails without cutting fur.

    A floor shadow can be eight-connected to the animal, so connected-component
    selection alone cannot reject it.  The supplied cats have warm, textured
    fur while the studio shadow is pale/neutral.  Build a conservative fur core,
    keep a four-pixel soft ring around it, and apply that restriction only to the
    lower part of the subject.  Dark tail tips are explicitly retained even when
    their chroma is low.
    """
    ys, _ = np.where(component)
    if len(ys) == 0:
        return alpha

    top = int(ys.min())
    bottom = int(ys.max())
    lower_start = top + int((bottom - top + 1) * 0.54)
    yy = np.arange(alpha.shape[0], dtype=np.int32)[:, None]
    lower = yy >= lower_start

    fur_core = component & (
        (chroma_excess >= 13.0)
        | ((darker >= 66.0) & (colour_distance >= 74.0))
    )
    solid_near = mask_filter(fur_core, ImageFilter.MaxFilter(5))
    allowed = mask_filter(fur_core, ImageFilter.MaxFilter(9))

    result = alpha.copy()
    result[lower & ~allowed] = 0
    # Preserve a narrow anti-aliased/furry edge, but never restore the broad
    # opaque ring created by the component close/fill operations.
    fringe = lower & allowed & ~solid_near
    result[fringe] = np.minimum(result[fringe], 112)

    # A symmetric dilation also grows a few pixels down into the floor.  Clamp
    # the lower contour per column to the last high-confidence fur pixel.  The
    # horizontal maximum bridges small gaps between toes and individual tail
    # hairs, while the vertical allowance keeps a two-to-three pixel soft edge.
    height, width = alpha.shape
    contour = np.full(width, -1, dtype=np.int32)
    for x in range(width):
        column = np.flatnonzero(fur_core[:, x])
        if len(column):
            contour[x] = int(column[-1])
    radius = max(2, int(round(height / 240.0)))
    padded = np.pad(contour, (radius, radius), mode="constant", constant_values=-1)
    contour = np.maximum.reduce(
        [padded[offset : offset + width] for offset in range(radius * 2 + 1)]
    )
    distance = yy - contour[None, :]
    has_contour = contour[None, :] >= 0
    beyond = lower & has_contour & (distance > radius)
    result[beyond] = 0
    contour_fringe = lower & has_contour & (distance > 0) & (distance <= radius)
    contour_cap = np.clip((radius + 1 - distance) * (128.0 / radius), 0, 128).astype(np.uint8)
    result[contour_fringe] = np.minimum(result[contour_fringe], contour_cap[contour_fringe])
    return result


def remove_background(image: Image.Image, trim_contact: bool = True) -> Image.Image:
    """Create a soft matte from neural subject segmentation plus edge evidence.

    U2Net separates the curled/dark tails from the almost identical studio
    shadow much more reliably than colour thresholds.  The legacy colour matte
    is retained only as a narrow, subject-adjacent whisker/fur detail layer; it
    can no longer grow a floor shadow or punch holes in a tail.
    """
    rgb = np.asarray(image.convert("RGB"), dtype=np.uint8)
    rgbf = rgb.astype(np.float32)
    background = polynomial_background(rgbf)

    delta = rgbf - background
    colour_distance = np.sqrt(np.sum(delta * delta, axis=2))
    chroma = rgbf.max(axis=2) - rgbf.min(axis=2)
    background_chroma = background.max(axis=2) - background.min(axis=2)
    chroma_excess = chroma - background_chroma
    luminance = rgbf[..., 0] * 0.2126 + rgbf[..., 1] * 0.7152 + rgbf[..., 2] * 0.0722
    bg_luminance = background[..., 0] * 0.2126 + background[..., 1] * 0.7152 + background[..., 2] * 0.0722
    darker = bg_luminance - luminance

    # The cat is warm and textured; the studio backdrop is nearly achromatic.
    score = np.maximum.reduce(
        [
            (colour_distance - 3.5) / 23.0,
            (chroma_excess - 1.0) / 25.0,
            (darker - 3.0) / 24.0,
        ]
    )
    soft = np.clip(score, 0.0, 1.0)
    # This high-confidence seed deliberately excludes the broad floor shadow.
    # A later, bounded soft ring restores fur, whiskers and a very small contact shadow.
    candidate = (chroma_excess > 18.0) | (
        ((colour_distance > 55.0) | (darker > 45.0)) & (chroma_excess > 7.0)
    )

    # Close narrow gaps so white chest fur remains part of the main animal.
    closed = mask_filter(candidate, ImageFilter.MaxFilter(7))
    closed = mask_filter(closed, ImageFilter.MinFilter(5))

    height, width = candidate.shape
    central_score = np.where(closed, score, -999.0)
    central_score[:, : int(width * 0.15)] = -999
    central_score[:, int(width * 0.85) :] = -999
    central_score[: int(height * 0.08), :] = -999
    central_score[int(height * 0.90) :, :] = -999
    flat = int(np.argmax(central_score))
    seed = (flat // width, flat % width)
    component = component_from_seed(closed, seed)
    component = fill_holes(component)
    component = mask_filter(component, ImageFilter.MinFilter(7))
    component = mask_filter(component, ImageFilter.MaxFilter(7))

    # Keep real wispy edges close to the body but reject detached logos and noise.
    inner = mask_filter(component, ImageFilter.MinFilter(5))
    near = mask_filter(component, ImageFilter.MaxFilter(17))
    alpha = (soft * 255.0).astype(np.uint8)
    alpha = np.where(near, alpha, 0).astype(np.uint8)
    alpha = np.where(component, np.maximum(alpha, 112), alpha).astype(np.uint8)
    alpha = np.where(inner, 255, alpha).astype(np.uint8)

    # Studio floor shadows can be attached to paws and therefore share the same
    # connected component.  Keep only a faint six-pixel contact fringe around
    # genuinely warm fur; remove the broad low-chroma floor patch.
    yy = np.arange(height, dtype=np.int32)[:, None]
    low_chroma_floor = (yy > int(height * 0.72)) & (chroma_excess < 8.0)
    animal_core = component & (chroma_excess >= 8.0)
    contact_near = mask_filter(animal_core, ImageFilter.MaxFilter(13))
    alpha = np.where(low_chroma_floor & ~contact_near, 0, alpha).astype(np.uint8)
    alpha = np.where(low_chroma_floor & contact_near, np.minimum(alpha, 48), alpha).astype(np.uint8)
    if trim_contact:
        alpha = trim_lower_contact_matte(alpha, component, chroma_excess, colour_distance, darker)
    alpha = np.asarray(Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(0.55)), dtype=np.uint8).copy()
    alpha[alpha < 14] = 0

    # Use the neural matte for body topology and tails.  Only restore very thin
    # warm/high-contrast detail immediately around its confident foreground;
    # this is where U2Net's 320 px input can miss individual white whiskers.
    neural = neural_foreground_alpha(image)
    neural_core = neural >= 90
    detail_near = mask_filter(neural_core, ImageFilter.MaxFilter(17))
    detail_evidence = (
        ((chroma_excess >= 7.0) & (colour_distance >= 18.0))
        | (colour_distance >= 82.0)
    ) & detail_near
    detail_alpha = np.where(detail_evidence, alpha, 0).astype(np.uint8)
    neural_support = mask_filter(neural >= 10, ImageFilter.MaxFilter(5))
    detail_alpha = np.where(neural_support, detail_alpha, 0).astype(np.uint8)
    alpha = np.maximum(neural, np.minimum(detail_alpha, 196)).astype(np.uint8)

    # The optional contact trim is intentionally conservative now.  The model
    # already excludes the floor; only discard near-invisible detached specks.
    if trim_contact:
        alpha[alpha < 5] = 0
    alpha = np.asarray(
        Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(0.22)),
        dtype=np.uint8,
    ).copy()

    # Transparent corners are an explicit invariant for the desktop window.
    alpha[0, :] = 0
    alpha[-1, :] = 0
    alpha[:, 0] = 0
    alpha[:, -1] = 0
    cleaned = despill(rgb, background, alpha)
    return Image.fromarray(np.dstack([cleaned, alpha]), "RGBA")


def remove_green_background(image: Image.Image) -> Image.Image:
    """Create a deterministic soft matte for the new uniform green-screen clips.

    The ordinary still/video matte is tuned for pale studio backgrounds and
    intentionally relies on U2Net for similarly coloured tails and shadows.
    These clips have a saturated green background instead, so a colour-derived
    alpha keeps fine whiskers more consistently and is substantially faster.
    """
    rgb = np.asarray(image.convert("RGB"), dtype=np.uint8)
    rgbf = rgb.astype(np.float32)
    height, width, _ = rgb.shape
    band = max(8, int(min(width, height) * 0.035))
    border = np.concatenate(
        [
            rgbf[:band].reshape(-1, 3),
            rgbf[-band:].reshape(-1, 3),
            rgbf[:, :band].reshape(-1, 3),
            rgbf[:, -band:].reshape(-1, 3),
        ],
        axis=0,
    )
    background_colour = np.median(border, axis=0)
    delta = rgbf - background_colour[None, None, :]
    colour_distance = np.sqrt(np.sum(delta * delta, axis=2))

    green_dominance = rgbf[..., 1] - np.maximum(rgbf[..., 0], rgbf[..., 2])
    background_dominance = float(
        background_colour[1] - max(background_colour[0], background_colour[2])
    )
    dominance_drop = background_dominance - green_dominance

    distance_score = np.clip((colour_distance - 5.0) / 55.0, 0.0, 1.0)
    dominance_score = np.clip((dominance_drop - 7.0) / 145.0, 0.0, 1.0)
    # Distance alone would turn darker H.264 green blocks into opaque green
    # outlines.  Require those pixels to also lose green dominance.
    score = np.maximum(
        dominance_score,
        distance_score * np.sqrt(np.maximum(dominance_score, 0.0)),
    )
    # Smoothstep gives the compressed green/fur boundary a soft but narrow edge.
    score = score * score * (3.0 - 2.0 * score)
    alpha = np.uint8(np.clip(score, 0.0, 1.0) * 255.0)
    solid = ((colour_distance >= 86.0) | (dominance_drop >= 168.0)) & (
        (green_dominance <= 32.0) | (rgbf[..., 1] <= 108.0)
    )
    alpha[solid] = 255
    alpha[colour_distance <= 4.0] = 0
    obvious_green = (green_dominance > 32.0) & (rgbf[..., 1] > 88.0)
    green_keep = np.clip((96.0 - green_dominance) / 64.0, 0.0, 1.0)
    alpha[obvious_green] = np.uint8(
        alpha[obvious_green].astype(np.float32) * green_keep[obvious_green]
    )
    opaque_core = mask_filter(alpha >= 245, ImageFilter.MinFilter(7))
    edge_region = (alpha > 0) & ~opaque_core
    green_spill = edge_region & (green_dominance > 12.0) & (rgbf[..., 1] > 72.0)
    spill_keep = np.clip((dominance_drop - 24.0) / 172.0, 0.0, 1.0)
    alpha[green_spill] = np.uint8(
        alpha[green_spill].astype(np.float32) * spill_keep[green_spill]
    )
    alpha = np.asarray(
        Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(0.38)),
        dtype=np.uint8,
    ).copy()
    alpha[alpha < 8] = 0
    alpha[(alpha > 248) & (green_dominance <= 32.0)] = 255
    alpha[0, :] = 0
    alpha[-1, :] = 0
    alpha[:, 0] = 0
    alpha[:, -1] = 0

    # Unmix part of the green contribution on semi-transparent pixels, then
    # cap residual green spill.  Opaque fur colour is never modified.
    fraction = (alpha.astype(np.float32) / 255.0)[..., None]
    safe = np.maximum(fraction, 0.16)
    estimated = (
        rgbf - background_colour[None, None, :] * (1.0 - fraction)
    ) / safe
    opaque_core = mask_filter(alpha >= 245, ImageFilter.MinFilter(7))
    edge_pixels = (alpha >= 8) & ~opaque_core
    edge = edge_pixels[..., None]
    strength = np.clip((1.0 - fraction) * 0.92 + 0.18, 0.0, 0.82)
    cleaned = np.where(
        edge,
        rgbf * (1.0 - strength) + estimated * strength,
        rgbf,
    )
    edge_mask = edge_pixels & (green_dominance > 6.0)
    green_cap = np.maximum(cleaned[..., 0], cleaned[..., 2]) * 1.08 + 2.0
    cleaned[..., 1][edge_mask] = np.minimum(
        cleaned[..., 1][edge_mask], green_cap[edge_mask]
    )
    result = Image.fromarray(
        np.dstack([np.uint8(np.clip(cleaned, 0, 255)), alpha]), "RGBA"
    )
    return keep_largest_alpha_component(result, threshold=12)


def alpha_bbox(image: Image.Image, threshold: int = 40) -> Tuple[int, int, int, int]:
    alpha = np.asarray(image.getchannel("A"), dtype=np.uint8)
    ys, xs = np.where(alpha >= threshold)
    if len(xs) == 0:
        raise RuntimeError("Foreground matte is empty")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def apply_guided_alpha_region(
    image: Image.Image,
    region: Tuple[int, int, int, int],
    polygon: Sequence[Tuple[int, int]],
    feather: float = 1.2,
) -> Image.Image:
    """Intersect one known-problem tail area with a softly feathered guide."""
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    guide = Image.new("L", image.size, 0)
    ImageDraw.Draw(guide).polygon(polygon, fill=255)
    guide = guide.filter(ImageFilter.GaussianBlur(feather))
    allowed = np.asarray(guide, dtype=np.uint8)
    left, top, right, bottom = region
    alpha = data[..., 3]
    alpha_region = alpha[top:bottom, left:right].astype(np.uint16)
    guide_region = allowed[top:bottom, left:right].astype(np.uint16)
    alpha[top:bottom, left:right] = ((alpha_region * guide_region) // 255).astype(np.uint8)
    data[..., 3] = alpha
    return Image.fromarray(data, "RGBA")


def apply_tail_texture_matte(
    image: Image.Image,
    source_rgb: Image.Image,
    region: Tuple[int, int, int, int],
    feather: float = 0.8,
) -> Image.Image:
    """Suppress neutral studio shadow while retaining warm/dark tail fur."""
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    rgb = np.asarray(source_rgb.convert("RGB"), dtype=np.uint8).astype(np.float32)
    background = polynomial_background(rgb)
    chroma = rgb.max(axis=2) - rgb.min(axis=2)
    bg_chroma = background.max(axis=2) - background.min(axis=2)
    chroma_excess = chroma - bg_chroma
    luma = rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722
    bg_luma = background[..., 0] * 0.2126 + background[..., 1] * 0.7152 + background[..., 2] * 0.0722
    darker = bg_luma - luma
    # Tail stripes can be nearly neutral but are much darker than the floor;
    # ordinary shadow is also dark, yet lacks the local warm chroma texture.
    confidence = np.maximum(
        np.clip((chroma_excess - 2.0) / 16.0, 0.0, 1.0),
        np.clip((darker - 75.0) / 70.0, 0.0, 1.0)
        * np.clip((chroma_excess + 1.0) / 8.0, 0.0, 1.0),
    )
    confidence = np.asarray(
        Image.fromarray(np.uint8(confidence * 255.0), "L").filter(
            ImageFilter.GaussianBlur(feather)
        ),
        dtype=np.uint8,
    )
    left, top, right, bottom = region
    alpha = data[..., 3]
    alpha_region = alpha[top:bottom, left:right].astype(np.uint16)
    confidence_region = confidence[top:bottom, left:right].astype(np.uint16)
    alpha[top:bottom, left:right] = (
        alpha_region * confidence_region // 255
    ).astype(np.uint8)
    data[..., 3] = alpha
    return Image.fromarray(data, "RGBA")


def pull_edge_colour_from_fur(
    image: Image.Image,
    region: Tuple[int, int, int, int],
    radius: float,
) -> Image.Image:
    """Fill translucent tail-edge RGB from neighbouring opaque fur.

    Alpha-composited pixels may contain pale floor colour or black values after
    mathematical despill.  Extending nearby opaque fur colour into only the
    semi-transparent edge prevents dark halos while preserving the alpha hair.
    """
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    alpha = data[..., 3].astype(np.float32)
    core_weight = np.clip((alpha - 150.0) / 90.0, 0.0, 1.0)
    weight_image = Image.fromarray(np.uint8(core_weight * 255.0), "L")
    blurred_weight = np.asarray(
        weight_image.filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32
    ) / 255.0
    estimate = np.zeros_like(data[..., :3], dtype=np.float32)
    for channel in range(3):
        weighted = data[..., channel].astype(np.float32) * core_weight
        blurred = np.asarray(
            Image.fromarray(np.uint8(np.clip(weighted, 0, 255)), "L").filter(
                ImageFilter.GaussianBlur(radius)
            ),
            dtype=np.float32,
        ) / 255.0
        estimate[..., channel] = (blurred / np.maximum(blurred_weight, 0.015)) * 255.0

    left, top, right, bottom = region
    yy, xx = np.indices(alpha.shape)
    edge = (alpha > 0) & (alpha < 235) & (blurred_weight > 0.015)
    edge &= (xx >= left) & (xx < right) & (yy >= top) & (yy < bottom)
    strength = np.clip((235.0 - alpha) / 190.0, 0.15, 0.92)[..., None]
    original = data[..., :3].astype(np.float32)
    mixed = original * (1.0 - strength) + estimate * strength
    data[..., :3][edge] = np.clip(mixed[edge], 0, 255).astype(np.uint8)
    return Image.fromarray(data, "RGBA")


def composite_polygon_region(
    foreground: Image.Image,
    background: Image.Image,
    polygon: Sequence[Tuple[int, int]],
    feather: float,
) -> Image.Image:
    guide = Image.new("L", background.size, 0)
    ImageDraw.Draw(guide).polygon(polygon, fill=255)
    guide = guide.filter(ImageFilter.GaussianBlur(feather))
    return Image.composite(foreground, background, guide)


def make_high_resolution_still(
    path: Path,
    tail_region: Tuple[int, int, int, int],
    tail_polygon: Sequence[Tuple[int, int]],
    blend_polygon: Sequence[Tuple[int, int]],
) -> Image.Image:
    """Matte the complete subject at source resolution before downsampling.

    Tail recovery polygons from 4.0 are intentionally ignored: they encoded a
    guessed contour and caused the missing/dirty tail edges reported by users.
    """
    with Image.open(path) as source_image:
        source = source_image.convert("RGB")
    return remove_background(source, trim_contact=True)


def make_high_resolution_portrait(
    path: Path,
    tail_region: Tuple[int, int, int, int],
    tail_polygon: Sequence[Tuple[int, int]],
    blend_polygon: Sequence[Tuple[int, int]],
) -> Image.Image:
    portrait = make_high_resolution_still(
        path, tail_region, tail_polygon, blend_polygon
    ).resize(PORTRAIT_SIZE, Image.Resampling.LANCZOS)
    return keep_largest_alpha_component(portrait)


def make_high_resolution_automatic_portrait(path: Path) -> Image.Image:
    """Resolve the still matte at source size before its only downsample."""
    with Image.open(path) as source_image:
        source = source_image.convert("RGB")
    return remove_background(source).resize(PORTRAIT_SIZE, Image.Resampling.LANCZOS)


def remove_tiny_alpha_islands(image: Image.Image, min_pixels: int = 8) -> Image.Image:
    """Discard isolated compression flecks while retaining the connected fur."""
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    alpha = data[..., 3]
    mask = alpha >= 14
    height, width = mask.shape
    visited = np.zeros_like(mask, dtype=bool)
    for start_y in range(height):
        for start_x in range(width):
            if visited[start_y, start_x] or not mask[start_y, start_x]:
                continue
            queue = [(start_y, start_x)]
            visited[start_y, start_x] = True
            component: List[Tuple[int, int]] = []
            while queue:
                y, x = queue.pop()
                component.append((y, x))
                for ny in range(max(0, y - 1), min(height, y + 2)):
                    for nx in range(max(0, x - 1), min(width, x + 2)):
                        if mask[ny, nx] and not visited[ny, nx]:
                            visited[ny, nx] = True
                            queue.append((ny, nx))
            if len(component) < min_pixels:
                for y, x in component:
                    alpha[y, x] = 0
    data[..., 3] = alpha
    return Image.fromarray(data, "RGBA")


def suppress_detached_soft_spill(
    image: Image.Image,
    region: Tuple[int, int, int, int],
    solid_threshold: int = 170,
    radius: int = 3,
) -> Image.Image:
    """Remove weak floor-shadow alpha not adjacent to a solid tail pixel."""
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    alpha = data[..., 3]
    solid = alpha >= solid_threshold
    size = radius * 2 + 1
    near_solid = mask_filter(solid, ImageFilter.MaxFilter(size))
    left, top, right, bottom = region
    yy, xx = np.indices(alpha.shape)
    inside = (xx >= left) & (xx < right) & (yy >= top) & (yy < bottom)
    spill = inside & (alpha > 0) & (alpha < solid_threshold) & ~near_solid
    alpha[spill] = 0
    data[..., 3] = alpha
    return Image.fromarray(data, "RGBA")


def keep_largest_alpha_component(
    image: Image.Image, threshold: int = 14
) -> Image.Image:
    """Remove all detached matte islands while preserving the connected cat."""
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    alpha = data[..., 3]
    mask = alpha >= threshold
    height, width = mask.shape
    visited = np.zeros_like(mask, dtype=bool)
    largest: List[Tuple[int, int]] = []
    for start_y in range(height):
        for start_x in range(width):
            if visited[start_y, start_x] or not mask[start_y, start_x]:
                continue
            queue = [(start_y, start_x)]
            visited[start_y, start_x] = True
            component: List[Tuple[int, int]] = []
            while queue:
                y, x = queue.pop()
                component.append((y, x))
                for ny in range(max(0, y - 1), min(height, y + 2)):
                    for nx in range(max(0, x - 1), min(width, x + 2)):
                        if mask[ny, nx] and not visited[ny, nx]:
                            visited[ny, nx] = True
                            queue.append((ny, nx))
            if len(component) > len(largest):
                largest = component
    keep = np.zeros_like(mask, dtype=bool)
    for y, x in largest:
        keep[y, x] = True
    # Preserve only sub-threshold antialiasing immediately around the subject.
    near = mask_filter(keep, ImageFilter.MaxFilter(5))
    alpha[~near] = 0
    data[..., 3] = alpha
    return Image.fromarray(data, "RGBA")


def composite_guided_region(
    frame: Image.Image,
    stable: Image.Image,
    polygon: Sequence[Tuple[int, int]],
    feather: float,
) -> Image.Image:
    """Use the stable sitting body in the tail area of head-turn frames."""
    guide = Image.new("L", frame.size, 0)
    ImageDraw.Draw(guide).polygon(polygon, fill=255)
    guide = guide.filter(ImageFilter.GaussianBlur(feather))
    return Image.composite(stable, frame, guide)


def refine_video_frame(frame: Image.Image) -> Image.Image:
    """Clean one same-source full-body video matte without changing anatomy.

    Previous builds pasted a moving head from video onto a different still
    body.  The neck angle, shoulder outline and coat pattern then disagreed.
    Retaining each complete video frame preserves the real head-neck-shoulder
    relationship; only semi-transparent edge RGB is pulled back toward nearby
    fur to reduce the pale studio halo.
    """
    cleaned = remove_tiny_alpha_islands(frame, min_pixels=5)
    data = np.asarray(cleaned.convert("RGBA"), dtype=np.uint8).copy()
    rgb = data[..., :3].astype(np.float32)
    # H.264 chroma blocks are especially visible in the generated head-turn
    # source.  Denoise only colour difference at sub-pixel strength while
    # retaining original luminance/fur detail.
    luma = rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114
    chroma_r = rgb[..., 0] - luma
    chroma_b = rgb[..., 2] - luma
    smooth_r = np.asarray(
        Image.fromarray(np.uint8(np.clip(chroma_r + 128.0, 0, 255)), "L").filter(
            ImageFilter.GaussianBlur(3.2)
        ),
        dtype=np.float32,
    ) - 128.0
    smooth_b = np.asarray(
        Image.fromarray(np.uint8(np.clip(chroma_b + 128.0, 0, 255)), "L").filter(
            ImageFilter.GaussianBlur(3.2)
        ),
        dtype=np.float32,
    ) - 128.0
    chroma_r = chroma_r * 0.05 + smooth_r * 0.95
    chroma_b = chroma_b * 0.05 + smooth_b * 0.95
    red = luma + chroma_r
    blue = luma + chroma_b
    green = (luma - red * 0.299 - blue * 0.114) / 0.587
    data[..., :3] = np.clip(np.dstack([red, green, blue]), 0, 255).astype(np.uint8)
    cleaned = Image.fromarray(data, "RGBA")
    return pull_edge_colour_from_fur(
        cleaned,
        (20, 20, frame.width - 20, frame.height - 15),
        2.2,
    )


def refine_video_tail(frame: Image.Image, sitting: bool) -> Image.Image:
    """Keep the model's complete same-source tail and remove detached specks."""
    return keep_largest_alpha_component(frame)


def match_clip_colour(
    frames: Sequence[Image.Image], reference: Image.Image, amount: float
) -> List[Image.Image]:
    """Bring video colour/luminance toward its matching source photo.

    The operation uses robust opaque-subject percentiles and applies the same
    transform to every frame, so the animation does not pulse in brightness.
    """
    if not frames:
        return []

    def statistics(image: Image.Image) -> Tuple[np.ndarray, np.ndarray]:
        data = np.asarray(image.convert("RGBA"), dtype=np.uint8)
        pixels = data[..., :3][data[..., 3] >= 220].astype(np.float32)
        return np.percentile(pixels, 50, axis=0), np.percentile(pixels, 84, axis=0)

    source_median, source_high = statistics(frames[len(frames) // 2])
    target_median, target_high = statistics(reference)
    scale = (target_high - target_median) / np.maximum(source_high - source_median, 8.0)
    scale = np.clip(scale, 0.82, 1.22)
    scale = 1.0 + (scale - 1.0) * amount
    offset = (target_median - source_median * scale) * amount

    corrected: List[Image.Image] = []
    for frame in frames:
        data = np.asarray(frame.convert("RGBA"), dtype=np.uint8).copy()
        rgb = data[..., :3].astype(np.float32) * scale[None, None, :] + offset[None, None, :]
        data[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
        corrected.append(Image.fromarray(data, "RGBA"))
    return corrected


def stabilise_transition_tail(frames: List[Image.Image], sit_master: Image.Image) -> None:
    """Gradually replace only the settled tail with the refined source still.

    The tail is still moving through the first half of the transition, so those
    frames keep their native video silhouette.  Once it starts curling around
    the paws, a long smoothstep blend suppresses compression/shadow flicker and
    converges on the source-photo tail without a one-frame pop.
    """
    if not frames:
        return
    start = max(0, len(frames) - 13)
    polygon = [(150, 326), (205, 316), (264, 330), (292, 360),
               (294, 430), (148, 430)]
    base_guide = Image.new("L", PORTRAIT_SIZE, 0)
    ImageDraw.Draw(base_guide).polygon(polygon, fill=255)
    base_guide = base_guide.filter(ImageFilter.GaussianBlur(5.0))
    for index in range(start, len(frames)):
        progress = (index - start + 1) / float(len(frames) - start)
        strength = progress * progress * (3.0 - 2.0 * progress)
        guide = base_guide.point(lambda value, s=strength: int(round(value * s)))
        frames[index] = Image.composite(sit_master, frames[index], guide)


def transform_to_reference(image: Image.Image, source_bbox: Tuple[int, int, int, int], target_bbox: Tuple[int, int, int, int]) -> Image.Image:
    """Apply one fixed scale/translation so clip motion stays temporally stable."""
    sw = max(1, source_bbox[2] - source_bbox[0])
    sh = max(1, source_bbox[3] - source_bbox[1])
    tw = max(1, target_bbox[2] - target_bbox[0])
    th = max(1, target_bbox[3] - target_bbox[1])
    scale = min(tw / float(sw), th / float(sh))

    resized = image.resize(
        (max(1, int(round(image.width * scale))), max(1, int(round(image.height * scale)))),
        Image.Resampling.LANCZOS,
    )
    source_center_x = (source_bbox[0] + source_bbox[2]) * 0.5 * scale
    target_center_x = (target_bbox[0] + target_bbox[2]) * 0.5
    source_baseline = source_bbox[3] * scale
    target_baseline = target_bbox[3]
    x = int(round(target_center_x - source_center_x))
    y = int(round(target_baseline - source_baseline))
    canvas = Image.new("RGBA", PORTRAIT_SIZE, (0, 0, 0, 0))
    canvas.alpha_composite(resized, (x, y))
    return canvas


def align_master_to_reference(master: Image.Image, reference: Image.Image) -> Image.Image:
    return transform_to_reference(master, alpha_bbox(master), alpha_bbox(reference))


def translate_rgba(image: Image.Image, offset_x: int, offset_y: int = 0) -> Image.Image:
    """Translate a pose without resizing it, leaving transparent safety space."""
    canvas = Image.new("RGBA", image.size, (0, 0, 0, 0))
    canvas.alpha_composite(image.convert("RGBA"), (offset_x, offset_y))
    return canvas


def clear_transparent_frame(image: Image.Image) -> Image.Image:
    """Zero a one-pixel outer alpha border without touching visible anatomy."""
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    data[0, :, 3] = 0
    data[-1, :, 3] = 0
    data[:, 0, 3] = 0
    data[:, -1, 3] = 0
    return Image.fromarray(data, "RGBA")


def matte_source_still(path: Path) -> Image.Image:
    with Image.open(path) as image:
        stage = fit_stage(image, PORTRAIT_SIZE)
    return remove_background(stage)


def matte_video_frames(
    paths: Iterable[Path],
    working_size: Tuple[int, int] = PORTRAIT_SIZE,
    trim_contact: bool = True,
) -> List[Image.Image]:
    """Matte video frames before the final downsample.

    Tail hairs are only one or two pixels wide on the 320 x 480 render stage.
    Segmenting there loses them permanently and exaggerates H.264 blocks.
    Transition frames therefore use a 480 x 720 working matte and are reduced
    only after alpha and edge colour have been resolved.
    """
    result: List[Image.Image] = []
    for path in paths:
        with Image.open(path) as image:
            stage = fit_stage(image, working_size)
        matted = remove_background(stage, trim_contact=trim_contact)
        if working_size != PORTRAIT_SIZE:
            matted = matted.resize(PORTRAIT_SIZE, Image.Resampling.LANCZOS)
        result.append(matted)
    return result


def matte_green_video_frames(paths: Iterable[Path]) -> List[Image.Image]:
    """Matte green-screen frames without cropping either wide tail pose."""
    result: List[Image.Image] = []
    for path in paths:
        with Image.open(path) as image:
            source = image.convert("RGB")
        scale = GREEN_VIDEO_HEIGHT / float(source.height)
        working = source.resize(
            (
                max(1, int(round(source.width * scale))),
                GREEN_VIDEO_HEIGHT,
            ),
            Image.Resampling.LANCZOS,
        )
        result.append(remove_green_background(working))
    return result


def embed_transition_reference(image: Image.Image, pose: str) -> Image.Image:
    """Place an existing stable pose on the square transition stage.

    The WPF window preserves its bottom centre while resizing.  Padding the
    portrait equally left/right and padding the sleep pose above therefore
    produces the exact same screen-space pixels before and after a transition.
    """
    canvas = Image.new("RGBA", TRANSITION_SIZE, (0, 0, 0, 0))
    if pose == "portrait":
        offset = ((TRANSITION_SIZE[0] - PORTRAIT_SIZE[0]) // 2, 0)
    elif pose == "sleep":
        offset = (0, TRANSITION_SIZE[1] - SLEEP_SIZE[1])
    else:
        raise ValueError("Unknown transition pose: " + pose)
    canvas.alpha_composite(image.convert("RGBA"), offset)
    return canvas


def _smoothed_alpha_boxes(
    frames: Sequence[Image.Image], radius: int = 2
) -> List[Tuple[float, float, float, float]]:
    boxes = [alpha_bbox(frame, 32) for frame in frames]
    smoothed: List[Tuple[float, float, float, float]] = []
    for index in range(len(boxes)):
        nearby = boxes[max(0, index - radius) : min(len(boxes), index + radius + 1)]
        values = np.asarray(nearby, dtype=np.float32)
        smoothed.append(tuple(float(value) for value in np.median(values, axis=0)))
    return smoothed


def place_transition_on_square(
    frames: Sequence[Image.Image],
    start_reference: Image.Image,
    end_reference: Image.Image,
) -> List[Image.Image]:
    """Align a complete-body transition to stable-pose screen coordinates."""
    if not frames:
        return []
    source_boxes = _smoothed_alpha_boxes(frames)
    source_start = source_boxes[0]
    source_end = source_boxes[-1]
    target_start = alpha_bbox(start_reference, 32)
    target_end = alpha_bbox(end_reference, 32)

    def scale_for(source_box, target_box) -> float:
        source_width = max(1.0, source_box[2] - source_box[0])
        source_height = max(1.0, source_box[3] - source_box[1])
        width_ratio = (target_box[2] - target_box[0]) / source_width
        height_ratio = (target_box[3] - target_box[1]) / source_height
        return math.sqrt(max(0.0001, width_ratio * height_ratio))

    start_scale = scale_for(source_start, target_start)
    end_scale = scale_for(source_end, target_end)
    start_center = (target_start[0] + target_start[2]) * 0.5
    end_center = (target_end[0] + target_end[2]) * 0.5
    start_baseline = float(target_start[3])
    end_baseline = float(target_end[3])

    placed: List[Image.Image] = []
    denominator = float(max(1, len(frames) - 1))
    for index, (frame, source_box) in enumerate(zip(frames, source_boxes)):
        progress = index / denominator
        eased = progress * progress * (3.0 - 2.0 * progress)
        scale = start_scale + (end_scale - start_scale) * eased
        target_center = start_center + (end_center - start_center) * eased
        target_baseline = start_baseline + (end_baseline - start_baseline) * eased
        resized = frame.resize(
            (
                max(1, int(round(frame.width * scale))),
                max(1, int(round(frame.height * scale))),
            ),
            Image.Resampling.LANCZOS,
        )
        source_center = (source_box[0] + source_box[2]) * 0.5 * scale
        source_baseline = source_box[3] * scale
        offset_x = int(round(target_center - source_center))
        offset_y = int(round(target_baseline - source_baseline))
        canvas = Image.new("RGBA", TRANSITION_SIZE, (0, 0, 0, 0))
        canvas.alpha_composite(resized, (offset_x, offset_y))
        placed.append(
            clear_transparent_frame(
                pull_edge_colour_from_fur(
                    keep_largest_alpha_component(canvas, threshold=10),
                    (1, 1, TRANSITION_SIZE[0] - 1, TRANSITION_SIZE[1] - 1),
                    1.8,
                )
            )
        )
    return placed


def smooth_clip_alpha(frames: Sequence[Image.Image]) -> List[Image.Image]:
    """Apply a 3-frame temporal median only to uncertain matte pixels.

    Opaque anatomy and all RGB motion stay untouched.  This suppresses the
    one-frame alpha sparks produced by H.264 noise around tail hairs without
    freezing the body or pasting a still tail over a moving frame.
    """
    if len(frames) < 3:
        return list(frames)
    alphas = [
        np.asarray(frame.getchannel("A"), dtype=np.uint8) for frame in frames
    ]
    result: List[Image.Image] = []
    for index, frame in enumerate(frames):
        previous = alphas[max(0, index - 1)]
        current = alphas[index]
        following = alphas[min(len(alphas) - 1, index + 1)]
        median = np.median(
            np.stack([previous, current, following], axis=0), axis=0
        ).astype(np.uint8)
        uncertain = (current > 3) & (current < 246)
        stable = current.copy()
        stable[uncertain] = np.uint8(
            (current[uncertain].astype(np.uint16) * 2 + median[uncertain]) // 3
        )
        data = np.asarray(frame.convert("RGBA"), dtype=np.uint8).copy()
        data[..., 3] = stable
        result.append(Image.fromarray(data, "RGBA"))
    return result


def normalise_clip(frames: List[Image.Image], reference: Image.Image) -> List[Image.Image]:
    source_box = alpha_bbox(frames[0])
    target_box = alpha_bbox(reference)
    return [
        clear_transparent_frame(
            transform_to_reference(frame, source_box, target_box)
        )
        for frame in frames
    ]


def blend_endpoint(frames: List[Image.Image], master: Image.Image, at_start: bool, count: int = 4) -> None:
    usable = min(count, len(frames))
    if at_start:
        for index in range(usable):
            # First frame is exactly the master, then motion takes over.
            amount = index / float(max(1, usable))
            frames[index] = Image.blend(master, frames[index], amount)
    else:
        for offset in range(usable):
            index = len(frames) - usable + offset
            amount = (offset + 1) / float(usable)
            frames[index] = Image.blend(frames[index], master, amount)


def make_sleep(master_path: Path) -> Image.Image:
    cutout = make_high_resolution_still(
        master_path,
        SLEEP_SOURCE_TAIL_REGION,
        SLEEP_SOURCE_TAIL_POLYGON,
        SLEEP_SOURCE_TAIL_BLEND,
    )
    box = alpha_bbox(cutout, 32)
    pad_x = max(8, int((box[2] - box[0]) * 0.04))
    pad_y = max(8, int((box[3] - box[1]) * 0.04))
    box = (
        max(0, box[0] - pad_x),
        max(0, box[1] - pad_y),
        min(cutout.width, box[2] + pad_x),
        min(cutout.height, box[3] + pad_y),
    )
    subject = cutout.crop(box)
    subject.thumbnail((460, 295), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", SLEEP_SIZE, (0, 0, 0, 0))
    canvas.alpha_composite(subject, ((SLEEP_SIZE[0] - subject.width) // 2, SLEEP_SIZE[1] - subject.height - 6))
    return keep_largest_alpha_component(remove_tiny_alpha_islands(canvas))


def save_clip(clip_id: str, frames: Sequence[Image.Image]) -> None:
    directory = PACK / "clips" / clip_id
    directory.mkdir(parents=True, exist_ok=True)
    for index, frame in enumerate(frames):
        frame.save(directory / "{0:04d}.png".format(index), optimize=True)


def save_look_frames(frames: Sequence[Image.Image]) -> Dict[str, int]:
    # Direction landmarks were originally reviewed at 12 fps.  Scale them to
    # the current render rate so upgrading to source-rate 24 fps keeps the same
    # head directions instead of shifting every mouse-look pose.
    scale = FPS / 12.0
    base_mapping = {
        "center": 0,
        "left_near": 10,
        "left_up": 20,
        "left_side": 30,
        "right_near": 60,
        "right_up": 70,
        "right_side": 80,
    }
    mapping = {
        name: int(round(index * scale)) for name, index in base_mapping.items()
    }
    directory = PACK / "look"
    directory.mkdir(parents=True)
    for name, index in mapping.items():
        safe_index = min(index, len(frames) - 1)
        frames[safe_index].save(directory / (name + ".png"), optimize=True)
        mapping[name] = safe_index
    return mapping


def build_manifest(frame_counts: Dict[str, int], look_mapping: Dict[str, int]) -> Path:
    root = ET.Element(
        "yabiAssets",
        {
            "version": "4.2",
            "portraitWidth": str(PORTRAIT_SIZE[0]),
            "portraitHeight": str(PORTRAIT_SIZE[1]),
            "sleepWidth": str(SLEEP_SIZE[0]),
            "sleepHeight": str(SLEEP_SIZE[1]),
            "transitionWidth": str(TRANSITION_SIZE[0]),
            "transitionHeight": str(TRANSITION_SIZE[1]),
        },
    )
    definitions = [
        ("stand_idle", 1, "Standing", "Standing", "hold", "forward"),
        ("stand_blink", FPS, "Standing", "Standing", "once", "forward"),
        ("stand_to_sit", FPS, "Standing", "Sitting", "once", "forward"),
        ("sit_idle", 1, "Sitting", "Sitting", "hold", "forward"),
        ("sit_lookaround", FPS, "Sitting", "Sitting", "once", "forward"),
        ("sit_to_sleep", FPS, "Sitting", "Sleeping", "once", "forward"),
        ("sleep_to_sit", FPS, "Sleeping", "Sitting", "once", "forward"),
        ("sleep_idle", 1, "Sleeping", "Sleeping", "hold", "forward"),
    ]
    for clip_id, fps, start, end, mode, direction in definitions:
        ET.SubElement(
            root,
            "clip",
            {
                "id": clip_id,
                "path": "clips/" + clip_id,
                "fps": str(fps),
                "frames": str(frame_counts[clip_id]),
                "startPose": start,
                "endPose": end,
                "mode": mode,
                "direction": direction,
                "cooldownMs": "0",
            },
        )
    look = ET.SubElement(root, "mouseLook", {"updateHz": "10", "deadZone": "34", "hysteresis": "18"})
    for name, index in look_mapping.items():
        ET.SubElement(look, "direction", {"id": name, "path": "look/" + name + ".png", "sourceFrame": str(index)})
    ET.indent(root, space="  ")
    path = PACK / "manifest.xml"
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
    return path


def checkerboard(size: Tuple[int, int], cell: int = 16) -> Image.Image:
    image = Image.new("RGB", size, "white")
    draw = ImageDraw.Draw(image)
    for y in range(0, size[1], cell):
        for x in range(0, size[0], cell):
            colour = (220, 220, 220) if ((x // cell + y // cell) % 2) else (246, 246, 246)
            draw.rectangle((x, y, x + cell - 1, y + cell - 1), fill=colour)
    return image


def composite_preview(items: Sequence[Tuple[str, Image.Image]], background: Image.Image) -> Image.Image:
    cell_w, cell_h = 340, 520
    result = Image.new("RGB", (cell_w * len(items), cell_h), (30, 30, 30))
    font = ImageFont.load_default()
    for index, (name, rgba) in enumerate(items):
        panel = background.resize((cell_w, cell_h)).convert("RGBA")
        display = rgba.copy()
        display.thumbnail((320, 455), Image.Resampling.LANCZOS)
        panel.alpha_composite(display, ((cell_w - display.width) // 2, 36 + (455 - display.height)))
        draw = ImageDraw.Draw(panel)
        draw.rectangle((0, 0, cell_w, 30), fill=(15, 15, 15, 220))
        draw.text((10, 8), name, fill="white", font=font)
        result.paste(panel.convert("RGB"), (cell_w * index, 0))
    return result


def make_previews(stand: Image.Image, sit: Image.Image, sleep: Image.Image, look: Image.Image) -> List[Path]:
    items = [("stand", stand), ("sit", sit), ("sleep", sleep), ("mouse-look", look)]
    backgrounds = {
        "transparent": checkerboard((340, 520)),
        "dark": Image.new("RGB", (340, 520), (24, 27, 33)),
        "blue": Image.new("RGB", (340, 520), (35, 96, 170)),
        "green": Image.new("RGB", (340, 520), (0, 255, 0)),
    }
    paths: List[Path] = []
    for name, background in backgrounds.items():
        path = PREVIEWS / ("preview_" + name + ".png")
        composite_preview(items, background).save(path, optimize=True)
        paths.append(path)
    return paths


def make_transition_previews(
    sit_to_sleep: Sequence[Image.Image], sleep_to_sit: Sequence[Image.Image]
) -> List[Path]:
    """Write contact sheets that expose transition scale, tail and edge defects."""
    cell = 240
    header = 24
    samples_per_clip = 5
    rows = (("sit_to_sleep", sit_to_sleep), ("sleep_to_sit", sleep_to_sit))
    backgrounds = {
        "transparent": checkerboard((cell, cell)),
        "dark": Image.new("RGB", (cell, cell), (24, 18, 14)),
        "blue": Image.new("RGB", (cell, cell), (28, 82, 148)),
    }
    paths: List[Path] = []
    font = ImageFont.load_default()
    for background_name, background in backgrounds.items():
        sheet = Image.new(
            "RGB",
            (cell * samples_per_clip, (cell + header) * len(rows)),
            (30, 30, 30),
        )
        for row_index, (clip_id, frames) in enumerate(rows):
            for sample_index in range(samples_per_clip):
                frame_index = int(
                    round((len(frames) - 1) * sample_index / float(samples_per_clip - 1))
                )
                panel = background.convert("RGBA")
                display = frames[frame_index].resize((cell, cell), Image.Resampling.LANCZOS)
                panel.alpha_composite(display)
                y = row_index * (cell + header)
                sheet.paste(panel.convert("RGB"), (sample_index * cell, y + header))
                draw = ImageDraw.Draw(sheet)
                draw.text(
                    (sample_index * cell + 7, y + 7),
                    "{0} {1}/{2}".format(clip_id, frame_index, len(frames) - 1),
                    fill="white",
                    font=font,
                )
        path = PREVIEWS / ("transitions_" + background_name + ".png")
        sheet.save(path, optimize=True)
        paths.append(path)
    return paths


def save_chroma_audit_masters(
    stand: Image.Image, sit: Image.Image, sleep: Image.Image
) -> None:
    """Write exact #00FF00 composites for edge inspection, not for re-keying."""
    directory = GENERATED / "chroma_audit"
    directory.mkdir(parents=True)
    for name, image in (("stand", stand), ("sit", sit), ("sleep", sleep)):
        background = Image.new("RGBA", image.size, (0, 255, 0, 255))
        background.alpha_composite(image)
        background.convert("RGB").save(
            directory / (name + "_green.png"), optimize=True
        )


def save_source_resolution_masters() -> None:
    """Export reusable source-resolution alpha and exact green audit images."""
    directory = GENERATED / "masters"
    directory.mkdir(parents=True)
    for name in ("stand", "sit", "sleep"):
        with Image.open(SOURCE / (name + "_master.png")) as source_image:
            cutout = remove_background(source_image.convert("RGB"))
        cutout.save(directory / (name + "_transparent.png"), optimize=True)
        green = Image.new("RGBA", cutout.size, (0, 255, 0, 255))
        green.alpha_composite(cutout)
        green.convert("RGB").save(directory / (name + "_green.png"), optimize=True)


def make_icon(stand: Image.Image) -> Tuple[Path, Path]:
    box = alpha_bbox(stand)
    crop = stand.crop(box)
    crop.thumbnail((224, 224), Image.Resampling.LANCZOS)
    preview = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    preview.alpha_composite(crop, ((256 - crop.width) // 2, 256 - crop.height - 8))
    png_path = GENERATED / "yabi-v4-icon.png"
    ico_path = GENERATED / "yabi-v4.ico"
    preview.save(png_path, optimize=True)
    preview.save(ico_path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    return png_path, ico_path


def subject_coverage(image: Image.Image) -> float:
    alpha = np.asarray(image.getchannel("A"), dtype=np.uint8)
    return float(np.count_nonzero(alpha > 32)) / float(alpha.size)


def temporal_tail_metrics(
    frames: Sequence[Image.Image], region: Tuple[int, int, int, int]
) -> Dict[str, float]:
    """Measure adjacent-frame tail changes without requiring a frozen tail."""
    if len(frames) < 2:
        return {
            "meanAdjacentAlphaDelta": 0.0,
            "peakAdjacentAlphaDelta": 0.0,
            "peakDeltaJerk": 0.0,
        }
    left, top, right, bottom = region
    samples = [
        np.asarray(frame.getchannel("A"), dtype=np.int16)[top:bottom, left:right]
        for frame in frames
    ]
    deltas = [np.abs(current - previous).mean() for previous, current in zip(samples, samples[1:])]
    jerk = np.abs(np.diff(deltas))
    return {
        "meanAdjacentAlphaDelta": round(float(np.mean(deltas)), 4),
        "peakAdjacentAlphaDelta": round(float(max(deltas)), 4),
        "peakDeltaJerk": round(float(max(jerk)) if len(jerk) else 0.0, 4),
    }


def region_motion_score(
    frames: Sequence[Image.Image], region: Tuple[int, int, int, int]
) -> float:
    """Mean opaque RGB deviation from frame zero in a required motion area."""
    if len(frames) < 2:
        return 0.0
    left, top, right, bottom = region
    samples = [
        np.asarray(frame.convert("RGBA"), dtype=np.int16)[top:bottom, left:right]
        for frame in frames
    ]
    reference = samples[0]
    scores: List[float] = []
    for sample in samples[1:]:
        shared = (reference[..., 3] >= 96) & (sample[..., 3] >= 96)
        if np.any(shared):
            delta = np.abs(sample[..., :3] - reference[..., :3]).mean(axis=2)
            scores.append(float(delta[shared].mean()))
    return round(float(max(scores)) if scores else 0.0, 4)


def validate_assets(clips: Dict[str, Sequence[Image.Image]], look_mapping: Dict[str, int], manifest_path: Path) -> Dict[str, object]:
    failures: List[str] = []
    report: Dict[str, object] = {
        "version": "4.2",
        "clips": {},
        "directions": look_mapping,
        "tailStability": {},
        "failures": failures,
    }
    for clip_id, frames in clips.items():
        if not frames:
            failures.append(clip_id + ": empty")
            continue
        first = frames[0]
        if clip_id == "sleep_idle":
            expected = SLEEP_SIZE
        elif clip_id in {"sit_to_sleep", "sleep_to_sit"}:
            expected = TRANSITION_SIZE
        else:
            expected = PORTRAIT_SIZE
        if first.size != expected:
            failures.append("{0}: size {1}, expected {2}".format(clip_id, first.size, expected))
        corners = [first.getpixel((0, 0))[3], first.getpixel((first.width - 1, 0))[3], first.getpixel((0, first.height - 1))[3], first.getpixel((first.width - 1, first.height - 1))[3]]
        coverage = subject_coverage(first)
        if any(corners):
            failures.append(clip_id + ": non-transparent corner")
        if not (0.05 <= coverage <= 0.72):
            failures.append("{0}: implausible coverage {1:.3f}".format(clip_id, coverage))
        # A fully transparent one-pixel frame border is required for every
        # frame, not merely clip endpoints.  This catches fast tail swings that
        # would otherwise be silently cut by the fixed transparent WPF window.
        for index, frame in enumerate(frames):
            frame_alpha = np.asarray(frame.getchannel("A"), dtype=np.uint8)
            if (
                np.any(frame_alpha[0, :])
                or np.any(frame_alpha[-1, :])
                or np.any(frame_alpha[:, 0])
                or np.any(frame_alpha[:, -1])
            ):
                failures.append(
                    "{0}: frame {1} touches canvas edge".format(clip_id, index)
                )
                break
        bbox_first = alpha_bbox(first)
        bbox_last = alpha_bbox(frames[-1])
        report["clips"][clip_id] = {
            "frames": len(frames),
            "size": list(first.size),
            "coverage": round(coverage, 4),
            "firstBBox": list(bbox_first),
            "lastBBox": list(bbox_last),
            "baselineDelta": bbox_last[3] - bbox_first[3],
        }

    parsed = ET.parse(manifest_path).getroot()
    manifest_ids = {node.attrib["id"] for node in parsed.findall("clip")}
    if manifest_ids != set(clips):
        failures.append("manifest clip set does not match generated clips")
    if set(look_mapping) != {"center", "left_near", "left_up", "left_side", "right_near", "right_up", "right_side"}:
        failures.append("mouse-look direction mapping incomplete")

    sit_transition_reference = embed_transition_reference(clips["sit_idle"][0], "portrait")
    sleep_transition_reference = embed_transition_reference(clips["sleep_idle"][0], "sleep")
    endpoint_expectations = {
        "sit_to_sleep": (sit_transition_reference, sleep_transition_reference),
        "sleep_to_sit": (sleep_transition_reference, sit_transition_reference),
    }
    for clip_id, (expected_first, expected_last) in endpoint_expectations.items():
        actual_first = np.asarray(clips[clip_id][0].convert("RGBA"), dtype=np.uint8)
        actual_last = np.asarray(clips[clip_id][-1].convert("RGBA"), dtype=np.uint8)
        if not np.array_equal(actual_first, np.asarray(expected_first, dtype=np.uint8)):
            failures.append(clip_id + ": first frame does not match its stable pose")
        if not np.array_equal(actual_last, np.asarray(expected_last, dtype=np.uint8)):
            failures.append(clip_id + ": last frame does not match its stable pose")
    tail_checks = {
        "stand_blink": temporal_tail_metrics(clips["stand_blink"], (224, 235, 320, 390)),
        "sit_lookaround": temporal_tail_metrics(clips["sit_lookaround"], (160, 320, 290, 430)),
    }
    report["tailStability"] = tail_checks
    # Delta scales approximately with the frame interval.  Keep the original
    # 12 fps perceptual limit while allowing equivalent source-rate motion.
    tail_jerk_limit = 12.0 * (12.0 / FPS)
    report["tailJerkLimit"] = tail_jerk_limit
    for clip_id, values in tail_checks.items():
        if values["peakDeltaJerk"] > tail_jerk_limit:
            failures.append(
                "{0}: abrupt tail alpha jerk {1}".format(
                    clip_id, values["peakDeltaJerk"]
                )
            )
    anatomy_motion = {
        "stand_blink_chestShoulder": region_motion_score(
            clips["stand_blink"], (82, 150, 224, 300)
        ),
        "sit_lookaround_neckShoulder": region_motion_score(
            clips["sit_lookaround"], (72, 145, 226, 292)
        ),
    }
    report["anatomyMotion"] = anatomy_motion
    if anatomy_motion["stand_blink_chestShoulder"] < 1.0:
        failures.append("stand_blink: chest/shoulder frozen independently of head")
    if anatomy_motion["sit_lookaround_neckShoulder"] < 3.0:
        failures.append("sit_lookaround: neck/shoulder frozen independently of head")
    report["ok"] = not failures
    path = GENERATED / "asset_validation.json"
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    if failures:
        raise RuntimeError("Asset validation failed:\n- " + "\n- ".join(failures))
    return report


def build_zip() -> Path:
    target = GENERATED / "yabi-animations-v4.zip"
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED) as archive:
        for path in sorted(PACK.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(PACK).as_posix())
    return target


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg"), help="path to ffmpeg")
    parser.add_argument(
        "--segmentation-model",
        type=Path,
        default=DEFAULT_SEGMENTATION_MODEL,
        help="offline U2Net ONNX model used only while rebuilding assets",
    )
    args = parser.parse_args()
    if not args.ffmpeg:
        parser.error("ffmpeg was not found")
    missing = [str(path) for path in list(CLIPS.values()) + [SOURCE / "stand_master.png", SOURCE / "sit_master.png", SOURCE / "sleep_master.png"] if not path.exists()]
    if missing:
        parser.error("missing source media: " + ", ".join(missing))

    configure_segmentation_model(args.segmentation_model)

    reset_generated()
    raw_paths = {clip_id: extract_video_frames(args.ffmpeg, clip_id, path) for clip_id, path in CLIPS.items()}

    stand_master = make_high_resolution_automatic_portrait(
        SOURCE / "stand_master.png"
    )
    # The source blink/transition videos swing the tail farther right than the
    # still pose.  Reserve the otherwise empty left side of the stage for the
    # whole standing/sitting action chain so no real tail frame is clipped.
    stand_master = translate_rgba(stand_master, PORTRAIT_SUBJECT_OFFSET_X)
    raw_transition = matte_video_frames(
        raw_paths["stand_to_sit"], working_size=VIDEO_MATTE_SIZE
    )
    transition = normalise_clip(raw_transition, stand_master)
    transition = [
        pull_edge_colour_from_fur(frame, (138, 250, 320, 445), 2.4)
        for frame in transition
    ]

    # Sit master is anchored to the final transition pose; all sitting assets share that anchor.
    sit_source = make_high_resolution_portrait(
        SOURCE / "sit_master.png",
        SIT_SOURCE_TAIL_REGION,
        SIT_SOURCE_TAIL_POLYGON,
        SIT_SOURCE_TAIL_BLEND,
    )
    sit_master = align_master_to_reference(sit_source, transition[-1])
    sleep_master = make_sleep(SOURCE / "sleep_master.png")

    sit_transition_reference = embed_transition_reference(sit_master, "portrait")
    sleep_transition_reference = embed_transition_reference(sleep_master, "sleep")
    sit_to_sleep = place_transition_on_square(
        matte_green_video_frames(raw_paths["sit_to_sleep"]),
        sit_transition_reference,
        sleep_transition_reference,
    )
    sleep_to_sit = place_transition_on_square(
        matte_green_video_frames(raw_paths["sleep_to_sit"]),
        sleep_transition_reference,
        sit_transition_reference,
    )

    blink = normalise_clip(
        matte_video_frames(raw_paths["stand_blink"], working_size=VIDEO_MATTE_SIZE),
        stand_master,
    )
    look = normalise_clip(
        matte_video_frames(
            raw_paths["sit_lookaround"],
            working_size=VIDEO_MATTE_SIZE,
            trim_contact=False,
        ),
        sit_master,
    )
    blink = [refine_video_frame(frame) for frame in blink]
    look = [
        keep_largest_alpha_component(
            refine_video_tail(refine_video_frame(frame), True)
        )
        for frame in look
    ]
    transition = smooth_clip_alpha(transition)
    blink = smooth_clip_alpha(blink)
    look = smooth_clip_alpha(look)
    sit_to_sleep = smooth_clip_alpha(sit_to_sleep)
    sleep_to_sit = smooth_clip_alpha(sleep_to_sit)
    blink = match_clip_colour(blink, stand_master, 0.45)
    look = match_clip_colour(look, sit_master, 0.72)
    sit_to_sleep = match_clip_colour(sit_to_sleep, sit_master, 0.42)
    sleep_to_sit = match_clip_colour(sleep_to_sit, sit_master, 0.42)

    blend_endpoint(blink, stand_master, True)
    blend_endpoint(blink, stand_master, False)
    blend_endpoint(transition, stand_master, True)
    blend_endpoint(transition, sit_master, False)
    blend_endpoint(look, sit_master, True)
    blend_endpoint(look, sit_master, False)
    blend_endpoint(sit_to_sleep, sit_transition_reference, True, count=6)
    blend_endpoint(sit_to_sleep, sleep_transition_reference, False, count=6)
    blend_endpoint(sleep_to_sit, sleep_transition_reference, True, count=6)
    blend_endpoint(sleep_to_sit, sit_transition_reference, False, count=6)

    clips: Dict[str, Sequence[Image.Image]] = {
        "stand_idle": [stand_master],
        "stand_blink": blink,
        "stand_to_sit": transition,
        "sit_idle": [sit_master],
        "sit_lookaround": look,
        "sit_to_sleep": sit_to_sleep,
        "sleep_to_sit": sleep_to_sit,
        "sleep_idle": [sleep_master],
    }
    for clip_id, frames in clips.items():
        save_clip(clip_id, frames)
    look_mapping = save_look_frames(look)
    manifest = build_manifest({name: len(frames) for name, frames in clips.items()}, look_mapping)
    make_previews(stand_master, sit_master, sleep_master, look[min(30, len(look) - 1)])
    make_transition_previews(sit_to_sleep, sleep_to_sit)
    save_chroma_audit_masters(stand_master, sit_master, sleep_master)
    save_source_resolution_masters()
    make_icon(stand_master)
    report = validate_assets(clips, look_mapping, manifest)
    archive = build_zip()
    print(json.dumps({"archive": str(archive), "sha256": sha256(archive), "validation": report}, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
