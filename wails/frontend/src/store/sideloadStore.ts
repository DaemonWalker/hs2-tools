import { SideloadModel } from '@shared/models/sideloadModel'
import { create } from 'zustand'
import { useShallow } from 'zustand/shallow'
import ipcHandler from '@renderer/logic/ipcUtils'

const { initSideload } = ipcHandler

interface SideloadState {
  sideload: SideloadModel
  isLoading: boolean
  init: () => Promise<void>
  getDownloadUrl: (guid: string) => string | undefined
}

export const useSideloadStore = create<SideloadState>((set, get) => ({
  sideload: {},
  isLoading: false,
  init: async () => {
    set(() => ({ isLoading: true }))
    try {
      const sideload = await initSideload()
      set(() => ({ sideload: sideload }))
    } finally {
      set(() => ({ isLoading: false }))
    }
  },
  getDownloadUrl: (guid: string) => {
    const sideload = get().sideload
    return sideload[guid]
  }
}))

// Selector hooks for better performance
export const useSideloadData = () => useSideloadStore(useShallow(state => state.sideload))
export const useGetDownloadUrl = () => useSideloadStore(state => state.getDownloadUrl)
