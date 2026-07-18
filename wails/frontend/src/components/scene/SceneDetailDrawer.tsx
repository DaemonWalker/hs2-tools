import { FC, useEffect, useState } from 'react'
import { Button, Card, Tag, Empty, Spin, Space, Descriptions, Typography } from 'antd'
import {
  CheckCircleOutlined,
  DownloadOutlined,
  TagsOutlined,
  InfoCircleOutlined,
  AppstoreOutlined,
  FileOutlined,
  ClockCircleOutlined,
  FolderOpenOutlined
} from '@ant-design/icons'

import { PngViewer } from '../share/PngViewer'
import { useModStore } from '@renderer/store/modStore'
import { DownloadButton } from '../share/DownloadButton'
import ipcUtils from '@renderer/logic/ipcUtils'

interface IProps {
  filePath: string
}

export const SceneDetailDrawer: FC<IProps> = ({ filePath }) => {
  return (
    <div className="scene-drawer-content">
      {/* 顶部：大图预览（横向） */}
      <div className="scene-drawer-section scene-drawer-image">
        <PngViewer filePath={filePath} />
      </div>

      {/* 中部：基本信息 */}
      <div className="scene-drawer-section scene-drawer-info">
        <SceneInfoCompact filePath={filePath} />
      </div>

      {/* 底部：模组依赖 */}
      <div className="scene-drawer-section scene-drawer-mods">
        <ModListCompact filePath={filePath} />
      </div>
    </div>
  )
}

// 紧凑版场景信息组件
const SceneInfoCompact: FC<{ filePath: string }> = ({ filePath }) => {
  const [info, setInfo] = useState<(string | undefined)[]>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setError(null)
    ipcUtils.readAllCharaNames(filePath)
      .then((data) => {
        setInfo(data)
        setLoading(false)
      })
      .catch((err) => {
        console.error('Failed to read scene info:', err)
        setError('读取场景信息失败')
        setLoading(false)
      })
  }, [filePath])

  const handleOpenFolder = () => {
    ipcUtils.openInFolder(filePath)
  }

  if (loading) {
    return (
      <Card className="drawer-card" size="small">
        <div style={{ padding: 24, textAlign: 'center' }}>
          <Spin size="small" />
        </div>
      </Card>
    )
  }

  if (error) {
    return (
      <Card className="drawer-card" size="small">
        <Empty description={error} image={Empty.PRESENTED_IMAGE_SIMPLE} />
      </Card>
    )
  }

  if (!info || info.length === 0) {
    return (
      <Card className="drawer-card" size="small">
        <Empty description="无法读取场景信息" />
      </Card>
    )
  }

  return (
    <Card
      className="drawer-card"
      size="small"
      title={
        <span>
          <InfoCircleOutlined style={{ marginRight: 'var(--spacing-xs)' }} />
          基本信息
        </span>
      }
    >
      <Descriptions column={1} size="small" labelStyle={{ width: 60, color: 'var(--text-secondary)' }}>
        <Descriptions.Item label="名称">
          <Tag color="blue">{info[0] || '未知'}</Tag>
        </Descriptions.Item>

        {info[1] && (
          <Descriptions.Item label="描述">
            <div 
              className="line-clamp-3"
              style={{ 
                fontSize: 12,
                lineHeight: 1.5,
                color: 'var(--text-secondary)',
                wordBreak: 'break-word'
              }}
              title={info[1]}
            >
              {info[1]}
            </div>
          </Descriptions.Item>
        )}
      </Descriptions>

      <div style={{ marginTop: 'var(--spacing-md)' }}>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 6 }}>
          <TagsOutlined style={{ marginRight: 'var(--spacing-xs)' }} />
          标签
        </div>
        <Space size={4} wrap>
          <Tag color="cyan">场景</Tag>
          <Tag color="purple">收藏</Tag>
          <Button type="dashed" size="small" style={{ fontSize: 12 }}>
            + 添加
          </Button>
        </Space>
      </div>

      <div style={{ marginTop: 'var(--spacing-md)', paddingTop: 'var(--spacing-sm)', borderTop: '1px solid var(--border-color)' }}>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)', marginBottom: 8 }}>
          <FileOutlined style={{ marginRight: 'var(--spacing-xs)' }} />
          文件信息
        </div>
        <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
          <div style={{ marginBottom: 8 }}>
            <div style={{ marginBottom: 4 }}>文件路径：</div>
            <div 
              className="text-ellipsis"
              style={{ 
                padding: 'var(--spacing-xs) var(--spacing-sm)', 
                background: 'var(--bg-secondary)', 
                borderRadius: 'var(--radius-sm)',
                fontSize: 11,
                lineHeight: 1.5,
                fontFamily: 'monospace'
              }}
              title={filePath}
            >
              {filePath}
            </div>
            <Button 
              type="primary" 
              size="small" 
              icon={<FolderOpenOutlined />}
              onClick={handleOpenFolder}
              style={{ marginTop: 8, fontSize: 12 }}
            >
              打开所在文件夹
            </Button>
          </div>
          <div style={{ marginBottom: 4 }}>
            <ClockCircleOutlined style={{ marginRight: 'var(--spacing-xs)' }} />
            修改时间：2024-01-15
          </div>
          <div>
            <FileOutlined style={{ marginRight: 'var(--spacing-xs)' }} />
            文件大小：2.5 MB
          </div>
        </div>
      </div>
    </Card>
  )
}

