import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { usePathSelector } from '@renderer/hooks/usePathSelector'
import { Button, Input, Space, message, Typography, Card, Tag, Tooltip } from 'antd'
import { FC, useEffect, useState } from 'react'
import {
  FolderOpenOutlined,
  PlayCircleOutlined,
  BuildOutlined,
  CheckCircleOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined
} from '@ant-design/icons'

const { Text } = Typography
const { launchGame, launchStudio, fileExists } = ipcUtils

export const Settings: FC = () => {
  const { settings, setPath } = useSettingStore()
  const [pathValid, setPathValid] = useState<boolean | null>(null)
  const [checking, setChecking] = useState(false)
  const [launching, setLaunching] = useState<'game' | 'studio' | null>(null)

  const { openSelectPath, loading: selecting } = usePathSelector({
    onSelect: async (path) => {
      await setPath(path)
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
        // 检查游戏主程序是否存在
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

  const handleLaunchGame = async () => {
    if (!settings?.path) {
      message.warning('请先设置游戏路径')
      return
    }
    setLaunching('game')
    try {
      await launchGame()
      message.success('游戏已启动')
    } catch (error) {
      message.error(error instanceof Error ? error.message : '启动失败')
    } finally {
      setLaunching(null)
    }
  }

  const handleLaunchStudio = async () => {
    if (!settings?.path) {
      message.warning('请先设置游戏路径')
      return
    }
    setLaunching('studio')
    try {
      await launchStudio()
      message.success('工作室已启动')
    } catch (error) {
      message.error(error instanceof Error ? error.message : '启动失败')
    } finally {
      setLaunching(null)
    }
  }

  const getPathStatusTag = () => {
    if (checking) {
      return (
        <Tag icon={<LoadingOutlined />} color="processing">
          检查中...
        </Tag>
      )
    }
    if (pathValid === null) {
      return (
        <Tag icon={<ExclamationCircleOutlined />} color="warning">
          未设置路径
        </Tag>
      )
    }
    if (pathValid) {
      return (
        <Tag icon={<CheckCircleOutlined />} color="success">
          路径有效
        </Tag>
      )
    }
    return (
      <Tag icon={<ExclamationCircleOutlined />} color="error">
        路径无效
      </Tag>
    )
  }

  return (
    <Card title="游戏路径设置" style={{ width: '100%' }}>
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        <Space wrap>
          <Button
            icon={<FolderOpenOutlined />}
            onClick={openSelectPath}
            loading={selecting}
            type="primary"
          >
            选择游戏路径
          </Button>
          {getPathStatusTag()}
        </Space>

        <Input.TextArea
          value={settings?.path || ''}
          disabled
          placeholder="请选择 HoneySelect2.exe 所在目录"
          autoSize={{ minRows: 1, maxRows: 3 }}
          style={{ fontFamily: 'monospace' }}
        />

        {pathValid === false && (
          <Text type="warning">
            该路径下未找到 HoneySelect2.exe，请确认路径是否正确
          </Text>
        )}

        <Space style={{ marginTop: 16 }}>
          <Tooltip title={!pathValid ? '请先设置有效的游戏路径' : ''}>
            <Button
              icon={<PlayCircleOutlined />}
              size="large"
              style={{ height: 60, width: 160 }}
              onClick={handleLaunchGame}
              loading={launching === 'game'}
              disabled={!pathValid}
              type="primary"
            >
              开始游戏
            </Button>
          </Tooltip>
          <Tooltip title={!pathValid ? '请先设置有效的游戏路径' : ''}>
            <Button
              icon={<BuildOutlined />}
              size="large"
              style={{ height: 60, width: 160 }}
              onClick={handleLaunchStudio}
              loading={launching === 'studio'}
              disabled={!pathValid}
            >
              开始工作室
            </Button>
          </Tooltip>
        </Space>
      </Space>
    </Card>
  )
}

export default Settings
