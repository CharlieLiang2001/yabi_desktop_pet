"""Package only freshly verified 4.3.4 artifacts, preserving earlier releases."""
from pathlib import Path
import hashlib
import html
import json
import shutil
import zipfile

PROJECT = Path(__file__).resolve().parents[1]
OUT = PROJECT.parents[1] / "outputs"
VALIDATION = PROJECT / "bin/validation434"
EXE = PROJECT / "bin/YabiDesktopPet434.exe"
NAME = "亚比桌宠4.3.4_养成优化版"
ASSET_HASH = "4EF33A60956ADD05008C2533A7FD4AF872357D0C02314D1262BC78E67E9013FE"
OLD_EXE_HASH = "C806E0619796D039ABDD0D808539C2A2CBC5FD306A2EB0AD6AFE4CE9D0E384F8"


def digest(path):
    with path.open("rb") as f:
        return stream_digest(f)


def stream_digest(f):
    value = hashlib.sha256()
    for part in iter(lambda: f.read(1024 * 1024), b""):
        value.update(part)
    return value.hexdigest().upper()


def require(relative, marker):
    path = VALIDATION / relative
    if not path.is_file() or path.stat().st_mtime < EXE.stat().st_mtime:
        raise SystemExit(f"Missing or stale evidence: {relative}")
    if marker not in path.read_text(encoding="utf-8-sig"):
        raise SystemExit(f"Failed evidence: {relative}")


def make_comparison(preview):
    preview.mkdir(exist_ok=True)
    for path in (VALIDATION / "ui").glob("*.png"):
        shutil.copy2(path, preview / path.name)
    pairs = [("状态卡 · " + title, f"old-status-{state}.png", f"status-{state}-100.png")
             for state, title in [("normal", "正常"), ("low", "低需求"), ("cooldown", "冷却与排队"),
                                  ("max-level", "满级"), ("long-text", "长文本")]]
    pairs += [("设置 · " + title, f"old-settings-{page}.png", f"settings-{page}-100.png")
              for page, title in [("general", "常规"), ("companion", "陪伴"), ("reminders", "提醒"),
                                  ("appearance", "外观"), ("data", "数据")]]
    sections = []
    for title, before, after in pairs:
        for name in (before, after):
            if not (preview / name).is_file():
                raise SystemExit(f"Missing comparison: {name}")
        sections.append(f'<section><h2>{html.escape(title)}</h2><div class="pair"><figure><figcaption>4.3.3 · 原版</figcaption><img src="{before}" alt="原版{title}"></figure><figure><figcaption>4.3.4 · 新版</figcaption><img src="{after}" alt="新版{title}"></figure></div></section>')
    (preview / "index.html").write_text('''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>亚比 4.3.4 · 界面对照</title><style>
body{margin:0;background:#f7f0e6;color:#493b32;font:15px/1.65 "Microsoft YaHei UI",sans-serif}main{max-width:1420px;margin:auto;padding:32px}h1{font-size:30px;margin-bottom:8px}p{color:#796c60;max-width:900px}section{background:#fffcf6;border:1px solid #e6d8c8;border-radius:16px;padding:24px;margin:24px 0}h2{font-size:19px;margin:0 0 18px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:24px}figure{margin:0;padding:16px;background:#f7f0e6;border-radius:12px}figcaption{font-weight:600;margin-bottom:16px;color:#697f61}img{display:block;max-width:100%;height:auto;margin:auto}a{color:#697f61}@media(max-width:760px){main{padding:16px}.pair{grid-template-columns:1fr}section{padding:14px}}</style><main><h1>亚比 4.3.4 · 温暖一点，也更清楚</h1><p>原版与新版均从真实 WPF 控件渲染，使用隔离的固定测试数据，不读取你的养成记录。状态卡对照覆盖正常、低需求、冷却、满级和长文本。新版另外保留 150% / 200% 渲染分辨率与小工作区截图；这不等同于实际混合 DPI 多屏测试。</p>'''
        + "\n".join(sections) + '<section><h2>小工作区</h2><div class="pair"><img src="status-small-workarea.png" alt="小工作区状态卡"><img src="settings-small-workarea.png" alt="小工作区设置"></div></section></main></html>', encoding="utf-8")


