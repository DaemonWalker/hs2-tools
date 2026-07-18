import { useState, useEffect, useCallback, useMemo } from 'react'
import downloadManager, { DownloadTask } from '@renderer/managers/DownloadManager'

/**
 * useDownloadManager Hook 返回值
 */
interface UseDownloadManagerReturn {
  // 任务列表
  tasks: DownloadTask[]
  activeTasks: DownloadTask[]
  completedTasks: DownloadTask[]
  failedTasks: DownloadTask[]

  // 统计
  totalCount: number
  activeCount: number
  completedCount: number
  failedCount: number

  // 全局统计
  totalSpeed: number // 总下载速度
  overallProgress: number // 整体进度

  // 操作方法
  download: (guid: string, name: string, url: string) => Promise<boolean>
  cancel: (taskId: string) => Promise<boolean>
  cancelAll: () => void
  retry: (taskId: string) => Promise<boolean>
  clearCompleted: () => void
}

/**
 * 使用下载管理器的 Hook
 * 订阅所有任务的变化，返回任务列表和操作方法
 */
export function useDownloadManager(): UseDownloadManagerReturn {
  const [, forceUpdate] = useState({})

  // 订阅任务变化
  useEffect(() => {
    const unsubscribe = downloadManager.subscribe(() => {
      forceUpdate({})
    })
    return unsubscribe
  }, [])

  // 获取任务列表 - 每次渲染时重新获取，确保数据最新
  const tasks = downloadManager.getAllTasks()
  const activeTasks = downloadManager.getActiveTasks()
  const completedTasks = downloadManager.getCompletedTasks()
  const failedTasks = downloadManager.getFailedTasks()

  // 统计数据
  const totalCount = tasks.length
  const activeCount = activeTasks.length
  const completedCount = completedTasks.length
  const failedCount = failedTasks.length

  // 全局统计 - 使用 useMemo 缓存计算
  const totalSpeed = useMemo(() => downloadManager.getTotalSpeed(), [tasks])
  const overallProgress = useMemo(() => downloadManager.getOverallProgress(), [tasks])

  // 操作方法
  const download = useCallback(async (guid: string, name: string, url: string): Promise<boolean> => {
    console.log(`[useDownloadManager] download() called with guid=${guid}, name=${name}, url=${url}`)
    const result = await downloadManager.download(guid, name, url)
    console.log(`[useDownloadManager] download() returned: ${result}`)
    return result
  }, [])

  const cancel = useCallback(async (taskId: string): Promise<boolean> => {
    return downloadManager.cancel(taskId)
  }, [])

  const cancelAll = useCallback(() => {
    downloadManager.cancelAll()
  }, [])

  const retry = useCallback(async (taskId: string): Promise<boolean> => {
    return downloadManager.retry(taskId)
  }, [])

  const clearCompleted = useCallback(() => {
    downloadManager.clearCompleted()
    forceUpdate({})
  }, [])

  return {
    tasks,
    activeTasks,
    completedTasks,
    failedTasks,
    totalCount,
    activeCount,
    completedCount,
    failedCount,
    totalSpeed,
    overallProgress,
    download,
    cancel,
    cancelAll,
    retry,
    clearCompleted
  }
}

/**
 * 订阅特定任务的 Hook
 * 用于单个组件只关注一个任务的状态
 * @param taskId 任务 ID
 * @returns 任务信息
 */
export function useTask(taskId: string | undefined): DownloadTask | undefined {
  const [task, setTask] = useState<DownloadTask | undefined>(() =>
    taskId ? downloadManager.getTask(taskId) : undefined
  )

  useEffect(() => {
    if (!taskId) {
      setTask(undefined)
      return
    }

    // 立即获取当前状态
    setTask(downloadManager.getTask(taskId))

    // 订阅任务变化
    const unsubscribe = downloadManager.subscribeToTask(taskId, (updatedTask) => {
      setTask(updatedTask)
    })

    return unsubscribe
  }, [taskId])

  return task
}

export default useDownloadManager
