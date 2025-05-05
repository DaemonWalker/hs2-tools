// import { CHANNEL_GET_ALL_MODS } from '@shared/constants'
import { DownloadModel } from '@shared/models/downloadModel'
import { BrowserWindow } from 'electron'
import { CancelError, download as dl } from 'electron-dl'
import info from '../../resources/sideload.zip?asset'
import StreamZip from 'node-stream-zip'
import { SideloadModel } from '@shared/models/sideloadModel'

const downloadTasks: Record<string, Electron.DownloadItem> = {}

export const getBatterRepackInfo = (): Promise<SideloadModel | undefined> => {
  console.log('getBatterRepackInfo', info)
  return new Promise((resolve) => {
    const zip = new StreamZip({ file: info })
    zip.on('error', (error) => {
      console.log(info, error)
      resolve(undefined)
    })
    zip.on('ready', async () => {
      const file = zip.entryDataSync('sideload.json')
      if (!file) {
        resolve(undefined)
      } else {
        resolve(JSON.parse(file.toString('utf-8')))
      }
      zip.close()
    })
  })
}

export const download = async (info: DownloadModel) => {
  console.log('download', info)
  const win = BrowserWindow.getFocusedWindow()
  if (!win) {
    return
  }
  try {
    const id = Date.now()
    // win.webContents.session.setProxy({ proxyRules: info.proxy || '' })
    const item = await dl(win, info.url, {
      directory: info.dir,
      onProgress: (e) => {
        win.webContents.send('download-progress', {
          [id]: { ...e, percent: e.percent * 100, name: info.guid }
        })
      },
      onCompleted: (e) => {
        win.webContents.send('download-complete', { guid: info.guid, path: e.path })
      }
    })
    downloadTasks[id] = item
    return id
  } catch (error) {
    if (error instanceof CancelError) {
      console.info('item.cancel() was called')
    } else {
      console.error(error)
    }
    return undefined
  }
}
