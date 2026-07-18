import ipcUtils from '@renderer/logic/ipcUtils'
import { useCallback, useState } from 'react'

const { selectPath } = ipcUtils

interface UsePathSelectorOptions {
  onSelect?: (path: string) => void | Promise<void>
}

export const usePathSelector = (options: UsePathSelectorOptions = {}) => {
  const { onSelect } = options
  const [loading, setLoading] = useState(false)

  const openSelectPath = useCallback(async () => {
    setLoading(true)
    try {
      const path = await selectPath()
      if (path) {
        await onSelect?.(path)
        return path
      }
    } finally {
      setLoading(false)
    }
    return null
  }, [onSelect])

  return {
    openSelectPath,
    loading
  }
}
