import { contextBridge, ipcRenderer } from 'electron'
import { electronAPI } from '@electron-toolkit/preload'
import { SideloadModel } from '@shared/models/sideloadModel'
import { Stringify } from '@shared/TypeConvert'
// import { CHANNEL_GET_ALL_MODS } from '@shared/constants'

// Custom APIs for renderer

const api = {}
export const bridge = {
  onDownloadProgress: 'download-progress',
  onDownloadComplete: 'download-complete',
  onSideloadInitProgress: 'get-all-mods',
  onSideloadInitFinish: 'sideload-init-finish'
} as const

type BridgeHandlers = {
  [K in keyof typeof bridge]: (callback: (args: any) => unknown) => void
}

export const bridgeHandlers: BridgeHandlers = Object.assign(
  {},
  ...Object.keys(bridge).map((key) => ({
    [key]: (callback: (args) => unknown) =>
      ipcRenderer.on(bridge[key as keyof typeof bridge], (_, args) => callback(args))
  }))
)

// export const bridge = {
//   onDownloadProgress: (callback) =>
//     ipcRenderer.on(bridgeName.onDownloadProgress, (_, args) => {
//       return callback(args)
//     }),
//   onDownloadComplete: (callback) =>
//     ipcRenderer.on(bridgeName.onDownloadComplete, (_, args) => {
//       return callback(args)
//     }),
//   onSideloadInitProgress: (callback) =>
//     ipcRenderer.on(bridgeName.onSideloadInitProgress, (_, args) => callback(args)),
//   onSideloadInitFinish: (callback: (args: SideloadModel) => unknown) =>
//     ipcRenderer.on(bridgeName.onSideloadInitFinish, (_, args: SideloadModel) => callback(args))
// }

// Use `contextBridge` APIs to expose Electron APIs to
// renderer only if context isolation is enabled, otherwise
// just add to the DOM global.
if (process.contextIsolated) {
  try {
    contextBridge.exposeInMainWorld('electron', electronAPI)
    contextBridge.exposeInMainWorld('api', api)
    contextBridge.exposeInMainWorld('bridge', bridgeHandlers)
  } catch (error) {
    console.error(error)
  }
} else {
  // @ts-ignore (define in dts)
  window.electron = electronAPI
  // @ts-ignore (define in dts)
  window.api = api
  // @ts-ignore (define in dts)
  window.bridge = bridgeHandlers
}
