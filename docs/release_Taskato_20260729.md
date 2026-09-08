# Taskato 发布与恢复说明

## 发布入口

在仓库根目录运行：

```powershell
.\scripts\build-release.ps1 -Version 2.40.0
```

脚本按顺序执行前端依赖锁定安装、类型检查、自动化测试、生产构建、.NET Release 验证和 `win-x64` 发布，再以隔离数据目录启动真实 WebView2 宿主完成自动烟测，随后生成便携 ZIP、SHA-256 校验文件和发布清单。安装 Inno Setup 后可追加 `-BuildInstaller` 生成安装包；便携编译器可通过 `-InnoCompilerPath <ISCC.exe>` 显式指定。安装包及其 SHA-256 会一并写入发布清单。

## 运行时策略

- 应用采用 `win-x64` 自包含发布，目标电脑不需要预装 .NET Desktop Runtime。
- 安装包检测 WebView2 Evergreen Runtime；缺失时使用微软固定入口下载安装。便携版使用者需要自行安装 WebView2 Runtime。
- 内嵌网页资源随应用发布，不依赖互联网加载业务界面。
- WebView2 缓存默认存放在 `%LOCALAPPDATA%/Taskato/WebView2`，不写入便携程序目录，也不会进入发布压缩包。

## 升级与回滚

- 覆盖升级前先在“设置 → 备份与恢复”创建 `.taskato-backup`。
- 数据恢复会先校验压缩包、清单版本和 SQLite 完整性，并延迟到下一次启动、数据库连接建立前执行。
- 应用恢复前会在数据目录保存 `RestoreRollback-*` 副本；若新版本异常，可退出应用、换回旧版程序并恢复备份。
- 当前版本不提供后台静默自动更新，升级由用户明确下载安装包完成，避免在缺少签名与更新服务时引入供应链风险。

## 签名与发布边界

仓库不包含代码签名证书。正式面向公众分发前，应在受控 CI 中对 `Taskato.exe` 和安装包签名，并将签名验证结果连同 SHA-256 发布。构建脚本不会读取或猜测本机证书。
