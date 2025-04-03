import { app, shell, BrowserWindow, ipcMain, dialog } from 'electron'
import { join } from 'path'
import { electronApp, optimizer, is } from '@electron-toolkit/utils'
import icon from '../../resources/icon.png?asset'
import fs from 'fs'
import { loadLocalMods, loadSettings, saveLocalMods, saveSettings } from './saveData'
import { checkTargetDir, fileExists, listAllFiles, moveFile, readAllCharaNames, readAllMods, readPngForShow } from './fileUtil'
import { readZipMod } from './zipUtil'
import electronDl from 'electron-dl'

function createWindow(): void {
  // Create the browser window.
  const mainWindow = new BrowserWindow({
    width: 900,
    height: 670,
    show: false,
    autoHideMenuBar: true,
    ...(process.platform === 'linux' ? { icon } : {}),
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false
    }
  })
  electronDl();

  mainWindow.on('ready-to-show', () => {
    mainWindow.show()
  })

  mainWindow.webContents.setWindowOpenHandler((details) => {
    shell.openExternal(details.url)
    return { action: 'deny' }
  })

  // HMR for renderer base on electron-vite cli.
  // Load the remote URL for development or the local html file for production.
  if (is.dev && process.env['ELECTRON_RENDERER_URL']) {
    mainWindow.loadURL(process.env['ELECTRON_RENDERER_URL'])
  } else {
    mainWindow.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

// This method will be called when Electron has finished
// initialization and is ready to create browser windows.
// Some APIs can only be used after this event occurs.
app.whenReady().then(() => {
  // Set app user model id for windows
  electronApp.setAppUserModelId('com.electron')

  // Default open or close DevTools by F12 in development
  // and ignore CommandOrControl + R in production.
  // see https://github.com/alex8088/electron-toolkit/tree/master/packages/utils
  app.on('browser-window-created', (_, window) => {
    optimizer.watchWindowShortcuts(window)
  })

  // IPC test
  ipcMain.handle('ping', () => console.log('pong'))
  ipcMain.handle('readDir', (_, path) => fs.readdirSync(path))
  ipcMain.handle('readPngForMod', (_, path) => readAllMods(path))
  ipcMain.handle('readAllCharaNames', (_, path) => readAllCharaNames(path))
  ipcMain.handle('readPngForShow', (_, path) => readPngForShow(path))
  ipcMain.handle('selectPath', () =>
    dialog
      .showOpenDialog({
        filters: [{ name: 'HoneySelect2.exe', extensions: ['exe'] }],
        properties: ['openFile']
      })
      .then((res) => (res.canceled ? undefined : res.filePaths[0]))
  )
  ipcMain.handle('loadSettings', () => loadSettings())
  ipcMain.handle('saveSettings', (_, setting) => saveSettings(setting))
  ipcMain.handle('loadLocalMods', () => loadLocalMods())
  ipcMain.handle('saveLocalMods', (_, mods) => saveLocalMods(mods))
  ipcMain.handle('getAllFiles', (_, path, options) => listAllFiles(path, options))
  ipcMain.handle('readZipMod', (_, path) => readZipMod(path))
  ipcMain.handle('moveFile', (_, files, target) => moveFile(files, target))
  ipcMain.handle('checkTargetDir', (_, target) => checkTargetDir(target))
  ipcMain.handle('fileExists', (_, path) => fileExists(path))
  ipcMain.handle('openFileSelector', () =>
    dialog
      .showOpenDialog({
        filters: [{ name: '*.png', extensions: ['png'] }],
        properties: ['openFile']
      })
      .then((res) => (res.canceled ? undefined : res.filePaths[0]))
  )
  ipcMain.handle('log', (_, data) => console.log(...data))

  createWindow()

  app.on('activate', function () {
    // On macOS it's common to re-create a window in the app when the
    // dock icon is clicked and there are no other windows open.
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

// Quit when all windows are closed, except on macOS. There, it's common
// for applications and their menu bar to stay active until the user quits
// explicitly with Cmd + Q.
app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

// In this file you can include the rest of your app's specific main process
// code. You can also put them in separate files and require them here.
