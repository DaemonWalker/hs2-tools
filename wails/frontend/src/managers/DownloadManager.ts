import ipcHandler from '@renderer/logic/ipcUtils'

// 下载任务状态
type DownloadStatus = 'pending' | 'downloading' | 'completed' | 'error' | 'cancelled'

// 下载任务信息
export interface DownloadTask {
  id: string // 唯一标识 (使用 guid)
  name: string // 显示名称
  url: string // 下载 URL
  status: DownloadStatus
  progress: {
    downloaded: number
    total: number
    percent: number
    speed: number
  }
  error?: string // 错误信息
  outputPath?: string // 下载完成后的路径
  startTime: number // 开始时间戳
  endTime?: number // 结束时间戳
}

// 监听器类型
type TaskListener = (tasks: Map<string, DownloadTask>) => void
type TaskUpdateListener = (task: DownloadTask) => void

/**
 * DownloadManager - 下载任务管理器
 * 单例模式，管理所有下载任务的生命周期
 */
class DownloadManager {
  private tasks: Map<string, DownloadTask> = new Map()
  private listeners: Set<TaskListener> = new Set()
  private updateListeners: Map<string, Set<TaskUpdateListener>> = new Map()
  private isIpcSetup = false

  // 单例模式
  private static instance: DownloadManager
  static getInstance(): DownloadManager {
    if (!DownloadManager.instance) {
      DownloadManager.instance = new DownloadManager()
    }
    return DownloadManager.instance
  }

  private constructor() {
    this.setupIpcListeners()
  }

  /**
   * 设置 IPC 事件监听
   */
  private setupIpcListeners(): void {
    if (this.isIpcSetup) return

    // 监听进度更新
    ipcHandler.onDownloadProgress((event) => {
      const { guid, type, downloaded = 0, total = -1, speed = 0, percent = 0 } = event

      const task = this.tasks.get(guid)
      if (!task) return

      if (type === 'progress') {
        task.progress = {
          downloaded,
          total,
          speed,
          percent
        }
        this.notifyTaskUpdate(task)
      } else if (type === 'error') {
        task.status = 'error'
        task.error = event.message || 'Download failed'
        task.endTime = Date.now()
        this.notifyTaskUpdate(task)
        this.notifyListeners()
      }
    })

    // 监听完成事件
    ipcHandler.onDownloadComplete((event) => {
      const { guid, success, message } = event

      const task = this.tasks.get(guid)
      if (!task) return

      if (success) {
        task.status = 'completed'
        task.progress.percent = 100
      } else {
        task.status = 'error'
        task.error = message || 'Download failed'
      }
      task.endTime = Date.now()

      this.notifyTaskUpdate(task)
      this.notifyListeners()
    })

    this.isIpcSetup = true
  }

  /**
   * 开始下载任务
   * @param guid 任务唯一标识
   * @param name 显示名称
   * @param url 下载 URL
   * @returns 是否成功启动
   */
  async download(guid: string, name: string, url: string): Promise<boolean> {
    console.log(`[DownloadManager] download() called with guid=${guid}, name=${name}, url=${url}`)
    
    // 检查是否已存在进行中的任务
    const existingTask = this.tasks.get(guid)
    console.log(`[DownloadManager] existingTask:`, existingTask)
    
    if (existingTask && existingTask.status === 'downloading') {
      console.log(`[DownloadManager] Task ${guid} is already downloading, returning false`)
      return false
    }

    // 创建新任务
    const task: DownloadTask = {
      id: guid,
      name,
      url,
      status: 'downloading',
      progress: {
        downloaded: 0,
        total: -1,
        percent: 0,
        speed: 0
      },
      startTime: Date.now()
    }

    this.tasks.set(guid, task)
    this.notifyListeners()
    this.notifyTaskUpdate(task)
    console.log(`[DownloadManager] Task ${guid} created and listeners notified`)

    try {
      console.log(`[DownloadManager] Calling ipcHandler.triggerDownload with name=${guid}, url=${url}`)
      // 调用 IPC 开始下载（Wails 中返回 void，无异常即表示成功启动）
      await ipcHandler.triggerDownload({ name: guid, url })
      console.log(`[DownloadManager] Download started successfully for ${guid}`)
      return true
    } catch (error) {
      console.error(`[DownloadManager] Failed to start download: ${guid}`, error)
      task.status = 'error'
      task.error = error instanceof Error ? error.message : String(error)
      task.endTime = Date.now()
      this.notifyTaskUpdate(task)
      this.notifyListeners()
      return false
    }
  }

