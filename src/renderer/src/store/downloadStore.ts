import { create } from 'zustand'

interface DownloadState {
  tasks: Record<string, { progress: number; status: string; speed: string }>
  setTasks: (tasks: Record<string, { progress: number; status: string; speed: string }>) => void
}

create<DownloadState>((set) => ({
  tasks: {},
  setTasks: (tasks) => set((state) => ({ tasks: { ...state.tasks, ...tasks } }))
}))
