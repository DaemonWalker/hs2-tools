---
name: update-document
description: |
  根据对话历史，智能分析并修改目标文档。

  **触发场景：**
  - 用户在对话中提到需要更新文档内容
  - 用户讨论了代码变更、功能修改或架构调整
  - 用户在对话中描述了需要记录到文档的信息
  - 用户要求"更新文档"、"修改文档"、"同步文档"
  - 用户要求根据讨论内容更新特定文档

  **核心功能：**
  1. 分析对话历史，提取需要更新到文档的关键信息
  2. 识别目标文档和需要修改的具体内容
  3. 智能合并对话中的信息到现有文档
  4. 保持文档格式和结构的一致性
---

# Update Document Skill

基于对话历史智能更新文档内容的工具。

## 工作流程

### 1. 分析对话历史

从对话中提取需要文档化的关键信息：

```typescript
// 提取对话中的关键信息
const extractFromHistory = (messages) => {
  const changes = []
  const additions = []
  const deletions = []

  for (const msg of messages) {
    // 识别代码变更
    if (msg.contains('修改了') || msg.contains('更新了') || msg.contains('重命名')) {
      changes.push(extractChangeInfo(msg))
    }
    // 识别新增功能
    if (msg.contains('添加了') || msg.contains('新增') || msg.contains('实现了')) {
      additions.push(extractAdditionInfo(msg))
    }
    // 识别删除内容
    if (msg.contains('删除了') || msg.contains('移除') || msg.contains('废弃')) {
      deletions.push(extractDeletionInfo(msg))
    }
  }

  return { changes, additions, deletions }
}
```

### 2. 确定目标文档

根据对话内容识别需要更新的文档：

```typescript
// 识别目标文档
const identifyTargetDocs = (extractedInfo) => {
  const targets = []

  // 根据文件路径推断文档
  if (extractedInfo.filePath?.includes('api/')) {
    targets.push('docs/API参考/')
  }
  if (extractedInfo.filePath?.includes('component/')) {
    targets.push('docs/前端开发/组件规范.md')
  }
  if (extractedInfo.filePath?.includes('go_tools/')) {
    targets.push('docs/Go工具/')
  }

  // 询问用户确认
  return confirmWithUser(targets)
}
```

### 3. 智能内容合并

将新信息合并到现有文档：

```typescript
// 智能合并策略
const mergeContent = (existing, newInfo, strategy) => {
  switch (strategy) {
    case 'append':
      // 追加到章节末尾
      return appendToSection(existing, newInfo.section, newInfo.content)

    case 'replace':
      // 替换特定段落
      return replaceSection(existing, newInfo.target, newInfo.content)

    case 'insert':
      // 插入到指定位置
      return insertAtPosition(existing, newInfo.position, newInfo.content)

    case 'update-link':
      // 更新交叉引用链接
      return updateCrossReferences(existing, newInfo.changes)
  }
}
```

### 4. 执行文档更新

应用修改并生成报告：

```typescript
const updateDocument = async (targetDoc, changes) => {
  // 读取原文档
  const original = await readFile(targetDoc)

  // 创建备份
  await createBackup(targetDoc)

  // 应用修改
  const updated = applyChanges(original, changes)

  // 验证格式
  if (validateMarkdown(updated)) {
    await writeFile(targetDoc, updated)
    return { success: true, changes: countChanges(original, updated) }
  }
}
```

## 具体应用场景

### 场景 1: 代码变更同步

当对话中讨论了代码修改时：

```
用户: 我修改了 scanLogic.ts 中的扫描逻辑，添加了并发限制
Claude: 需要更新文档记录这个变更
→ 识别目标: docs/功能模块/Mod管理.md
→ 更新内容: 添加并发限制的实现说明
```

### 场景 2: API 接口变更

当对话中讨论了 API 调整时：

```
用户: 新的下载接口添加了重试次数参数
Claude: 同步更新 API 文档
→ 识别目标: docs/API参考/IPC接口.md
→ 更新内容: 添加 retryCount 参数说明
```

### 场景 3: 架构设计讨论

当对话中讨论了架构决策时：

```
用户: 我们决定用 Zustand 替换 Redux 做状态管理
Claude: 更新架构文档
→ 识别目标: docs/架构设计/整体架构.md + docs/前端开发/状态管理.md
→ 更新内容: 替换状态管理方案说明
```

