import { initDB } from '@renderer/logic/dbUtils'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useDownloadStore } from '@renderer/store/downloadStore'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore } from '@renderer/store/settingStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { DownloadCompleteInfo, DownloadingInfo } from '@shared/models/downloadModel'
import { FC, useEffect } from 'react'

const { readZipMod, setProxy, disableWindowsSleep, enableWindowsSleep } = ipcUtils

export const AppInitialization: FC = () => {
  const { setTask } = useDownloadStore()
  const { setCurrent } = useSideloadStore()
  const { settings } = useSettingStore()

  useEffect(() => {
    window.electron.onDownloadProgress((progress: DownloadingInfo) => {
      setTask(progress)
    })
    window.electron.onDownloadComplete(async (info: DownloadCompleteInfo) => {
      const modInfo = await readZipMod(info.path)
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
  useEffect(() => {
    if (settings?.proxy?.uri.length > 0) {
      setProxy(settings.proxy)
    }
  }, [settings.proxy])

  useEffect(() => {
    if (!!settings?.windowsSleep?.disabled) {
      disableWindowsSleep()
    } else if (!settings.windowsSleep?.disabled && settings.windowsSleep?.taskId) {
      enableWindowsSleep(settings.windowsSleep.taskId)
    }
  }, [settings.windowsSleep])
  return <></>
}
