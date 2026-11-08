# Sound Party Companion

非官方、仅本地运行的 Windows 11 x64 桌面音箱伴侣。适用设备：`Xiaomi蓝牙音箱Pro`；设计参考 `MDZ-39-DB / Xiaomi Sound Party`。

`0.1.1` 使用 .NET 10 LTS，提供连接状态、音频控制、媒体与电量显示，以及原位切换的设置面板。源码见 [GitHub 仓库](https://github.com/fang-123559/SoundPartyCompanion)，正式安装包见 [v0.1.1 Release](https://github.com/fang-123559/SoundPartyCompanion/releases/tag/v0.1.1)。详见 [发布说明](./docs/release-0.1.1.md)。

## 安装与首次使用

1. 在 Windows“设置 → 蓝牙和设备”配对音箱，并保持蓝牙开启。
2. 下载 [Windows x64 安装包](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/SoundPartyCompanion-Setup-0.1.1-win-x64.exe) 和 [SHA256 校验文件](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/SoundPartyCompanion-Setup-0.1.1-win-x64.exe.sha256)，核对文件来源和 SHA256。[打包报告](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/packaging-report-0.1.1.json) 记录源码提交及构建环境。
3. 运行安装包。默认安装到当前用户的 `%LocalAppData%\Programs\SoundPartyCompanion`，无需管理员权限，也无需另装 .NET。
4. 从开始菜单打开 Sound Party Companion。组件区分“已配对但离线”“已连接”和“当前输出”；点击卡片展开音量、静音和可用媒体控制。
5. 托盘菜单可显示/隐藏、打开设置、导出诊断和退出。首次使用默认启用登录启动，可在设置关闭；`Alt+F4` 隐藏到托盘。
6. 打开设置时，设置面板在原位置替换组件；关闭后恢复组件的位置和紧凑/展开状态。透明度调整在返回组件后查看；“组件位置”行可重置组件位置。

安装包未签名，Windows 可能显示 SmartScreen 提示。SHA256 校验内容完整性，不代表发布者身份认证。

升级保留设置、设备选择缓存及低电提醒去重状态。卸载默认保留数据；明确选择删除才清理固定配置和四份应用日志。未知文件、链接与已导出诊断 ZIP 保留。

## 功能与限制

- 蓝牙真实连接状态、A2DP 当前输出切换、音量、静音、Windows 媒体播放控制。
- Windows HFP 系统电量、采集时间与缓存过期提示；20%/10% 低电提醒可关闭，同一放电区间去重。
- 紧凑、展开、离线组件；多屏位置记忆、DPI 适配、置顶与透明度设置。
- 媒体按钮依赖播放器提供的 Windows 能力；没有媒体会话时隐藏媒体区。
- 不提供充电识别，不根据电量上升或 USB 插拔推断充电。USB-C 只作为供电路径，蓝牙断开仍显示离线。
- Bass、灯效与 HFP 切换入口保持隐藏；不支持 TWS、Party Connect、多音箱、其他型号兼容承诺、OTA、自动更新或代码签名。

## 诊断与反馈

右键托盘 →“导出诊断”→ 选择本地 ZIP 保存位置。取消不生成文件，不覆盖已有文件，也不会自动上传。

诊断包仅包含版本、基础状态和脱敏固定日志；不含配置、设备选择缓存、设备标识、用户名、个人路径或媒体信息。反馈时说明 Windows 版本、应用版本、复现步骤及期望/实际行为，检查 ZIP 后再主动分享。

## 本地构建

```powershell
.\scripts\install-dotnet.ps1
.\scripts\package.ps1
```

打包脚本会依次还原、构建、测试、发布并编译安装器。只验证工程时可运行 `.\build.ps1 -Task All`。

固定 SDK `10.0.402`，C# 12，Windows 目标后缀保持 `windows10.0.19041.0`，实际产品系统要求 Windows 11 x64。SDK 下载按微软官方发布元数据校验 SHA512；构建工具在 `.tools/`，NuGet 包在 `.packages/`。

安装器使用项目内便携 Inno Setup `7.1.0`，校验官方 `Pyrsys B.V.` 签名；当前编译器标示 `Non-commercial use only`。本项目为个人、非商业项目，许可声明见 [第三方声明](./THIRD-PARTY-NOTICES.md)。

本地产物位于 `artifacts/installer/`，含安装包、SHA256 和带版本的打包报告。报告记录 SDK/运行时、源码提交、工作树状态及载荷哈希；私人备份、日志、工具、PDB 不进入安装包或源码提交。Windows [构建配置](./.github/workflows/build.yml) 会执行构建、测试、发布和安装器编译，运行结果与构建附件见 [GitHub Actions](https://github.com/fang-123559/SoundPartyCompanion/actions/workflows/build.yml)；不会自动创建 Release。

## 文档与许可

- [更新日志](./CHANGELOG.md) · [0.1.1 发布说明](./docs/release-0.1.1.md)
- [产品说明](./PRD.md) · [功能说明](./SPEC.md)
- [界面与交互](./DESIGN.md) · [版本与功能范围](./PLAN.md)
- [MIT LICENSE](./LICENSE) · [第三方声明](./THIRD-PARTY-NOTICES.md)

不使用小米账号、云服务或遥测；所有设置和日志留在本机。本项目不隶属于或代表小米官方。
