# Go 工具接口文档

本文档介绍 Go 命令行工具的接口。

命令行工具位于 `wails/cmd/` 目录，复用 `wails/internal/` 包实现，与应用内嵌功能完全一致。编译方式：

```bash
cd wails
go build -ldflags="-s -w" -o build/bin/scanner.exe ./cmd/scanner
go build -ldflags="-s -w" -o build/bin/downloader.exe ./cmd/downloader
go build -ldflags="-s -w" -o build/bin/sideloader.exe ./cmd/sideloader
```

## 通用接口规范

### 输入方式

1. **命令行参数模式** - 简单操作
2. **JSON 模式** - 批量操作（推荐）

### 输出格式

```json
{
  "success": true,
  "data": { },
  "error": null
}
```

### 退出码

| 码 | 含义 |
|----|------|
| 0 | 成功 |
| 1 | 通用错误 |
| 2 | 参数错误 |
| 3 | 文件不存在 |
| 4 | 权限错误 |

## Scanner 接口

### 基本调用

```bash
# 命令行参数模式
scanner -action <action> -path <path> [options]

# JSON 模式
echo '{"action":"...",...}' | scanner -json
```

### scanDir - 扫描目录

递归扫描目录，返回文件列表。

**参数**：

```json
{
  "action": "scanDir",
  "path": "C:\\HS2\\UserData\\chara\\female",
  "options": {
    "excludeDir": ["backup", "temp"],
    "targetExtension": [".png"]
  }
}
```

**返回**：

```json
{
  "success": true,
  "data": [
    "C:\\HS2\\UserData\\chara\\female\\card1.png",
    "C:\\HS2\\UserData\\chara\\female\\card2.png"
  ]
}
```

### readPngMods - 提取 PNG Mods

从 PNG 文件提取 Mod GUID 列表。

**参数**：

```json
{
  "action": "readPngMods",
  "path": "C:\\card.png"
}
```

**返回**：

```json
{
  "success": true,
  "data": ["author.mod.v1", "author.mod.v2"]
}
```

### readPngNames - 提取 PNG 名称

从 PNG 文件提取角色名称列表。

**参数**：

```json
{
  "action": "readPngNames",
  "path": "C:\\card.png"
}
```

**返回**：

```json
{
  "success": true,
  "data": ["角色名称", "别名1", "别名2"]
}
```

### readPngImage - 提取 PNG 图像

从 PNG 文件提取 Base64 图像数据。

**参数**：

```json
{
  "action": "readPngImage",
  "path": "C:\\card.png"
}
```

**返回**：

```json
{
  "success": true,
  "data": "iVBORw0KGgoAAAANSUhEUgAA..."
}
```

### readZipMod - 解析 zipmod

解析单个 zipmod 文件。

**参数**：

```json
{
  "action": "readZipMod",
  "path": "C:\\mod.zipmod"
}
```

**返回**：

```json
{
  "success": true,
  "data": {
    "author.mod.version": {
      "name": "Mod Name",
      "version": "1.0.0",
      "path": "C:\\mod.zipmod"
    }
  }
}
```

### readZipModBatch - 批量解析 zipmod

并发解析多个 zipmod 文件。

**参数**：

```json
{
  "action": "readZipModBatch",
  "paths": ["a.zipmod", "b.zipmod", "c.zipmod"],
  "concurrency": 4
}
```

**返回**：

```json
{
  "success": true,
  "data": {
    "mod.a": { "name": "A", "version": "1.0", "path": "a.zipmod" },
    "mod.b": { "name": "B", "version": "1.0", "path": "b.zipmod" }
  }
}
```

### readPngModsBatch - 批量提取 PNG Mods

并发从多个 PNG 提取 Mod GUID。

**参数**：

```json
{
  "action": "readPngModsBatch",
  "paths": ["a.png", "b.png", "c.png"],
  "concurrency": 8
}
```

**返回**：

```json
{
  "success": true,
  "data": [
    { "path": "a.png", "modIds": ["mod.a", "mod.b"] },
    { "path": "b.png", "modIds": ["mod.c"] }
  ]
}
```

### readPngNamesBatch - 批量提取 PNG 名称

并发从多个 PNG 提取角色名称。

**参数**：

```json
{
  "action": "readPngNamesBatch",
  "paths": ["a.png", "b.png"],
  "concurrency": 8
}
```

**返回**：

```json
{
  "success": true,
  "data": [
    { "path": "a.png", "names": ["角色A", "别名A1"] },
    { "path": "b.png", "names": ["角色B"] }
  ]
}
```

### readPngImagesBatch - 批量提取 PNG 图像

并发从多个 PNG 提取图像数据。

**参数**：

```json
{
  "action": "readPngImagesBatch",
  "paths": ["a.png", "b.png"],
  "concurrency": 4
}
```

