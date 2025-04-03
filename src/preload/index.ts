import { contextBridge, ipcRenderer } from 'electron'
import { electronAPI } from '@electron-toolkit/preload'

// Custom APIs for renderer
const api = {}

// Use `contextBridge` APIs to expose Electron APIs to
// renderer only if context isolation is enabled, otherwise
// just add to the DOM global.
if (process.contextIsolated) {
  try {
    contextBridge.exposeInMainWorld('electron', electronAPI)
    contextBridge.exposeInMainWorld('api', api)
    contextBridge.exposeInMainWorld('download', {
      onDownloadProgress: (callback) =>
        ipcRenderer.on('download-progress', (_, args) => callback(args)),
      onDownloadComplete: (callback) =>
        ipcRenderer.on('download-complete', (_, args) => callback(args))
    })
  } catch (error) {
    console.error(error)
  }
} else {
  // @ts-ignore (define in dts)
  window.electron = electronAPI
  // @ts-ignore (define in dts)
  window.api = api
  // @ts-ignore (define in dts)
  window.download = {
    onDownloadProgress: (callback) =>
      ipcRenderer.on('download-progress', (_, args) => callback(args)),
    onDownloadComplete: (callback) =>
      ipcRenderer.on('download-complete', (_, args) => callback(args))
  }
}
