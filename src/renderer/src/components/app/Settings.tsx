import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { Button, Input, Space } from 'antd'
import { FC, useState } from 'react'
// import { dialog } from 'electron'

const { selectPath } = ipcUtils

export const Settings: FC = () => {
  const { settings, setPath } = useSettingStore()
  const openSelectPath = () => {
    selectPath().then((path) => {
      if (path) setPath(path)
    })
  }
  return (
    <div style={{ display: 'flex', width: '100%', gap: 10, alignItems: 'end' }}>
      <Button onClick={openSelectPath}>选择路径</Button>
      <Input value={settings?.path} disabled style={{ flex: 1 }}></Input>
      <Button style={{ height: 60, width: 160 }}>开始游戏</Button>
      <Button style={{ height: 60, width: 160 }}>开始工作室</Button>
    </div>
  )
}
