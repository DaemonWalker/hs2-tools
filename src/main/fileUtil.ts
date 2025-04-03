import { Options } from '@shared/models/getFileOptions'
import * as fs from 'fs'
import * as path from 'path'

const REPLACE_CHARS = ['%', ':', '!', '"', '$']

const NAME_START = Buffer.from('fullname', 'binary')
const NAME_END = Buffer.from('personality', 'binary')

const MOD_START = Buffer.from('ModID', 'binary')
const MOD_END = Buffer.from('Slot', 'binary')

const PNG_END = Buffer.from('IEND', 'binary')

const bufferToString = (buffer: Buffer): string => {
  const result = buffer
    .toString('utf-8')
    .replace(/\uFFFD/g, '')
    .trim()
  if (REPLACE_CHARS.includes(result[0])) {
    return result.substring(1)
  } else {
    return result
  }
}

const searchBuffer = (start: Buffer, end: Buffer, data: Buffer): string[] => {
  const result: string[] = []
  const startLength = start.length
  const endLength = end.length
  let startIndex = data.indexOf(start)
  while (startIndex !== -1) {
    const endIndex = data.indexOf(end)
    const buffer = data.subarray(startIndex + startLength, endIndex)
    const s = bufferToString(buffer)
    result.push(s)
    data = data.subarray(endIndex + endLength)
    startIndex = data.indexOf(start)
  }
  return [...new Set(result.filter((p) => p.length > 0))]
}

export const listAllFiles = (dir: string, options: Options): string[] => {
  if (!fs.existsSync(dir)) {
    return []
  }
  const { excludeDir, targetExtension } = options ?? {}
  let fileList: string[] = []
  const entries = fs.readdirSync(dir, { withFileTypes: true })

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name)
    if (
      entry.isDirectory() &&
      (!excludeDir || !excludeDir.find((p) => entry.name.indexOf(p) > -1))
    ) {
      fileList = fileList.concat(listAllFiles(fullPath, options))
    } else if (!targetExtension || targetExtension.indexOf(path.extname(entry.name)) > -1) {
      fileList.push(fullPath)
    }
  }

  return fileList
}

export const checkTargetDir = (target: string) => {
  // 检查目标文件夹是否存在，不存在则创建
  if (!fs.existsSync(target)) {
    fs.mkdirSync(target, { recursive: true })
  }
}

/**
 * 将文件列表移动到指定目标文件夹
 * @param files 要移动的文件的绝对路径数组
 * @param target 目标文件夹的绝对路径
 */
export const moveFile = (file: string, target: string) => {
  const fileName = path.basename(file)
  const targetPath = path.join(target, fileName)
  fs.renameSync(file, targetPath)
}

export const readAllCharaNames = (path: string): string[] => {
  if (!fileExists(path)) {
    return []
  }

  let data = fs.readFileSync(path)
  return searchBuffer(NAME_START, NAME_END, data)
}

export const readAllMods = (path: string): string[] => {
  if (!fileExists(path)) {
    return []
  }
  let data = fs.readFileSync(path)
  return searchBuffer(MOD_START, MOD_END, data)
}

export const fileExists = (filePath: string): boolean => {
  const exists = fs.existsSync(filePath)
  if (!exists) {
    return false
  }
  return path.extname(filePath) === '.png'
}

export const readPngForShow = (filePath: string): string | undefined => {
  if (!fileExists(filePath)) {
    return undefined
  }
  const buffer = fs.readFileSync(filePath)
  const endIndex = buffer.lastIndexOf(PNG_END)
  if (endIndex === -1) {
    return undefined
  }
  const pngBuffer = buffer.subarray(0, endIndex + PNG_END.length)
  return pngBuffer.toString('base64')
}
