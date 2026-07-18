import { useEffect, useCallback, useRef } from 'react'
import ipcHandler from '@renderer/logic/ipcUtils'

/**
 * Go 下载器进度消息
 */
export interface DownloadProgress {
  type: 'progress' | 'complete' | 'error' | 'info'
  downloaded: number
  total: number
  speed: number
  percent: number
  message?: string
}

/**
 * 下载任务信息
 */
export interface DownloadTask {
  guid: string
  name: string
  progress: DownloadProgress
}

/**
 * 使用下载进度的 Hook
 * 
 * @example
 * ```tsx
 * const { tasks, registerDownload, cancelDownload } = useDownloadProgress({
 *   onProgress: (guid, progress) => console.log(guid, progress.percent),
 *   onComplete: (guid, success) => console.log(guid, success ? '完成' : '失败')
 * })
 * ```
 */
export const useDownloadProgress = (options?: {
  onProgress?: (guid: string, progress: DownloadProgress) => void
  onComplete?: (guid: string, success: boolean, message?: string) => void
  onError?: (guid: string, message: string) => void
}) => {
  const tasksRef = useRef<Map<string, DownloadTask>>(new Map())

  // 使用 ref 保存 callbacks，避免每次渲染都重新注册监听器
  const callbacksRef = useRef(options)
  callbacksRef.current = options

  // 设置 IPC 事件监听
  useEffect(() => {
    // 监听进度更新
    const removeProgressListener = ipcHandler.onDownloadProgress((event) => {
      const { guid, type, downloaded = 0, total = -1, speed = 0, percent = 0, message } = event

      if (type === 'progress') {
        // 更新任务进度
        const task = tasksRef.current.get(guid)
        if (task) {
          task.progress = { type, downloaded, total, speed, percent }
        }

        callbacksRef.current?.onProgress?.(guid, { type, downloaded, total, speed, percent })
      } else if (type === 'error') {
        callbacksRef.current?.onError?.(guid, message || 'Unknown error')
        tasksRef.current.delete(guid)
      }
    })

    // 监听完成事件
    const removeCompleteListener = ipcHandler.onDownloadComplete((event) => {
      const { guid, success, message } = event
      callbacksRef.current?.onComplete?.(guid, success, message)
      tasksRef.current.delete(guid)
    })

    return () => {
      removeProgressListener()
      removeCompleteListener()
    }
  }, [])

  /**
   * 注册新的下载任务
   */
  const registerDownload = useCallback((guid: string, name: string) => {
    tasksRef.current.set(guid, {
      guid,
      name,
      progress: {
        type: 'progress',
        downloaded: 0,
        total: -1,
        speed: 0,
        percent: 0
      }
    })
  }, [])

  /**
   * 取消下载
   */
  const cancelDownload = useCallback(async (guid: string) => {
    const success = await ipcHandler.cancelDownload(guid)
    if (success) {
      tasksRef.current.delete(guid)
    }
    return success
  }, [])

  /**
   * 获取所有活跃任务
   */
  const getActiveTasks = useCallback((): DownloadTask[] => {
    return Array.from(tasksRef.current.values())
  }, [])

  /**
   * 获取指定任务的进度
   */
  const getTaskProgress = useCallback((guid: string): DownloadProgress | undefined => {
    return tasksRef.current.get(guid)?.progress
  }, [])

  return {
    registerDownload,
    cancelDownload,
    getActiveTasks,
    getTaskProgress
  }
}

export default useDownloadProgress
