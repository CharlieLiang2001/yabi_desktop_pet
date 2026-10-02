#!/usr/bin/env python3
"""Create the user-facing single EXE and portable ZIP deliverables."""

from __future__ import annotations

import hashlib
import shutil
import zipfile
from pathlib import Path


PROJECT = Path(__file__).resolve().parents[1]
WORKSPACE = PROJECT.parents[1]
OUTPUTS = WORKSPACE / "outputs"
WORK = WORKSPACE / "work" / "yabi-desktop-pet-v4.2" / "package"
SOURCE_EXE = PROJECT / "bin" / "YabiDesktopPet42.exe"
DOCS = PROJECT / "docs"
PREVIEWS = PROJECT / "assets" / "generated" / "previews"
MASTERS = PROJECT / "assets" / "generated" / "masters"

DELIVERY_EXE = OUTPUTS / "亚比桌宠4.2_陪伴增强版_双击运行.exe"
DELIVERY_ZIP = OUTPUTS / "亚比桌宠4.2_陪伴增强版_便携版.zip"
PORTABLE_ROOT = WORK / "亚比桌宠4.2_陪伴增强版_便携版"
PORTABLE_EXE = PORTABLE_ROOT / "亚比桌宠4.2_陪伴增强版.exe"


def digest_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def digest_file(path: Path) -> str:
    return digest_bytes(path.read_bytes())


