/**
 * 基于对话历史的文档更新脚本
 * 智能分析对话内容，自动更新目标文档
 */

const fs = require('fs')
const path = require('path')
const glob = require('glob')

// ============ 对话历史分析 ============

/**
 * 从对话历史中提取变更信息
 */
function analyzeConversation(messages) {
  const changes = []
  const additions = []
  const deletions = []
  const fileOperations = []

  for (const msg of messages) {
    const text = typeof msg === 'string' ? msg : msg.content || ''

    // 识别文件操作
    const fileOpPatterns = [
      { pattern: /重命名[\s了]*['"]?([^'"\n]+)['"]?[\s为]+['"]?([^'"\n]+)['"]?/g, type: 'rename' },
      { pattern: /移[动除][\s了]*['"]?([^'"\n]+)['"]?[\s到]+['"]?([^'"\n]+)['"]?/g, type: 'move' },
      { pattern: /删除[\s了]*['"]?([^'"\n]+)['"]?(?:\s|$)/g, type: 'delete' },
      { pattern: /(?:创建|新增|添加)[\s了]*['"]?([^'"\n]+)['"]?(?:\s*(?:文件|目录|文件夹))?/g, type: 'create' }
    ]

    for (const { pattern, type } of fileOpPatterns) {
      let match
      while ((match = pattern.exec(text)) !== null) {
        fileOperations.push({
          type,
          source: match[1]?.trim(),
          target: match[2]?.trim(),
          fullMatch: match[0]
        })
      }
    }

    // 识别代码变更
    const changePatterns = [
      /(?:修改|更新|调整)[\s了]*(.+?)(?:的|中的)?(.+?)(?:功能|逻辑|代码|实现)/,
      /(?:实现|添加|支持)[\s了]*(.+?)(?:功能|特性|接口|方法)/,
      /(?:重构|优化)[\s了]*(.+?)(?:代码|逻辑|结构)/
    ]

    for (const pattern of changePatterns) {
      const match = text.match(pattern)
      if (match) {
        changes.push({
          action: match[0],
          target: match[1]?.trim(),
          detail: match[2]?.trim(),
          context: text.substring(Math.max(0, match.index - 50), match.index + match[0].length + 50)
        })
      }
    }

    // 识别新增内容
    if (/添加|新增|实现|引入|创建/.test(text)) {
      const featureMatch = text.match(/(?:添加|新增|实现|引入|创建)[\s了]*(.+?)(?:功能|模块|组件|接口|页面)/)
      if (featureMatch) {
        additions.push({
          description: featureMatch[0],
          feature: featureMatch[1]?.trim(),
          context: text
        })
      }
    }
  }

  return { changes, additions, deletions, fileOperations }
}

/**
 * 根据变更信息确定目标文档
 */
function identifyTargetDocs(analysis, docsDir = 'docs') {
  const targets = new Set()
  const { changes, additions, fileOperations } = analysis

  // 根据文件路径推断文档
  const allChanges = [...changes, ...additions, ...fileOperations]

  for (const change of allChanges) {
    const targetPath = change.target || change.source || ''

    // API 相关
    if (/api|接口|endpoint|handler/i.test(targetPath)) {
      targets.add(path.join(docsDir, 'API参考'))
    }

    // 组件相关
    if (/component|组件|页面|tsx|jsx|vue/i.test(targetPath)) {
      targets.add(path.join(docsDir, '前端开发/组件规范.md'))
    }

    // Go 工具相关
    if (/go_tools|downloader|scanner|sideloader/i.test(targetPath)) {
      targets.add(path.join(docsDir, 'Go工具'))
    }

    // 状态管理
    if (/store|状态|redux|zustand/i.test(targetPath)) {
      targets.add(path.join(docsDir, '前端开发/状态管理.md'))
    }

    // 功能模块
    if (/mod|角色卡|场景|下载|扫描/i.test(targetPath)) {
      targets.add(path.join(docsDir, '功能模块'))
    }

    // 架构相关
    if (/架构|设计|结构|重构/i.test(targetPath)) {
      targets.add(path.join(docsDir, '架构设计/整体架构.md'))
    }

    // 构建/配置
    if (/vite|webpack|electron|build|配置/i.test(targetPath)) {
      targets.add(path.join(docsDir, '开发指南/构建配置.md'))
    }
  }

  return Array.from(targets)
}

