# HS2-Tools 开发速查手册（AI 助手用）

> 本文件是给 AI 编码助手（Kimi Code、Claude Code 等）的项目指引，是唯一事实来源。
> `CLAUDE.md` 仅为指向本文件的兼容指针。

HS2-Tools 是一个基于 **.NET 10 + WPF** 的桌面应用，用于管理 Honey Select 2、恋活（KK）、恋活 Sunshine（KKS）游戏的模组和角色卡。

## 核心信息

| 项目 | 说明 |
|------|------|
| **技术栈** | .NET 10 LTS + WPF（`net10.0-windows`） |
| **UI 模式** | MVVM（CommunityToolkit.Mvvm，源生成器） |
| **多游戏** | `Models/GameProfile.cs` 静态注册表（hs2/kk/kks）；配置按游戏隔离（settings.json 的 `currentGame` + `games`） |
| **HTML 解析** | HtmlAgilityPack |
| **MessagePack** | MessagePack-CSharp 3.x（仅底层 Reader/Writer 步行，卡片/场景结构化解析） |
| **SQLite 存储** | Microsoft.Data.Sqlite 8.x（`analysis-cache.db`：Mod/卡片分析缓存 + Sideload 索引与扫描记录，分表隔离） |
| **测试** | xUnit（`HS2Tools.Tests`） |
| **配置存储** | 统一强类型配置 settings.json（ConfigService）；可重建的 Mod/卡片分析结果和 Sideload 本地记录存 SQLite。绿色版：数据目录优先程序所在目录下 `data/`，无写权限回退 `%AppData%/hs2-tools/`，首启自动迁移旧 %AppData% 配置、数据库与 Sideload JSON |
| **日志** | `Services/ErrorLog.cs` → 数据目录下 `error.log`（永不抛出） |

## 目录结构

```
HS2Tools/
├── Services/      # 核心服务单例（Config/Scanner+CharaCardParser/AnalysisCache/Downloader+DownloadManager/
│                  #   Sideloader/SideloadDatabase/GameLauncher/ErrorLog），与 UI 解耦可单测
├── Models/        # AppSettings（含 per-game GameData）/ GameProfiles 静态注册表
├── ViewModels/    # 每页一个 VM（+ 共用 CardDetailViewModel）
├── Views/         # MainWindow 单窗口主壳 + HomeView/7 个功能页面（PageManager 缓存保状态）
├── Controls/      # CardGridControl（虚拟化网格）/ CardDetailPanel / VirtualizingWrapPanel
├── Themes/        # 单主题资源字典（控件一律 DynamicResource）
└── Resources/     # sideload.zip（EmbeddedResource，仅 HS2 库的 SideloadDatabase 回退）
HS2Tools.Tests/    # xUnit：解析器字节级回归 + VM 测试 + 真实环境基准（env 门控）
```

## 常用命令

```bash
dotnet build
dotnet test
dotnet publish HS2Tools/HS2Tools.csproj -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -o publish   # 单文件自包含 exe
```

GitHub Release 通过 `.github/workflows/release.yml` 发布：手动触发时输入前三段版本号，每天北京时间 02:17 自动检查最新版本标签之后的新提交，有提交才递增修订号并发布；均以 `github.run_number` 作为第四段 Build 号。首次发布须手动执行；测试成功后发布 .NET 10 `win-x64` 完整产物、SHA-256、同名标签和 Release，不在页面展示版本号。

真实环境基准（默认跳过，设环境变量后运行）：
`HS2_REAL_CARD_DIRS`（卡片目录，分号分隔）/ `HS2_REAL_MODS_DIR` / `HS2_REAL_GAME_DIR` / `HS2TOOLS_REAL_CRAWL=1`（真实站点爬虫对照）。

## 关键约定