def main() -> int:
    required = [
        SOURCE_EXE,
        DOCS / "使用说明.txt",
        DOCS / "开源项目参考说明.md",
        DOCS / "验证报告.md",
        PREVIEWS / "preview_transparent.png",
        PREVIEWS / "preview_dark.png",
        PREVIEWS / "preview_blue.png",
        PREVIEWS / "preview_green.png",
        MASTERS / "stand_transparent.png",
        MASTERS / "sit_transparent.png",
        MASTERS / "sleep_transparent.png",
        MASTERS / "stand_green.png",
        MASTERS / "sit_green.png",
        MASTERS / "sleep_green.png",
        PREVIEWS / "ui-status-card.png",
        PREVIEWS / "ui-settings.png",
        PREVIEWS / "ui-reminders.png",
        PREVIEWS / "ui-data.png",
        PREVIEWS / "transitions_transparent.png",
        PREVIEWS / "transitions_dark.png",
        PREVIEWS / "transitions_blue.png",
        PROJECT / "assets" / "generated" / "transition_validation.json",
    ]
    missing = [str(path) for path in required if not path.exists()]
    if missing:
        raise SystemExit("Missing packaging inputs: " + ", ".join(missing))

    OUTPUTS.mkdir(parents=True, exist_ok=True)
    WORK.mkdir(parents=True, exist_ok=True)
    if PORTABLE_ROOT.exists():
        resolved = PORTABLE_ROOT.resolve()
        if resolved.parent != WORK.resolve():
            raise RuntimeError("Refusing to clean unexpected package directory")
        shutil.rmtree(PORTABLE_ROOT)
    (PORTABLE_ROOT / "预览").mkdir(parents=True)
    (PORTABLE_ROOT / "单独素材").mkdir(parents=True)

    shutil.copy2(SOURCE_EXE, DELIVERY_EXE)
    shutil.copy2(SOURCE_EXE, PORTABLE_EXE)
    shutil.copy2(DOCS / "使用说明.txt", OUTPUTS / "亚比桌宠4.2_陪伴增强版_使用说明.txt")
    shutil.copy2(DOCS / "验证报告.md", OUTPUTS / "亚比桌宠4.2_陪伴增强版_验证报告.md")
    shutil.copy2(PREVIEWS / "preview_transparent.png", OUTPUTS / "亚比桌宠4.2_陪伴增强版_透明背景预览.png")
    shutil.copy2(PREVIEWS / "preview_green.png", OUTPUTS / "亚比桌宠4.2_陪伴增强版_绿底检查图.png")
    shutil.copy2(PREVIEWS / "ui-status-card.png", OUTPUTS / "亚比桌宠4.2_状态卡预览.png")
    shutil.copy2(PREVIEWS / "ui-settings.png", OUTPUTS / "亚比桌宠4.2_设置界面预览.png")
    shutil.copy2(PREVIEWS / "ui-reminders.png", OUTPUTS / "亚比桌宠4.2_提醒界面预览.png")
    shutil.copy2(PREVIEWS / "ui-data.png", OUTPUTS / "亚比桌宠4.2_养成数据预览.png")
    shutil.copy2(PREVIEWS / "transitions_transparent.png", OUTPUTS / "亚比桌宠4.2_新增睡眠动作预览.png")
    shutil.copy2(PROJECT / "assets" / "generated" / "transition_validation.json", OUTPUTS / "亚比桌宠4.2_新增睡眠动作验证.json")
    for source_name, delivery_name in (
        ("stand_transparent.png", "站立_透明.png"),
        ("sit_transparent.png", "坐姿_透明.png"),
        ("sleep_transparent.png", "睡姿_透明.png"),
        ("stand_green.png", "站立_绿底.png"),
        ("sit_green.png", "坐姿_绿底.png"),
        ("sleep_green.png", "睡姿_绿底.png"),
    ):
        shutil.copy2(MASTERS / source_name, PORTABLE_ROOT / "单独素材" / delivery_name)

    shutil.copy2(DOCS / "使用说明.txt", PORTABLE_ROOT / "使用说明.txt")
    shutil.copy2(DOCS / "开源项目参考说明.md", PORTABLE_ROOT / "开源项目参考说明.md")
    shutil.copy2(DOCS / "验证报告.md", PORTABLE_ROOT / "验证报告.md")
    shutil.copy2(PREVIEWS / "preview_transparent.png", PORTABLE_ROOT / "预览" / "透明背景预览.png")
    shutil.copy2(PREVIEWS / "preview_dark.png", PORTABLE_ROOT / "预览" / "深色背景预览.png")
    shutil.copy2(PREVIEWS / "preview_blue.png", PORTABLE_ROOT / "预览" / "蓝色背景预览.png")
    shutil.copy2(PREVIEWS / "preview_green.png", PORTABLE_ROOT / "预览" / "绿底检查图.png")
    shutil.copy2(PREVIEWS / "ui-status-card.png", PORTABLE_ROOT / "预览" / "状态卡.png")
    shutil.copy2(PREVIEWS / "ui-settings.png", PORTABLE_ROOT / "预览" / "设置界面.png")
    shutil.copy2(PREVIEWS / "ui-reminders.png", PORTABLE_ROOT / "预览" / "提醒界面.png")
    shutil.copy2(PREVIEWS / "ui-data.png", PORTABLE_ROOT / "预览" / "养成数据.png")
    shutil.copy2(PREVIEWS / "transitions_transparent.png", PORTABLE_ROOT / "预览" / "新增睡眠动作_透明背景.png")
    shutil.copy2(PREVIEWS / "transitions_dark.png", PORTABLE_ROOT / "预览" / "新增睡眠动作_深色背景.png")
    shutil.copy2(PREVIEWS / "transitions_blue.png", PORTABLE_ROOT / "预览" / "新增睡眠动作_蓝色背景.png")
    shutil.copy2(PROJECT / "assets" / "generated" / "transition_validation.json", PORTABLE_ROOT / "新增睡眠动作验证.json")

    exe_hash = digest_file(DELIVERY_EXE)
    hash_text = (
        "亚比桌宠 4.2 · SHA-256\n"
        "=========================\n"
        f"{exe_hash}  {DELIVERY_EXE.name}\n"
        f"{exe_hash}  {PORTABLE_EXE.name}（便携 ZIP 内）\n"
        "\n两个 EXE 的字节内容完全一致。\n"
    )
    (OUTPUTS / "亚比桌宠4.2_陪伴增强版_SHA256.txt").write_text(hash_text, encoding="utf-8-sig")
    (PORTABLE_ROOT / "SHA256.txt").write_text(hash_text, encoding="utf-8-sig")

    if DELIVERY_ZIP.exists():
        DELIVERY_ZIP.unlink()
    with zipfile.ZipFile(DELIVERY_ZIP, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for path in sorted(PORTABLE_ROOT.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(WORK).as_posix())

    expected_member = PORTABLE_ROOT.relative_to(WORK).as_posix() + "/" + PORTABLE_EXE.name
    with zipfile.ZipFile(DELIVERY_ZIP, "r") as archive:
        inner_hash = digest_bytes(archive.read(expected_member))
        bad_member = archive.testzip()
    if bad_member is not None:
        raise RuntimeError("ZIP integrity check failed at " + bad_member)
    if inner_hash != exe_hash:
        raise RuntimeError("Portable ZIP EXE hash differs from standalone EXE")

    print("Standalone EXE:", DELIVERY_EXE)
    print("Portable ZIP:", DELIVERY_ZIP)
    print("EXE SHA-256:", exe_hash)
    print("ZIP SHA-256:", digest_file(DELIVERY_ZIP))
    print("ZIP member EXE hash matches: YES")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
