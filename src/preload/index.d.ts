import { ElectronAPI } from '@electron-toolkit/preload'
import { DownloadingInfo } from '@shared/models/downloadModel'

declare global {
  interface Window {
    electron: ElectronAPI & {
      onDownloadProgress: (callback: (args: DownloadingInfo) => void) => void
      onDownloadComplete: (callback: (args: DownloadCompleteInfo) => void) => void
      getAllMods: (callback: (args: string) => void) => void
    }
    api: unknown
  }
}
