import { useModStore } from '@renderer/store/modStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { Button } from 'antd'
import { FC, useMemo, useState } from 'react'
import ipcUtils from '@renderer/logic/ipcUtils'

const { triggerDownload } = ipcUtils

export const SideloadInit: FC = () => {
  const { sideload } = useSideloadStore()
  const { mods, useage } = useModStore()
  const [downloading, setDownloading] = useState<boolean>(false)
  const [progress, setProgress] = useState<number>(0)
  const downloadList = useMemo(
    () =>
      Object.keys(useage)
        .filter((k) => !mods[k] && !!sideload[k])
        .map((k) => ({ name: k, url: sideload[k] })),
    [sideload, mods, useage]
  )
  const onClick = async () => {
    setDownloading(true)
    for (let i = 0; i < downloadList.length; i++) {
      setProgress(0)
      const mod = downloadList[i]
      await triggerDownload(mod)
    }
    setDownloading(false)
  }
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <Button onClick={onClick} loading={downloading}>
        补全全部缺失Mods
      </Button>
      <div>
        <span>{progress}</span> / <span>{downloadList.length}</span>
      </div>
    </div>
  )
}
