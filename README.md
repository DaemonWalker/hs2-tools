# hs2-tools

一个基于 **.NET 8 + WPF** 的 Windows 桌面应用，用于管理 HoneySelect2 游戏的模组和角色卡。

> 本项目已由 Wails(Go) + React 迁移至 .NET 8 + WPF（旧代码已移除，git 历史可查）。
> 迁移方案、行为复刻清单与验证记录见 [migration/](migration/README.md)。

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
- **测试**: xUnit（206 项，含与旧 Go CLI 的输出对照与真实环境基准）

## 构建与测试

```bash
cd hs2-tools-csharp
dotnet build
dotnet test
```

发布单文件自包含 exe（产物在 `hs2-tools-csharp/publish/`）：

```bash
dotnet publish src/HS2Tools/HS2Tools.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -o publish
```

## 目录结构

```
├── hs2-tools-csharp/      # 应用本体（全部代码）
│   ├── src/HS2Tools/        # WPF 应用（Services / ViewModels / Views / Controls）
│   └── tests/HS2Tools.Tests/  # xUnit 测试
├── migration/             # 迁移文档（方案、复刻清单、各阶段验收记录）
└── docs/                  # 旧版（Wails）文档，存档参考
```

## 运行环境

- Windows 10/11（x64）
- 运行发布产物无需安装 .NET（自包含单文件）；开发需 .NET 8 SDK