- **决策一律请示用户**：修改或新增功能时，只要出现两种及以上可行方案/取舍（实现方式、行为口径、兼容范围等），必须直接询问用户选择，不得自行替用户做决定。
- **单窗口主壳 + 页面缓存**：MainWindow 持有侧栏底部全局游戏下拉框与工作台共用的 VM；PageManager 缓存 UserControl 页面，切页保状态。状态下沉服务层单例，跨页通信走服务事件；VM/页面与服务同寿，永久订阅。
- **游戏特定知识集中在 GameProfiles**（exe 名、目录相对路径、卡片标记字节、Parameter 名字段键、sideload 数据源与 base URL）；服务层一律经 `ConfigService.CurrentProfile` 按当前游戏求值，不得另写游戏常量。角色卡/场景页监听游戏与路径变化并自动重扫；目录缺少当前游戏 exe 时清空网格并提示，不得误展示其他游戏内容。卡片解析按卡头标记自动识别格式（HS2【AIS_Chara】与 KK/KKS【KoiKatuChara】都认，不依赖当前游戏）。KK/KKS 的 exe 名按常见安装版填写，待真实环境验证。
- **行为保真**：扫描/下载/爬虫逻辑按既定行为 1:1 复刻（字节级 hack 全部保留，注释注明出处）；网络/编码边界行为有既定口径，改动前先读相关注释与测试。例外：Mod GUID 在卡片引用、zipmod manifest 与站点索引入口仅移除控制字符，保留 Unicode 与首尾空格，匹配时不得 Trim；仅当原始 GUID 无精确命中且 Trim 后相同才提示修复。未被引用的源 zipmod 直接改为首个目标 GUID、其余目标复制；源 GUID 仍被引用时全部复制，支持多卡片使用不同空格变体。
- **Sideload 更新**（经用户确认偏离"全量 1:1 复刻"的一组可靠性/性能修复）：本地库与扫描 meta 存 `analysis-cache.db` 的 `sideload_sources` / `sideload_entries` / `sideload_scan_meta`，按 source_id 隔离（KK/KKS 共享 kkec）；旧 `sideload*.json` 首启自动迁入并保留 `.migrated` 备份；内嵌/空回退库首次 Update 覆盖种子库不合并，之后对用户库合并，连续两次已应用扫描未见才淘汰；空结果保留旧库，有目录/Mod 抓取失败时询问用户是否使用本次结果（确认前禁止切游戏；meta 记 PartialFailure+FailedCount+ResultApplied），404 自然淘汰不算失败；增量爬取——本地记录的 autoindex Last modified 不晚于本站当前时间即零请求复用旧 GUID，日期缺失/无法解析仅原始指纹完全一致时复用，其余保守重抓；已发现/已处理只统计 Mod 文件不含目录，已发现不是去重后的成功 GUID 数；suffix range 首请求省 HEAD、尾部窗口 16→256KB 几何扩窗、本地头+manifest 合并 Range（大小一律以中央目录为准，兼容 ZIP64 哨兵与 data descriptor；ZIP64 支持 EOCD64 locator + extra 0x0001）；瞬时失败（超时/连接重置/429/5xx）3 次指数退避重试；重复 GUID 按相对路径字典序确定性裁决；目录遍历由单个生产者串行执行，子目录进入本地队列、Mod 进入 Channel，由 10 个异步消费者解析（不得改回父目录持有信号量等待子目录的递归模型）；Cancel 走 CancellationToken 即时中断在飞请求；SQLite 更新走事务；下载落盘文件名经 `DownloadManager.SanitizeFileName`（GUID 仅作任务 Id/显示名，含 : / 的真实 GUID 不得直接 Path.Combine）；下载复用 DownloaderService（代理变化重建）、读停滞 60s 超时、完成后 ZIP 校验（PK 头+manifest.xml，失败删文件并标记失败，防 HTML 错误页当 zipmod）；爬虫"Found: guid" 不进 error.log（洪水），ErrorLog 写串行化；Sideload 页 Reload 任务快照一次+字典索引，SaveMeta 走 MetaChanged 事件（不触发整表重建）。
- **Mods 窗口去重/整理/清理走分析缓存**：数据源是「开始分析」持久化的 `GameData.ModEntries`（完整条目，含重复 guid、覆盖 unusedmods）/ `CharaUsage` / `SceneUsage` / `LastAnalysisTime`，不自行重扫磁盘（硬盘压力考虑）。清理覆盖 mods/unusedmods，仅选择未被 ModUsage 引用、manifest 未声明 shader 且 GUID 存在于当前 Sideload 数据库的文件，排除下载目录；确认后永久删除，成功项同步移出 ModEntries 并按剩余条目重建 LocalMods。shader 豁免按 mod 自身判定：manifest 含 `<MaterialEditor><Shader .../>` 声明（`ModInfo.ShaderNames` 非空）即视为在用，按人物卡引用同口径处理（不进 unusedmods；误在其中的移回 mods，首页移回提示同样纳入）——不做卡片内容匹配。执行前逐文件存在性校验（消失的跳过记日志），执行后同步改写缓存（去重移除落选条目；整理按赢家最终落点重建，移动失败的落选者保留；清理移除成功删除条目并按剩余条目重建 LocalMods）。Mods 窗口「刷新模组列表」保持轻量只更新 LocalMods 展示。
- **卡片/场景解析**：以 IllusionModdingAPI/BepisPlugins（KK/KKS 参照 kkloader）为基准做结构化解析（`CharaCardParser`：BlockHeader + KKEx/UAR，MessagePack-CSharp 底层 Reader/Writer）；旧字节扫描（`SearchBuffer`）仅作数据区内回退路径（HS2 与 KK 字段名都认），不再全文件扫描。数据区起点 = 真 IEND chunk（PNG chunk 步行定位；反向字节扫描仅作非 PNG/损坏文件回退——追加的游戏数据可能碰巧含 "IEND" 字节，反扫会误判截断点）。blob 信封按游戏分两种（`GameProfile.CharaBlobHasFacePng`）：HS2 = version + lang/userID/dataID；KK/KKS = version + facePng（int32 长度 + 字节），无 lang/userID/dataID（真实卡实测）。完整卡片接口一次读盘返回名称/Mod/图像；数据块用 `ReadOnlyMemory` 切片，不额外 `ToArray()`。
- **SQLite 存储**：`AnalysisCacheService` 管理 `analysis-cache.db` 的 `card_cache` / `zipmod_cache`，按 `game_id + scope + path` 隔离；scope 固定为 mods/unused/scene/chara。指纹 = 文件大小 + 最后修改时间，缓存版本变化只清空这两类分析表，不得删除 Sideload 三表。首页分析、网格、详情、探索器和场景整理共享；未变化卡片的无图分析不得再读盘，有图页面只补读图像。分析缓存读写失败记日志并回退完整解析，不替代 `GameData.ModEntries` 等整理缓存。`SideloadDatabaseService` 在同一数据库独立管理 Sideload 三表。场景批量整理共享一次扫描/解析，移动保持任务顺序且不重复移动同一场景。
- 不得"静默吞错"：跳过的条目、失败的任务至少 `ErrorLog` 记一条；用户可见失败统一"XX失败：原因"。
- 长任务（扫描/补全/爬虫/整理/清理）防重入：CanExecute + 标志位双保险；取消一律 CancellationTokenSource。
- 测试夹具程序化合成（PNG 标记字节 / zipmod），不提交大文件；ErrorLog 在测试中统一重定向临时目录。
- 修改行为时同步更新 `docs/` 对应文档；修改本文件的约定时保持 `CLAUDE.md` 指针不变。
