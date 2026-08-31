# hs2-tools

一个基于 **.NET 8 + WPF** 的 Windows 桌面应用，用于管理 HoneySelect2 游戏的模组和角色卡。

## 功能特性

- 🎮 游戏模组管理（扫描、使用统计、未使用筛选）
- 👤 角色卡管理（虚拟化网格、搜索、排序、收藏、详情与缺失 Mod 一键补全）
- 🖼️ 场景卡管理（网格浏览 + 按角色智能整理）
- ⬇️ Mod 下载与补全（代理、断点续传、取消/重试、实时进度）
- 📦 BetterRepack sideload 数据库浏览与更新（远程 ZIP 中央目录解析，无需整包下载）
- 🚀 启动游戏 / 工作室、防休眠、单卡查看器

## 技术栈

- **框架**: .NET 8 LTS + WPF（`net8.0-windows`），MVVM（CommunityToolkit.Mvvm）
- **HTML 解析**: HtmlAgilityPack
- **卡片/场景解析**: MessagePack-CSharp（`【AIS_Chara】` BlockHeader + KKEx 结构化解析，基准 IllusionModdingAPI/BepisPlugins）
- **测试**: xUnit（211 项：解析器回归、VM 测试、真实环境基准）

## 构建与测试

```bash
dotnet build
dotnet test
```

发布单文件自包含 exe（产物在 `publish/`）：

```bash
dotnet publish HS2Tools/HS2Tools.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -o publish
```

## 目录结构

```
├── HS2Tools/        # WPF 应用（Services / ViewModels / Views / Controls）
├── HS2Tools.Tests/  # xUnit 测试
├── HS2Tools.sln     # 解决方案文件
└── docs/            # 项目文档
```

## 文档

详见 [docs/](docs/README.md)：项目介绍、快速开始、架构设计、功能模块、开发规范。

## 运行环境

- Windows 10/11（x64）
- 运行发布产物无需安装 .NET（自包含单文件）；开发需 .NET 8 SDK
