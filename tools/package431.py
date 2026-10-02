#!/usr/bin/env python3
"""Create the Yabi Desktop Pet 4.3.1 delivery files.

This is a 4.3 packaging wrapper: the manifest, atomic ZIP write and integrity
checks remain in :mod:`package43`, while this module supplies the 4.3.1 input
and output paths.  It does not rebuild assets or alter the 4.3 deliverables.
"""

from __future__ import annotations

import package43 as pack


# Keep the 4.3 asset and preview inputs.  The 4.3.1 build produces its own EXE,
# self-test evidence, validation directory and documentation.
PROJECT = pack.PROJECT
pack.SOURCE_EXE = PROJECT / "bin" / "YabiDesktopPet431.exe"
pack.SELF_TEST = PROJECT / "bin" / "self-test431.txt"
pack.VALIDATION_DIR = PROJECT / "bin" / "validation431"
pack.ASSET_VALIDATION = PROJECT / "assets" / "generated" / "validation43.json"
pack.DOCS = PROJECT / "docs"
pack.PREVIEWS = PROJECT / "assets" / "generated" / "previews43"

pack.USE_DOC = pack.DOCS / "使用说明4.3.1.txt"
pack.REFERENCE_DOC = pack.DOCS / "开源项目参考说明4.3.md"
pack.REPORT_DOC = pack.DOCS / "验证报告4.3.1.md"

pack.PRODUCT = "亚比桌宠4.3.1_鼠标注视优化版"
pack.PORTABLE_ROOT_NAME = pack.PRODUCT + "_便携版"
pack.PACKAGE_EXE_NAME = pack.PRODUCT + ".exe"

pack.DELIVERY_EXE = pack.OUTPUTS / (pack.PRODUCT + "_双击运行.exe")
pack.DELIVERY_ZIP = pack.OUTPUTS / (pack.PRODUCT + "_便携版.zip")
pack.DELIVERY_USE_DOC = pack.OUTPUTS / (pack.PRODUCT + "_使用说明.txt")
pack.DELIVERY_REPORT = pack.OUTPUTS / (pack.PRODUCT + "_验证报告.md")
pack.DELIVERY_HASH = pack.OUTPUTS / (pack.PRODUCT + "_SHA256.txt")
pack.DELIVERY_PREVIEWS = pack.OUTPUTS / (pack.PRODUCT + "_界面预览")


_build_entries43 = pack.build_entries


def build_entries(validation):
    """Reuse the 4.3 manifest while naming this package's evidence folder."""

    entries = _build_entries43(validation)
    old_prefix = f"{pack.PORTABLE_ROOT_NAME}/验证资料/validation43/"
    new_prefix = f"{pack.PORTABLE_ROOT_NAME}/验证资料/validation431/"
    return [
        pack.ZipEntry(
            entry.source,
            new_prefix + entry.archive_name[len(old_prefix):],
        )
        if entry.archive_name.startswith(old_prefix)
        else entry
        for entry in entries
    ]


def write_hash_file(exe_hash: str) -> None:
    """Write the 4.3.1 hash note; package43's text is fixed at 4.3."""

    text = (
        "亚比桌宠 4.3.1 · 鼠标注视优化版\n"
        "=================================\n"
        f"{exe_hash}  {pack.DELIVERY_EXE.name}\n"
        f"{exe_hash}  {pack.PACKAGE_EXE_NAME}（便携 ZIP 内）\n"
        "\n两个 EXE 的字节内容完全一致。\n"
    )
    pack.DELIVERY_HASH.write_text(text, encoding="utf-8-sig")


# package43.main resolves helpers through its own module globals, so replacing
# these wrappers keeps all existing atomic-write and ZIP verification logic.
pack.build_entries = build_entries
pack.write_hash_file = write_hash_file


def main() -> int:
    return pack.main()


if __name__ == "__main__":
    raise SystemExit(main())