// ============ 文档更新逻辑 ============

/**
 * 扫描文档目录
 */
function scanDocs(docsDir = 'docs') {
  const pattern = path.join(docsDir, '**/*.md')
  return glob.sync(pattern)
}

/**
 * 提取 markdown 链接
 */
function extractLinks(content) {
  const regex = /\[([^\]]+)\]\(([^)]+)\)/g
  const links = []
  let match
  while ((match = regex.exec(content)) !== null) {
    links.push({
      text: match[1],
      target: match[2],
      index: match.index,
      length: match[0].length
    })
  }
  return links
}

/**
 * 计算新的相对路径
 */
function computeNewRelativePath(sourceFile, newTarget) {
  const relativePath = path.relative(path.dirname(sourceFile), newTarget)
  return relativePath.replace(/\\/g, '/')
}

/**
 * 根据文件操作更新链接
 */
function updateLinksForFileOperation(content, operation, sourceFile, allFiles) {
  let updated = content
  const links = extractLinks(content)

  for (const link of links) {
    // 跳过外部链接
    if (link.target.startsWith('http://') || link.target.startsWith('https://')) {
      continue
    }

    // 解析链接的绝对路径
    const linkAbsolute = path.resolve(path.dirname(sourceFile), link.target)
    const linkNormalized = path.normalize(linkAbsolute)
    const opSourceNormalized = path.normalize(operation.source)

    // 检查链接是否指向被操作的文件
    if (linkNormalized.includes(opSourceNormalized) ||
        path.basename(linkNormalized) === path.basename(opSourceNormalized)) {

      let newTarget

      switch (operation.type) {
        case 'rename':
        case 'move':
          // 计算到新路径的相对路径
          newTarget = computeNewRelativePath(sourceFile, operation.target)
          break
        case 'delete':
          // 标记为无效链接
          newTarget = null
          break
        default:
          continue
      }

      if (newTarget !== null) {
        const oldLink = `[${link.text}](${link.target})`
        const newLink = `[${link.text}](${newTarget})`
        updated = updated.replace(oldLink, newLink)
      }
    }
  }

  return updated
}

/**
 * 生成内容更新建议
 */
function generateUpdateSuggestion(analysis, targetDoc) {
  const suggestions = []
  const { changes, additions } = analysis

  const docName = path.basename(targetDoc, '.md')

  // 根据文档类型生成不同的建议
  for (const change of changes) {
    if (targetDoc.includes('API参考')) {
      suggestions.push({
        type: 'append',
        section: '接口列表',
        content: generateApiDoc(change)
      })
    } else if (targetDoc.includes('组件规范')) {
      suggestions.push({
        type: 'append',
        section: '组件说明',
        content: generateComponentDoc(change)
      })
    } else if (targetDoc.includes('Go工具')) {
      suggestions.push({
        type: 'append',
        section: docName,
        content: generateToolDoc(change)
      })
    } else if (targetDoc.includes('功能模块')) {
      suggestions.push({
        type: 'append',
        section: '功能说明',
        content: generateFeatureDoc(change)
      })
    }
  }

  for (const addition of additions) {
    suggestions.push({
      type: 'append',
      section: '更新日志',
      content: `- ${addition.description}\n`
    })
  }

  return suggestions
}

/**
 * 生成 API 文档片段
 */
function generateApiDoc(change) {
  return `
### ${change.target || '新接口'}

${change.detail || '暂无详细说明'}

**变更说明:**
${change.action}

---
`
}

/**
 * 生成组件文档片段
 */
function generateComponentDoc(change) {
  return `
#### ${change.target || '新组件'}

${change.detail || '组件功能说明'}

**实现变更:**
${change.action}

---
`
}

/**
 * 生成工具文档片段
 */
function generateToolDoc(change) {
  return `
### ${change.target || '新功能'}

${change.detail || '工具功能说明'}

---
`
}

/**
 * 生成功能文档片段
 */
