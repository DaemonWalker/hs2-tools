# HS2-Tools 开发速查手册（AI 助手用）

> 本文件是给 AI 编码助手（Kimi Code、Claude Code 等）的项目指引，是唯一事实来源。
> `CLAUDE.md` 仅为指向本文件的兼容指针。

HS2-Tools 是一个基于 **.NET 8 + WPF** 的桌面应用，用于管理 Honey Select 2 游戏的模组和角色卡。

## 核心信息

| 项目 | 说明 |
|------|------|
| **技术栈** | .NET 8 LTS + WPF（`net8.0-windows`） |
| **UI 模式** | MVVM（CommunityToolkit.Mvvm，源生成器） |
| **HTML 解析** | HtmlAgilityPack |
| **MessagePack** | MessagePack-CSharp 3.x（仅底层 Reader/Writer 步行，卡片/场景结构化解析） |
| **测试** | xUnit（`HS2Tools.Tests`） |
| **配置存储** | 统一强类型配置 `%AppData%/hs2-tools/settings.json`（ConfigService） |
| **日志** | `Services/ErrorLog.cs` → `%AppData%/hs2-tools/error.log`（永不抛出） |

## 目录结构

```
HS2Tools/
├── Services/      # 核心服务单例（Config/Scanner+CharaCardParser/Downloader+DownloadManager/
│                  #   Sideloader/SideloadDatabase/GameLauncher/ErrorLog），与 UI 解耦可单测
├── ViewModels/    # 每页一个 VM（+ 共用 CardDetailViewModel）
├── Views/         # 每页一个独立 Window（WindowManager 单例 + Hide/Show 保状态）
├── Controls/      # CardGridControl（虚拟化网格）/ CardDetailPanel / VirtualizingWrapPanel
├── Themes/        # 单主题资源字典（控件一律 DynamicResource）
└── Resources/     # sideload.zip（EmbeddedResource，SideloadDatabase 回退库）
HS2Tools.Tests/    # xUnit：解析器字节级回归 + VM 测试 + 真实环境基准（env 门控）
```

## 常用命令

```bash
dotnet build
dotnet test
dotnet publish HS2Tools/HS2Tools.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -o publish   # 单文件自包含 exe
```

真实环境基准（默认跳过，设环境变量后运行）：
`HS2_REAL_CARD_DIRS`（卡片目录，分号分隔）/ `HS2_REAL_MODS_DIR` / `HS2_REAL_GAME_DIR` / `HS2TOOLS_REAL_CRAWL=1`（真实站点爬虫对照）。

## 关键约定

- **状态下沉服务层单例**，Window 只是视图；跨窗体通信走服务事件；VM/窗口与服务同寿，永久订阅。
- **行为保真**：扫描/下载/爬虫逻辑按既定行为 1:1 复刻（字节级 hack 全部保留，注释注明出处）；网络/编码边界行为有既定口径，改动前先读相关注释与测试。
- **卡片/场景解析**：以 IllusionModdingAPI/BepisPlugins 为基准做结构化解析（`CharaCardParser`：`【AIS_Chara】` BlockHeader + KKEx/UAR，MessagePack-CSharp 底层 Reader/Writer）；旧字节扫描（`SearchBuffer`）仅作数据区内回退路径，不再全文件扫描。
- 不得"静默吞错"：跳过的条目、失败的任务至少 `ErrorLog` 记一条；用户可见失败统一"XX失败：原因"。
- 长任务（扫描/补全/爬虫/整理）防重入：CanExecute + 标志位双保险；取消一律 CancellationTokenSource。
- 测试夹具程序化合成（PNG 标记字节 / zipmod），不提交大文件；ErrorLog 在测试中统一重定向临时目录。
- 修改行为时同步更新 `docs/` 对应文档；修改本文件的约定时保持 `CLAUDE.md` 指针不变。
