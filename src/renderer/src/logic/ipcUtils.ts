import { useSettingStore } from '@renderer/store/settingStore'
import { EXE_NAME } from '@shared/constants'
import { DownloadModel } from '@shared/models/downloadModel'
import { Options } from '@shared/models/getFileOptions'
import { ModModel } from '@shared/models/modModel'

export const readDir = (dirPath: string): Promise<string[]> =>
  window.electron.ipcRenderer.invoke('readDir', dirPath)

export const readPngForMod = (filePath: string): Promise<string> =>
  window.electron.ipcRenderer.invoke('readPngForMod', filePath)

export const readPngForShow = (filePath: string): Promise<string> =>
  window.electron.ipcRenderer.invoke('readPngForShow', filePath)

export const selectPath = (): Promise<string | undefined> =>
  window.electron.ipcRenderer
    .invoke('selectPath')
    .then((res: string | undefined) => res?.replace(EXE_NAME, ''))

export const loadSettings = (): Promise<{ path: string | undefined }> =>
  window.electron.ipcRenderer
    .invoke('loadSettings')
    .then((res) => (res ? res : { path: undefined }))

export const saveSettings = (setting: unknown): Promise<void> =>
  window.electron.ipcRenderer.invoke('saveSettings', setting)

export const loadLocalMods = (): Promise<ModModel> =>
  window.electron.ipcRenderer.invoke('loadLocalMods').then((res) => (res ? res : {}))

export const saveLocalMods = (mods: ModModel): Promise<void> =>
  window.electron.ipcRenderer.invoke('saveLocalMods', mods)

export const getAllFiles = (path: string, options: Options = undefined): Promise<string[]> =>
  window.electron.ipcRenderer.invoke('getAllFiles', path, options)

export const getZipModInfo = (filePath: string): Promise<Promise<ModModel | undefined>> =>
  window.electron.ipcRenderer.invoke('readZipMod', filePath)

export const moveFile = (file: string, target: string): Promise<void> =>
  window.electron.ipcRenderer.invoke('moveFile', file, target)

export const checkTargetDir = (target: string): Promise<string[]> =>
  window.electron.ipcRenderer.invoke('checkTargetDir', target)

export const getCardCharaNames = (path: string): Promise<string[]> =>
  window.electron.ipcRenderer.invoke('readAllCharaNames', path).then((data) => (data ? data : ['']))

export const fileExists = (path: string): Promise<boolean> =>
  window.electron.ipcRenderer.invoke('fileExists', path)

export const openFileSelector = (): Promise<string | undefined> =>
  window.electron.ipcRenderer.invoke('openFileSelector')

export const getCardMods = (path: string): Promise<string[] | undefined> =>
  window.electron.ipcRenderer.invoke('readPngForMod', path)

export const triggerDownload = (info: Omit<DownloadModel, 'dir'>): Promise<string> => {
  return window.electron.ipcRenderer.invoke('triggerDownload', {
    ...info,
    dir: useSettingStore.getState().modsPath()!
  })
}

export const initSideload = (url?: string) =>
  window.electron.ipcRenderer.invoke('initSideload', url)

export const log = (...data: any[]) => window.electron.ipcRenderer.invoke('log', data)
