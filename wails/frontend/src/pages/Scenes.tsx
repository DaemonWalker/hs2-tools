import { FC, useState, useCallback, useRef, memo } from 'react'
import { CardGrid, CardGridRef, SortType } from '@renderer/components/share/CardGrid'
import { SceneDetailDrawer } from '@renderer/components/scene/SceneDetailDrawer'
import { SceneOrganizer } from '@renderer/components/scene/SceneOrganizer'
import { 
  Input, 
  Button, 
  Space, 
  Drawer, 
  Dropdown, 
  Badge,
  Tooltip,
  Segmented
} from 'antd'
import { 
  SortAscendingOutlined, 
  FilterOutlined, 
  ReloadOutlined, 
  DownOutlined, 
  HeartFilled, 
  FolderOutlined,
  AppstoreOutlined,
  BuildOutlined,
  InboxOutlined,
  SearchOutlined
} from '@ant-design/icons'

const sortOptions = [
  { key: 'favorite', label: '收藏优先', icon: <HeartFilled style={{ color: 'var(--color-error)' }} /> },
  { key: 'nameAsc', label: '名称 A-Z', icon: <SortAscendingOutlined /> },
  { key: 'nameDesc', label: '名称 Z-A', icon: <SortAscendingOutlined style={{ transform: 'rotate(180deg)' }} /> },
  { key: 'path', label: '路径排序', icon: <FolderOutlined /> },
]

type ViewType = 'gallery' | 'organize'

export const Scenes: FC = memo(() => {
  const [selectedScene, setSelectedScene] = useState<string | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(false)
  const [sortType, setSortType] = useState<SortType>('favorite')
  const [searchText, setSearchText] = useState('')
  const [viewType, setViewType] = useState<ViewType>('gallery')
  const cardGridRef = useRef<CardGridRef>(null)

  // 处理卡片点击
  const handleCardClick = useCallback((path: string) => {
    setSelectedScene(path)
    setDrawerOpen(true)
  }, [])

  // 关闭抽屉
  const handleCloseDrawer = useCallback(() => {
    setDrawerOpen(false)
    setTimeout(() => setSelectedScene(null), 300)
  }, [])

  // 处理搜索
  const handleSearch = useCallback((value: string) => {
    setSearchText(value)
    // TODO: 实现搜索过滤逻辑
  }, [])



  return (
    <div className="scene-explorer">
      {/* 顶部工具栏 */}
      <div className="explorer-toolbar">
        <div className="toolbar-left">
          <Space size={16} align="center">
            <span className="toolbar-title">
              <AppstoreOutlined style={{ marginRight: 8 }} />
              场景管理
            </span>
            <Segmented
              value={viewType}
              onChange={(value) => setViewType(value as ViewType)}
              options={[
                { 
                  label: '场景库', 
                  value: 'gallery',
                  icon: <AppstoreOutlined />
                },
                { 
                  label: '智能整理', 
                  value: 'organize',
                  icon: <BuildOutlined />
                },
              ]}
              size="small"
            />
          </Space>
        </div>
        
        <div className="toolbar-center">
          {viewType === 'gallery' && (
            <Input.Search
              placeholder="搜索场景名称、角色..."
              size="small"
              style={{ width: 280 }}
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              onSearch={handleSearch}
              allowClear
              prefix={<SearchOutlined style={{ color: 'var(--text-muted)' }} />}
            />
          )}
        </div>

        <div className="toolbar-right">
          <Space size={8}>
            {viewType === 'gallery' ? (
              <>
                <span style={{ fontSize: 14, color: 'var(--text-secondary)' }}>
                  共 <Badge count={totalCount} showZero color="var(--theme-primary)" style={{ margin: '0 4px' }} /> 个场景
                </span>
                <Tooltip title="刷新列表">
                  <Button 
                    size="small" 
                    icon={<ReloadOutlined />} 
                    onClick={() => cardGridRef.current?.reload()}
                    loading={isLoading}
                  />
                </Tooltip>
                <Dropdown
                  menu={{
                    items: sortOptions.map(opt => ({
                      key: opt.key,
                      label: opt.label,
                      icon: opt.icon,
                    })),
                    selectedKeys: [sortType],
                    onClick: ({ key }) => setSortType(key as SortType),
                  }}
                  placement="bottomRight"
                >
                  <Button size="small" icon={<SortAscendingOutlined />}>
                    排序 <DownOutlined style={{ fontSize: 10 }} />
                  </Button>
                </Dropdown>
                <Tooltip title="筛选功能即将推出">
                  <Button size="small" icon={<FilterOutlined />} disabled>
                    筛选
                  </Button>
                </Tooltip>
              </>
            ) : (
              <span style={{ fontSize: 14, color: 'var(--text-secondary)' }}>
                <InboxOutlined style={{ marginRight: 6 }} />
                按角色智能归档场景
              </span>
            )}
          </Space>
        </div>
      </div>

      {/* 主内容区域 */}
      <div className="explorer-content">
        {viewType === 'gallery' ? (
          <div className="explorer-grid-container">
            <CardGrid
              ref={cardGridRef}
              type="scene"
              onCardClick={handleCardClick}
              selectedPath={selectedScene}
              onTotalChange={setTotalCount}
              onLoadingChange={setIsLoading}
              sortType={sortType}
              searchText={searchText}
            />
          </div>
        ) : (
          <div className="organizer-container">
            <SceneOrganizer onOrganizeComplete={() => cardGridRef.current?.reload()} />
          </div>
        )}
      </div>

      {/* 右侧抽屉详情 */}
      <Drawer
        title="场景详细信息"
        placement="right"
        width={480}
        open={drawerOpen}
        onClose={handleCloseDrawer}
        maskClosable={true}
        className="scene-detail-drawer"
        styles={{
          body: { 
            padding: 0,
            height: 'calc(100% - 55px)',
            overflow: 'auto'
          }
        }}
      >
        {selectedScene && <SceneDetailDrawer filePath={selectedScene} />}
      </Drawer>
    </div>
  )
})

Scenes.displayName = 'Scenes'

export default Scenes
