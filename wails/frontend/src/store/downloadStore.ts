import { create } from 'zustand'
import { useShallow } from 'zustand/shallow'
import { formatBytes, formatSpeed, formatTime, estimateRemainingTime } from '@renderer/utils/format'

export { formatBytes, formatSpeed, formatTime, estimateRemainingTime }

/**
 * 单个下载任务的进度信息
 */
export interface DownloadTaskProgress {
  guid: string
  name: string
  downloaded: number
  total: number
  speed: number
  percent: number
  status: 'pending' | 'downloading' | 'completed' | 'error'
  message?: string
}

/**
 * 下载状态
 */
interface DownloadState {
  // 批量下载信息
  totalCount: number
  currentIndex: number
  isBatchDownloading: boolean

  // 当前活跃的任务
  currentTask: DownloadTaskProgress | null

  // 所有任务的历史记录
  tasks: Record<string, DownloadTaskProgress>

  // 操作方法
  startBatch: (total: number) => void
  startTask: (guid: string, name: string) => void
  updateProgress: (
    guid: string,
    progress: { downloaded: number; total: number; speed: number; percent: number }
  ) => void
  completeTask: (guid: string, success: boolean, message?: string) => void
  nextTask: () => void
  reset: () => void
}

export const useDownloadStore = create<DownloadState>((set) => ({
  totalCount: 0,
  currentIndex: 0,
  isBatchDownloading: false,
  currentTask: null,
  tasks: {},

  /**
   * 开始批量下载
   */
  startBatch: (total: number) => {
    set({
      totalCount: total,
      currentIndex: 0,
      isBatchDownloading: true,
      currentTask: null,
      tasks: {}
    })
  },

  /**
   * 开始单个任务
   */
  startTask: (guid: string, name: string) => {
    set((state) => {
      const newTask: DownloadTaskProgress = {
        guid,
        name,
        downloaded: 0,
        total: -1,
        speed: 0,
        percent: 0,
        status: 'downloading'
      }
      return {
        currentTask: newTask,
        tasks: { ...state.tasks, [guid]: newTask }
      }
    })
  },

  /**
   * 更新任务进度
   */
  updateProgress: (guid, progress) => {
    set((state) => {
      const task = state.tasks[guid]
      if (!task) return state

      const updatedTask: DownloadTaskProgress = {
        ...task,
        downloaded: progress.downloaded,
        total: progress.total,
        speed: progress.speed,
        percent: progress.percent
      }

      return {
        currentTask: state.currentTask?.guid === guid ? updatedTask : state.currentTask,
        tasks: { ...state.tasks, [guid]: updatedTask }
      }
    })
  },

  /**
   * 完成任务
   */
  completeTask: (guid, success, message) => {
    set((state) => {
      const task = state.tasks[guid]
      if (!task) return state

      const updatedTask: DownloadTaskProgress = {
        ...task,
        status: success ? 'completed' : 'error',
        percent: success ? 100 : task.percent,
        message
      }

      return {
        currentTask: state.currentTask?.guid === guid ? updatedTask : state.currentTask,
        tasks: { ...state.tasks, [guid]: updatedTask }
      }
    })
  },

  /**
   * 进入下一个任务
   */
  nextTask: () => {
    set((state) => ({
      currentIndex: state.currentIndex + 1,
      currentTask: null
    }))
  },

  /**
   * 重置状态
   */
  reset: () => {
    set({
      totalCount: 0,
      currentIndex: 0,
      isBatchDownloading: false,
      currentTask: null,
      tasks: {}
    })
  }
}))

// Selector hooks for better performance
export const useDownloadCurrentTask = () => useDownloadStore(state => state.currentTask)
export const useDownloadTasks = () => useDownloadStore(useShallow(state => state.tasks))
export const useDownloadBatchStatus = () => useDownloadStore(useShallow(state => ({
  totalCount: state.totalCount,
  currentIndex: state.currentIndex,
  isBatchDownloading: state.isBatchDownloading
})))
export const useDownloadActions = () => useDownloadStore(useShallow(state => ({
  startBatch: state.startBatch,
  startTask: state.startTask,
  updateProgress: state.updateProgress,
  completeTask: state.completeTask,
  nextTask: state.nextTask,
  reset: state.reset
})))
