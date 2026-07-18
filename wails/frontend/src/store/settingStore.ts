import { getSettings, saveSettings } from '@renderer/logic/dbUtils'
import { CHARA_FEMALE_PATH, MODS_PATH, SCENE_PATH } from '@shared/constants'
import { SettingModel, SettingWindowForm } from '@shared/models/settingModel'
import { create } from 'zustand'
import { useShallow } from 'zustand/shallow'

interface SettingState {
  settings: SettingModel
  setPath: (path: string) => Promise<void>
  init: () => Promise<void>
  scenePath: () => string
  charaFemalePath: () => string
  modsPath: () => string
  setSettingForm: (form: Partial<SettingWindowForm>) => Promise<void>
}

export const useSettingStore = create<SettingState>((set, get) => ({
  settings: { path: undefined, proxy: { uri: '', username: '', password: '' } },
  init: async () => {
    const settings = await getSettings()
    set(() => ({ 
      settings: { 
        ...settings, 
        windowsSleep: settings.windowsSleep ?? { disabled: false } 
      } 
    }))
  },
  setPath: async (path: string) => {
    const settings = { ...get().settings, path: path }
    await saveSettings(settings)
    set(() => ({ settings }))
  },
  scenePath: () => get().settings.path + SCENE_PATH,
  charaFemalePath: () => get().settings.path + CHARA_FEMALE_PATH,
  modsPath: () => get().settings.path + MODS_PATH,
  setSettingForm: async (form) => {
    const current = get().settings
    const settings: SettingModel = {
      ...current,
      ...form,
      // 深合并嵌套对象（只在传入时合并）
      proxy: form.proxy ? { ...current.proxy, ...form.proxy } : current.proxy,
      windowsSleep: form.windowsSleep
        ? { ...current.windowsSleep, ...form.windowsSleep }
        : current.windowsSleep
    }
    await saveSettings(settings)
    set(() => ({ settings }))
  }
}))

// Selector hooks for better performance - prevents unnecessary re-renders
export const useSettingsPath = () => useSettingStore(useShallow(state => state.settings.path))
export const useSettingsProxy = () => useSettingStore(useShallow(state => state.settings.proxy))
export const useSettingsWindowsSleep = () => useSettingStore(useShallow(state => state.settings.windowsSleep))
export const useScenePathSelector = () => useSettingStore(state => state.scenePath)
export const useCharaFemalePathSelector = () => useSettingStore(state => state.charaFemalePath)
export const useModsPathSelector = () => useSettingStore(state => state.modsPath)
