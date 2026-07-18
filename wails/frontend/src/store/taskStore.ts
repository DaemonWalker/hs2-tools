import { create } from 'zustand'
import { useShallow } from 'zustand/shallow'

export type ScanStatus = 'idle' | 'running' | 'complete' | 'error'
export type SideloadUpdateStatus = 'idle' | 'running' | 'success' | 'error'

interface ScanProgress {
  current: number
  info: {
    mod: string
    scene: string
    chara: string
  }
}

interface SideloadUpdateProgress {
  message?: string
  percent?: number
  current?: number  // 剩余任务数
  error?: string
}

interface TaskState {
  // Scan 任务状态
  scanStatus: ScanStatus
  scanProgress: ScanProgress
  setScanStatus: (status: ScanStatus) => void
  setScanProgress: (progress: Partial<ScanProgress>) => void
  resetScan: () => void

  // Sideload 更新任务状态
  sideloadUpdateStatus: SideloadUpdateStatus
  sideloadUpdateProgress: SideloadUpdateProgress
  setSideloadUpdateStatus: (status: SideloadUpdateStatus) => void
  setSideloadUpdateProgress: (progress: SideloadUpdateProgress) => void
  resetSideloadUpdate: () => void
}

const initialScanProgress: ScanProgress = {
  current: -1,
  info: {
    mod: '',
    scene: '',
    chara: ''
  }
}

const initialSideloadUpdateProgress: SideloadUpdateProgress = {}

export const useTaskStore = create<TaskState>((set) => ({
  // Scan 初始状态
  scanStatus: 'idle',
  scanProgress: { ...initialScanProgress },
  setScanStatus: (status) => set({ scanStatus: status }),
  setScanProgress: (progress) =>
    set((state) => ({
      scanProgress: { ...state.scanProgress, ...progress }
    })),
  resetScan: () =>
    set({
      scanStatus: 'idle',
      scanProgress: { ...initialScanProgress }
    }),

  // Sideload 更新初始状态
  sideloadUpdateStatus: 'idle',
  sideloadUpdateProgress: { ...initialSideloadUpdateProgress },
  setSideloadUpdateStatus: (status) => set({ sideloadUpdateStatus: status }),
  setSideloadUpdateProgress: (progress) =>
    set((state) => ({
      sideloadUpdateProgress: { ...state.sideloadUpdateProgress, ...progress }
    })),
  resetSideloadUpdate: () =>
    set({
      sideloadUpdateStatus: 'idle',
      sideloadUpdateProgress: { ...initialSideloadUpdateProgress }
    })
}))

// Selector hooks for better performance
export const useScanStatus = () => useTaskStore(useShallow(state => ({
  status: state.scanStatus,
  progress: state.scanProgress,
  setStatus: state.setScanStatus,
  setProgress: state.setScanProgress,
  reset: state.resetScan
})))

export const useSideloadUpdateStatus = () => useTaskStore(useShallow(state => ({
  status: state.sideloadUpdateStatus,
  progress: state.sideloadUpdateProgress,
  setStatus: state.setSideloadUpdateStatus,
  setProgress: state.setSideloadUpdateProgress,
  reset: state.resetSideloadUpdate
})))
