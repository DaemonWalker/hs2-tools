import { triggerDownload } from '@renderer/logic/ipcUtils'
import { Button } from 'antd'
import { FC, useState } from 'react'

interface IProps {
  modName: string
}

export const DownloadButton: FC<IProps> = ({ modName }) => {
  const [tasking, setTasking] = useState<boolean>(false)

  const onClick = () => {
    setTasking(true)
    triggerDownload('', modName).finally(() => setTasking(false))
  }
  return (
    <Button loading={tasking} onClick={onClick}>
      下载
    </Button>
  )
}
