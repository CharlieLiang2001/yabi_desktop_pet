"""Package the gaze opacity hotfix; preserve earlier deliveries."""
import package43 as pack

pack.SOURCE_EXE = pack.PROJECT / "bin/YabiDesktopPet433.exe"
pack.SELF_TEST = pack.PROJECT / "bin/validation433/self-test433.txt"
pack.VALIDATION_DIR = pack.PROJECT / "bin/validation433"
pack.USE_DOC = pack.DOCS / "使用说明4.3.3.txt"
pack.REPORT_DOC = pack.DOCS / "验证报告4.3.3.md"
pack.REFERENCE_DOC = pack.DOCS / "注视修复说明4.3.3.md"
pack.preview_sources = lambda: []
pack.PRODUCT = "亚比桌宠4.3.3_注视修复版"
pack.PORTABLE_ROOT_NAME = pack.PRODUCT + "_便携版"
pack.PACKAGE_EXE_NAME = pack.PRODUCT + ".exe"
pack.DELIVERY_EXE = pack.OUTPUTS / (pack.PRODUCT + "_双击运行.exe")
pack.DELIVERY_ZIP = pack.OUTPUTS / (pack.PRODUCT + "_便携版.zip")
pack.DELIVERY_USE_DOC = pack.OUTPUTS / (pack.PRODUCT + "_使用说明.txt")
pack.DELIVERY_REPORT = pack.OUTPUTS / (pack.PRODUCT + "_验证报告.md")
pack.DELIVERY_HASH = pack.OUTPUTS / (pack.PRODUCT + "_SHA256.txt")
pack.DELIVERY_PREVIEWS = pack.OUTPUTS / (pack.PRODUCT + "_界面预览")
original_entries = pack.build_entries
pack.build_entries = lambda validation: [
    pack.ZipEntry(e.source, e.archive_name.replace("/validation43/", "/validation433/"))
    for e in original_entries(validation)
]

def write_hash(exe_hash):
    pack.DELIVERY_HASH.write_text(
        "亚比桌宠4.3.3 · SHA-256\n" + exe_hash + "  " + pack.DELIVERY_EXE.name
        + "\n" + exe_hash + "  " + pack.PACKAGE_EXE_NAME + "（ZIP内）\n", encoding="utf-8-sig")

pack.write_hash_file = write_hash

if __name__ == "__main__":
    # Refuse to package stale or unsuccessful tests as a working hotfix.
    import json
    required_passes = {
        pack.SELF_TEST: "结果：PASS",
        pack.VALIDATION_DIR / "gaze/result.txt": "integration: PASS",
        pack.VALIDATION_DIR / "gaze-eco/result.txt": "integration: PASS",
        pack.VALIDATION_DIR / "natural/native-regression.txt": "native regression: PASS",
        pack.VALIDATION_DIR / "gaze-render/render-result.txt": "PASS:",
    }
    for path, expected in required_passes.items():
        if not path.exists() or path.stat().st_mtime < pack.SOURCE_EXE.stat().st_mtime:
            raise SystemExit("Missing or stale verification: " + str(path))
        if expected not in path.read_text(encoding="utf-8-sig"):
            raise SystemExit("Verification did not pass: " + str(path))
    for mode in ("gaze", "gaze-eco", "natural", "idle"):
        benchmark = json.loads((pack.VALIDATION_DIR / ("benchmark433-" + mode + ".json")).read_text(encoding="utf-8-sig"))
        if benchmark["exitCode"] != 0 or not benchmark["allResponding"]:
            raise SystemExit("Unsuccessful benchmark: " + mode)
    raise SystemExit(pack.main())
