import { useSettingStore } from '@renderer/store/settingStore'
import { EXE_NAME } from '@shared/constants'
import { Options } from '@shared/models/getFileOptions'
import { ModModel } from '@shared/models/modModel'
import { ProxyInfo } from '@shared/models/proxyInfo'
import {
  ReadDir, ReadPngForMod, ReadPngForShow, SelectPath,
  LoadSettings, SaveSettings, LoadLocalMods, SaveLocalMods, GetAllFiles,
  ReadZipMod, ReadZipModBatch,
  ReadPngModsBatch, ReadPngNamesBatch, ReadPngImagesBatch, ReadPngPageDataBatch,
  MoveFile, CheckTargetDir, FileExists, DirExists,
  OpenFileSelector, ReadAllCharaNames,
  TriggerDownload, CancelDownload,
  GetDownloaderStatus, GetScannerStatus, GetSideloaderStatus,
  RunSideloader, StopSideloader, IsSideloaderRunning,
  InitSideload,
  DisableWindowsSleep, EnableWindowsSleep,
  SetProxy,
  LaunchGame, LaunchStudio,
  OpenInFolder,
  Log,
  Ping
} from '@wailsjs/go/main/App'
import { EventsOn, EventsOff } from '@wailsjs/runtime'

// Go 下载器相关类型
export type DownloadProgressEvent = {
  guid: string
  type: 'progress' | 'complete' | 'error' | 'info'
  downloaded?: number
  total?: number
  speed?: number
  percent?: number
  path?: string
  message?: string
}

export type DownloadCompleteEvent = {
  guid: string
  success: boolean
  message?: string
}

export type DownloaderStatus = {
  goDownloaderAvailable: boolean
  goDownloaderPath: string
}

export type ScannerStatus = {
  scannerAvailable: boolean
  scannerPath: string
  version: string
}

// Sideloader 相关类型
export type SideloaderProgressEvent = {
  type: 'info' | 'progress' | 'complete' | 'error'
  message?: string
  current?: number
  total?: number
  percent?: number
}

export type SideloaderCompleteEvent = {
  success: boolean
  data?: import('@shared/models/sideloadModel').SideloadModel
  error?: string
}

export type SideloaderStoppedEvent = {
  stopped: boolean
}

export type SideloaderStatus = {
  sideloaderAvailable: boolean
  sideloaderPath: string
  version: string
}

