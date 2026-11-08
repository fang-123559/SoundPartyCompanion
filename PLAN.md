# Sound Party Companion 版本与功能范围

## 当前版本

[v0.1.1](https://github.com/fang-123559/SoundPartyCompanion/releases/tag/v0.1.1) 面向 Windows 11 x64 与 `Xiaomi蓝牙音箱Pro`，提供当前用户安装包、SHA256 校验文件和打包报告。

本版使用 .NET 10 LTS，设置改为原位切换，并修复卸载时选择删除数据却保留已知数据的问题。完整变更见 [更新日志](./CHANGELOG.md)。

## 提供的功能

- 蓝牙连接与当前 A2DP 输出状态。
- 音量、静音、输出切换和 Windows 媒体控制。
- Windows 系统电量、缓存提示与 20% / 10% 低电提醒。
- 紧凑、展开、离线形态，以及多屏位置记忆、置顶和透明度设置。
- 登录启动、系统托盘与主动导出本地诊断 ZIP。

## 使用边界

媒体控制依赖播放器提供的能力，电量依赖 Windows 上报的数据。不提供充电识别；USB-C 仅作为供电路径。

不提供 Bass、灯效或 HFP 切换，不支持 TWS、Party Connect、多音箱管理、其他型号兼容、OTA、恢复出厂或自动更新。

安装包自带运行库，未做代码签名。安装与使用见 [README](./README.md)，具体行为见 [功能说明](./SPEC.md)。
