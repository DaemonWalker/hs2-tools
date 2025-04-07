import { initSideload } from '@renderer/logic/ipcUtils'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { Button } from 'antd'
import { FC, useState } from 'react'

export const SideloadInit: FC = () => {
  const { current, setMap } = useSideloadStore()
  const [running, setRunning] = useState<boolean>(false)
  const init = async () => {
    setRunning(true)
    const result = await initSideload()
    setMap(result)
    setRunning(false)
  }
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <Button loading={running} onClick={init}>
        初始化Sideload
      </Button>
      <div style={{ textWrap: 'wrap' }}>{current}</div>
    </div>
  )
}
