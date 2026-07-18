import { useSideloadStore } from '@renderer/store/sideloadStore'
import { Button, Progress, Tag, message } from 'antd'
import { FC, useCallback } from 'react'
import { DownloadOutlined, CheckCircleOutlined } from '@ant-design/icons'
import { useDownloadManager, useTask } from '@renderer/hooks/useDownloadManager'

interface IProps {
  modName: string
  isLocal: boolean
}

/**
 * 单个 Mod 下载按钮
 * 支持显示下载进度
 * 
 * 三种状态：
 * 1. mod 存在 (isLocal=true) -> 显示绿色对号
 * 2. mod 不存在但在 sideload 中 -> 显示下载按钮
 * 3. mod 不存在且不在 sideload 中 -> 显示不可用按钮
 */
export const DownloadButton: FC<IProps> = ({ modName, isLocal }) => {
  const { getDownloadUrl } = useSideloadStore()
  const downloadUrl = getDownloadUrl(modName)

  const { download, cancel } = useDownloadManager()

  // 使用 useTask 订阅特定任务
  const task = useTask(modName)

  const isDownloading = task?.status === 'downloading'
  const isCompleted = task?.status === 'completed'

  // 开始下载
  const onClick = useCallback(async () => {
    console.log(`[DownloadButton] onClick called, modName=${modName}, downloadUrl=${downloadUrl}`)
    
    if (!downloadUrl) {
      console.warn(`[DownloadButton] No downloadUrl available for ${modName}`)
      message.error('没有可用的下载链接')
      return
    }

    console.log(`[DownloadButton] Calling download(${modName}, ${modName}, ${downloadUrl})`)
    
    try {
      // 使用 DownloadManager 的 download 方法
      const success = await download(modName, modName, downloadUrl)
      console.log(`[DownloadButton] download returned: ${success}`)
      
      if (success) {
        message.success('已经添加下载任务')
      } else {
        console.warn(`[DownloadButton] download returned false for ${modName}`)
        message.error('下载启动失败')
      }
    } catch (error) {
      console.error(`[DownloadButton] download threw error:`, error)
      message.error(`下载出错: ${error}`)
    }
  }, [modName, downloadUrl, download])

  // 取消下载
  const onCancel = useCallback(async () => {
    await cancel(modName)
  }, [modName, cancel])

  // 显示进度条（下载中）
  if (isDownloading && task) {
    return (
      <div style={{ width: 120 }}>
        <Progress
          percent={Number(task.progress.percent.toFixed(1))}
          size="small"
          status="active"
          format={(percent) => `${percent?.toFixed(0)}%`}
        />
        <Button size="small" onClick={onCancel} style={{ marginTop: 4 }} danger block aria-label={`取消下载 ${modName}`}>
          取消
        </Button>
      </div>
    )
  }

  // 显示完成状态（下载已完成）
  if (isCompleted) {
    return (
      <Button icon={<CheckCircleOutlined />} disabled type="text" style={{ color: 'var(--color-success)' }} aria-label={`${modName} 下载已完成`}>
        已完成
      </Button>
    )
  }

  // mod 已存在本地 -> 显示绿色对号
  if (isLocal) {
    return (
      <Tag color="success" icon={<CheckCircleOutlined />}>
        已拥有
      </Tag>
    )
  }

  // mod 不在 sideload 中 -> 显示不可用按钮
  if (!downloadUrl) {
    return (
      <Button
        icon={<DownloadOutlined />}
        disabled
        type="primary"
        size="small"
        aria-label={`无法下载 ${modName}`}
      >
        下载
      </Button>
    )
  }

  // mod 在 sideload 中且未下载 -> 显示下载按钮
  return (
    <Button
      icon={<DownloadOutlined />}
      onClick={onClick}
      type="primary"
      size="small"
      aria-label={`下载 ${modName}`}
    >
      下载
    </Button>
  )
}

export default DownloadButton
