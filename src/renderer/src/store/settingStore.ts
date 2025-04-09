import { getSettings, saveSettings } from '@renderer/logic/dbUtils'
import { pathJoin } from '@renderer/logic/ioUtils'
import { CHARA_FEMALE_PATH, MODS_PATH, SCENE_PATH } from '@shared/constants'
import { ProxyInfo } from '@shared/models/proxyInfo'
import { SettingModel, SettingWindowForm } from '@shared/models/settingModel'
import { create } from 'zustand'

interface SettingState {
  settings: SettingModel
  setPath: (path: string) => Promise<void>
  init: () => Promise<void>
  scenePath: () => string
  charaFemalePath: () => string
  modsPath: () => string
  setSettingForm: (form: SettingWindowForm) => Promise<void>
}

export const useSettingStore = create<SettingState>((set, get) => ({
  settings: { path: undefined, proxy: { uri: '' } },
  init: async () => {
    const settings = await getSettings()
    set(() => ({ settings: { ...settings, windowsSleep: { disabled: false } } }))
  },
  setPath: async (path: string) => {
    const settings = { ...get().settings, path: path }
    await saveSettings(settings)
    set(() => ({ settings }))
  },
  scenePath: () => pathJoin(get().settings.path!, SCENE_PATH),
  charaFemalePath: () => pathJoin(get().settings.path!, CHARA_FEMALE_PATH),
  modsPath: () => pathJoin(get().settings.path!, MODS_PATH),
  setSettingForm: async (form) => {
    const settings = { ...get().settings, ...form }
    await saveSettings(settings)
    set(() => ({ settings }))
  }
}))