def main():
    for relative, marker in {
        "self-test434.txt": "结果：PASS", "gaze-render/render-result.txt": "PASS:",
        "natural/native-regression.txt": "native regression: PASS", "gaze/result.txt": "integration: PASS",
        "gaze-eco/result.txt": "integration: PASS", "ui/layout-checks.txt": "PASS: settings data",
        "ui/desktop-bounds.txt": "Desktop UI bounds: PASS",
    }.items():
        require(relative, marker)
    if digest(PROJECT / "assets/generated/yabi-animations-v43.zip") != ASSET_HASH:
        raise SystemExit("Original animation asset pack changed.")
    if digest(OUT / "亚比桌宠4.3.3_注视修复版_双击运行.exe") != OLD_EXE_HASH:
        raise SystemExit("Previous EXE changed.")
    results = []
    for mode in ("idle", "natural", "gaze", "gaze-eco"):
        path = VALIDATION / f"benchmark434-{mode}.json"
        if path.stat().st_mtime < EXE.stat().st_mtime:
            raise SystemExit(f"Stale benchmark {mode}")
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if data["exitCode"] != 0 or not data["allResponding"]:
            raise SystemExit(f"Failed benchmark {mode}")
        results.append(data)
    preview = OUT / (NAME + "_界面预览")
    make_comparison(preview)
    rows = "\n".join(f'| {r["mode"]} | {r["durationSeconds"]:.1f} | {r["meanWorkingMiB"]:.2f} | {r["averageCpuOneCorePercent"]:.2f}% |' for r in results)
    startup = (VALIDATION / "startup-idle.txt").read_text(encoding="utf-8-sig")
    desktop_bounds = (VALIDATION / "ui/desktop-bounds.txt").read_text(encoding="utf-8-sig")
    report = f'''# 亚比桌宠 4.3.4 验证报告

## 结论

完整自检、18 项养成专项回归、暖色主题模板与窗口检查通过。原有完整动作完成两轮播放回归，标准/节能注视回归通过。没有修改猫咪素材或真实养成数据。

## 养成与界面

- 已验证：清醒/睡眠余量分离、跨午夜、小数秒、系统挂起/恢复时钟、旧存档及 v2 余量读取、异常数值、照顾冷却、满值照顾、满级、低需求提示确认、保存失败与重试、损坏备份、原子替换失败保留原文件。
- 请求测试覆盖同一照顾仅开始一次、重置后旧请求失效；动作回归覆盖排队替换、取消喂食不奖励、动作预览不奖励和提醒重复确认。
- UI 已检查正常、低需求、冷却、满级、长文本，五页设置，100%/150%/200% 离屏渲染与小工作区。截图由真实 WPF 控件产生，不是效果图。
- 自动 GUI 回归检查显示/隐藏、置顶、穿透和镜像开关；未修改真实注册表启动项。
- 修正了本轮发现的状态卡显示前 Window.Measure 触发 WPF GetWindowMinMax fail-fast 问题；改为测量卡片内容后，标准/节能完整注视回归均通过。

## 性能实测

| 模式 | 测试时长（秒） | 稳态平均工作集 MiB | 平均 CPU（单核口径） |
|---|---:|---:|---:|
{rows}

4.3.3 历史空闲基线：163.87 MiB、CPU 0.53%。基线与本轮不是同日同负载测试，只供参考。是否达到 150 MB 历史目标以本表实际值为准，不将旧目标当作已达成。

启动取证：
```
{startup.strip()}
```

## 验证边界

- 100%/150%/200% 使用不同 DPI 的 RenderTargetBitmap 验证布局和清晰度；另有实际双显示器 96/144 DPI 的状态卡四角边界检查。未实测 200% 物理显示器或更复杂的显示器布局，边界通过不等同于所有窗口混合 DPI 行为通过。
- 系统休眠通过可注入时钟测试，不使用户电脑真正休眠。真实键盘快捷键、鼠标单击/双击/拖动手感和边缘吸附仍需要用户桌面验收；保留原实现。
- 两轮连续运行是既有完整动作序列回归，并非全天运行压力测试。
- 本版仍为小幅照片网格注视，不是全角度 3D 转头。

实际显示器边界取证：
```
{desktop_bounds.strip()}
```

## 文件完整性

动作 ZIP SHA-256：`{ASSET_HASH}`，与改动前一致。4.3.3 EXE SHA-256 未变。便携 ZIP 逐项 CRC 校验，并验证内置 EXE 与独立 EXE SHA-256 完全一致。
'''
    report_path = PROJECT / "docs/验证报告4.3.4.md"
    report_path.write_text(report, encoding="utf-8")
    delivery_report = OUT / (NAME + "_验证报告.md")
    shutil.copy2(report_path, delivery_report)
    delivery_doc = OUT / (NAME + "_使用说明.txt")
    shutil.copy2(PROJECT / "docs/使用说明4.3.4.txt", delivery_doc)
    standalone = OUT / (NAME + "_双击运行.exe")
    shutil.copy2(EXE, standalone)
    exe_hash = digest(standalone)
    archive = OUT / (NAME + "_便携版.zip")
    temporary = archive.with_suffix(".zip.tmp")
    root = NAME + "_便携版/"
    exe_entry = root + NAME + ".exe"
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        z.write(standalone, exe_entry)
        z.write(delivery_doc, root + "使用说明.txt")
        z.write(delivery_report, root + "验证报告.md")
        z.writestr(root + "EXE_SHA256.txt", exe_hash + "  " + NAME + ".exe\n")
        for path in sorted(preview.rglob("*")):
            if path.is_file(): z.write(path, root + "界面预览/" + path.relative_to(preview).as_posix())
        for path in sorted(VALIDATION.rglob("*")):
            if path.is_file(): z.write(path, root + "验证资料/" + path.relative_to(VALIDATION).as_posix())
    with zipfile.ZipFile(temporary) as z:
        if z.testzip() is not None: raise SystemExit("ZIP CRC failed")
        with z.open(exe_entry) as f:
            if stream_digest(f) != exe_hash: raise SystemExit("ZIP EXE hash mismatch")
    temporary.replace(archive)
    if digest(EXE) != exe_hash: raise SystemExit("Source changed during packaging")
    (OUT / (NAME + "_SHA256.txt")).write_text(exe_hash + "  " + standalone.name + "\n" + exe_hash + "  ZIP 内 EXE\n" + digest(archive) + "  " + archive.name + "\n", encoding="utf-8-sig")
    print(json.dumps({"exe": str(standalone), "zip": str(archive), "exe_sha256": exe_hash, "zip_sha256": digest(archive), "preview": str(preview / "index.html")}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
