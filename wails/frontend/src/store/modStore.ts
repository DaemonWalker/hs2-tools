import { getLocalMods, getModUseage, saveLocalMods, saveModUseage } from '@renderer/logic/dbUtils'
import { ModModel, ModUseageModel } from '@shared/models/modModel'
import { create } from 'zustand'
import { useShallow } from 'zustand/shallow'

interface ModState {
  mods: ModModel
  useage: ModUseageModel

  init: () => Promise<void>

  setMods: (mods: ModModel) => void
  setUseage: (useage: ModUseageModel) => void
}

export const useModStore = create<ModState>((set) => ({
  mods: {},
  useage: {},
  init: async () => {
    const mods = await getLocalMods()
    const useage = await getModUseage()
    set(() => ({ mods, useage }))
  },
  setMods: async (mods) => {
    await saveLocalMods(mods)
    set(() => ({ mods }))
  },
  setUseage: async (useage) => {
    await saveModUseage(useage)
    set(() => ({ useage }))
  }
}))

// Selector hooks for better performance
export const useModsSelector = () => useModStore(useShallow(state => state.mods))
export const useModUsageSelector = () => useModStore(useShallow(state => state.useage))