function generateFeatureDoc(change) {
  return `
### ${change.target || '新特性'}

${change.detail || '功能说明'}

---
`
}

/**
 * 应用内容更新
 */
function applyContentUpdate(content, suggestions) {
  let updated = content

  for (const suggestion of suggestions) {
    switch (suggestion.type) {
      case 'append':
        updated = appendToSection(updated, suggestion.section, suggestion.content)
        break
      case 'replace':
        updated = updated.replace(suggestion.target, suggestion.content)
        break
      case 'insert':
        const pos = updated.indexOf(suggestion.position)
        if (pos !== -1) {
          updated = updated.slice(0, pos) + suggestion.content + updated.slice(pos)
        }
        break
    }
  }

  return updated
}

/**
 * 在指定章节末尾追加内容
 */
function appendToSection(content, sectionTitle, newContent) {
  // 匹配章节标题 (支持不同级别)
  const sectionPatterns = [
    new RegExp(`(## ${escapeRegex(sectionTitle)}.*?)(\\n## |\\n### |$)`, 's'),
    new RegExp(`(### ${escapeRegex(sectionTitle)}.*?)(\\n## |\\n### |$)`, 's'),
    new RegExp(`(${escapeRegex(sectionTitle)}.*?)(\\n## |\\n### |$)`, 's')
  ]

  for (const pattern of sectionPatterns) {
    if (pattern.test(content)) {
      return content.replace(pattern, `$1\n${newContent}$2`)
    }
  }

  // 如果没找到章节，在文件末尾追加
  return content + `\n\n## ${sectionTitle}\n\n${newContent}\n`
}

/**
 * 转义正则特殊字符
 */
