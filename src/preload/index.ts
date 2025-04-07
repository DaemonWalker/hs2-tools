import { contextBridge, ipcRenderer } from 'electron'
import { electronAPI } from '@electron-toolkit/preload'
// import { CHANNEL_GET_ALL_MODS } from '@shared/constants'

// Custom APIs for renderer
const api = {}

const onDownloadProgress = (callback) =>
  ipcRenderer.on('download-progress', (_, args) => {
    return callback(args)
  })
const onDownloadComplete = (callback) =>
  ipcRenderer.on('download-complete', (_, args) => {
    return callback(args)
  })

const getAllMods = (callback) => ipcRenderer.on("get-all-mods", (_, args) => callback(args))

// Use `contextBridge` APIs to expose Electron APIs to
// renderer only if context isolation is enabled, otherwise
// just add to the DOM global.
if (process.contextIsolated) {
  try {
    contextBridge.exposeInMainWorld('electron', {
      ...electronAPI,
      onDownloadProgress,
      onDownloadComplete,
      getAllMods
    })
    contextBridge.exposeInMainWorld('api', api)
  } catch (error) {
    console.error(error)
  }
} else {
  // @ts-ignore (define in dts)
  window.electron = {
    ...electronAPI,
    onDownloadProgress,
    onDownloadComplete,
    getAllMods
  }
  // @ts-ignore (define in dts)
  window.api = api
}
