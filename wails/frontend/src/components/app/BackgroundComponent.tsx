import { initDB } from '@renderer/logic/dbUtils'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore, useSettingsProxy, useSettingsWindowsSleep } from '@renderer/store/settingStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { useCurrentTheme } from '@renderer/store/themeStore'
import { FC, useEffect, useRef } from 'react'

const { setProxy, disableWindowsSleep, enableWindowsSleep } = ipcUtils

export const BackgroundComponent: FC = () => {
  const { setSettingForm } = useSettingStore()
  const currentTheme = useCurrentTheme()
  const proxy = useSettingsProxy()
  const windowsSleep = useSettingsWindowsSleep()
  const prevDisabledRef = useRef<boolean | undefined>(undefined)

  // 初始化数据库和主题
  useEffect(() => {
    // 应用当前主题
    document.documentElement.setAttribute('data-theme', currentTheme)

    initDB().then(() => {
      useSettingStore.getState().init()
      useModStore.getState().init()
      useSideloadStore.getState().init()
    })
  }, [])

  useEffect(() => {
    if (proxy?.uri && proxy.uri.length > 0) {
      setProxy(proxy)
    }
  }, [proxy])

  // 管理 Windows 休眠阻止
  useEffect(() => {
    const currentDisabled = windowsSleep?.disabled
    const currentTaskId = windowsSleep?.taskId

    // 只有当状态发生变化时才处理
    if (currentDisabled === prevDisabledRef.current) {
      return
    }
    prevDisabledRef.current = currentDisabled

    if (currentDisabled) {
      // 开启阻止休眠，并保存返回的 taskId
      disableWindowsSleep().then((taskId: number) => {
        setSettingForm({
          windowsSleep: { disabled: true, taskId }
        }).catch(console.error)
      })
    } else if (currentTaskId !== undefined) {
      // 关闭阻止休眠，使用保存的 taskId
      enableWindowsSleep(currentTaskId).then(() => {
        setSettingForm({
          windowsSleep: { disabled: false, taskId: undefined }
        }).catch(console.error)
      })
    }
  }, [windowsSleep?.disabled, windowsSleep?.taskId, setSettingForm])

  return <></>
}
