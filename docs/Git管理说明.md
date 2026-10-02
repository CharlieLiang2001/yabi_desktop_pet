# Git 管理说明

## 范围与初始状态

本地仓库根目录为 `yabi_desktop_pet/current/`，默认分支为 `main`。远程仓库为 https://github.com/CharlieLiang2001/yabi_desktop_pet 。
管理源码、测试、构建脚本、文档和应用清单；不包含同级旧版归档、上层猫脸喂食器工程或交付目录。

仓库中的提交是源码快照；大型素材和本地养成数据不在快照中。远程仓库目前为公开仓库，推送前应检查新增文件是否适合公开。

## 素材与备份

`assets/source/`、`assets/generated/`、`bin/`、视频、EXE、ZIP、缓存及用户养成存档均被忽略，文件仍保留在原位置。不要用 `git clean -fdx` 清理工程，它可能删除这些未受版本保护的文件。

**Git 不是本项目的完整素材备份。** 请独立备份 `assets/`、旧版归档和上层 `outputs/`；真实养成数据仍在 `%APPDATA%\YabiDesktopPet\`。

在另一台机器克隆源码后，至少需要从独立备份恢复以下文件才能直接构建：

- `assets/generated/yabi-animations-v43.zip`
- `assets/generated/yabi-v4.ico`

重建素材还需要原始图片、视频和素材脚本使用的外部输入；打包脚本另依赖保留的历史交付及验证证据。仅克隆源码不能恢复这些文件。后续若要协作同步素材，可另行配置 Git LFS 或专门的素材存储，目前未安装或启用这些服务。

## 日常操作

在仓库根目录执行：

```powershell
git status --short
git diff
git log --oneline -10
```

需要创建后续版本快照时，先检查将要纳入的内容：

```powershell
git add --dry-run .
git add .
git diff --cached --stat
git diff --cached
git commit -m "Describe the change"
git push origin main
```

提交需要用户自己的 Git 姓名和邮箱；如未配置，只设置本仓库的 `user.name` 与 `user.email`，不要使用虚构身份。版本标签和分支按需要创建。
