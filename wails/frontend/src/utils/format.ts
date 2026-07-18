/**
 * 格式化字节大小
 */
export const formatBytes = (bytes: number, decimals = 2): string => {
  if (bytes === 0) return '0 B'
  if (bytes < 0) return 'Unknown'

  const k = 1024
  const dm = decimals < 0 ? 0 : decimals
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']

  const i = Math.floor(Math.log(bytes) / Math.log(k))

  return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + ' ' + sizes[i]
}

/**
 * 格式化速度
 */
export const formatSpeed = (bytesPerSecond: number): string => {
  return formatBytes(bytesPerSecond) + '/s'
}

/**
 * 格式化时间
 */
export const formatTime = (seconds: number): string => {
  if (seconds < 0 || !isFinite(seconds)) return '--:--'

  const mins = Math.floor(seconds / 60)
  const secs = Math.floor(seconds % 60)

  if (mins > 99) return '99:59+'

  return `${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`
}

/**
 * 计算预计剩余时间
 */
export const estimateRemainingTime = (
  downloaded: number,
  total: number,
  speed: number
): number => {
  if (total <= 0 || speed <= 0) return -1
  const remaining = total - downloaded
  return remaining / speed
}
