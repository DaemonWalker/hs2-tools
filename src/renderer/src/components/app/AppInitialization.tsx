import { initDB } from '@renderer/logic/dbUtils'
import { getZipModInfo } from '@renderer/logic/ipcUtils'
import { useDownloadStore } from '@renderer/store/downloadStore'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore } from '@renderer/store/settingStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { DownloadCompleteInfo, DownloadingInfo, DownloadModel } from '@shared/models/downloadModel'
import { FC, useEffect } from 'react'

export const AppInitialization: FC = () => {
  const { setTask } = useDownloadStore()
  const { setCurrent } = useSideloadStore()

  useEffect(() => {
    window.electron.onDownloadProgress((progress: DownloadingInfo) => {
      setTask(progress)
    })
    window.electron.onDownloadComplete(async (info: DownloadCompleteInfo) => {
      const modInfo = await getZipModInfo(info.path)
      if (!modInfo) {
        return
      }
      const mods = useModStore.getState().mods
      mods[info.guid] = modInfo[info.guid]
      useModStore.getState().setMods(mods)
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
