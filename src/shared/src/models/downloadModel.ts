export interface DownloadModel {
  url: string
  proxy?: string
  dir: string
  name: string
}

export interface DownloadTaskInfo {
  current: number
  total: number
  percent: number
  name: string
}

export type DownloadingInfo = { [id: string]: DownloadTaskInfo }
