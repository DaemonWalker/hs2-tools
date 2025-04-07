import { getSideload, saveSideload } from '@renderer/logic/dbUtils'
import { SideloadModel } from '@shared/models/sideloadModel'
import { create } from 'zustand'

interface SideloadState {
  current: string
  sideload: SideloadModel
  setCurrent: (current: string) => void
  setMap: (map: SideloadModel) => void
  init: () => Promise<void>
}

export const useSideloadStore = create<SideloadState>((set) => ({
  current: '',
  sideload: {},
  setCurrent: (current) => set({ current }),
  setMap: async (sideload) => {
    await saveSideload({ ...sideload })
    set({ sideload })
  },
  init: async () => {
    const sideload = await getSideload()
    set(() => ({ sideload: sideload }))
  }
}))
