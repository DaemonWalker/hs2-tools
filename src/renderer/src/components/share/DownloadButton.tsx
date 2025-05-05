import ipcUtils from '@renderer/logic/ipcUtils'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { Button } from 'antd'
import { FC, useState } from 'react'

const { triggerDownload } = ipcUtils

interface IProps {
  modName: string
}

export const DownloadButton: FC<IProps> = ({ modName }) => {
  const { getDownloadUrl } = useSideloadStore()
  const downloadUrl = getDownloadUrl(modName)
  const [tasking, setTasking] = useState<boolean>(false)

  const onClick = () => {
    setTasking(true)
    triggerDownload({ name: modName, url: downloadUrl }).finally(() => setTasking(false))
  }
  return (
    <Button loading={tasking} onClick={onClick} disabled={!downloadUrl}>
      下载
    </Button>
  )
}
