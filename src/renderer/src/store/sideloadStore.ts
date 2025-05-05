import { SideloadModel } from '@shared/models/sideloadModel'
import { create } from 'zustand'
import ipcHandler from '@renderer/logic/ipcUtils'

const { initSideload } = ipcHandler

interface SideloadState {
  sideload: SideloadModel
  init: () => Promise<void>
  getDownloadUrl: (guid: string) => string | undefined
}

export const useSideloadStore = create<SideloadState>((set, get) => ({
  sideload: {},
  init: async () => {
    const sideload = await initSideload()
    set(() => ({ sideload: sideload }))
  },
  getDownloadUrl: (guid: string) => {
    const sideload = get().sideload
    return sideload[guid]
  }
}))
