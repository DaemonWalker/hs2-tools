export const pathJoin = (...parts: string[]) => {
  const sep = '/'
  return parts.join(sep).replace(new RegExp(sep + '{1,}', 'g'), sep)
}

// 从完整路径获取文件名（不含扩展名）
export const getFileName = (filePath: string): string => {
  const sep = filePath.includes('\\') ? '\\' : '/'
  const parts = filePath.split(sep)
  const fileNameWithExt = parts[parts.length - 1] || ''
  return fileNameWithExt.replace(/\.[^/.]+$/, '')
}

// 从完整路径获取文件名（含扩展名）
export const getFileNameWithExt = (filePath: string): string => {
  const sep = filePath.includes('\\') ? '\\' : '/'
  const parts = filePath.split(sep)
  return parts[parts.length - 1] || ''
}