// IPC Utils 类型定义
const ipcUtils = {
  readDir: (dirPath: string): Promise<string[]> =>
    ReadDir(dirPath),

  readPngForMod: (filePath: string): Promise<string[]> =>
    ReadPngForMod(filePath),

  readPngForShow: (filePath: string): Promise<string> =>
    ReadPngForShow(filePath),

  selectPath: (): Promise<string | undefined> =>
    SelectPath().then((res: string | undefined) => res?.replace(EXE_NAME, '')),

  loadSettings: (): Promise<{ path: string | undefined }> =>
    LoadSettings().then((res: Record<string, unknown> | null) => (res ? res as { path: string | undefined } : { path: undefined })),

  saveSettings: (setting: unknown): Promise<void> =>
    SaveSettings(setting as Record<string, unknown>),

  loadLocalMods: (): Promise<ModModel> =>
    LoadLocalMods().then((res: Record<string, unknown> | null) => (res ? res as ModModel : {})),

  saveLocalMods: (mods: ModModel): Promise<void> =>
    SaveLocalMods(mods as Record<string, unknown>),

  getAllFiles: (path: string, options: Options = undefined as unknown as Options): Promise<string[]> =>
    GetAllFiles(path, options as unknown as { excludeDir?: string[]; targetExtension?: string[] }),

  readZipMod: (filePath: string): Promise<ModModel | undefined> =>
    ReadZipMod(filePath).then((res: unknown) => res as ModModel),

  readZipModBatch: (filePaths: string[]): Promise<ModModel> =>
    ReadZipModBatch(filePaths).then((res: unknown) => res as ModModel),

  readPngModsBatch: (filePaths: string[]): Promise<Array<{ path: string; modIds: string[] }>> =>
    ReadPngModsBatch(filePaths),

  readPngNamesBatch: (filePaths: string[]): Promise<Array<{ path: string; names: string[] }>> =>
    ReadPngNamesBatch(filePaths),

  readPngImagesBatch: (filePaths: string[]): Promise<Array<{ path: string; imageData: string }>> =>
    ReadPngImagesBatch(filePaths),

  readPngPageDataBatch: (
    filePaths: string[]
  ): Promise<Array<{ path: string; names: string[]; imageData: string }>> =>
    ReadPngPageDataBatch(filePaths),

  moveFile: (file: string, target: string): Promise<void> =>
    MoveFile(file, target),

  checkTargetDir: (target: string): Promise<void> =>
    CheckTargetDir(target),

  readAllCharaNames: (path: string): Promise<string[]> =>
    ReadAllCharaNames(path).then((data: string[]) => (data ? data : [''])),

  fileExists: (path: string): Promise<boolean> =>
    FileExists(path),

  dirExists: (path: string): Promise<boolean> =>
    DirExists(path),

  openFileSelector: (defaultPath?: string): Promise<string | undefined> =>
    OpenFileSelector(defaultPath || ''),

  triggerDownload: (info: { name: string; url: string }): Promise<void> => {
    console.log(`[ipcUtils] triggerDownload called with:`, info)
    const basePath = useSettingStore.getState().modsPath()!
    console.log(`[ipcUtils] basePath from settings: ${basePath}`)
    const downloadDir = `${basePath}/hs2-tool-download`
    console.log(`[ipcUtils] downloadDir: ${downloadDir}`)
    const payload = {
      name: info.name,
      url: info.url,
      dir: downloadDir
    }
    console.log(`[ipcUtils] invoking TriggerDownload with payload:`, payload)
    return TriggerDownload(payload)
  },

  cancelDownload: (guid: string): Promise<boolean> =>
    CancelDownload(guid),

  getDownloaderStatus: (): Promise<DownloaderStatus> =>
    GetDownloaderStatus(),

  getScannerStatus: (): Promise<ScannerStatus> =>
    GetScannerStatus(),

  getSideloaderStatus: (): Promise<SideloaderStatus> =>
    GetSideloaderStatus(),

  runSideloader: (): Promise<import('@shared/models/sideloadModel').SideloadModel | undefined> =>
    RunSideloader().then(() => undefined),

  stopSideloader: (): Promise<boolean> =>
    StopSideloader(),

  isSideloaderRunning: (): Promise<boolean> =>
    IsSideloaderRunning(),

  onSideloaderProgress: (callback: (event: SideloaderProgressEvent) => void): (() => void) => {
    EventsOn('sideloader:progress', callback)
    return () => EventsOff('sideloader:progress')
  },

  onSideloaderComplete: (callback: (event: SideloaderCompleteEvent) => void): (() => void) => {
    EventsOn('sideloader:complete', callback)
    return () => EventsOff('sideloader:complete')
  },

  onSideloaderStopped: (callback: (event: SideloaderStoppedEvent) => void): (() => void) => {
    // Wails 中没有单独的 stopped 事件，使用 complete 事件代替
    const wrapped = (event: SideloaderCompleteEvent) => {
      callback({ stopped: true })
    }
    EventsOn('sideloader:complete', wrapped)
    return () => EventsOff('sideloader:complete')
  },

  onDownloadProgress: (callback: (event: DownloadProgressEvent) => void): (() => void) => {
    EventsOn('download:progress', callback)
    return () => EventsOff('download:progress')
  },

  onDownloadComplete: (callback: (event: DownloadCompleteEvent) => void): (() => void) => {
    EventsOn('download:complete', callback)
    return () => EventsOff('download:complete')
  },

  initSideload: () => InitSideload(),

  log: (...data: any[]) => Log(data),

  ping: () => Ping(),

  disableWindowsSleep: () => DisableWindowsSleep(),

  enableWindowsSleep: (id: number) => EnableWindowsSleep(id),

  setProxy: (proxy: ProxyInfo) => SetProxy(proxy as { uri: string; username: string; password: string }),

  launchGame: (): Promise<{ success: boolean }> =>
    LaunchGame().then(() => ({ success: true })).catch((e: Error) => { throw e }),

  launchStudio: (): Promise<{ success: boolean }> =>
    LaunchStudio().then(() => ({ success: true })).catch((e: Error) => { throw e }),

  openInFolder: (filePath: string): Promise<void> =>
    OpenInFolder(filePath)
}

export default ipcUtils