  /**
   * 取消下载任务
   * @param taskId 任务 ID
   * @returns 是否成功取消
   */
  async cancel(taskId: string): Promise<boolean> {
    const task = this.tasks.get(taskId)
    if (!task || task.status !== 'downloading') {
      return false
    }

    try {
      const success = await ipcHandler.cancelDownload(taskId)
      if (success) {
        task.status = 'cancelled'
        task.endTime = Date.now()
        this.notifyTaskUpdate(task)
        this.notifyListeners()
      }
      return success
    } catch (error) {
      console.error(`[DownloadManager] Failed to cancel download: ${taskId}`, error)
      return false
    }
  }

  /**
   * 取消所有下载任务
   */
  cancelAll(): void {
    for (const [guid, task] of this.tasks) {
      if (task.status === 'downloading') {
        this.cancel(guid)
      }
    }
  }

  /**
   * 重试失败/取消的任务
   * @param taskId 任务 ID
   */
  async retry(taskId: string): Promise<boolean> {
    const task = this.tasks.get(taskId)
    if (!task) return false

    if (task.status === 'downloading') {
      return false
    }

    // 重置状态
    task.status = 'downloading'
    task.error = undefined
    task.progress = {
      downloaded: 0,
      total: -1,
      percent: 0,
      speed: 0
    }
    task.startTime = Date.now()
    task.endTime = undefined

    this.notifyListeners()
    this.notifyTaskUpdate(task)

    try {
      await ipcHandler.triggerDownload({ name: taskId, url: task.url })
      return true
    } catch (error) {
      console.error(`[DownloadManager] Failed to retry download: ${taskId}`, error)
      task.status = 'error'
      task.error = error instanceof Error ? error.message : String(error)
      task.endTime = Date.now()
      this.notifyTaskUpdate(task)
      this.notifyListeners()
      return false
    }
  }

  /**
   * 清除已完成的任务
   */
  clearCompleted(): void {
    for (const [guid, task] of this.tasks) {
      if (task.status === 'completed' || task.status === 'cancelled') {
        this.tasks.delete(guid)
      }
    }
    this.notifyListeners()
  }

  /**
   * 获取指定任务
   */
  getTask(taskId: string): DownloadTask | undefined {
    return this.tasks.get(taskId)
  }

  /**
   * 获取所有任务
   */
  getAllTasks(): DownloadTask[] {
    return Array.from(this.tasks.values())
  }

  /**
   * 获取进行中的任务
   */
  getActiveTasks(): DownloadTask[] {
    return this.getAllTasks().filter((t) => t.status === 'downloading')
  }

  /**
   * 获取已完成的任务
   */
  getCompletedTasks(): DownloadTask[] {
    return this.getAllTasks().filter((t) => t.status === 'completed')
  }

  /**
   * 获取失败的任务
   */
  getFailedTasks(): DownloadTask[] {
    return this.getAllTasks().filter((t) => t.status === 'error')
  }

  /**
   * 订阅所有任务变化
   * @returns 取消订阅函数
   */
  subscribe(listener: TaskListener): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  /**
   * 订阅特定任务变化
   * @returns 取消订阅函数
   */
  subscribeToTask(taskId: string, listener: TaskUpdateListener): () => void {
    if (!this.updateListeners.has(taskId)) {
      this.updateListeners.set(taskId, new Set())
    }
    this.updateListeners.get(taskId)!.add(listener)

    return () => {
      const listeners = this.updateListeners.get(taskId)
      if (listeners) {
        listeners.delete(listener)
        if (listeners.size === 0) {
          this.updateListeners.delete(taskId)
        }
      }
    }
  }

  /**
   * 通知所有监听器
   */
  private notifyListeners(): void {
    for (const listener of this.listeners) {
      listener(this.tasks)
    }
  }

  /**
   * 通知特定任务的监听器
   */
  private notifyTaskUpdate(task: DownloadTask): void {
    const listeners = this.updateListeners.get(task.id)
    if (listeners) {
      for (const listener of listeners) {
        listener(task)
      }
    }
  }

  /**
   * 计算总下载速度
   */
  getTotalSpeed(): number {
    return this.getActiveTasks().reduce((sum, task) => sum + task.progress.speed, 0)
  }

  /**
   * 计算整体进度（加权平均）
   */
  getOverallProgress(): number {
    const activeTasks = this.getActiveTasks()
    if (activeTasks.length === 0) return 0

    const totalPercent = activeTasks.reduce((sum, task) => sum + task.progress.percent, 0)
    return totalPercent / activeTasks.length
  }
}

// 导出单例实例
export const downloadManager = DownloadManager.getInstance()
export default downloadManager
