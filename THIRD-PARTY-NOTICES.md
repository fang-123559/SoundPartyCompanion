# 第三方声明

Sound Party Companion 源码采用 [MIT](./LICENSE)。第三方组件仍遵循各自许可。

## 随安装包分发

- Microsoft .NET 10 和 Windows Desktop Runtime（WPF、Windows Forms）：Microsoft 与贡献者，主要许可为 MIT，包含采用其他许可的组件。打包脚本从实际 runtime pack 提取许可与 .NET 声明；Desktop pack 不含声明，因此从匹配运行时版本的 WPF/Windows Forms 官方 release tag 下载并合并原文，完整放入安装目录 `licenses/`。文件及 SHA256 列入打包报告。
- 安装器由 [Inno Setup 7.1.0](https://jrsoftware.org/) 编译。Copyright © 1997–2026 Jordan Russell；Portions Copyright © 2000–2026 Martijn Laan。保留安装器原有版权和网站标识，附带官方工具的 `license.txt`；当前编译器启动时标示 `Non-commercial use only`。本项目为个人、非商业项目。
- 应用图标、织物纹理、线条与抽象媒体图形在项目源码中生成；不包含小米官方商标、产品照片或播放器封面。系统字体由 Windows 提供，不随安装包分发。

## 仅开发和测试使用

Microsoft.NET.Test.Sdk、xunit、xunit.runner.visualstudio、coverlet.collector 与 Windows SDK 引用组件仅用于构建或测试，不加入应用载荷。具体版本见 `Directory.Packages.props` 与项目还原结果；依赖的许可文件保留在 NuGet 包中。

本项目非小米官方产品，与小米没有隶属关系。产品名称仅用于说明已验证设备。源码的 MIT 许可不授予第三方商标权。
