# Sound Party Companion v0.1.1

发布日期：2026-11-07。

面向 **Windows 11 x64** 的非官方、本地音箱伴侣，当前已验证设备为 **Xiaomi蓝牙音箱Pro**（设计参考 MDZ-39-DB / Xiaomi Sound Party）。安装包自带 .NET 运行库。

## 下载

- [Windows x64 安装包](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/SoundPartyCompanion-Setup-0.1.1-win-x64.exe)
- [SHA256 校验文件](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/SoundPartyCompanion-Setup-0.1.1-win-x64.exe.sha256)
- [打包报告](https://github.com/fang-123559/SoundPartyCompanion/releases/download/v0.1.1/packaging-report-0.1.1.json)

## 本版变化

- 迁移至 .NET 10 LTS，SDK 为 `10.0.402`，.NET / Windows Desktop Runtime 为 `10.0.13`，无需另装运行库。
- 设置采用原位切换：打开时暂时隐藏组件，关闭后恢复原位置和紧凑/展开状态；保留配色与控件风格，明确“组件位置”和重置按钮的归属。
- 修复卸载时选择删除却保留已知数据的问题。只清理固定设置、设备缓存和日志，保留未知文件、目录链接及已导出的 ZIP。
- 源码采用 MIT；安装包包含运行库与相关第三方许可声明，并提供源码提交、构建环境及文件哈希记录。

## 安装与升级

先在 Windows 蓝牙设置中配对音箱，再运行安装包。已有版本从托盘退出后可直接覆盖安装，设置、设备缓存及低电提醒去重状态会保留。无需先卸载，也无需管理员权限。

安装包尚未代码签名，Windows 可能显示 SmartScreen 提示。使用下方 SHA256 核对安装文件；卸载默认保留数据，明确选择“是”才删除已知设置、缓存和日志。

## 验证结果

- ✅ Windows CI 构建 0 警告、0 错误，**132/132 自动测试通过**，self-contained 发布、安装器编译和附件上传成功。
- ✅ 用户已确认连接/音频 8 项、媒体/电量 3 项及原位设置切换通过；升级、卸载保留/删除、原数据恢复与本机无原配置基础启动已有验证。
- 真正注销登录后的自动启动、另一台或干净 Windows 环境尚未验证；旧版边界验收不冒充本版全量复测。按个人使用范围按需补充，不追加全面测试。

## 构建来源

附件直接取自 [成功的 Windows CI](https://github.com/fang-123559/SoundPartyCompanion/actions/runs/42378450425)，未用本机包替换或重新构建。

- 源码提交：[`4dc4f3f99165665a9f3379a643324c496c24571f`](https://github.com/fang-123559/SoundPartyCompanion/commit/4dc4f3f99165665a9f3379a643324c496c24571f)
- 标签：`v0.1.1`
- 安装包 SHA256：

```text
F007E6DAEF5494A98EF7735DA4913FBFD3450260214678CD3FBD914622B34798
```

## 已知范围

媒体控制取决于播放器提供的 Windows 能力。电量来自 Windows HFP 属性，离线缓存与当前读数区分；充电识别暂缓，USB-C 仅作为供电路径。Bass、灯效和 HFP 切换入口隐藏；不支持多型号、TWS、Party Connect、OTA 或自动更新。

本项目不隶属于或代表小米官方，不使用小米账号、云服务或遥测。
