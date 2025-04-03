import xml2js from 'xml2js'
import StreamZip from 'node-stream-zip'
import { ModModel } from '@shared/models/modModel'

export const readZipMod = async (path: string): Promise<ModModel | undefined> =>
  new Promise((resolve) => {
    const zip = new StreamZip({ file: path })
    zip.on('error', (error) => {
      console.log(path, error)
      resolve(undefined)
    })
    zip.on('ready', async () => {
      const file = zip.entryDataSync('manifest.xml')
      if (!file) {
        resolve(undefined)
      } else {
        const obj = (await xml2js.parseStringPromise(file.toString('utf-8')))?.['manifest']
        if (!obj) {
          resolve(undefined)
        } else {
          const name = obj['name'] && obj['name'].length > 0 ? (obj['name'][0] as string) : ''
          resolve({
            [obj['guid'] as string]: {
              name,
              version: obj['version'] as string,
              path: path as string,
              used: 0
            }
          })
        }
      }
      zip.close()
    })
  })
