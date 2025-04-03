import { getSettings, saveSettings } from '@renderer/logic/dbUtils'
import { pathJoin } from '@renderer/logic/ioUtils'
import { CHARA_FEMALE_PATH, MODS_PATH, SCENE_PATH } from '@shared/constants'
import { create } from 'zustand'

interface SettingState {
  settings: {
    path: string | undefined
  }
  setPath: (path: string) => Promise<void>
  init: () => Promise<void>
  scenePath: () => string
  charaFemalePath: () => string
  modsPath: () => string
}

export const useSettingStore = create<SettingState>((set, get) => ({
  settings: { path: undefined },
  init: async () => {
    const settings = await getSettings()
    set(() => ({ settings }))
  },
  setPath: async (path: string) => {
    const settings = { ...get().settings, path: path }
    await saveSettings(settings)
    set(() => ({ settings }))
  },
  scenePath: () => pathJoin(get().settings.path!, SCENE_PATH),
  charaFemalePath: () => pathJoin(get().settings.path!, CHARA_FEMALE_PATH),
  modsPath: () => pathJoin(get().settings.path!, MODS_PATH)
}))
