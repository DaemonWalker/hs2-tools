# hs2-tools (Wails 应用)

HoneySelect2 游戏模组与角色卡管理工具的 Wails 实现。

## 目录说明

```
wails/
├── main.go / app.go   # 应用入口与前端绑定方法
├── internal/          # 后端核心包（downloader / scanner / sideloader / utils）
├── cmd/               # 可独立编译的命令行工具
├── frontend/          # React + TypeScript 前端
├── resources/         # 内嵌资源（sideload.zip）
└── build/             # Wails 构建配置（图标、安装器等）
```

## 开发

```bash
wails dev
```

## 构建

```bash
wails build                  # 当前平台
wails build -platform windows/amd64
```

更多内容见根目录 [README.md](../README.md) 与 [CLAUDE.md](../CLAUDE.md)。
