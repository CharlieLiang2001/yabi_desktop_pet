"""Build the complete-body Yabi 4.3 transparent action pack.

This builder is deliberately separate from ``build_assets.py``.  It keeps the
4.2 pack intact, copies it to ``pack43`` and adds complete frames from the new
green-screen videos.  No head, body or tail layers are composed together: one
output PNG always comes from one source-video frame (with only a short,
whole-frame endpoint blend against the stable sitting pose).

The source videos are decoded at their real frame count and rendered at the
existing 24 fps contract.  Their audio tracks are never copied to the pack or
to the preview movies.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import shutil
import subprocess
import zipfile
from fractions import Fraction
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple
from xml.etree import ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "assets" / "source"
GENERATED = ROOT / "assets" / "generated"
OLD_PACK = GENERATED / "pack"
PACK = GENERATED / "pack43"
PREVIEWS = GENERATED / "previews43"
WORK = ROOT.parents[1] / "work" / "yabi43-assets"
RAW = WORK / "raw"
PORTRAIT_SIZE = (320, 480)
FPS = 24
WORKING_WIDTH = 640

AI_VIDEO = ROOT.parents[1] / "ai_video"
VIDEO_SOURCES = {
    "feed": ("sit_feed_response.mp4.mp4", "sit_feed_response.mp4"),
    "pet": ("sit_pet_response.mp4.mp4", "sit_pet_response.mp4"),
    "groom": ("sit_groom_paw.mp4.mp4", "sit_groom_paw.mp4"),
    "scratch": ("sit_scratch_ear.mp4.mp4", "sit_scratch_ear.mp4"),
    "notice_all": ("sit_notice_up_all.mp4.mp4", "sit_notice_up_all.mp4"),
}

NEW_ACTIONS = {
    "notice_all": {
        "displayName": "完整环顾",
        "trigger": "manual:lookaround",
        "cooldownMs": 15000,
        "priority": 60,
        "sourceKey": "notice_all",
    },
    "feed": {
        "displayName": "喂食回应",
        "trigger": "care:feed",
        "cooldownMs": 1200000,
        "priority": 80,
        "sourceKey": "feed",
    },
    "pet": {
        "displayName": "摸摸回应",
        "trigger": "care:pet",
        "cooldownMs": 300000,
        "priority": 80,
        "sourceKey": "pet",
    },
    "groom": {
        "displayName": "舔爪",
        "trigger": "manual:groom",
        "cooldownMs": 90000,
        "priority": 40,
        "sourceKey": "groom",
    },
    "scratch": {
        "displayName": "挠耳",
        "trigger": "manual:scratch",
        "cooldownMs": 90000,
        "priority": 40,
        "sourceKey": "scratch",
    },
    "notice_left": {
        "displayName": "注意左侧",
        "trigger": "mouse:attention:left",
        "cooldownMs": 15000,
        "priority": 60,
        "sourceKey": "notice_all",
        "frameOffset": 0,
        "frames": 121,
    },
    "notice_right": {
        "displayName": "注意右侧",
        "trigger": "mouse:attention:right",
        "cooldownMs": 15000,
        "priority": 60,
        "sourceKey": "notice_all",
        "frameOffset": 120,
        "frames": 97,
    },
    "notice_up": {
        "displayName": "抬头注意",
        "trigger": "mouse:attention:up",
        "cooldownMs": 15000,
        "priority": 60,
        "sourceKey": "notice_all",
        "frameOffset": 216,
        "frames": 73,
    },
}


def load_legacy_module():
    """Load old matte helpers without running its 4.2 build entry point."""
    path = ROOT / "tools" / "build_assets.py"
    spec = importlib.util.spec_from_file_location("yabi_legacy_assets", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("Cannot load legacy asset helpers: " + str(path))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    # The 4.2 helper's Python flood-fill is useful for still audits but too
    # expensive for 821 video frames.  The green matte already has a connected
    # border-derived key; do the final component audit at output resolution.
    module.keep_largest_alpha_component = lambda image, threshold=14: image
    return module


LEGACY = load_legacy_module()


def run(command: Sequence[str], *, capture: bool = False) -> str:
    result = subprocess.run(
        list(command),
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode:
        raise RuntimeError(
            "Command failed ({0}):\n{1}\n{2}".format(
                result.returncode, " ".join(str(value) for value in command), result.stderr
            )
        )
    return result.stdout if capture else ""


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def safe_clean(path: Path) -> None:
    resolved = path.resolve()
    allowed = [PACK.resolve(), PREVIEWS.resolve(), RAW.resolve()]
    if resolved not in allowed:
        raise RuntimeError("Refusing to clean unexpected asset path: " + str(resolved))
    if path.exists():
        shutil.rmtree(path)
    path.mkdir(parents=True, exist_ok=True)


def ensure_sources() -> Dict[str, Dict[str, object]]:
    metadata: Dict[str, Dict[str, object]] = {}
    for key, (original_name, stable_name) in VIDEO_SOURCES.items():
        original = AI_VIDEO / original_name
        if not original.exists():
            raise FileNotFoundError("New source video is missing: " + str(original))
        target = SOURCE / stable_name
        shutil.copy2(original, target)
        metadata[key] = {
            "originalName": original_name,
            "stableName": stable_name,
            "path": str(target),
            "sha256": sha256(original),
            "bytes": original.stat().st_size,
        }
    return metadata


def fraction_value(value: str | None) -> float:
    if not value or value in {"0/0", "N/A"}:
        return 0.0
    try:
        return float(Fraction(value))
    except (ValueError, ZeroDivisionError):
        return 0.0


def probe_video(ffprobe: str, path: Path) -> Dict[str, object]:
    raw = run(
        [
            ffprobe,
            "-v",
            "error",
            "-show_streams",
            "-show_format",
            "-of",
            "json",
            str(path),
        ],
        capture=True,
    )
    data = json.loads(raw)
    video = next((item for item in data.get("streams", []) if item.get("codec_type") == "video"), None)
    if not video:
        raise RuntimeError("No video stream in " + str(path))
    duration = float(video.get("duration") or data.get("format", {}).get("duration") or 0.0)
    timing_data = json.loads(run([ffprobe, "-v", "error", "-select_streams", "v:0", "-show_frames",
        "-show_entries", "frame=best_effort_timestamp_time,duration_time", "-of", "json", str(path)], capture=True))
    frame_times = [round(float(frame["best_effort_timestamp_time"]) * 1000, 3)
        for frame in timing_data["frames"]]
    frame_durations = [round(float(frame.get("duration_time", 1.0 / FPS)) * 1000, 3)
        for frame in timing_data["frames"]]
    fps = fraction_value(str(video.get("avg_frame_rate") or ""))
    if fps <= 0:
        fps = fraction_value(str(video.get("r_frame_rate") or ""))
    return {
        "codec": video.get("codec_name"),
        "width": int(video.get("width") or 0),
        "height": int(video.get("height") or 0),
        "avgFrameRate": video.get("avg_frame_rate"),
        "realFps": fps,
        "durationSeconds": duration,
        "durationMs": int(round(duration * 1000.0)),
        "frameCountHint": int(video.get("nb_frames") or 0),
        "hasAudio": any(item.get("codec_type") == "audio" for item in data.get("streams", [])),
        "frameTimesMs": frame_times,
        "frameDurationsMs": frame_durations,
    }


def extract_frames(ffmpeg: str, clip_id: str, source: Path) -> List[Path]:
    target = RAW / clip_id
    target.mkdir(parents=True, exist_ok=True)
    # passthrough keeps the source's complete frame sequence and elapsed motion;
    # the renderer's 24-fps contract is recorded in the manifest separately.
    run(
        [
            ffmpeg,
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source),
            "-an",
            "-vf",
            "scale={0}:-2".format(WORKING_WIDTH),
            "-fps_mode",
            "passthrough",
            "-start_number",
            "0",
            str(target / "%04d.png"),
            "-y",
        ]
    )
    frames = sorted(target.glob("*.png"))
    if not frames:
        raise RuntimeError("No frames extracted for " + clip_id)
    return frames


def matte_frame(path: Path) -> Image.Image:
    with Image.open(path) as image:
        source = image.convert("RGB")
    # The legacy keyer estimates the green from the current video's border, so
    # bright and dark green clips do not share a hard-coded colour value.
    result = LEGACY.remove_green_background(source)
    data = np.asarray(result.convert("RGBA"), dtype=np.uint8).copy()
    data[..., 3][data[..., 3] < 8] = 0
    result = Image.fromarray(data, "RGBA")
    result = LEGACY.pull_edge_colour_from_fur(
        result, (2, 2, result.width - 2, result.height - 2), 1.8
    )
    return result


def alpha_bbox(image: Image.Image, threshold: int = 40) -> Tuple[int, int, int, int]:
    alpha = np.asarray(image.getchannel("A"), dtype=np.uint8)
    ys, xs = np.where(alpha >= threshold)
    if len(xs) == 0:
        raise RuntimeError("Empty alpha matte")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def median_box(boxes: Sequence[Tuple[int, int, int, int]]) -> Tuple[float, float, float, float]:
    values = np.asarray(boxes, dtype=np.float32)
    return tuple(float(value) for value in np.median(values, axis=0))  # type: ignore[return-value]


def rgba_to_stage(
    frames: Sequence[Image.Image],
    source_anchor: Tuple[float, float, float, float],
    target_box: Tuple[int, int, int, int],
) -> Tuple[List[Image.Image], Dict[str, object]]:
    """Place a clip using one fixed scale/translation for every frame."""
    source_boxes = [alpha_bbox(frame) for frame in frames]
    union = (
        min(box[0] for box in source_boxes),
        min(box[1] for box in source_boxes),
        max(box[2] for box in source_boxes),
        max(box[3] for box in source_boxes),
    )
    source_w = max(1.0, source_anchor[2] - source_anchor[0])
    source_h = max(1.0, source_anchor[3] - source_anchor[1])
    target_w = max(1.0, target_box[2] - target_box[0])
    target_h = max(1.0, target_box[3] - target_box[1])
    scale = min(target_w / source_w, target_h / source_h)
    # Keep the complete action union inside a two-pixel transparent safety
    # border.  This is a single clip-wide scale, never a per-frame resize.
    safe_left, safe_top, safe_right, safe_bottom = 2.0, 2.0, 318.0, 478.0
    union_w = max(1.0, union[2] - union[0])
    union_h = max(1.0, union[3] - union[1])
    scale = min(scale, (safe_right - safe_left) / union_w, (safe_bottom - safe_top) / union_h)
    target_center_x = (target_box[0] + target_box[2]) * 0.5
    target_baseline = float(target_box[3])
    anchor_center_x = (source_anchor[0] + source_anchor[2]) * 0.5
    anchor_baseline = source_anchor[3]
    offset_x = target_center_x - anchor_center_x * scale
    offset_y = target_baseline - anchor_baseline * scale
    mapped_union = (
        union[0] * scale + offset_x,
        union[1] * scale + offset_y,
        union[2] * scale + offset_x,
        union[3] * scale + offset_y,
    )
    if mapped_union[0] < safe_left:
        offset_x += safe_left - mapped_union[0]
    if mapped_union[2] > safe_right:
        offset_x -= mapped_union[2] - safe_right
    if mapped_union[1] < safe_top:
        offset_y += safe_top - mapped_union[1]
    if mapped_union[3] > safe_bottom:
        offset_y -= mapped_union[3] - safe_bottom

    placed: List[Image.Image] = []
    for frame in frames:
        resized = frame.resize(
            (
                max(1, int(round(frame.width * scale))),
                max(1, int(round(frame.height * scale))),
            ),
            Image.Resampling.LANCZOS,
        )
        canvas = Image.new("RGBA", PORTRAIT_SIZE, (0, 0, 0, 0))
        canvas.alpha_composite(resized, (int(round(offset_x)), int(round(offset_y))))
        canvas = LEGACY.clear_transparent_frame(canvas)
        placed.append(canvas)
    return placed, {
        "scale": scale,
        "offset": [offset_x, offset_y],
        "sourceAnchor": list(source_anchor),
        "sourceUnion": list(union),
        "targetBox": list(target_box),
        "outputUnion": [
            union[0] * scale + offset_x,
            union[1] * scale + offset_y,
            union[2] * scale + offset_x,
            union[3] * scale + offset_y,
        ],
    }


def remove_small_components_at_stage(image: Image.Image) -> Image.Image:
    """Remove only clearly detached matte islands after downsampling.

    A small 320x480 scan is safe enough for one frame and avoids applying a
    wide morphological erosion to whiskers or tail hairs.  The main keyer has
    already removed border-connected green; this pass only drops components
    smaller than 4 pixels at the final render size.
    """
    data = np.asarray(image.convert("RGBA"), dtype=np.uint8).copy()
    alpha = data[..., 3]
    mask = alpha >= 18
    height, width = mask.shape
    visited = np.zeros_like(mask, dtype=bool)
    keep = np.zeros_like(mask, dtype=bool)
    for sy in range(height):
        for sx in range(width):
            if visited[sy, sx] or not mask[sy, sx]:
                continue
            stack = [(sy, sx)]
            visited[sy, sx] = True
            component: List[Tuple[int, int]] = []
            while stack:
                y, x = stack.pop()
                component.append((y, x))
                for ny in range(max(0, y - 1), min(height, y + 2)):
                    for nx in range(max(0, x - 1), min(width, x + 2)):
                        if mask[ny, nx] and not visited[ny, nx]:
                            visited[ny, nx] = True
                            stack.append((ny, nx))
            if len(component) >= 4:
                for y, x in component:
                    keep[y, x] = True
    near = np.asarray(Image.fromarray(np.uint8(keep) * 255, "L").filter(ImageFilter.MaxFilter(3))) > 0
    alpha[~near] = 0
    data[..., 3] = alpha
    data[0, :, 3] = 0
    data[-1, :, 3] = 0
    data[:, 0, 3] = 0
    data[:, -1, 3] = 0
    return Image.fromarray(data, "RGBA")


def endpoint_blend(frames: List[Image.Image], stable: Image.Image, count: int = 3) -> None:
    """Use a short, whole-frame blend at stable sitting endpoints."""
    count = min(count, len(frames))
    for index in range(count):
        frames[index] = Image.blend(stable, frames[index], (index + 1) / float(count + 1))
    for offset in range(count):
        index = len(frames) - count + offset
        frames[index] = Image.blend(frames[index], stable, (offset + 1) / float(count + 1))


def save_frames(clip_id: str, frames: Sequence[Image.Image]) -> None:
    directory = PACK / "clips" / clip_id
    directory.mkdir(parents=True, exist_ok=True)
    for index, frame in enumerate(frames):
        frame.save(directory / "{0:04d}.png".format(index), optimize=True)


def checkerboard(size: Tuple[int, int]) -> Image.Image:
    image = Image.new("RGB", size, (246, 246, 246))
    draw = ImageDraw.Draw(image)
    cell = 20
    for y in range(0, size[1], cell):
        for x in range(0, size[0], cell):
            if ((x // cell) + (y // cell)) % 2:
                draw.rectangle((x, y, x + cell - 1, y + cell - 1), fill=(220, 220, 220))
    return image


def contact_sheet(clip_id: str, frames: Sequence[Image.Image]) -> None:
    samples = 6
    indices = [int(round((len(frames) - 1) * i / float(samples - 1))) for i in range(samples)]
    cell = (180, 270)
    backgrounds = {
        "transparent": checkerboard(cell),
        "dark": Image.new("RGB", cell, (24, 27, 33)),
        "blue": Image.new("RGB", cell, (28, 92, 164)),
    }
    font = ImageFont.load_default()
    for background_name, background in backgrounds.items():
        sheet = Image.new("RGB", (cell[0] * samples, cell[1] + 24), (20, 20, 20))
        for col, index in enumerate(indices):
            panel = background.convert("RGBA").copy()
            display = frames[index].resize(cell, Image.Resampling.LANCZOS)
            panel.alpha_composite(display)
            sheet.paste(panel.convert("RGB"), (col * cell[0], 24))
            ImageDraw.Draw(sheet).text((col * cell[0] + 4, 6), "{0} {1}".format(clip_id, index), fill="white", font=font)
        sheet.save(PREVIEWS / (clip_id + "_contact_" + background_name + ".png"), optimize=True)


def make_preview_video(ffmpeg: str, clip_id: str, start: int = 0, count: int | None = None) -> Path:
    source = PACK / "clips" / "notice_all" if clip_id.startswith("notice_") else PACK / "clips" / clip_id
    output = PREVIEWS / (clip_id + "_preview.mp4")
    command = [
        ffmpeg,
        "-hide_banner",
        "-loglevel",
        "error",
        "-framerate",
        str(FPS),
        "-start_number",
        str(start),
        "-i",
        str(source / "%04d.png"),
    ]
    if count is not None:
        command.extend(["-frames:v", str(count)])
    command.extend(
        [
            "-an",
            "-c:v",
            "libx264",
            "-pix_fmt",
            "yuv420p",
            "-movflags",
            "+faststart",
            "-y",
            str(output),
        ]
    )
    run(command)
    return output


def stage_bbox_union(frames: Sequence[Image.Image]) -> Tuple[int, int, int, int]:
    boxes = [alpha_bbox(frame) for frame in frames]
    return (
        min(box[0] for box in boxes),
        min(box[1] for box in boxes),
        max(box[2] for box in boxes),
        max(box[3] for box in boxes),
    )


def frame_metrics(frames: Sequence[Image.Image]) -> Dict[str, object]:
    bboxes = [alpha_bbox(frame) for frame in frames]
    alpha_counts = []
    edge_hits = []
    tail_counts = []
    for frame in frames:
        alpha = np.asarray(frame.getchannel("A"), dtype=np.uint8)
        alpha_counts.append(int(np.count_nonzero(alpha >= 32)))
        edge_hits.append(
            int(
                np.count_nonzero(alpha[:2, :])
                + np.count_nonzero(alpha[-2:, :])
                + np.count_nonzero(alpha[:, :2])
                + np.count_nonzero(alpha[:, -2:])
            )
        )
        tail_counts.append(int(np.count_nonzero(alpha[315:460, 145:315] >= 32)))
    return {
        "frameCount": len(frames),
        "size": [frames[0].width, frames[0].height],
        "unionBBox": list(stage_bbox_union(frames)),
        "firstBBox": list(bboxes[0]),
        "lastBBox": list(bboxes[-1]),
        "minOpaquePixels": min(alpha_counts),
        "maxOpaquePixels": max(alpha_counts),
        "edgeAlphaPixelsMax": max(edge_hits),
        "tailOpaquePixelsMin": min(tail_counts),
        "tailOpaquePixelsMax": max(tail_counts),
        "tailCropSuspected": max(edge_hits) > 0,
        "transparentCorners": all(
            int(np.asarray(frame.getchannel("A"), dtype=np.uint8)[corner]) == 0
            for frame in frames
            for corner in [(0, 0), (0, 319), (479, 0), (479, 319)]
        ),
    }


def build_manifest(
    probe: Dict[str, Dict[str, object]],
    frame_counts: Dict[str, int],
    source_metadata: Dict[str, Dict[str, object]],
    head_anchor: Tuple[float, float, float],
) -> Path:
    root = ET.Element(
        "yabiAssets",
        {
            "version": "4.3",
            "portraitWidth": str(PORTRAIT_SIZE[0]),
            "portraitHeight": str(PORTRAIT_SIZE[1]),
            "sleepWidth": "480",
            "sleepHeight": "320",
            "transitionWidth": "480",
            "transitionHeight": "480",
            "headX": "{0:.5f}".format(head_anchor[0]),
            "headY": "{0:.5f}".format(head_anchor[1]),
            "bodyHeight": "{0:.5f}".format(head_anchor[2]),
            "renderFps": str(FPS),
            "newActionPack": "complete-body",
        },
    )
    # Existing files are deliberately copied unchanged into pack43.  These
    # definitions keep 4.2 consumers able to read the pack while the 4.3
    # controller can select the richer metadata on new actions.
    old_defs = [
        ("stand_idle", 1, "Standing", "Standing", "hold"),
        ("stand_blink", 24, "Standing", "Standing", "once"),
        ("stand_to_sit", 24, "Standing", "Sitting", "once"),
        ("sit_idle", 1, "Sitting", "Sitting", "hold"),
        ("sit_lookaround", 24, "Sitting", "Sitting", "once"),
        ("sit_to_sleep", 24, "Sitting", "Sleeping", "once"),
        ("sleep_to_sit", 24, "Sleeping", "Sitting", "once"),
        ("sleep_idle", 1, "Sleeping", "Sleeping", "hold"),
    ]
    for clip_id, fps, start, end, mode in old_defs:
        path = PACK / "clips" / clip_id
        count = len(list(path.glob("*.png"))) if path.exists() else 0
        ET.SubElement(
            root,
            "clip",
            {
                "id": clip_id,
                "path": "clips/" + clip_id,
                "fps": str(fps),
                "frames": str(count),
                "startPose": start,
                "endPose": end,
                "mode": mode,
                "direction": "forward",
                "cooldownMs": "0",
                "displayName": {"stand_idle": "站立", "stand_blink": "站立眨眼", "stand_to_sit": "站立到坐下",
                    "sit_idle": "坐姿", "sit_lookaround": "旧版完整环顾", "sit_to_sleep": "坐姿到睡眠",
                    "sleep_to_sit": "睡眠到坐姿", "sleep_idle": "睡姿"}[clip_id],
            },
        )

    for clip_id, definition in NEW_ACTIONS.items():
        source_key = str(definition["sourceKey"])
        info = probe[source_key]
        count = int(definition.get("frames", frame_counts[source_key]))
        offset = int(definition.get("frameOffset", 0))
        real_fps = float(info["realFps"] or FPS)
        times = info.get("frameTimesMs", [])
        if times:
            local_times = [round(value - times[offset], 3) for value in times[offset:offset + count]]
            duration_ms = round(local_times[-1] + info["frameDurationsMs"][offset + count - 1], 3)
        else:
            local_times = []
            duration_ms = int(round(count / real_fps * 1000.0))
        attrs = {
            "id": clip_id,
            "path": "clips/" + source_key,
            "fps": str(FPS),
            "frames": str(count),
            "startPose": "Sitting",
            "endPose": "Sitting",
            "mode": "once",
            "direction": "forward",
            "displayName": str(definition["displayName"]),
            "trigger": str(definition["trigger"]),
            "cooldownMs": str(int(definition["cooldownMs"])),
            "priority": str(int(definition["priority"])),
            "interruptPolicy": "complete",
            "optional": "true",
            "durationMs": str(duration_ms),
            "frameOffset": str(offset),
            "sourceVideo": str(source_metadata[source_key]["stableName"]),
            "sourceRealFps": "{0:.6f}".format(real_fps),
        }
        node = ET.SubElement(root, "clip", attrs)
        if local_times:
            ET.SubElement(node, "timing", {"ms": ",".join(str(value) for value in local_times)})

    look = ET.SubElement(root, "mouseLook", {"updateHz": "10", "mode": "event-attention"})
    for clip_id in ("notice_left", "notice_right", "notice_up"):
        ET.SubElement(look, "action", {"clip": clip_id, "path": "clips/notice_all"})

    ET.indent(root, space="  ")
    path = PACK / "manifest.xml"
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
    return path


def validate_pack(
    manifest: Path,
    source_metadata: Dict[str, Dict[str, object]],
    probes: Dict[str, Dict[str, object]],
    frames_by_source: Dict[str, Sequence[Image.Image]],
    placement: Dict[str, Dict[str, object]],
    target_box: Tuple[int, int, int, int],
) -> Dict[str, object]:
    root = ET.parse(manifest).getroot()
    clip_nodes = {node.attrib["id"]: node for node in root.findall("clip")}
    report: Dict[str, object] = {
        "version": root.attrib.get("version"),
        "manifest": str(manifest),
        "source": source_metadata,
        "probe": probes,
        "targetSittingBBox": list(target_box),
        "headAnchor": {
            "headX": float(root.attrib["headX"]),
            "headY": float(root.attrib["headY"]),
            "bodyHeight": float(root.attrib["bodyHeight"]),
        },
        "clips": {},
        "checks": {},
    }
    failures: List[str] = []
    clip_report: Dict[str, object] = {}
    for source_key, frames in frames_by_source.items():
        metrics = frame_metrics(frames)
        metrics["placement"] = placement[source_key]
        metrics["sourceFrameCount"] = len(frames)
        metrics["sourceDurationMs"] = probes[source_key]["durationMs"]
        clip_report[source_key] = metrics
        if metrics["frameCount"] <= 0:
            failures.append(source_key + ": no output frames")
        if metrics["size"] != list(PORTRAIT_SIZE):
            failures.append(source_key + ": wrong output dimensions")
        if metrics["edgeAlphaPixelsMax"]:
            failures.append(source_key + ": alpha touches output edge")
        if metrics["tailCropSuspected"]:
            failures.append(source_key + ": tail crop suspected")
    report["clips"] = clip_report

    expected_segments = {
        "notice_left": (0, 121),
        "notice_right": (120, 97),
        "notice_up": (216, 73),
    }
    segment_report: Dict[str, object] = {}
    for clip_id, (offset, count) in expected_segments.items():
        node = clip_nodes.get(clip_id)
        if node is None:
            failures.append(clip_id + ": missing manifest node")
            continue
        if int(node.attrib.get("frameOffset", "-1")) != offset or int(node.attrib.get("frames", "-1")) != count:
            failures.append(clip_id + ": unexpected frame offset/count")
        selected = list(frames_by_source["notice_all"])[offset : offset + count]
        segment_report[clip_id] = {
            "frameOffset": offset,
            "frames": count,
            "firstBBox": list(alpha_bbox(selected[0])),
            "lastBBox": list(alpha_bbox(selected[-1])),
            "durationMs": float(node.attrib.get("durationMs", "0")),
        }
    report["noticeSegments"] = segment_report
    report["checks"] = {
        "allNewActionsRegistered": all(key in clip_nodes for key in NEW_ACTIONS),
        "allSourceHashesRecorded": all(bool(item.get("sha256")) for item in source_metadata.values()),
        "allNewFramesPortrait320x480": all(
            metrics["size"] == list(PORTRAIT_SIZE) for metrics in clip_report.values()
        ),
        "allOutputCornersTransparent": all(
            bool(metrics["transparentCorners"]) for metrics in clip_report.values()
        ),
        "noOutputEdgeAlpha": all(
            metrics["edgeAlphaPixelsMax"] == 0 for metrics in clip_report.values()
        ),
        "noticeOffsetsMatch": len(segment_report) == 3
        and all(
            int(clip_nodes[key].attrib.get("frameOffset", "-1")) == value[0]
            and int(clip_nodes[key].attrib.get("frames", "-1")) == value[1]
            for key, value in expected_segments.items()
        ),
    }
    report["ok"] = not failures
    report["failures"] = failures
    return report


def validate_zip(path: Path) -> Dict[str, object]:
    with zipfile.ZipFile(path, "r") as archive:
        names = archive.namelist()
        manifest = ET.fromstring(archive.read("manifest.xml"))
        parsed: Dict[str, int] = {}
        for node in manifest.findall("clip"):
            clip_path = node.attrib["path"].rstrip("/") + "/"
            count = sum(1 for name in names if name.startswith(clip_path) and name.endswith(".png"))
            parsed[node.attrib["id"]] = count
        return {
            "bytes": path.stat().st_size,
            "sha256": sha256(path),
            "entries": len(names),
            "manifestVersion": manifest.attrib.get("version"),
            "clipPngCounts": parsed,
            "hasNewActions": all("clips/" + key in " ".join(names) for key in ("feed", "pet", "groom", "scratch", "notice_all")),
        }


def build_zip() -> Path:
    target = GENERATED / "yabi-animations-v43.zip"
    if target.exists():
        target.unlink()
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED) as archive:
        for path in sorted(PACK.rglob("*")):
            if path.is_file() and path.name != "validation43.json":
                archive.write(path, path.relative_to(PACK).as_posix())
    return target


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ffmpeg", default=shutil.which("ffmpeg"), required=False)
    parser.add_argument("--ffprobe", default=shutil.which("ffprobe"), required=False)
    parser.add_argument("--repack-only", action="store_true", help="Revalidate existing RGBA frames and rebuild metadata without changing pixels")
    args = parser.parse_args()
    if not args.ffmpeg or not args.ffprobe:
        parser.error("ffmpeg and ffprobe are required")

    if args.repack_only:
        previous = json.loads((GENERATED / "validation43.json").read_text(encoding="utf-8"))
        metadata = previous["source"]
        probes = {key: probe_video(args.ffprobe, SOURCE / names[1]) for key, names in VIDEO_SOURCES.items()}
        placed = {key: [Image.open(path).convert("RGBA") for path in sorted((PACK / "clips" / key).glob("*.png"))]
            for key in VIDEO_SOURCES}
        target = tuple(previous["targetSittingBBox"])
        # Calibrated between the eyes on the unchanged 320x480 sitting image.
        head = (100.0 / 320, 149.0 / 480, (target[3] - target[1]) / 480)
        manifest = build_manifest(probes, {key: len(frames) for key, frames in placed.items()}, metadata, head)
        report = validate_pack(manifest, metadata, probes, placed,
            {key: previous["clips"][key]["placement"] for key in VIDEO_SOURCES}, target)
        archive = build_zip()
        report["zip"] = validate_zip(archive)
        report["previewPaths"] = previous.get("previewPaths", {})
        report["manifestSha256"] = sha256(manifest)
        (GENERATED / "validation43.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({"ok": report["ok"], "zip": str(archive), "clips": len(NEW_ACTIONS)}, ensure_ascii=False))
        return 0 if report["ok"] else 1

    source_metadata = ensure_sources()
    safe_clean(PACK)
    safe_clean(PREVIEWS)
    safe_clean(RAW)
    shutil.copytree(OLD_PACK, PACK, dirs_exist_ok=True)

    source_probes: Dict[str, Dict[str, object]] = {}
    raw_paths: Dict[str, List[Path]] = {}
    matte_frames: Dict[str, List[Image.Image]] = {}
    for source_key, (_, stable_name) in VIDEO_SOURCES.items():
        source_path = SOURCE / stable_name
        source_probes[source_key] = probe_video(args.ffprobe, source_path)
        raw_paths[source_key] = extract_frames(args.ffmpeg, source_key, source_path)
        matte_frames[source_key] = [matte_frame(path) for path in raw_paths[source_key]]
        if len(matte_frames[source_key]) < 2:
            raise RuntimeError("Too few decoded frames for " + source_key)

    stable_path = PACK / "clips" / "sit_idle" / "0000.png"
    with Image.open(stable_path) as image:
        stable = image.convert("RGBA")
    target_box = alpha_bbox(stable)
    # The first/last 0.5 seconds are the neutral sitting pose for each source.
    # One median anchor per clip prevents body/tail freezing and prevents
    # per-frame scale jitter.
    placed: Dict[str, List[Image.Image]] = {}
    placement: Dict[str, Dict[str, object]] = {}
    for source_key, frames in matte_frames.items():
        edge = max(1, min(12, len(frames) // 8))
        neutral_boxes = [alpha_bbox(frame) for frame in frames[:edge] + frames[-edge:]]
        anchor = median_box(neutral_boxes)
        result, info = rgba_to_stage(frames, anchor, target_box)
        result = [LEGACY.clear_transparent_frame(frame) for frame in result]
        # Whole-frame temporal alpha smoothing keeps green compression blocks
        # from sparkling without freezing any RGB body or tail motion.
        result = LEGACY.smooth_clip_alpha(result)
        result = [remove_small_components_at_stage(frame) for frame in result]
        endpoint_blend(result, stable, count=3)
        placed[source_key] = result
        placement[source_key] = info
        save_frames(source_key, result)
        contact_sheet(source_key, result)

    # The full mother clip is the shared frame source for three complete-body
    # attention actions.  The source ends are retained; offsets are metadata.
    for clip_id, definition in NEW_ACTIONS.items():
        if clip_id.startswith("notice_") and "frameOffset" in definition:
            offset = int(definition["frameOffset"])
            count = int(definition["frames"])
            preview_frames = placed["notice_all"][offset : offset + count]
            contact_sheet(clip_id, preview_frames)

    preview_paths: Dict[str, str] = {}
    preview_paths["feed"] = str(make_preview_video(args.ffmpeg, "feed"))
    preview_paths["pet"] = str(make_preview_video(args.ffmpeg, "pet"))
    preview_paths["groom"] = str(make_preview_video(args.ffmpeg, "groom"))
    preview_paths["scratch"] = str(make_preview_video(args.ffmpeg, "scratch"))
    preview_paths["notice_all"] = str(make_preview_video(args.ffmpeg, "notice_all"))
    for clip_id, definition in NEW_ACTIONS.items():
        if clip_id.startswith("notice_") and "frameOffset" in definition:
            preview_paths[clip_id] = str(
                make_preview_video(
                    args.ffmpeg,
                    clip_id,
                    int(definition["frameOffset"]),
                    int(definition["frames"]),
                )
            )

    # Head anchor is derived from the stable sitting portrait.  It tracks the
    # face rather than the tail-extended body bbox: 31% into the bbox width and
    # 13% down from its top, matching the existing 4.2 mouse-look anchor.
    head_x_px = 100.0
    head_y_px = 149.0
    head_anchor = (head_x_px / PORTRAIT_SIZE[0], head_y_px / PORTRAIT_SIZE[1], (target_box[3] - target_box[1]) / PORTRAIT_SIZE[1])
    manifest = build_manifest(source_probes, {key: len(value) for key, value in placed.items()}, source_metadata, head_anchor)
    report = validate_pack(manifest, source_metadata, source_probes, placed, placement, target_box)
    zip_path = build_zip()
    report["previewPaths"] = preview_paths
    report["zip"] = validate_zip(zip_path)
    report["manifestSha256"] = sha256(manifest)
    validation_path = GENERATED / "validation43.json"
    validation_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    shutil.copy2(validation_path, PACK / "validation43.json")
    print(json.dumps({"manifest": str(manifest), "validation": str(validation_path), "zip": str(zip_path), "headAnchor": report["headAnchor"], "ok": report["ok"], "zipSha256": report["zip"]["sha256"]}, ensure_ascii=False, indent=2))
    if not report["ok"]:
        raise RuntimeError("4.3 asset validation failed:\n- " + "\n- ".join(report["failures"]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
