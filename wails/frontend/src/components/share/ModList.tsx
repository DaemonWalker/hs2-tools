import { FC, useEffect, useMemo, useState, memo } from 'react'
import { List, Card, Tag, Spin, Empty, Space } from 'antd'
import { useModsSelector } from '@renderer/store/modStore'
import ipcUtils from '@renderer/logic/ipcUtils'
import { DownloadButton } from './DownloadButton'
import { 
  CheckCircleOutlined, 
  DownloadOutlined,
  AppstoreOutlined 
} from '@ant-design/icons'

const { readPngForMod } = ipcUtils

interface IState {
  filePath: string
}

interface ModItem {
  name: string
  isLocal: boolean
}

// Memoized list item to prevent unnecessary re-renders
const ModListItem = memo(({ item }: { item: ModItem }) => (
  <List.Item
    style={{ 
      padding: '8px 16px',
      borderBottom: '1px solid var(--border-color)'
    }}
  >
    <div style={{ 
      width: '100%', 
      display: 'flex',
      alignItems: 'center'
    }}>
      <div style={{ 
        flex: 1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        marginRight: 8
      }}>
        {item.name}
      </div>
      <div>
        <DownloadButton modName={item.name} isLocal={item.isLocal} />
      </div>
    </div>
  </List.Item>
))

ModListItem.displayName = 'ModListItem'

export const ModList: FC<IState> = memo(({ filePath }) => {
  const [mods, setMods] = useState<string[] | undefined>(undefined)
  const [loading, setLoading] = useState(true)
  const localMods = useModsSelector()

  useEffect(() => {
    setLoading(true)
    readPngForMod(filePath).then((data) => {
      setMods(data)
      setLoading(false)
    })
  }, [filePath])

  const data: ModItem[] = useMemo(
    () => mods?.map((p) => ({ name: p, isLocal: !!localMods?.[p] })) || [],
    [mods, localMods]
  )

  const localCount = useMemo(() => data.filter(item => item.isLocal).length, [data])

  if (loading) {
    return (
      <Card className="theme-card" size="small">
        <div style={{ padding: 24, textAlign: 'center' }}>
          <Spin aria-label="加载模组依赖中" />
        </div>
      </Card>
    )
  }

  if (!data || data.length === 0) {
    return (
      <Card className="theme-card" size="small">
        <Empty description="未检测到模组依赖" />
      </Card>
    )
  }

  return (
    <Card 
      className="theme-card" 
      size="small"
      style={{ height: '100%', display: 'flex', flexDirection: 'column' }}
      title={
        <Space>
          <AppstoreOutlined />
          <span>模组依赖</span>
          <Tag color="blue">{data.length} 个</Tag>
          {localCount > 0 && (
            <Tag color="green">
              <CheckCircleOutlined /> {localCount} 已拥有
            </Tag>
          )}
          {localCount < data.length && (
            <Tag color="orange">
              <DownloadOutlined /> {data.length - localCount} 需下载
            </Tag>
          )}
        </Space>
      }
      bodyStyle={{ 
        padding: 0,
        height: 'calc(100% - 46px)',  // 减去 Card header 高度
        overflow: 'auto'
      }}
    >
      <List
        size="small"
        dataSource={data}
        renderItem={(item) => <ModListItem item={item} />}
      />
    </Card>
  )
})

ModList.displayName = 'ModList'
