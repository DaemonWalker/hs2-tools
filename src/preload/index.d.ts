import { ElectronAPI } from '@electron-toolkit/preload'
import { DownloadingInfo } from '@shared/models/downloadModel'
import { bridgeHandlers } from './index'
declare global {
  interface Window {
    electron: ElectronAPI
    bridge: typeof bridgeHandlers
    api: unknown
  }
}
