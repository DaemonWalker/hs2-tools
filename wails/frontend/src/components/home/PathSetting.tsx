import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { usePathSelector } from '@renderer/hooks/usePathSelector'
import { Button, Input, Space, message, Tooltip, Badge } from 'antd'
import { FC, useEffect, useState } from 'react'
import { FolderOpenOutlined, CheckCircleOutlined, ExclamationCircleOutlined } from '@ant-design/icons'

const { fileExists } = ipcUtils

export const PathSetting: FC = () => {
  const { settings, setPath } = useSettingStore()
  const [pathValid, setPathValid] = useState<boolean | null>(null)
  const [checking, setChecking] = useState(false)

  const { openSelectPath, loading } = usePathSelector({
    onSelect: async (selectedPath) => {
      await setPath(selectedPath)
      message.success('游戏路径已更新')
    }
  })

  // 验证路径有效性
  useEffect(() => {
    if (!settings?.path) {
      setPathValid(null)
      return
    }

    const checkPath = async () => {
      setChecking(true)
      try {
        const gameExists = await fileExists(`${settings.path}/HoneySelect2.exe`)
        setPathValid(gameExists)
      } catch {
        setPathValid(false)
      } finally {
        setChecking(false)
      }
    }

    checkPath()
  }, [settings?.path])

  const getStatusIcon = () => {
    if (checking) return <Badge status="processing" />
    if (pathValid === null) return <Badge status="warning" />
    if (pathValid) return <CheckCircleOutlined style={{ color: 'var(--color-success)' }} />
    return <ExclamationCircleOutlined style={{ color: 'var(--color-error)' }} />
  }

  const getStatusText = () => {
    if (checking) return '检查中...'
    if (pathValid === null) return '未设置'
    if (pathValid) return '路径有效'
    return '路径无效'
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="small">
      <Button icon={<FolderOpenOutlined />} onClick={openSelectPath} loading={loading} block aria-label="选择游戏路径">
        选择游戏路径
      </Button>
      <Input
        value={settings?.path}
        disabled
        placeholder="请选择 HS2 游戏根目录"
        size="small"
        aria-label="当前游戏路径"
        suffix={
          <Tooltip title={getStatusText()}>
            <span aria-label={`路径状态: ${getStatusText()}`}>{getStatusIcon()}</span>
          </Tooltip>
        }
      />
    </Space>
  )
}

export default PathSetting
