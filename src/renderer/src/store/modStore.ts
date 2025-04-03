import { getLocalMods, saveLocalMods } from '@renderer/logic/dbUtils'
import { ModModel } from '@shared/models/modModel'
import { create } from 'zustand'

interface ModState {
  mods: ModModel
  setMods: (mods: ModModel) => void
  init: () => Promise<void>
}

export const useModStore = create<ModState>((set) => ({
  mods: {},
  init: async () => {
    const mods = await getLocalMods()
    set(() => ({ mods }))
  },
  setMods: async (mods) => {
    await saveLocalMods(mods)
    set(() => ({ mods }))
  }
}))
