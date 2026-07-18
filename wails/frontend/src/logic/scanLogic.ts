import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { ModModel } from '@shared/models/modModel'
const { getAllFiles, readZipModBatch, readPngModsBatch } = ipcUtils

// 批处理大小，避免单次传输数据过大
const BATCH_SIZE = 500

export const scanMods = async (setStep?: () => any, setScanProgress?: (text: string) => any) => {
  const modsPath = useSettingStore.getState().modsPath
  const allLocalMods = await getAllFiles(modsPath(), { targetExtension: ['.zipmod'] })
  setScanProgress?.(`0/${allLocalMods.length}`)

  const result: ModModel = {}

  // 分批处理，避免单次传输过大
  for (let i = 0; i < allLocalMods.length; i += BATCH_SIZE) {
    const batch = allLocalMods.slice(i, i + BATCH_SIZE)
    const batchResult = await readZipModBatch(batch)

    // 合并结果
    Object.assign(result, batchResult)

    setScanProgress?.(`${Math.min(i + BATCH_SIZE, allLocalMods.length)}/${allLocalMods.length}`)
  }

  setStep?.()
  return result
}

export const scanScene = async (setStep?: () => any, setScanProgress?: (text: string) => any) => {
  const scenePath = useSettingStore.getState().scenePath
  const sceneFiles = await getAllFiles(scenePath(), { targetExtension: ['.png'] })
  setScanProgress?.(`0/${sceneFiles.length}`)

  const result: Record<string, number> = {}

  // 分批批量处理
  for (let i = 0; i < sceneFiles.length; i += BATCH_SIZE) {
    const batch = sceneFiles.slice(i, i + BATCH_SIZE)
    const batchResults = await readPngModsBatch(batch)

    // 统计 mod 使用情况
    for (const item of batchResults) {
      const modIds = item.modIds ?? []
      for (const mod of modIds) {
        result[mod] = (result[mod] ?? 0) + 1
      }
    }

    setScanProgress?.(`${Math.min(i + BATCH_SIZE, sceneFiles.length)}/${sceneFiles.length}`)
  }

  setStep?.()
  return result
}

export const scanFemale = async (setStep?: () => any, setScanProgress?: (text: string) => any) => {
  const { charaFemalePath } = useSettingStore.getState()
  const charaFiles = await getAllFiles(charaFemalePath(), { targetExtension: ['.png'] })
  setScanProgress?.(`0/${charaFiles.length}`)

  const result: Record<string, number> = {}

  // 分批批量处理
  for (let i = 0; i < charaFiles.length; i += BATCH_SIZE) {
    const batch = charaFiles.slice(i, i + BATCH_SIZE)
    const batchResults = await readPngModsBatch(batch)

    // 统计 mod 使用情况
    for (const item of batchResults) {
      const modIds = item.modIds ?? []
      for (const mod of modIds) {
        result[mod] = (result[mod] ?? 0) + 1
      }
    }

    setScanProgress?.(`${Math.min(i + BATCH_SIZE, charaFiles.length)}/${charaFiles.length}`)
  }

  setStep?.()
  return result
}
