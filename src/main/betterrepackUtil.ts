// import { CHANNEL_GET_ALL_MODS } from '@shared/constants'
import { DownloadModel } from '@shared/models/downloadModel'
import { SideloadModel } from '@shared/models/sideloadModel'
import { load } from 'cheerio'
import { BrowserWindow } from 'electron'
import { CancelError, download as dl } from 'electron-dl'

const GUID_START = Buffer.from(`<guid>`, 'binary')
const GUID_END = Buffer.from(`</guid>`, 'binary')
const FETCH_SIZE = 1024
const MAX_PART_FETCH = 5

const downloadTasks: Record<string, Electron.DownloadItem> = {}

const checkBuffer = (buffer: Buffer) => buffer.includes(GUID_START) && buffer.includes(GUID_END)

const fetchRange = async (url: string, start: number, end: number) => {
  const fileResponse = await fetch(url, {
    method: 'GET',
    headers: {
      'user-agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54',
      accept: '*/*',
      'accept-encoding': 'identity',
      range: `bytes=${start}-${end}`,
      host: 'sideload.betterrepack.com'
    }
  })
  if (!fileResponse.ok) {
    console.log(`Error: ${fileResponse.status}`, url)
    return undefined
  }
  return Buffer.from(await fileResponse.arrayBuffer())
}

const findGuid = async (url: string, size: number): Promise<string | undefined> => {
  let leftBuffer = Buffer.alloc(0)
  let rightBuffer = Buffer.alloc(0)
  let buffer: Buffer | undefined = undefined

  for (let i = 0; i < MAX_PART_FETCH; i++) {
    if (size - (i + 1) * FETCH_SIZE >= 0) {
      rightBuffer = Buffer.concat([
        (await fetchRange(
          url,
          Math.max(0, size - (i + 1) * FETCH_SIZE),
          size - i * FETCH_SIZE - 1
        )) ?? Buffer.alloc(0),
        rightBuffer
      ])
      if (checkBuffer(rightBuffer)) {
        buffer = rightBuffer
        break
      }
    }
    if (i * FETCH_SIZE < size) {
      leftBuffer = Buffer.concat([
        leftBuffer,
        (await fetchRange(url, i * FETCH_SIZE, Math.min((i + 1) * FETCH_SIZE - 1, size - 1))) ??
          Buffer.alloc(0)
      ])
      if (checkBuffer(leftBuffer)) {
        buffer = leftBuffer
        break
      }
    }
  }
  if (!buffer) {
    return undefined
  }
  const guidStart = buffer.indexOf(GUID_START)
  const guidEnd = buffer.indexOf(GUID_END, guidStart)
  const guid = buffer.subarray(guidStart + GUID_START.length, guidEnd)
  return guid.toString('utf-8')
}

const getModGuid = async (url: string, baseUrl: string): Promise<SideloadModel> => {
  const res = await fetch(url, {
    method: 'HEAD',
    headers: {
      'user-agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54',
      accept: '*/*',
      'accept-encoding': 'identity'
    }
  })
  const contentLength = res.headers.get('content-length')
  if (!contentLength) {
    return {}
  }
  const size = parseInt(contentLength)
  const guid = await findGuid(url, size)
  return guid ? { [guid]: url.replace(baseUrl, '') } : {}
}

export const getAllMods = async (url: string): Promise<SideloadModel> => {
  const win = BrowserWindow.getFocusedWindow()
  if (!win) {
    return {}
  }

  let result = {}
  const queue: string[][] = [[url, '']]
  while (queue.length > 0) {
    const fetchUrl = queue.shift()!.join('')
    win.webContents.send('get-all-mods', `${fetchUrl} 剩余:${queue.length}`)

    const response = await fetch(fetchUrl)
    if (!response.ok) {
      console.log(`Error: ${response.status} ${response.statusText}`)
      continue
    }

    const html = await response.text()
    const dom = load(html)
    const hrefs = dom('table#indexlist tr:gt(1) a')
      .map((_, el) => decodeURI(dom(el).attr('href') ?? ''))
      .get()
    const urls = [...new Set(hrefs)]
    const dirs = urls.filter((u) => !u.endsWith('.zipmod'))
    queue.push(...dirs.map((d) => [fetchUrl, d]))

    const zipmods = urls.filter((u) => u.endsWith('.zipmod'))
    const zipmodTasks = await Promise.all(
      zipmods.map((mod) =>
        getModGuid(fetchUrl + mod, url).catch((e) => {
          console.log(e)
        })
      )
    )
    for (const zipmodTask of zipmodTasks) {
      result = { ...result, ...zipmodTask }
    }
  }

  return result
}

export const download = async (info: DownloadModel) => {
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
