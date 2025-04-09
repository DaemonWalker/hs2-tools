import { useSettingStore } from '@renderer/store/settingStore'
import { EXE_NAME } from '@shared/constants'
import { DownloadModel } from '@shared/models/downloadModel'
import { Options } from '@shared/models/getFileOptions'
import { IPCHandlerModel } from '@shared/models/ipcHandlerModel'
import { ModModel } from '@shared/models/modModel'
import { ProxyInfo } from '@shared/models/proxyInfo'

const ipcHandler: IPCHandlerModel = {
  readDir: (dirPath: string): Promise<string[]> =>
    window.electron.ipcRenderer.invoke('readDir', dirPath),
  readPngForMod: (filePath: string): Promise<string[]> =>
    window.electron.ipcRenderer.invoke('readPngForMod', filePath),
  readPngForShow: (filePath: string): Promise<string> =>
    window.electron.ipcRenderer.invoke('readPngForShow', filePath),
  selectPath: (): Promise<string | undefined> =>
    window.electron.ipcRenderer
      .invoke('selectPath')
      .then((res: string | undefined) => res?.replace(EXE_NAME, '')),
  loadSettings: (): Promise<{ path: string | undefined }> =>
    window.electron.ipcRenderer
      .invoke('loadSettings')
      .then((res) => (res ? res : { path: undefined })),
  saveSettings: (setting: unknown): Promise<void> =>
    window.electron.ipcRenderer.invoke('saveSettings', setting),
  loadLocalMods: (): Promise<ModModel> =>
    window.electron.ipcRenderer.invoke('loadLocalMods').then((res) => (res ? res : {})),
  saveLocalMods: (mods: ModModel): Promise<void> =>
    window.electron.ipcRenderer.invoke('saveLocalMods', mods),
  getAllFiles: (path: string, options: Options = undefined): Promise<string[]> =>
    window.electron.ipcRenderer.invoke('getAllFiles', path, options),
  readZipMod: (filePath: string): Promise<ModModel | undefined> =>
    window.electron.ipcRenderer.invoke('readZipMod', filePath),
  moveFile: (file: string, target: string): Promise<void> =>
    window.electron.ipcRenderer.invoke('moveFile', file, target),
  checkTargetDir: (target: string): Promise<void> =>
    window.electron.ipcRenderer.invoke('checkTargetDir', target),
  readAllCharaNames: (path: string): Promise<string[]> =>
    window.electron.ipcRenderer
      .invoke('readAllCharaNames', path)
      .then((data) => (data ? data : [''])),
  fileExists: (path: string): Promise<boolean> =>
    window.electron.ipcRenderer.invoke('fileExists', path),
  openFileSelector: (): Promise<string | undefined> =>
    window.electron.ipcRenderer.invoke('openFileSelector'),
  triggerDownload: (info: Omit<DownloadModel, 'dir'>): Promise<number | undefined> => {
    return window.electron.ipcRenderer.invoke('triggerDownload', {
      ...info,
      dir: useSettingStore.getState().modsPath()!
    })
  },
  initSideload: (url?: string) => window.electron.ipcRenderer.invoke('initSideload', url),
  log: (...data: any[]) => window.electron.ipcRenderer.invoke('log', data),
  ping: () => window.electron.ipcRenderer.invoke('ping'),
  disableWindowsSleep: () => window.electron.ipcRenderer.invoke('enableWindowsSleep'),
  enableWindowsSleep: (id: number) => window.electron.ipcRenderer.invoke('disableWindowsSleep', id),
  setProxy: (proxy: ProxyInfo) => window.electron.ipcRenderer.invoke('setProxy', proxy)
}

export default ipcHandler
