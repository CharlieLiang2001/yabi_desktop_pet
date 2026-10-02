"""Package the continuous-gaze build without changing previous deliveries."""
import package43 as pack

pack.SOURCE_EXE = pack.PROJECT / "bin/YabiDesktopPet432.exe"
pack.SELF_TEST = pack.PROJECT / "bin/self-test432.txt"
pack.VALIDATION_DIR = pack.PROJECT / "bin/validation432"
pack.USE_DOC = pack.DOCS / "使用说明4.3.2.txt"
pack.REPORT_DOC = pack.DOCS / "验证报告4.3.2.md"
pack.REFERENCE_DOC = pack.DOCS / "连续注视设计与素材要求4.3.2.md"
# Direction-video previews belong to the old one-shot behavior. This delivery
# includes native continuous-gaze screenshots, not misleading old gaze demos.
pack.preview_sources = lambda: []
pack.PRODUCT = "亚比桌宠4.3.2_连续注视体验版"
pack.PORTABLE_ROOT_NAME = pack.PRODUCT + "_便携版"
pack.PACKAGE_EXE_NAME = pack.PRODUCT + ".exe"
pack.DELIVERY_EXE = pack.OUTPUTS / (pack.PRODUCT + "_双击运行.exe")
pack.DELIVERY_ZIP = pack.OUTPUTS / (pack.PRODUCT + "_便携版.zip")
pack.DELIVERY_USE_DOC = pack.OUTPUTS / (pack.PRODUCT + "_使用说明.txt")
pack.DELIVERY_REPORT = pack.OUTPUTS / (pack.PRODUCT + "_验证报告.md")
pack.DELIVERY_HASH = pack.OUTPUTS / (pack.PRODUCT + "_SHA256.txt")
pack.DELIVERY_PREVIEWS = pack.OUTPUTS / (pack.PRODUCT + "_界面预览")

original_entries = pack.build_entries
def entries(validation):
    return [pack.ZipEntry(e.source, e.archive_name.replace("/validation43/", "/validation432/")) for e in original_entries(validation)]
pack.build_entries = entries

def write_hash(exe_hash):
    pack.DELIVERY_HASH.write_text(
        "亚比桌宠4.3.2 · SHA-256\n" + exe_hash + "  " + pack.DELIVERY_EXE.name
        + "\n" + exe_hash + "  " + pack.PACKAGE_EXE_NAME + "（ZIP内）\n", encoding="utf-8-sig")
pack.write_hash_file = write_hash

if __name__ == "__main__":
    raise SystemExit(pack.main())
