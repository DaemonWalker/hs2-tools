import { initDB } from '@renderer/logic/dbUtils'
import { useDownloadStore } from '@renderer/store/downloadStore'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore } from '@renderer/store/settingStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { DownloadingInfo } from '@shared/models/downloadModel'
import { FC, useEffect } from 'react'

export const AppInitialization: FC = () => {
  const { setTask } = useDownloadStore()
  const { setCurrent } = useSideloadStore()

  useEffect(() => {
    window.electron.onDownloadProgress((progress: DownloadingInfo) => {
      setTask(progress)
    })
    window.electron.getAllMods((url) => {
      setCurrent(url)
    })
    initDB().then(() => {
      useSettingStore.getState().init()
      useModStore.getState().init()
      useSideloadStore.getState().init()
    })
  }, [])
  return <></>
}