function escapeRegex(string) {
  return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

// ============ 主函数 ============

/**
 * 基于对话历史更新文档
 */
function updateFromConversation(conversationHistory, options = {}) {
  const docsDir = options.docsDir || 'docs'
  const dryRun = options.dryRun || false

  console.log('🔍 分析对话历史...')

  // 分析对话
  const analysis = analyzeConversation(conversationHistory)
  console.log(`  - 发现 ${analysis.changes.length} 处代码变更`)
  console.log(`  - 发现 ${analysis.additions.length} 处新增内容`)
  console.log(`  - 发现 ${analysis.fileOperations.length} 个文件操作`)

  // 确定目标文档
  const targetDocs = identifyTargetDocs(analysis, docsDir)
  console.log(`\n📄 识别到 ${targetDocs.length} 个可能需要更新的文档:`)
  targetDocs.forEach(doc => console.log(`  - ${doc}`))

  // 扫描现有文档
  const allDocs = scanDocs(docsDir)

  // 处理文件操作导致的链接更新
  const linkUpdates = []
  if (analysis.fileOperations.length > 0) {
    console.log('\n🔗 检查链接更新...')

    for (const operation of analysis.fileOperations) {
      if (operation.type === 'rename' || operation.type === 'move') {
        for (const doc of allDocs) {
          const content = fs.readFileSync(doc, 'utf-8')
          const updated = updateLinksForFileOperation(content, operation, doc, allDocs)

          if (content !== updated) {
            linkUpdates.push({
              doc,
              operation,
              changed: true
            })

            if (!dryRun) {
              // 创建备份
              const backupPath = doc + '.backup'
              if (!fs.existsSync(backupPath)) {
                fs.writeFileSync(backupPath, content)
              }
              fs.writeFileSync(doc, updated)
            }
          }
        }
      }
    }

    console.log(`  - 更新了 ${linkUpdates.length} 个文档中的链接`)
  }

  // 生成内容更新建议
  const contentSuggestions = []
  for (const targetDoc of targetDocs) {
    if (fs.existsSync(targetDoc)) {
      const stat = fs.statSync(targetDoc)
      if (stat.isDirectory()) {
        // 如果是目录，查找最匹配的文档
        const docsInDir = glob.sync(path.join(targetDoc, '*.md'))
        for (const doc of docsInDir) {
          const suggestions = generateUpdateSuggestion(analysis, doc)
          if (suggestions.length > 0) {
            contentSuggestions.push({ doc, suggestions })
          }
        }
      } else {
        const suggestions = generateUpdateSuggestion(analysis, targetDoc)
        if (suggestions.length > 0) {
          contentSuggestions.push({ doc: targetDoc, suggestions })
        }
      }
    }
  }

  console.log(`\n📝 生成 ${contentSuggestions.length} 个内容更新建议`)

  // 生成报告
  return {
    analysis,
    targetDocs,
    linkUpdates,
    contentSuggestions,
    dryRun
  }
}

/**
 * 生成更新报告
 */
function generateReport(result) {
  const { analysis, targetDocs, linkUpdates, contentSuggestions, dryRun } = result

  let report = `
## 文档更新报告

### 分析结果
- 对话中提取的变更: ${analysis.changes.length} 项
- 识别目标文档: ${targetDocs.length} 个
- 链接更新: ${linkUpdates.length} 个文档
- 内容建议: ${contentSuggestions.length} 个
${dryRun ? '- ⚠️ 干运行模式，未实际应用变更' : ''}

`

  if (linkUpdates.length > 0) {
    report += `### 链接更新详情\n\n| 文档 | 操作类型 | 变更文件 |\n|------|---------|----------|\n`
    for (const update of linkUpdates) {
      report += `| ${path.relative('docs', update.doc)} | ${update.operation.type} | ${update.operation.source} |\n`
    }
    report += '\n'
  }

  if (contentSuggestions.length > 0) {
    report += `### 内容更新建议\n\n| 目标文档 | 建议操作 |\n|---------|---------|\n`
    for (const item of contentSuggestions) {
      const ops = item.suggestions.map(s => s.type).join(', ')
      report += `| ${path.relative('docs', item.doc)} | ${ops} |\n`
    }
    report += '\n'

    report += `### 详细建议\n\n`
    for (const item of contentSuggestions) {
      report += `**${path.relative('docs', item.doc)}**\n\n`
      for (const suggestion of item.suggestions) {
        report += `- ${suggestion.type} 到 "${suggestion.section}" 章节\n`
      }
      report += '\n'
    }
  }

  report += `### 建议操作\n\n`
  if (dryRun) {
    report += `- [ ] 检查更新建议是否合理\n- [ ] 运行正式更新（去掉 dryRun 选项）\n`
  } else {
    report += `- [x] 已自动应用链接更新\n- [ ] 检查内容更新建议，手动确认后应用\n- [ ] 如需恢复，使用 .backup 文件\n`
  }

  return report
}

// ============ 导出 ============

module.exports = {
  analyzeConversation,
  identifyTargetDocs,
  updateFromConversation,
  generateReport,
  updateLinksForFileOperation,
  applyContentUpdate,
  generateUpdateSuggestion
}

// 命令行执行
if (require.main === module) {
  const args = process.argv.slice(2)
  const command = args[0] || 'analyze'

  if (command === 'analyze') {
    // 分析模式：从 stdin 读取对话历史
    let input = ''
    process.stdin.setEncoding('utf-8')
    process.stdin.on('data', chunk => input += chunk)
    process.stdin.on('end', () => {
      try {
        const messages = JSON.parse(input)
        const result = updateFromConversation(messages, { dryRun: true })
        console.log(generateReport(result))
      } catch (e) {
        console.error('解析输入失败:', e.message)
        console.error('请提供 JSON 格式的对话历史')
      }
    })
  } else if (command === 'update') {
    // 更新模式
    let input = ''
    process.stdin.setEncoding('utf-8')
    process.stdin.on('data', chunk => input += chunk)
    process.stdin.on('end', () => {
      try {
        const messages = JSON.parse(input)
        const result = updateFromConversation(messages, { dryRun: false })
        console.log(generateReport(result))
      } catch (e) {
        console.error('解析输入失败:', e.message)
      }
    })
  } else {
    console.log(`
用法: node update-from-conversation.js [command]

命令:
  analyze   分析对话历史，生成更新建议（干运行）
  update    分析并应用更新

示例:
  echo '[{"content":"重命名了文件"}]' | node update-from-conversation.js analyze
    `)
  }
}
