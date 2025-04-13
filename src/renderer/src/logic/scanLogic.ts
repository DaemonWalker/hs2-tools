import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { ModModel } from '@shared/models/modModel'
const { getAllFiles, readPngForMod, readZipMod } = ipcUtils

export const scanMods = async (setStep?: () => any, setScanProgress?: (text: string) => any) => {
  const modsPath = useSettingStore.getState().modsPath
  setStep?.()
  const allLocalMods = await getAllFiles(modsPath(), { targetExtension: ['.zipmod'] })
  setScanProgress?.(`0/${allLocalMods.length}`)

  setStep?.()
  const result: ModModel = {}
  for (let i = 0; i < allLocalMods.length; i++) {
    const mod = allLocalMods[i]
    const zipInfo = await readZipMod(mod)
    if (zipInfo) {
      const guid = Object.keys(zipInfo)[0]
      result[guid] = { ...zipInfo[guid]!, path: mod }
    }
    setScanProgress?.(`${i + 1}/${allLocalMods.length}`)
  }
  return result
}

export const scanScene = async (setStep?: () => any, setScanProgress?: (text: string) => any) => {
  const scenePath = useSettingStore.getState().scenePath
  setStep?.()
  const sceneFiles = await getAllFiles(scenePath(), { targetExtension: ['.png'] })
  setScanProgress?.(`0/${sceneFiles.length}`)

  setStep?.()
  const result: Record<string, number> = {}
  for (let i = 0; i < sceneFiles.length; i++) {
    const file = sceneFiles[i]
    const mods = await readPngForMod(file)
    if (mods) {
      for (const mod of mods) {
        result[mod] = (result[mod] ?? 0) + 1
      }
    }
    setScanProgress?.(`${i + 1}/${sceneFiles.length}`)
  }
  return result
}

export const scanFemale = async (setStep?: () => any, setScanProgress?: (text: string) => any) => {
  const { charaFemalePath } = useSettingStore.getState()
  setStep?.()
  const charaFiles = await getAllFiles(charaFemalePath(), { targetExtension: ['.png'] })
  setScanProgress?.(`0/${charaFiles.length}`)

  setStep?.()
  const result: Record<string, number> = {}
  for (let i = 0; i < charaFiles.length; i++) {
    const file = charaFiles[i]
    const mods = await readPngForMod(file)
    if (mods) {
      for (const mod of mods) {
        result[mod] = (result[mod] ?? 0) + 1
      }
    }
    setScanProgress?.(`${i + 1}/${charaFiles.length}`)
  }
  return result
}
