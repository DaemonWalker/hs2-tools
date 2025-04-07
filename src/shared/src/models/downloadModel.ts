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

export type DownloadCompleteInfo = Pick<File, 'path'> & Pick<DownloadModel, 'guid'>
