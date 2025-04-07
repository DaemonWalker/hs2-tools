import { DownloadModel } from '@shared/models/downloadModel'
import { BrowserWindow } from 'electron'
import { download as dl, CancelError } from 'electron-dl'

export const download = async (info: DownloadModel) => {
  const win = BrowserWindow.getFocusedWindow()
  if (!win) {
    return
  }
  try {
    console.log('download', info)
    const id = Date.now()
    win.webContents.session.setProxy({ proxyRules: info.proxy || '' })
    await dl(win, info.url, {
      directory: info.dir,
      onProgress: (e) => {
        win.webContents.send('download-progress', {
          [id]: { ...e, percent: e.percent * 100, name: info.name }
        })
      },
      onCompleted: (e) => {
        win.webContents.send('download-complete', id)
      }
    })
  } catch (error) {
    if (error instanceof CancelError) {
      console.info('item.cancel() was called')
    } else {
      console.error(error)
    }
  }
}
