# HS2-Tools → C# 迁移文档

本目录记录 HS2-Tools 从 **Wails(Go) + React** 迁移到 **.NET 8 + WPF** 的完整方案。
迁移实施前请先通读本目录全部文档；本文档为导航与最终决策记录。

> 状态：**阶段 0~2 已完成**（仓库根 `hs2-tools-csharp/`：解决方案骨架、5 个核心服务移植 + xUnit 104 项测试、应用骨架）。
> 阶段 1 验证已过：scanner 与 Go CLI 输出一致（每次测试自动构建基准 CLI）；sideloader 真实站点对照 go=12,472 / cs=12,366、guid 重合度 99.06%。
> 迁移期间现有 Go/Wails 代码不做任何修改。

## 文档导航

| 文档 | 内容 |
|---|---|
| [01-现状分析.md](01-现状分析.md) | 现有代码完整分析：规模、后端算法、API 面、前端功能、存储现状、死代码核实 |
| [02-迁移方案.md](02-迁移方案.md) | 目标架构、项目结构、技术映射、必须复刻的行为细节、UI 与多窗体设计 |
| [03-阶段计划.md](03-阶段计划.md) | 分阶段实施计划、工作量估算、风险与对策、验证策略 |

## 实施状态

| 阶段 | 状态 | 交付与验证 |
|---|---|---|
| 0. 准备 | ✅ 完成 | `hs2-tools-csharp/HS2Tools.sln`：`src/HS2Tools`（net8.0-windows WPF）+ `tests/HS2Tools.Tests`（xUnit）；sideload.zip 内嵌资源；测试夹具以合成 PNG/zipmod 程序化生成（不提交大文件） |
| 1. 核心服务移植 | ✅ 完成 | 5 个服务（Config/Scanner/Downloader+DownloadManager/Sideloader/GameLauncher）；xUnit **104 项全绿**；scanner 与 Go CLI 输出一致（测试自动构建基准 CLI）；sideloader 真实站点对照 go=12,472 / cs=12,366、**guid 重合度 99.06%**（29 分钟，内存 <40MB） |
| 2. 应用骨架 | ✅ 完成 | ServiceContainer、WindowManager（单例+Hide/Show）、单主题资源字典、MainWindow 导航 + 7 占位窗口、游戏路径校验徽标；启动冒烟通过 |
| 3. 页面迁移 | ⬜ 未开始 | — |
| 4. 打磨打包 | ⬜ 未开始 | — |

阶段 1 实施期对原方案的修订（.NET 平台差异，重要）见 [02-迁移方案.md](02-迁移方案.md) 第 10 节。

## 最终决策记录

### 技术栈

| 决策项 | 结论 |
|---|---|
| 目标框架 | **.NET 8 LTS + WPF**（`net8.0-windows`，`UseWPF=true`），仅支持 Windows |
| UI 模式 | **MVVM**，使用 `CommunityToolkit.Mvvm` |
| 窗体架构 | **每页一个独立 Window**（多窗体），主窗口常驻做首页与导航；窗口单例 + Hide/Show 保持状态 |
| UI 风格 | 原生风格**单主题**；主题机制留扩展口，双主题（赛博紫夜/简洁专业）后期可再加（决策 A7） |
| CLI 工具 | **不保留**独立命令行工具，能力全部并入 C# 服务层；Go 版 CLI 仅在迁移期充当测试基准 |
| MVVM 辅助库 | CommunityToolkit.Mvvm（官方维护，源生成器，无反射开销） |
| HTML 解析 | HtmlAgilityPack（替代 goquery） |

### 功能项决策（A 类，用户已逐项确认）

| # | 功能 | 结论 |
|---|---|---|
| A1 | 开始游戏 / 开始工作室 | **保留并修复**（现版按钮无事件、配置双轨制导致功能整体不可用；C# 版统一配置 + `Process.Start` 修复） |
| A2 | CharaExplorer 搜索框 | **保留并接通**（现版未接过滤逻辑） |
| A3 | CharaExplorer 筛选按钮 | **砍掉**（纯占位，筛选维度从未定义） |
| A4 | 详情抽屉"一键下载缺失 Mod" | **保留**，做成真实功能（复用 DownloadManager） |
| A5 | 详情抽屉"标签" | **砍掉**（硬编码假数据，PNG 中无标签数据来源） |
| A6 | 详情抽屉修改时间/文件大小 | **保留**，改为 `FileInfo` 真实数据 |
| A7 | 双主题系统 | **暂不做**，保留后期添加可能（ThemeManager/ResourceDictionary 留扩展口） |

### 死代码与架构削减（B/C 类，按方案默认执行）

- **14 个死 API 不移植**：`readDir`、`loadSettings`、`saveSettings`、`loadLocalMods`、`saveLocalMods`、`readZipMod`、`readPngNamesBatch`、`readPngImagesBatch`、`getDownloaderStatus`、`getScannerStatus`、`getSideloaderStatus`、`isSideloaderRunning`、`log`、`ping`
- **7 个死组件不重写**：`app/Settings.tsx`、`share/CardList.tsx`、`share/ResponsiveContainer.tsx`、`character/CharaThumbnail.tsx`、`character/CharaDetail.tsx`、`scene/SceneDetail.tsx`、`mods/ModStatus.tsx`
- 存储死代码：Go 端 `setting.json`/`localMods.json`、IndexedDB 废 store、双套下载状态体系 → C# 版统一为一份强类型配置 + 单一 DownloadManager
- F15 按键防休眠 hack → `SetThreadExecutionState` P/Invoke
- `OpenInFolder` 跨平台分支、Wails 批量 API 的分批壳 → 砍

以上死代码的核实证据见 [01-现状分析.md](01-现状分析.md) 第 6 节。
