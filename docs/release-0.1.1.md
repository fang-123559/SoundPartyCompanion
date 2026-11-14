# Sound Party Companion v0.1.1

发布日期：2026-11-07。

面向 **Windows 11 x64** 的非官方、本地音箱伴侣，适用设备为 **Xiaomi蓝牙音箱Pro**（设计参考 MDZ-39-DB / Xiaomi Sound Party）。安装包自带 .NET 运行库。

## 下载

- [Windows x64 安装包](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/SoundPartyCompanion-Setup-0.1.1-win-x64.exe)
- [SHA256 校验文件](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/SoundPartyCompanion-Setup-0.1.1-win-x64.exe.sha256)
- [打包报告](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/packaging-report-0.1.1.json)

## 本版变化

- 使用 .NET 10 LTS，安装包自带 .NET / Windows Desktop Runtime `10.0.13`，无需另装运行库。
- 设置采用原位切换：打开时暂时隐藏组件，关闭后恢复原位置和紧凑/展开状态，保留现有配色与控件风格。
- “组件位置”独立成行，明确“重置位置”按钮的作用。
- 修复卸载时选择删除数据却保留已知数据的问题。只清理固定设置、设备缓存和日志，保留未知文件、目录链接及已导出的 ZIP。
- 源码采用 MIT，安装包包含运行库与相关第三方许可声明。

## 安装与升级

1. 在 Windows 蓝牙设置中配对音箱，并保持蓝牙开启。
2. 下载并核对安装包 SHA256，再运行安装包。无需管理员权限或另装 .NET。
3. 从开始菜单启动应用，点击卡片展开音量、静音和媒体控制；托盘菜单提供设置、诊断导出和退出。

已有版本先从托盘退出，再直接覆盖安装。设置、设备缓存及低电提醒去重状态会保留，无需先卸载。

安装包未做代码签名，Windows 可能显示 SmartScreen 提示。SHA256 用于核对文件完整性，不代表发布者身份认证。

卸载默认保留数据，明确选择“是”才删除已知设置、缓存和日志。

### 双击安装包没有反应

确认安装包来自本页的下载链接，并核对下方 SHA256。右键安装包，打开 **属性 → 常规**。若底部「安全」提示右侧有 **解除锁定**，勾选后点 **应用 → 确定**，再双击安装包。这一步只移除该文件的下载标记。

如果没有这个选项，或重试后仍未弹出安装向导，请查看 **Windows 安全中心 → 病毒和威胁防护 → 保护历史记录**。反馈时附上 Windows 版本和出现的提示。

## 安装包 SHA256

```text
F007E6DAEF5494A98EF7735DA4913FBFD3450260214678CD3FBD914622B34798
```

## 功能与限制

- 提供连接状态、A2DP 输出、音量、静音、媒体控制、系统电量与低电提醒。
- 媒体信息与按钮取决于播放器提供的 Windows 能力；没有活动媒体时隐藏媒体区。
- 电量来自 Windows HFP 属性，离线缓存与当前读数分别显示。不提供充电识别，USB-C 仅作为供电路径；蓝牙断开仍显示离线。
- 不提供 Bass、灯效和 HFP 切换；不支持其他型号兼容、TWS、Party Connect、多音箱、OTA、恢复出厂或自动更新。

设置与日志留在本机，不使用小米账号、云服务、遥测或自动上传。本项目不隶属于或代表小米官方。
