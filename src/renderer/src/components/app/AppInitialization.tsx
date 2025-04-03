import { initDB } from '@renderer/logic/dbUtils'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore } from '@renderer/store/settingStore'
import { FC, useEffect } from 'react'

export const AppInitialization: FC = () => {
  useEffect(() => {
    initDB().then(() => {
      useSettingStore.getState().init()
      useModStore.getState().init()
    })
  }, [])
  return <></>
}
