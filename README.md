# 亚比桌宠 4.3.4 · 养成优化版

Windows 10/11 x64 写实风格桌宠。4.3.4 修正养成计时、休息奖励与存档边界，统一奶油暖色状态卡和五页设置。保留 4.3.3 注视透明度修复、托盘和完整视频，不修改角色素材或增加生产依赖。

这是可运行的“小幅照片网格近似”体验版，不是独立眼球或真实3D侧脸模型。站立、坐姿支持连续上下左右注视，眼部先响应，头颈随后跟随；下半身和尾巴固定。原4.3照片、视频及动作ZIP均未修改，不新增生产依赖，离线静音运行。

## 工程结构

版本管理范围与素材备份要求见 [Git 管理说明](docs/Git管理说明.md)。Git 仅管理代码和文档，不包含大型素材及交付包；只克隆本仓库无法直接构建单文件 EXE，所需动作 ZIP 和图标须从独立素材备份恢复。

- `assets/`：稳定姿势图片、4.2 动作包、4.3 完整全身动作包与背景接触表。
- `src/ContinuousGazeController.cs`：连续坐标、时间步长平滑和站立/坐姿源图锚点。
- `src/ContinuousGazeView.cs`：独立纹理连续网格、正确的 RGBA 材质和预计算权重；`MainWindow.Gaze.cs` 负责桌面坐标和互斥。
- `src/ContinuousGazeRenderTests.cs`：猫本体 alpha、预乘颜色和视频缓冲区可写性测试；`GazeDesktopProbe.cs` 仅测试时进行桌宠区域原生屏幕取证。
- `src/` 其他文件：既有WPF窗口、动作队列、养成/提醒与桌面集成。
- `tools/build.ps1`：使用系统 .NET Framework x64 `csc.exe` 编译单文件 EXE；本工程没有 `.csproj`。
- `tools/build_assets43.py`：生成 4.3 透明动作包、预览和素材校验数据；不会替换 4.2 动作包。
- `src/CareController.cs`、`CareSnapshot.cs`、`CareRuntimeClock.cs`：养成、只读展示状态和排除系统休眠的单调计时；`CareRegressionTests.cs` 为隔离回归测试。
- `src/UiTheme.cs`：共享暖色样式；`UiPreview434.cs` 只在显式测试参数下生成固定状态截图。
- `tools/package434.py`：核验新鲜测试证据后生成 EXE、便携 ZIP、界面对照、报告与 SHA-256，不覆盖旧交付。

## 构建与验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1
.\bin\YabiDesktopPet434.exe --self-test --self-test-output .\bin\validation434\self-test434.txt
.\bin\YabiDesktopPet434.exe --ui-preview-output .\bin\validation434\ui
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\benchmark434.ps1 -Mode natural -Seconds 330
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\benchmark434.ps1 -Mode gaze -Seconds 135
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\benchmark434.ps1 -Mode gaze -Seconds 135 -Eco
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\benchmark434.ps1 -Mode idle -Seconds 60
python -X utf8 .\tools\package434.py
```

需要重建 4.3 素材时可使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1 -RebuildAssets
```

`--test-gaze --exit-after 135` 使用隔离内存数据、独立单实例锁和注入的屏幕像素坐标，不移动系统指针、不修改用户设置或注册表；兼容的取证位置仍为 `%TEMP%\YabiDesktopPet-v4.3.3-gaze`。测试约67秒自动退出，135秒是超时上限。测试会短暂显示自有深色/浅色背景窗口，检查真实桌面合成。`benchmark434.ps1` 支持 `-Eco`、`-Mode idle` 和 `-Mode natural`，收集资料到 `bin/validation434/`。所有 GUI 测试必须串行运行。新旧 UI 对比的旧版截图由工作区 `work/yabi434/ComparePreview.cs` 从保留的 4.3.3 EXE 隔离渲染；不执行旧版入口或读取真实数据。

实时注视无驻留、方向冷却或两分钟互动门槛。标准/节能更新间隔为33/50ms；跨屏、拖动、其他动作、其他面板和专注免打扰时暂停/回正。眼球尚未独立分层；更大角度需要额外模型素材。`docs/使用说明4.3.3.txt`、`docs/注视修复说明4.3.3.md` 和 `docs/验证报告4.3.3.md` 记录边界、根因与验证结果。4.3.2 的旧报告仅作历史记录，其“猫可见”结论被本次 alpha 检查纠正。

## 数据

- `%APPDATA%\YabiDesktopPet\settings.xml`：沿用 4.3 schema，兼容 4.2/4.1 迁移。
- `%APPDATA%\YabiDesktopPet\profile.xml`：version=2，本地养成状态、清醒/睡眠独立余量、小数秒、统计和成就；兼容旧版加载。
- 养成保存采用同目录唯一临时文件、刷盘后原子替换，保留 `.bak`；失败不覆盖旧数据，状态卡/数据页显示可恢复提示。损坏养成数据会备份后恢复默认值。
