import { dialog, powerSaveBlocker } from 'electron'
import fs from 'fs'
import {
  checkTargetDir,
  fileExists,
  listAllFiles,
  moveFile,
  readAllCharaNames,
  readAllMods,
  readPngForShow
} from './fileUtil'
import { loadLocalMods, loadSettings, saveLocalMods, saveSettings } from './saveData'
import { readZipMod } from './zipUtil'
import { download, getAllMods } from './betterrepackUtil'
import { ProxyAgent, setGlobalDispatcher } from 'undici'
import { ProxyInfo } from '@shared/models/proxyInfo'

export const ipcHandler = {
  handle: {
    ping: () => console.log('pong'),
    readDir: (_, path) => fs.readdirSync(path),
    readPngForMod: (_, path) => readAllMods(path),
    readAllCharaNames: (_, path) => readAllCharaNames(path),
    readPngForShow: (_, path) => readPngForShow(path),
    selectPath: () =>
      dialog
        .showOpenDialog({
          filters: [{ name: 'HoneySelect2.exe', extensions: ['exe'] }],
          properties: ['openFile']
        })
        .then((res) => (res.canceled ? undefined : res.filePaths[0])),
    loadSettings: () => loadSettings(),
    saveSettings: (_, setting) => saveSettings(setting),
    loadLocalMods: () => loadLocalMods(),
    saveLocalMods: (_, mods) => saveLocalMods(mods),
    getAllFiles: (_, path, options) => listAllFiles(path, options),
    readZipMod: (_, path) => readZipMod(path),
    moveFile: (_, files, target) => moveFile(files, target),
    checkTargetDir: (_, target) => checkTargetDir(target),
    fileExists: (_, path: string) => fileExists(path),
    openFileSelector: () =>
      dialog
        .showOpenDialog({
          filters: [{ name: '*.png', extensions: ['png'] }],
          properties: ['openFile']
        })
        .then((res) => (res.canceled ? undefined : res.filePaths[0])),
    triggerDownload: (_, info) => download(info),
    initSideload: (_, url) => getAllMods(url).catch((e) => console.log(e)),
    disableWindowsSleep: (_) => powerSaveBlocker.start('prevent-app-suspension'),
    enableWindowsSleep: (_, id) => powerSaveBlocker.stop(id),
    setProxy: (_, proxy: ProxyInfo) => {
      setGlobalDispatcher(new ProxyAgent({ ...proxy }))
    },
    log: (_, data) => console.log(...data)
  }
}

export const bindHandler = (ipcMain: Electron.IpcMain) => {
  for (const [key, value] of Object.entries(ipcHandler.handle)) {
    ipcMain.handle(key, value)
  }
}
