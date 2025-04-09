import { useModStore } from '@renderer/store/modStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { FC, useMemo, useState } from 'react'
import { Button, Typography } from 'antd'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useDownloadStore } from '@renderer/store/downloadStore'

const { triggerDownload } = ipcUtils
const { Text } = Typography

interface IProps {
  guid: string
}
export const ModStatus: FC<IProps> = ({ guid }) => {
  const { sideload } = useSideloadStore()
  const { mods } = useModStore()
  const [taskId, setTaskId] = useState<string>()
  const { tasks } = useDownloadStore()
  const [downloading, setDownloading] = useState<boolean>(
    !!(taskId && tasks && tasks[taskId] && tasks[taskId].current <= 100)
  )

  const onClick = async () => {
    setDownloading(true)
    const downloadUrl = sideload[guid]
    if (!downloadUrl) {
      return
    }
    setTaskId(await triggerDownload({ guid, url: downloadUrl }))
    setDownloading(false)
  }

  const loading = useMemo(() => {
    let progress: number | undefined = undefined
    if (taskId && tasks && tasks[taskId]) {
      progress = tasks[taskId].current
    }
    if (progress && progress >= 100) {
      return false
    }
    return downloading
  }, [downloading, tasks, taskId])

  const component = useMemo(() => {
    if (!mods) {
      return <></>
    }
    if (mods[guid]) {
      return <Text style={{ color: 'green' }}>已在本地</Text>
    }
    if (sideload[guid]) {
      return (
        <Button onClick={onClick} loading={loading}>
          下载
        </Button>
      )
    } else {
      return <Text style={{ color: 'red' }}>无法下载</Text>
    }
  }, [mods, guid, sideload, loading])

  return component
}
