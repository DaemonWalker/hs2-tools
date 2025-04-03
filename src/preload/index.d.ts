import { ElectronAPI } from '@electron-toolkit/preload'

declare global {
  interface Window {
    electron: ElectronAPI
    api: unknown
    download: {
      onDownloadProgress: (callback: (args: unknown) => void) => void
      onDownloadComplete: (callback: (args: unknown) => void) => void
    }
  }
}
