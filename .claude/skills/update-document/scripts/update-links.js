/**
 * 文档链接更新脚本
 * 自动扫描和修复 docs/ 目录下的交叉引用链接
 */

const fs = require('fs')
const path = require('path')
const glob = require('glob')

// 计算编辑距离 (Levenshtein distance)
function levenshtein(a, b) {
  const matrix = []
  for (let i = 0; i <= b.length; i++) {
    matrix[i] = [i]
  }
  for (let j = 0; j <= a.length; j++) {
    matrix[0][j] = j
  }
  for (let i = 1; i <= b.length; i++) {
    for (let j = 1; j <= a.length; j++) {
      const cost = b[i - 1] === a[j - 1] ? 0 : 1
      matrix[i][j] = Math.min(
        matrix[i - 1][j] + 1,
        matrix[i][j - 1] + 1,
        matrix[i - 1][j - 1] + cost
      )
    }
  }
  return matrix[b.length][a.length]
}

// 扫描所有 markdown 文件
function scanDocs(docsDir = 'docs') {
  const pattern = path.join(docsDir, '**/*.md')
  return glob.sync(pattern)
}

// 提取 markdown 链接
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

// 检查链接是否有效
function validateLink(sourceFile, targetPath, allFiles) {
  // 忽略外部链接
  if (targetPath.startsWith('http://') || targetPath.startsWith('https://')) {
    return { valid: true, type: 'external' }
  }

  // 忽略锚点链接
  if (targetPath.startsWith('#')) {
    return { valid: true, type: 'anchor' }
  }

  // 计算绝对路径
  const absolutePath = path.resolve(path.dirname(sourceFile), targetPath)

  if (fs.existsSync(absolutePath)) {
    return { valid: true, type: 'internal' }
  }

  // 尝试查找匹配的文件
  const targetName = path.basename(targetPath)

  // 策略 1: 精确文件名匹配
  const exactMatch = allFiles.find(f => path.basename(f) === targetName)
  if (exactMatch) {
    return {
      valid: false,
      type: 'moved',
      suggestion: exactMatch
    }
  }

  // 策略 2: 相似度匹配
  const candidates = allFiles.map(f => ({
    file: f,
    distance: levenshtein(targetPath, path.relative('docs', f))
  })).sort((a, b) => a.distance - b.distance)

  if (candidates.length > 0 && candidates[0].distance < 10) {
    return {
      valid: false,
      type: 'similar',
      suggestion: candidates[0].file
    }
  }

  return { valid: false, type: 'broken' }
}

// 计算新的相对路径
function computeNewRelativePath(sourceFile, newTarget) {
  const relativePath = path.relative(path.dirname(sourceFile), newTarget)
  return relativePath.replace(/\\/g, '/')
}

// 主函数
function main() {
  const docsDir = process.argv[2] || 'docs'

  console.log(`🔍 扫描文档目录: ${docsDir}`)

  const files = scanDocs(docsDir)
  console.log(`📄 发现 ${files.length} 个 markdown 文件`)

  const allLinks = []
  const issues = []

  // 收集所有链接
  for (const file of files) {
    const content = fs.readFileSync(file, 'utf-8')
    const links = extractLinks(content)

    for (const link of links) {
      const validation = validateLink(file, link.target, files)

      allLinks.push({
        source: file,
        ...link,
        ...validation
      })

      if (!validation.valid) {
        issues.push({
          source: file,
          text: link.text,
          oldTarget: link.target,
          ...validation
        })
      }
    }
  }

  console.log(`🔗 发现 ${allLinks.length} 个链接`)
  console.log(`⚠️  发现 ${issues.length} 个问题链接`)

  // 生成报告
  if (issues.length > 0) {
    console.log('\n📋 问题链接列表:')
    console.log('| 源文件 | 链接文本 | 当前目标 | 问题类型 | 建议修复 |')
    console.log('|--------|----------|----------|----------|----------|')

    for (const issue of issues) {
      const fix = issue.suggestion
        ? computeNewRelativePath(issue.source, issue.suggestion)
        : '手动修复'
      console.log(`| ${path.relative(docsDir, issue.source)} | ${issue.text} | ${issue.oldTarget} | ${issue.type} | ${fix} |`)
    }
  }

  // 返回结果供其他工具使用
  return {
    totalFiles: files.length,
    totalLinks: allLinks.length,
    issueCount: issues.length,
    issues,
    allLinks
  }
}

// 执行修复
function fixLinks(docsDir = 'docs') {
  const result = main()

  if (result.issueCount === 0) {
    console.log('\n✅ 所有链接正常，无需修复')
    return
  }

  console.log('\n🔧 开始自动修复...')

  const fixed = []

  for (const issue of result.issues) {
    if (issue.suggestion) {
      const newRelative = computeNewRelativePath(issue.source, issue.suggestion)

      // 读取文件内容
      let content = fs.readFileSync(issue.source, 'utf-8')

      // 创建备份
      const backupPath = issue.source + '.backup'
      if (!fs.existsSync(backupPath)) {
        fs.writeFileSync(backupPath, content)
      }

      // 替换链接
      const oldLink = `[${issue.text}](${issue.oldTarget})`
      const newLink = `[${issue.text}](${newRelative})`
      content = content.replace(oldLink, newLink)

      // 写回文件
      fs.writeFileSync(issue.source, content)

      fixed.push({
        file: issue.source,
        old: issue.oldTarget,
        new: newRelative
      })

      console.log(`✅ 修复: ${path.relative(docsDir, issue.source)} - ${issue.oldTarget} → ${newRelative}`)
    }
  }

  console.log(`\n📊 修复完成: ${fixed.length}/${result.issueCount} 个问题已自动修复`)

  if (fixed.length < result.issueCount) {
    console.log('⚠️  剩余问题需要手动检查和修复')
  }

  return fixed
}

module.exports = { main, fixLinks, scanDocs, extractLinks, validateLink }

// 如果直接运行脚本
if (require.main === module) {
  const action = process.argv[2] || 'check'
  const docsDir = process.argv[3] || 'docs'

  if (action === 'fix') {
    fixLinks(docsDir)
  } else {
    main(docsDir)
  }
}
