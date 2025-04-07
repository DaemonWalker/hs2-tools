// import { CHANNEL_GET_ALL_MODS } from '@shared/constants'
import { SideloadModel } from '@shared/models/sideloadModel'
import { load } from 'cheerio'
import { BrowserWindow } from 'electron'

const GUID_START = Buffer.from(`<guid>`, 'binary')
const GUID_END = Buffer.from(`</guid>`, 'binary')
const FETCH_SIZE = 1024
const MAX_PART_FETCH = 5

const findGuid = async (
  url: string,
  range: (idx: number) => string
): Promise<string | undefined> => {
  let i = 0
  let buffer = Buffer.alloc(0)
  let findMainfest = false
  while (i < MAX_PART_FETCH) {
    const fileResponse = await fetch(url, {
      method: 'GET',
      headers: {
        'user-agent':
          'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/111.0.0.0 Safari/537.36 Edg/111.0.1661.54',
        accept: '*/*',
        'accept-encoding': 'identity',
        range: range(i),
        host: 'sideload.betterrepack.com'
      }
    })
    if (!fileResponse.ok) {
      console.log(`Error: ${fileResponse.status}`, url)
      return undefined
    }
    buffer = Buffer.concat([Buffer.from(await fileResponse.arrayBuffer()), buffer])
    if (buffer.includes(GUID_START) && buffer.includes(GUID_END)) {
      findMainfest = true
      break
    }
    i++
  }
  if (!findMainfest) {
    return undefined
  }
  const guidStart = buffer.indexOf(GUID_START)
  const guidEnd = buffer.indexOf(GUID_END, guidStart)
  const guid = buffer.subarray(guidStart + GUID_START.length, guidEnd)
  return guid.toString('utf-8')
}

const getModGuid = async (url: string): Promise<SideloadModel> => {
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

  const front = await findGuid(
    url,
    (i) => `bytes=${size - (i + 1) * FETCH_SIZE}-${size - i * FETCH_SIZE - 1}`
  )
  if (front) {
    return { [front]: url }
  }
  const end = await findGuid(url, (i) => `bytes=${i * FETCH_SIZE}-${(i + 1) * FETCH_SIZE - 1}`)
  if (end) {
    return { [end]: url }
  }
  return {}
}

export const getAllModsAsync = async (url: string) => {
  const baseUrl = url ?? 'https://sideload.betterrepack.com/download/AISHS2/'
  const win = BrowserWindow.getFocusedWindow()
  if (!win) {
    return
  }

  let result = {}
  const queue: string[][] = [[baseUrl, '']]
  while (queue.length > 0) {
    const fetchUrl = queue.shift()!.join('')
    win.webContents.send('get-all-mods', fetchUrl)

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
        getModGuid(fetchUrl + mod).catch((e) => {
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
