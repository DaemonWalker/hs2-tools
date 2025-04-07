import { DownloadingInfo, DownloadModel, DownloadTaskInfo } from '@shared/models/downloadModel'
import { create } from 'zustand'

interface DownloadState {
  tasks: DownloadingInfo
  setTask: (tasks: DownloadingInfo) => void
}

export const useDownloadStore = create<DownloadState>((set) => ({
  tasks: {},
  setTask: (tasks) => set((state) => ({ tasks: { ...state.tasks, ...tasks } }))
}))
