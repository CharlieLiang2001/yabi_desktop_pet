#!/usr/bin/env python3
"""Create the user-facing Yabi Desktop Pet 4.3 deliverables.

The package is assembled from an explicit input manifest.  The portable ZIP
is written directly from that manifest (there is no recursive staging copy),
and the current 4.3 ZIP is replaced atomically after its contents have been
checked.  Existing 4.2 deliverables are never read or removed by this script.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import BinaryIO, List, Sequence, Tuple


PROJECT = Path(__file__).resolve().parents[1]
WORKSPACE = PROJECT.parents[1]
OUTPUTS = WORKSPACE / "outputs"

SOURCE_EXE = PROJECT / "bin" / "YabiDesktopPet43.exe"
SELF_TEST = PROJECT / "bin" / "self-test43.txt"
VALIDATION_DIR = PROJECT / "bin" / "validation43"
ASSET_VALIDATION = PROJECT / "assets" / "generated" / "validation43.json"
DOCS = PROJECT / "docs"
PREVIEWS = PROJECT / "assets" / "generated" / "previews43"

USE_DOC = DOCS / "使用说明4.3.txt"
REFERENCE_DOC = DOCS / "开源项目参考说明4.3.md"
REPORT_DOC = DOCS / "验证报告4.3.md"

PRODUCT = "亚比桌宠4.3_自然互动版"
PORTABLE_ROOT_NAME = PRODUCT + "_便携版"
PACKAGE_EXE_NAME = PRODUCT + ".exe"

DELIVERY_EXE = OUTPUTS / (PRODUCT + "_双击运行.exe")
DELIVERY_ZIP = OUTPUTS / (PRODUCT + "_便携版.zip")
DELIVERY_USE_DOC = OUTPUTS / (PRODUCT + "_使用说明.txt")
DELIVERY_REPORT = OUTPUTS / (PRODUCT + "_验证报告.md")
DELIVERY_HASH = OUTPUTS / (PRODUCT + "_SHA256.txt")
DELIVERY_PREVIEWS = OUTPUTS / (PRODUCT + "_新动作功能预览")

PREVIEW_ACTIONS: Tuple[str, ...] = (
    "feed",
    "pet",
    "groom",
    "scratch",
    "notice_all",
    "notice_left",
    "notice_right",
    "notice_up",
)
PREVIEW_BACKGROUNDS: Tuple[str, ...] = ("dark", "blue", "transparent")
SUPPORTED_VALIDATION_SUFFIXES = {".png", ".txt", ".json", ".csv", ".log"}


@dataclass(frozen=True)
class ZipEntry:
    source: Path
    archive_name: str


def digest_stream(stream: BinaryIO) -> str:
    digest = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(chunk)
    return digest.hexdigest().upper()


def digest_file(path: Path) -> str:
    with path.open("rb") as stream:
        return digest_stream(stream)


def preview_sources() -> List[Path]:
    paths: List[Path] = []
    for action in PREVIEW_ACTIONS:
        for background in PREVIEW_BACKGROUNDS:
            paths.append(PREVIEWS / f"{action}_contact_{background}.png")
        paths.append(PREVIEWS / f"{action}_preview.mp4")
    return paths


def validation_sources() -> List[Path]:
    if not VALIDATION_DIR.is_dir():
        return []
    return sorted(
        path
        for path in VALIDATION_DIR.rglob("*")
        if path.is_file()
    )


def required_inputs() -> Tuple[List[Path], List[Path]]:
    fixed = [
        SOURCE_EXE,
        USE_DOC,
        REFERENCE_DOC,
        REPORT_DOC,
        SELF_TEST,
        ASSET_VALIDATION,
        *preview_sources(),
    ]
    validation = validation_sources()
    missing = [path for path in fixed if not path.is_file()]
    if not validation:
        missing.append(VALIDATION_DIR)
    unsupported = [
        path
        for path in validation
        if path.suffix.lower() not in SUPPORTED_VALIDATION_SUFFIXES
    ]
    if unsupported:
        raise SystemExit(
            "Unsupported files in bin/validation43 (expected PNG/TXT/JSON/CSV/LOG):\n"
            + "\n".join(f"- {path}" for path in unsupported)
        )
    if missing:
        raise SystemExit(
            "Missing packaging inputs:\n" + "\n".join(f"- {path}" for path in missing)
        )
    return fixed, validation


def copy_delivery_files(validation: Sequence[Path]) -> None:
    OUTPUTS.mkdir(parents=True, exist_ok=True)
    shutil.copy2(SOURCE_EXE, DELIVERY_EXE)
    shutil.copy2(USE_DOC, DELIVERY_USE_DOC)
    shutil.copy2(REPORT_DOC, DELIVERY_REPORT)
    DELIVERY_PREVIEWS.mkdir(parents=True, exist_ok=True)
    for source in preview_sources():
        shutil.copy2(source, DELIVERY_PREVIEWS / source.name)
    for source in validation:
        if source.suffix.lower() != ".png":
            continue
        relative = source.relative_to(VALIDATION_DIR)
        target = DELIVERY_PREVIEWS / "功能界面" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def write_hash_file(exe_hash: str) -> None:
    text = (
        "亚比桌宠 4.3 · SHA-256\n"
        "=========================\n"
        f"{exe_hash}  {DELIVERY_EXE.name}\n"
        f"{exe_hash}  {PACKAGE_EXE_NAME}（便携 ZIP 内）\n"
        "\n两个 EXE 的字节内容完全一致。\n"
    )
    DELIVERY_HASH.write_text(text, encoding="utf-8-sig")


def build_entries(validation: Sequence[Path]) -> List[ZipEntry]:
    entries = [
        ZipEntry(SOURCE_EXE, f"{PORTABLE_ROOT_NAME}/{PACKAGE_EXE_NAME}"),
        ZipEntry(USE_DOC, f"{PORTABLE_ROOT_NAME}/{USE_DOC.name}"),
        ZipEntry(REFERENCE_DOC, f"{PORTABLE_ROOT_NAME}/{REFERENCE_DOC.name}"),
        ZipEntry(REPORT_DOC, f"{PORTABLE_ROOT_NAME}/{REPORT_DOC.name}"),
        ZipEntry(DELIVERY_HASH, f"{PORTABLE_ROOT_NAME}/SHA256.txt"),
        ZipEntry(SELF_TEST, f"{PORTABLE_ROOT_NAME}/验证资料/{SELF_TEST.name}"),
        ZipEntry(
            ASSET_VALIDATION,
            f"{PORTABLE_ROOT_NAME}/验证资料/{ASSET_VALIDATION.name}",
        ),
    ]
    for source in preview_sources():
        entries.append(
            ZipEntry(source, f"{PORTABLE_ROOT_NAME}/新动作与功能预览/{source.name}")
        )
    for source in validation:
        relative = source.relative_to(VALIDATION_DIR).as_posix()
        entries.append(
            ZipEntry(
                source,
                f"{PORTABLE_ROOT_NAME}/验证资料/validation43/{relative}",
            )
        )
        if source.suffix.lower() == ".png":
            entries.append(
                ZipEntry(
                    source,
                    f"{PORTABLE_ROOT_NAME}/新动作与功能预览/功能界面/{relative}",
                )
            )
    archive_names = [entry.archive_name for entry in entries]
    if len(archive_names) != len(set(archive_names)):
        raise RuntimeError("Duplicate archive member in 4.3 package manifest")
    return entries


def write_zip_atomically(entries: Sequence[ZipEntry]) -> Tuple[str, str, bool]:
    temp_zip = OUTPUTS / f".{DELIVERY_ZIP.name}.{os.getpid()}.tmp"
    try:
        with zipfile.ZipFile(
            temp_zip,
            "w",
            compression=zipfile.ZIP_DEFLATED,
            compresslevel=6,
        ) as archive:
            for entry in entries:
                archive.write(entry.source, entry.archive_name)

        exe_hash = digest_file(DELIVERY_EXE)
        expected_member = f"{PORTABLE_ROOT_NAME}/{PACKAGE_EXE_NAME}"
        with zipfile.ZipFile(temp_zip, "r") as archive:
            bad_member = archive.testzip()
            if bad_member is not None:
                raise RuntimeError("ZIP integrity check failed at " + bad_member)
            inner_hash = digest_stream(archive.open(expected_member, "r"))
        if inner_hash != exe_hash:
            raise RuntimeError("Portable ZIP EXE hash differs from standalone EXE")

        # os.replace is atomic on the same volume and does not touch any 4.2
        # path.  The final file is opened once more so the reported checks are
        # about the delivered ZIP rather than only its temporary predecessor.
        os.replace(temp_zip, DELIVERY_ZIP)
        with zipfile.ZipFile(DELIVERY_ZIP, "r") as archive:
            final_bad_member = archive.testzip()
            if final_bad_member is not None:
                raise RuntimeError("Final ZIP integrity check failed at " + final_bad_member)
            final_inner_hash = digest_stream(archive.open(expected_member, "r"))
        if final_inner_hash != exe_hash:
            raise RuntimeError("Final portable ZIP EXE hash differs from standalone EXE")
        return exe_hash, digest_file(DELIVERY_ZIP), True
    finally:
        if temp_zip.exists():
            temp_zip.unlink()


def main() -> int:
    _fixed, validation = required_inputs()
    copy_delivery_files(validation)
    exe_hash = digest_file(DELIVERY_EXE)
    write_hash_file(exe_hash)
    entries = build_entries(validation)
    final_exe_hash, zip_hash, zip_ok = write_zip_atomically(entries)
    result = {
        "standaloneExe": str(DELIVERY_EXE),
        "portableZip": str(DELIVERY_ZIP),
        "usageFile": str(DELIVERY_USE_DOC),
        "reportFile": str(DELIVERY_REPORT),
        "sha256File": str(DELIVERY_HASH),
        "previewDirectory": str(DELIVERY_PREVIEWS),
        "exeSha256": final_exe_hash,
        "zipSha256": zip_hash,
        "zipTestzip": zip_ok,
        "portableExeSha256Matches": True,
        "zipEntries": len(entries),
        "validationFiles": len(validation),
        "validationPreviewPngFiles": sum(
            1 for path in validation if path.suffix.lower() == ".png"
        ),
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
