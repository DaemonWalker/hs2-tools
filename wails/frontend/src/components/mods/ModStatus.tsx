import { useModStore } from '@renderer/store/modStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { FC, useMemo, useCallback } from 'react'
import { Button, Typography, Progress } from 'antd'
import { useDownloadManager, useTask } from '@renderer/hooks/useDownloadManager'

const { Text } = Typography

interface IProps {
  guid: string
}

/**
 * Mod 状态组件
 * 显示 Mod 的本地存在状态或下载按钮
 */
export const ModStatus: FC<IProps> = ({ guid }) => {
  const { sideload } = useSideloadStore()
  const { mods } = useModStore()
  const { download, cancel } = useDownloadManager()

  // 使用 useTask 订阅特定任务
  const task = useTask(guid)

  const isDownloading = task?.status === 'downloading'
  const isCompleted = task?.status === 'completed'

  const onClick = useCallback(async () => {
    const downloadUrl = sideload[guid]
    if (!downloadUrl) {
      return
    }

    // 使用 DownloadManager 的 download 方法
    await download(guid, guid, downloadUrl)
  }, [guid, sideload, download])

  const onCancel = useCallback(() => {
    cancel(guid)
  }, [guid, cancel])

  // 计算是否处于加载状态
  const loading = useMemo(() => {
    return isDownloading && (task?.progress.percent ?? 0) < 100
  }, [isDownloading, task])

  const component = useMemo(() => {
    // Mod 已存在于本地
    if (mods?.[guid]) {
      return <Text style={{ color: 'green' }}>已在本地</Text>
    }

    // 下载中 - 显示进度
    if (isDownloading && task) {
      return (
        <div style={{ width: 100 }}>
          <Progress
            percent={Number(task.progress.percent.toFixed(1))}
            size="small"
            status="active"
            format={(percent) => `${percent?.toFixed(0)}%`}
          />
          <Button size="small" onClick={onCancel} danger block style={{ marginTop: 4 }}>
            取消
          </Button>
        </div>
      )
    }

    // 下载完成
    if (isCompleted) {
      return <Text style={{ color: 'green' }}>下载完成</Text>
    }

    // 可以下载
    if (sideload[guid]) {
      return (
        <Button onClick={onClick} loading={loading} size="small" type="primary">
          下载
        </Button>
      )
    }

    // 无法下载
    return <Text style={{ color: 'red' }}>无法下载</Text>
  }, [mods, guid, sideload, isDownloading, isCompleted, task, loading, onClick, onCancel])

  return component
}

export default ModStatus