### 场景 4: 目录结构调整

当对话中重构了目录时：

```
用户: 我把 docs/开发指南/ 移到了 docs/快速开始/
Claude: 更新所有相关链接
→ 识别目标: 所有引用 docs/开发指南/ 的文档
→ 更新内容: 修复交叉引用链接
```

## 更新策略

### 内容追加策略

```typescript
// 在指定章节末尾添加内容
const appendToSection = (content, sectionTitle, newContent) => {
  const sectionRegex = new RegExp(`(## ${sectionTitle}.*?)(\n## |$)`, 's')
  return content.replace(sectionRegex, `$1\n${newContent}\n$2`)
}
```

### 内容替换策略

```typescript
// 替换特定段落
const replaceSection = (content, oldText, newText) => {
  // 使用模糊匹配找到最相似的段落
  const similarity = findSimilarSection(content, oldText)
  if (similarity.score > 0.8) {
    return content.replace(similarity.match, newText)
  }
}
```

### 链接更新策略

```typescript
// 更新交叉引用
const updateCrossReferences = (content, pathMapping) => {
  let updated = content
  for (const [oldPath, newPath] of Object.entries(pathMapping)) {
    // 匹配 markdown 链接
    const linkRegex = new RegExp(`\\[([^\\]]+)\\]\\(${escapeRegex(oldPath)}\\)`, 'g')
    updated = updated.replace(linkRegex, `[$1](${newPath})`)
  }
  return updated
}
```

## 输出格式

执行完成后，输出更新报告：

```markdown
## 文档更新报告

### 分析结果
- 对话中提取的变更: 5 项
- 识别目标文档: 3 个
- 自动更新: 3 个
- 需要确认: 0 个

### 更新详情

| 目标文档 | 更新类型 | 变更内容 | 状态 |
|---------|---------|---------|------|
| docs/功能模块/Mod管理.md | 追加 | 添加并发限制说明 | ✅ 已更新 |
| docs/API参考/IPC接口.md | 替换 | 更新 download 接口参数 | ✅ 已更新 |
| docs/架构设计/整体架构.md | 修改 | 更新状态管理方案 | ✅ 已更新 |

### 变更预览

**docs/功能模块/Mod管理.md** (第 45-52 行)
```diff
+ ### 并发控制
+
+ 为避免同时扫描大量文件造成系统负担，扫描器实现了并发限制：
+ - 默认并发数: 5
+ - 可通过配置调整
```

### 建议操作

- [x] 已自动更新所有识别到的文档
- [ ] 建议检查更新内容的准确性
- [ ] 如需调整，可使用备份文件恢复
```

## 使用示例

### 示例 1: 根据代码变更更新文档

```
用户: 我重构了 sideloader 模块，把下载功能抽离成了独立的 downloader 工具

Claude 执行:
1. 分析对话 → 识别出架构变更
2. 确定目标 → docs/Go工具/概述.md, docs/Go工具/下载器.md
3. 生成更新 → 添加 downloader 工具说明
4. 应用变更 → 更新文档
```

### 示例 2: 更新 API 文档

```
用户: downloadStore 添加了 pauseDownload 和 resumeDownload 方法

Claude 执行:
1. 分析对话 → 识别 API 新增
2. 确定目标 → docs/前端开发/状态管理.md
3. 生成更新 → 添加新方法说明
4. 应用变更 → 更新文档
```

### 示例 3: 修复文档链接

```
用户: 我把 docs/项目介绍.md 重命名为 docs/README.md

Claude 执行:
1. 分析对话 → 识别文件移动
2. 扫描引用 → 查找所有指向旧路径的链接
3. 计算新路径 → 生成相对路径
4. 批量更新 → 修复所有链接
```

## 注意事项

1. **备份机制** - 修改前自动创建 `.backup` 文件
2. **用户确认** - 重大变更前向用户展示预览
3. **增量更新** - 支持多次对话累积更新
4. **格式保持** - 保持原有 Markdown 格式和风格
5. **链接同步** - 文件移动时自动更新交叉引用

## 相关文件

- `scripts/update-links.js` - 链接更新脚本
- `scripts/merge-content.js` - 内容合并脚本
- `scripts/analyze-history.js` - 对话历史分析脚本
