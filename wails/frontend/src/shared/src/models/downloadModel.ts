export interface DownloadModel {
  url: string
  proxy?: string
  dir: string
  guid: string
}

export interface DownloadTaskInfo {
  current: number
  total: number
  percent: number
  name: string
}

export type DownloadingInfo = { [id: string]: DownloadTaskInfo }

export type DownloadCompleteInfo = { path: string } & Pick<DownloadModel, 'guid'>

// 下载任务状态
export type DownloadStatus = 'pending' | 'downloading' | 'completed' | 'error' | 'cancelled'

// 下载任务进度
export interface DownloadProgressInfo {
  downloaded: number
  total: number
  percent: number
  speed: number
}

// 下载任务（用于 UI 展示）
export interface DownloadTaskDetail {
  id: string
  name: string
  url: string
  status: DownloadStatus
  progress: DownloadProgressInfo
  error?: string
  outputPath?: string
  startTime: number
  endTime?: number
}