**返回**：

```json
{
  "success": true,
  "data": [
    { "path": "a.png", "imageData": "iVBORw0K..." },
    { "path": "b.png", "imageData": "iVBORw0K..." }
  ]
}
```

### readPngPageDataBatch - 批量提取页面数据

并发从多个 PNG 提取名称和图像（优化版本）。

**参数**：

```json
{
  "action": "readPngPageDataBatch",
  "paths": ["a.png", "b.png"],
  "concurrency": 4
}
```

**返回**：

```json
{
  "success": true,
  "data": [
    {
      "path": "a.png",
      "names": ["角色A"],
      "imageData": "iVBORw0K..."
    }
  ]
}
```

### moveFile - 移动文件

移动文件，支持跨盘符。

**参数**：

```json
{
  "action": "moveFile",
  "path": "C:\\source.png",
  "targetPath": "D:\\dest.png"
}
```

**返回**：

```json
{ "success": true, "data": null }
```

### checkDir - 检查目录

检查目录是否存在，不存在则创建。

**参数**：

```json
{
  "action": "checkDir",
  "path": "C:\\new\\directory"
}
```

### fileExists - 检查文件

检查文件是否存在且是 PNG。

**参数**：

```json
{
  "action": "fileExists",
  "path": "C:\\file.png"
}
```

**返回**：

```json
{ "success": true, "data": true }
```

## Downloader 接口

### 基本调用

```bash
downloader -url <url> -out <path> [options]
```

### 参数

| 参数 | 说明 | 必需 |
|------|------|------|
| `-url` | 下载 URL | 是 |
| `-out` | 输出路径 | 是 |
| `-proxy` | 代理地址 | 否 |
| `-resume` | 启用断点续传（默认 true） | 否 |
| `-timeout` | 连接超时秒数 | 否 |

### 输出

Downloader 通过 stdout 输出 JSON 行：

```json
{"type":"info","message":"Starting download..."}
{"type":"progress","downloaded":1048576,"total":10485760,"speed":524288,"percent":10.0}
{"type":"progress","downloaded":2097152,"total":10485760,"speed":512000,"percent":20.0}
...
{"type":"complete","path":"C:\\file.zip","total":10485760,"message":"Download completed"}
```

### 示例

```bash
# 基本下载
./downloader -url "http://example.com/file.zip" -out "file.zip"

# 使用代理
./downloader -url "http://example.com/file.zip" -out "file.zip" -proxy "http://127.0.0.1:7890"

# 不启用断点续传
./downloader -url "http://example.com/file.zip" -out "file.zip" -resume=false
```

## Sideloader 接口

### 基本调用

```bash
sideloader [options]
```

### 参数

| 参数 | 说明 |
|------|------|
| `-proxy` | 代理地址 |

### 输出

Sideloader 输出日志到 stderr，结果保存到 `result.json`：

```
[Sideloader] Starting crawl from https://sideload.betterrepack.com/download/AISHS2/
[Sideloader] Processing: https://sideload.betterrepack.com/download/AISHS2/
[Sideloader] Found: author.mod.v1
...
[Sideloader] Crawl completed, found 12345 mods
[Sideloader] Saved to result.json
```

### 结果格式

```json
{
  "author.mod.v1": "download/AISHS2/mod1.zipmod",
  "author.mod.v2": "download/AISHS2/mod2.zipmod"
}
```

### 示例

```bash
# 基本使用
./sideloader

# 使用代理
./sideloader -proxy "http://127.0.0.1:7890"
```

## 集成说明

### 应用内集成（主要方式）

迁移到 Wails 后，Go 工具的功能已通过 `wails/app.go` 的绑定方法直接暴露给前端，无需通过子进程调用命令行工具。前端调用示例：

```typescript
import ipcUtils from '../logic/ipcUtils'

// 扫描目录（对应 scanner -action scanDir）
const files = await ipcUtils.getAllFiles('C:\\HS2', {
  targetExtension: ['.png']
})

// 下载文件（进度通过 download:progress 事件推送）
await ipcUtils.triggerDownload({ name: 'mod.zipmod', url: 'download/AISHS2/mod.zipmod' })
```

### 独立命令行使用

`wails/cmd/` 下的工具也可独立编译，用于脚本化或调试场景：

```bash
cd wails

# 扫描目录
go run ./cmd/scanner -action scanDir -path "C:\\HS2" -ext png

# JSON 模式
echo '{"action":"scanDir","path":"C:\\HS2","options":{"targetExtension":[".png"]}}' | go run ./cmd/scanner -json

# 下载文件
go run ./cmd/downloader -url "http://example.com/file.zip" -out "file.zip"

# 爬取 Sideload 数据（结果保存到当前目录 result.json）
go run ./cmd/sideloader -proxy "http://127.0.0.1:7890"
```