// 紧凑版模组列表
const ModListCompact: FC<{ filePath: string }> = ({ filePath }) => {
  const [mods, setMods] = useState<string[] | undefined>(undefined)
  const [loading, setLoading] = useState(true)
  const { mods: localMods } = useModStore()

  useEffect(() => {
    setLoading(true)
    ipcUtils.readPngForMod(filePath).then((data) => {
      setMods(data)
      setLoading(false)
    })
  }, [filePath])

  const data = mods?.map((p) => ({ name: p, isLocal: !!localMods?.[p] })) || []
  const localCount = data.filter((item) => item.isLocal).length
  const missingCount = data.length - localCount

  if (loading) {
    return (
      <Card className="drawer-card" size="small">
        <div style={{ padding: 24, textAlign: 'center' }}>
          <Spin size="small" />
        </div>
      </Card>
    )
  }

  if (!data || data.length === 0) {
    return (
      <Card className="drawer-card" size="small">
        <Empty description="未检测到模组依赖" />
      </Card>
    )
  }

  return (
    <Card
      className="drawer-card mod-list-card"
      size="small"
      title={
        <Space>
          <AppstoreOutlined />
          <span>模组依赖</span>
          <Tag color="blue">{data.length} 个</Tag>
        </Space>
      }
      extra={
        missingCount > 0 && (
          <Button type="primary" size="small" icon={<DownloadOutlined />}>
            一键下载 ({missingCount})
          </Button>
        )
      }
    >
      {/* 统计信息 */}
      <div style={{ marginBottom: 'var(--spacing-sm)', display: 'flex', gap: 'var(--spacing-sm)', flexShrink: 0 }}>
        <Tag color="success" icon={<CheckCircleOutlined />}>
          已拥有 {localCount}
        </Tag>
        {missingCount > 0 && (
          <Tag color="warning" icon={<DownloadOutlined />}>
            缺少 {missingCount}
          </Tag>
        )}
      </div>

      {/* 模组列表 */}
      <div className="mod-list-container">
        {data.map((item) => (
          <div
            key={item.name}
            className="mod-list-item"
          >
            <Typography.Text
              copyable
              style={{
                flex: 1,
                fontSize: 12,
                color: 'var(--text-primary)'
              }}
              ellipsis={{ tooltip: item.name }}
            >
              {item.name}
            </Typography.Text>
            <DownloadButton modName={item.name} isLocal={item.isLocal} />
          </div>
        ))}
      </div>
    </Card>
  )
}
