import { useModStore } from '@renderer/store/modStore'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { Button, Space, Typography, Alert } from 'antd'
import { FC, useMemo, useCallback } from 'react'
import ipcUtils from '@renderer/logic/ipcUtils'
import { ProgressModal } from '@renderer/modals/ProgressModal'
import { useDownloadStore } from '@renderer/store/downloadStore'
import { useDownloadProgress } from '@renderer/hooks/useDownloadProgress'
import { DownloadOutlined } from '@ant-design/icons'

const { triggerDownload } = ipcUtils
const { Text } = Typography

export const SideloadInit: FC = () => {
  const { sideload } = useSideloadStore()
  const { mods, useage } = useModStore()
  const { startBatch, startTask, nextTask, reset } = useDownloadStore()

  const downloadList = useMemo(
    () =>
      Object.keys(useage)
        .filter((k) => !mods[k] && !!sideload[k])
        .map((k) => ({ name: k, url: sideload[k] })),
    [sideload, mods, useage]
  )

  useDownloadProgress({
    onProgress: (guid, progress) => {
      console.log(`[Download] ${guid}: ${progress.percent.toFixed(1)}%`)
    },
    onComplete: (guid, success, message) => {
      console.log(`[Download] ${guid}: ${success ? '完成' : '失败'}`, message || '')
    },
    onError: (guid, message) => {
      console.error(`[Download] ${guid} 错误:`, message)
    }
  })

  const onClick = useCallback(async () => {
    if (downloadList.length === 0) return

    startBatch(downloadList.length)

    try {
      for (let i = 0; i < downloadList.length; i++) {
        const mod = downloadList[i]
        startTask(mod.name, mod.name)
        try {
          await triggerDownload(mod)
        } catch (error) {
          console.error(`[SideloadInit] 下载失败: ${mod.name}`, error)
        }
        nextTask()
      }
    } finally {
      setTimeout(() => {
        reset()
      }, 1500)
    }
  }, [downloadList, startBatch, startTask, nextTask, reset])

  if (downloadList.length === 0) {
    return (
      <Alert
        message="所有 Mods 已就绪"
        description="当前没有需要补全的缺失 Mods"
        type="success"
        showIcon
        style={{ padding: '12px 16px' }}
      />
    )
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <ProgressModal />
      <Button 
        type="primary" 
        onClick={onClick}
        icon={<DownloadOutlined />}
        size="large"
        block
        style={{ height: 44 }}
      >
        补全缺失 {downloadList.length} 个 Mods
      </Button>
      <Text type="secondary" style={{ fontSize: 12 }}>
        检测到 {downloadList.length} 个角色/场景依赖的 Mods 尚未下载
      </Text>
    </Space>
  )
}

export default SideloadInit
