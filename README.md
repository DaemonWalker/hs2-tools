# hs2-tools

一个基于 Wails + React + TypeScript 的桌面应用，用于管理 HoneySelect2 游戏的模组和角色卡。

## 功能特性

- 🎮 游戏模组管理
- 👤 角色卡管理
- 🖼️ 场景卡管理
- ⬇️ Mod 下载与补全（支持代理、断点续传、实时进度）
- 🔍 自动扫描本地 Mod
- 📦 批量操作支持
- 🎨 双主题皮肤系统（赛博紫夜 / 简洁专业）

## 技术栈

- **桌面框架**: Wails v2（Go 后端 + WebView 前端）
- **后端**: Go 1.25（downloader / scanner / sideloader / utils 内嵌包）
- **前端框架**: React 18 + TypeScript
- **构建工具**: Vite
- **状态管理**: Zustand
- **UI 组件**: Ant Design 5

## 推荐 IDE 配置

- [VSCode](https://code.visualstudio.com/) + [ESLint](https://marketplace.visualstudio.com/items?itemName=dbaeumer.vscode-eslint) + [Prettier](https://marketplace.visualstudio.com/items?itemName=esbenp.prettier-vscode)

## 环境要求

- Go 1.21+
- Node.js 18+
- [Wails CLI v2](https://wails.io/docs/gettingstarted/installation)（`go install github.com/wailsapp/wails/v2/cmd/wails@latest`）

## 项目设置

### 安装依赖

```bash
$ npm install
$ cd wails/frontend && npm install
```

### 开发模式

```bash
$ npm run wails:dev
```

### 构建

```bash
# 构建当前平台可执行文件
$ npm run wails:build

# 打包 Windows 安装包
$ npm run wails:build:win
```

## 项目结构

```
hs2-tools/
├── wails/                   # Wails 应用（全部应用代码）
│   ├── main.go / app.go     # 入口与前端绑定方法
│   ├── internal/            # 后端核心包
│   │   ├── downloader/      # 下载器（代理、断点续传、进度回调）
│   │   ├── scanner/         # 扫描器（PNG 卡片解析、zipmod 解析）
│   │   ├── sideloader/      # BetterRepack 爬虫
│   │   └── utils/           # 配置持久化等工具
│   ├── cmd/                 # 可独立编译的命令行工具
│   │   ├── downloader/
│   │   ├── scanner/
│   │   └── sideloader/
│   ├── frontend/            # React 前端
│   │   └── src/
│   │       ├── assets/      # 静态资源（含主题样式）
│   │       ├── components/  # UI 组件
│   │       ├── hooks/       # React Hooks
│   │       ├── modals/      # 弹窗组件
│   │       ├── pages/       # 页面组件
│   │       ├── store/       # Zustand 状态管理
│   │       └── logic/       # 业务逻辑（含后端调用封装）
│   ├── resources/           # 内嵌资源（sideload.zip）
│   └── build/               # Wails 构建配置
└── docs/                    # 文档
```

## 文档

- [CLAUDE.md](CLAUDE.md) - 项目架构和开发指南
- [docs/项目介绍.md](docs/项目介绍.md) - 项目背景和功能特性
- [docs/架构设计/整体架构.md](docs/架构设计/整体架构.md) - 架构设计说明
- [docs/Go工具/概述.md](docs/Go工具/概述.md) - Go 后端工具说明
- [docs/前端开发/主题系统.md](docs/前端开发/主题系统.md) - 主题皮肤系统使用说明

## 主题皮肤

应用支持双主题切换，可在导航栏右侧点击主题按钮进行切换：

| 主题 | 说明 |
|------|------|
| **赛博紫夜** | 深色背景 + 紫蓝渐变 + 毛玻璃效果，适合夜间使用 |
| **简洁专业** | 浅色背景 + 清新蓝色 + 简洁风格，适合日间使用 |

主题选择会自动保存，下次启动应用时自动恢复。

## 许可证

MIT
