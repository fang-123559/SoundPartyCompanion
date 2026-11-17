# Sound Party Companion v0.1.2

发布日期：2026-11-17。

此版本面向 Windows 11 x64 和 Xiaomi蓝牙音箱Pro，安装包自带 .NET 运行库。

## 本版变化

- 展开组件或从托盘恢复时独立读取音量；键盘音量旋钮通过 Windows 音量通知同步数字和滑块。
- 改进 A2DP 输出识别与重连恢复，优先按设备 ContainerId 匹配活动播放端点。音量未读到时显示 `--`，提示「正在等待音频连接」；实际 0% 仍显示为 0%。
- 播放、暂停、上一首和下一首只控制卡片显示的媒体会话。上一首和下一首是否可用取决于播放器向 Windows 提供的能力。

如果浏览器没有为当前视频提供 Windows 上一首或下一首指令，组件无法切换网页内分集，请使用网页播放器的分集列表。

## 下载与升级

- [Windows x64 安装包](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.2/SoundPartyCompanion-Setup-0.1.2-win-x64.exe)
- [SHA256 校验文件](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.2/SoundPartyCompanion-Setup-0.1.2-win-x64.exe.sha256)
- [打包报告](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.2/packaging-report-0.1.2.json)

已有版本请先从托盘退出，核对安装包 SHA256 后直接覆盖安装，无需先卸载。设置、设备缓存和低电提醒去重状态会保留。安装包未做代码签名。
