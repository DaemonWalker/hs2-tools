import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type ThemeType = 'modern-dark' | 'clean-light'

interface ThemeState {
  currentTheme: ThemeType
  setTheme: (theme: ThemeType) => void
  toggleTheme: () => void
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set, get) => ({
      currentTheme: 'modern-dark',
      setTheme: (theme) => {
        set({ currentTheme: theme })
        // 应用主题到 document
        document.documentElement.setAttribute('data-theme', theme)
      },
      toggleTheme: () => {
        const newTheme = get().currentTheme === 'modern-dark' ? 'clean-light' : 'modern-dark'
        get().setTheme(newTheme)
      }
    }),
    {
      name: 'theme-storage',
      onRehydrateStorage: () => (state) => {
        // 持久化恢复后应用主题
        if (state) {
          document.documentElement.setAttribute('data-theme', state.currentTheme)
        }
      }
    }
  )
)

// Selector hooks for better performance
export const useCurrentTheme = () => useThemeStore(state => state.currentTheme)
export const useThemeActions = () => useThemeStore(state => ({ 
  setTheme: state.setTheme, 
  toggleTheme: state.toggleTheme 
}))
