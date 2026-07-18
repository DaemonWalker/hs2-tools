# HS2-Tools 开发速查手册

HS2-Tools 是一个基于 Wails + React + TypeScript + Go 的桌面应用，用于管理 Honey Select 2 游戏的模组和角色卡。

## 核心信息

| 项目 | 说明 |
|------|------|
| **技术栈** | Wails v2 + React 18 + TypeScript + Go 1.25 |
| **构建工具** | Vite（前端）+ Wails CLI（打包） |
| **状态管理** | Zustand |
| **UI 组件** | Ant Design 5 |
| **样式方案** | Tailwind + CSS 变量 + 双主题系统 |

## 目录结构

```
hs2-tools/
├── wails/              # Wails 应用（全部应用代码）
│   ├── main.go        # 入口（嵌入前端产物，注册绑定）
│   ├── app.go         # App 结构体：暴露给前端的绑定方法
│   ├── internal/      # 后端核心包
│   │   ├── downloader/  # 下载器（代理、断点续传、进度回调）
│   │   ├── scanner/     # 扫描器（PNG 卡片解析、zipmod 解析）
│   │   ├── sideloader/  # BetterRepack 爬虫
│   │   └── utils/       # 配置持久化等工具
│   ├── cmd/           # 可独立编译的命令行工具
│   ├── frontend/      # React 前端
│   ├── resources/     # 内嵌资源（sideload.zip）
│   └── build/         # Wails 构建配置
└── docs/              # 项目文档
```

## 常用命令

```bash
# 开发模式
npm run wails:dev

# 构建
npm run wails:build

# 打包 Windows
npm run wails:build:win

# 前端单独开发 / 构建
cd wails/frontend
npm run dev
npm run build
```

## 关键入口文件

| 用途 | 路径 |
|------|------|
| 应用入口 | `wails/main.go` |
| 后端绑定方法 | `wails/app.go` |
| 前端入口 | `wails/frontend/src/main.tsx` |
| App 组件 | `wails/frontend/src/App.tsx` |
| 后端调用封装 | `wails/frontend/src/logic/ipcUtils.ts` |
| 主题配置 | `wails/frontend/src/assets/themes/` |

## 前后端通信

- **方法调用**：前端通过 `wails/frontend/wailsjs/go/main/App` 生成的绑定调用 Go 方法，`logic/ipcUtils.ts` 统一封装
- **事件推送**：后端 `EventsEmit` → 前端 `EventsOn`，共 4 个事件：
  - `download:progress` / `download:complete`
  - `sideloader:progress` / `sideloader:complete`

## 文档导航

- [项目介绍](docs/项目介绍.md) - 项目背景和功能特性
- [.impeccable.md](.impeccable.md) - **设计上下文** - 品牌个性、用户画像、设计原则
- [开发指南](docs/快速开始/开发指南.md) - 开发环境搭建和调试
- [整体架构](docs/架构设计/整体架构.md) - 架构设计说明
- [进程通信](docs/架构设计/进程通信.md) - 前后端通信机制

### 功能模块

- [首页与初始化](docs/功能模块/首页与初始化.md)
- [角色卡管理](docs/功能模块/角色卡管理.md)
- [场景管理](docs/功能模块/场景管理.md)
- [Mod管理](docs/功能模块/Mod管理.md)
- [系统设置](docs/功能模块/系统设置.md)

### Go 工具

- [Go工具概述](docs/Go工具/概述.md)
- [下载器](docs/Go工具/下载器.md)
- [扫描器](docs/Go工具/扫描器.md) - 包含 PNG 编码处理逻辑详细说明
- [爬虫工具](docs/Go工具/爬虫工具.md)

### 前端开发

- [主题系统](docs/前端开发/主题系统.md)
- [状态管理](docs/前端开发/状态管理.md)
- [组件规范](docs/前端开发/组件规范.md)

## 开发规范

- 使用 TypeScript 严格模式
- 组件使用函数式 + Hooks 写法
- 状态管理优先使用 Zustand
- 后端调用统一通过 `logic/ipcUtils.ts` 封装
- 主题样式使用 CSS 变量

## 最近更新

### 2025 迁移

#### Electron → Wails 架构迁移
- **桌面框架** - Electron 35 替换为 Wails v2.12，Go 后端直接内嵌
- **Go 工具内嵌** - 原外部进程工具（downloader/scanner/sideloader）迁移为 `wails/internal/` 包，同时保留 `wails/cmd/` 独立 CLI 版本
- **IPC 兼容层** - `logic/ipcUtils.ts` 将 Wails 绑定封装成与 Electron ipcRenderer 同形的接口，业务代码无需改写
- **清理** - 删除旧 Electron 代码（`src/`）、旧 Go 工具（`go_tools/`）及未引用资源

### 2024-03

#### 角色卡管理功能增强
- **无限滚动加载** - 使用 ahooks `useInfiniteScroll` 替换分页，每批加载 24 个
- **排序功能** - 支持 4 种排序方式（名称 A-Z、名称 Z-A、路径、收藏优先）
- **收藏功能** - 使用 localStorage 存储收藏状态，支持收藏优先排序
- **图片加载失败处理** - 卡片和详情页都显示"暂无预览"占位图
- **详情抽屉增强** - 显示文件路径、添加"打开所在文件夹"按钮
- **UI 调整** - 移除列表/网格切换，固定卡片宽度 180px，抽屉高度调整为 70%

#### Go Scanner 优化
- **错误处理优化** - `readPngImage` 失败时返回空字符串而非错误
- **数据清洗简化** - `bufferToString` 只保留 `TrimSpace`，移除替换字符和特殊字符清理
- **批量处理改进** - `readPngPageDataBatch` 中名称失败跳过，图片失败返回空字符串

#### 组件改进
- **PngViewer** - 添加图片加载失败处理，显示占位图
- **CardGrid** - 使用 `forwardRef` 暴露 `reload`、`total`、`loading` 方法

---

更多详细信息请查看 [docs/README.md](docs/README.md)
