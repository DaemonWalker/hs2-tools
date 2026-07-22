# HS2-Tools 开发速查手册

HS2-Tools 是一个基于 **.NET 8 + WPF** 的桌面应用，用于管理 Honey Select 2 游戏的模组和角色卡。
（已从 Wails(Go)+React 迁移，迁移决策与验收记录见 `migration/`。）

## 核心信息

| 项目 | 说明 |
|------|------|
| **技术栈** | .NET 8 LTS + WPF（`net8.0-windows`） |
| **UI 模式** | MVVM（CommunityToolkit.Mvvm，源生成器） |
| **HTML 解析** | HtmlAgilityPack |
| **测试** | xUnit（`tests/HS2Tools.Tests`） |
| **配置存储** | 统一强类型配置 `%AppData%/hs2-tools/settings.json`（ConfigService） |
| **日志** | `Services/ErrorLog.cs` → `%AppData%/hs2-tools/error.log`（永不抛出） |

## 目录结构

```
hs2-tools-csharp/
├── src/HS2Tools/
│   ├── Services/      # 核心服务单例（Config/Scanner/Downloader+DownloadManager/
│   │                  #   Sideloader/SideloadDatabase/GameLauncher/ErrorLog），与 UI 解耦可单测
│   ├── ViewModels/    # 每页一个 VM（+ 共用 CardDetailViewModel）
│   ├── Views/         # 每页一个独立 Window（WindowManager 单例 + Hide/Show 保状态）
│   ├── Controls/      # CardGridControl（虚拟化网格）/ CardDetailPanel / VirtualizingWrapPanel
│   ├── Themes/        # 单主题资源字典（控件一律 DynamicResource）
│   └── Resources/     # sideload.zip（EmbeddedResource，SideloadDatabase 回退库）
└── tests/HS2Tools.Tests/  # xUnit：解析器字节级回归 + VM 测试 + Go CLI 对照（需 wails/ 基准，已随迁移完成退役为跳过）
```

## 常用命令

```bash
cd hs2-tools-csharp
dotnet build
dotnet test
dotnet publish src/HS2Tools/HS2Tools.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -o publish   # 单文件自包含 exe
```

真实环境基准（默认跳过，设环境变量后运行）：
`HS2_REAL_CARD_DIRS`（卡片目录，分号分隔）/ `HS2_REAL_MODS_DIR` / `HS2_REAL_GAME_DIR` / `HS2TOOLS_REAL_CRAWL=1`（真实站点爬虫对照）。

## 关键约定

- **状态下沉服务层单例**，Window 只是视图；跨窗体通信走服务事件；VM/窗口与服务同寿，永久订阅。
- **行为保真**：解析/扫描/下载/爬虫逻辑与旧 Go 版 1:1（字节级 hack 全部保留），与原版的**有意差异**全部记录在 `migration/02-迁移方案.md` §10——接到"与旧版不一致"反馈先查该节。
- 原版"静默吞错"点位不得默默 continue：至少 `ErrorLog` 记一条。
- 长任务（扫描/补全/爬虫/整理）防重入：CanExecute + 标志位双保险；取消一律 CancellationTokenSource。
- 测试夹具程序化合成（PNG 标记字节 / zipmod），不提交大文件；ErrorLog 在测试中统一重定向临时目录。
- 修改代码后保持 `migration/` 文档同步（§10 修订记录 + 03 阶段完成记录）。
