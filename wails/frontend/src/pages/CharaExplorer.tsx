import { FC, useState, useCallback, useRef, memo } from 'react'
import { CardGrid, CardGridRef, SortType } from '@renderer/components/share/CardGrid'
import { CharaDetailDrawer } from '@renderer/components/character/CharaDetailDrawer'
import { Input, Button, Space, Drawer, Dropdown } from 'antd'
import { SortAscendingOutlined, FilterOutlined, ReloadOutlined, DownOutlined, HeartFilled, FolderOutlined } from '@ant-design/icons'

const sortOptions = [
  { key: 'favorite', label: '收藏优先', icon: <HeartFilled style={{ color: 'var(--color-error)' }} /> },
  { key: 'nameAsc', label: '名称 A-Z', icon: <SortAscendingOutlined /> },
  { key: 'nameDesc', label: '名称 Z-A', icon: <SortAscendingOutlined style={{ transform: 'rotate(180deg)' }} /> },
  { key: 'path', label: '路径排序', icon: <FolderOutlined /> },
]

export const CharaExplorer: FC = memo(() => {
  const [selectedCard, setSelectedCard] = useState<string | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [totalCount, setTotalCount] = useState(0)
  const [isLoading, setIsLoading] = useState(false)
  const [sortType, setSortType] = useState<SortType>('favorite')
  const cardGridRef = useRef<CardGridRef>(null)

  // 处理卡片点击
  const handleCardClick = useCallback((path: string) => {
    setSelectedCard(path)
    setDrawerOpen(true)
  }, [])

  // 关闭抽屉
  const handleCloseDrawer = useCallback(() => {
    setDrawerOpen(false)
    setTimeout(() => setSelectedCard(null), 300)
  }, [])

  return (
    <div className="chara-explorer">
      {/* 顶部工具栏 */}
      <div className="explorer-toolbar">
        <div className="toolbar-left">
          <span className="toolbar-title">女性角色</span>
        </div>
        
        <div className="toolbar-center">
          <Input.Search
            placeholder="搜索角色名称..."
            size="small"
            style={{ width: 220 }}
            enterButton
          />
        </div>

        <div className="toolbar-right">
          <Space size={8}>
            <span style={{ fontSize: 14, color: 'var(--text-secondary)' }}>
              共 {totalCount} 个角色
            </span>
            <Button 
              size="small" 
              icon={<ReloadOutlined />} 
              onClick={() => cardGridRef.current?.reload()}
              loading={isLoading}
            >
              刷新
            </Button>
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
            <Button size="small" icon={<FilterOutlined />}>筛选</Button>
          </Space>
        </div>
      </div>

      {/* 网格列表区域 */}
      <div className="explorer-grid-container">
        <CardGrid
          ref={cardGridRef}
          type="chara"
          onCardClick={handleCardClick}
          selectedPath={selectedCard}
          onTotalChange={setTotalCount}
          onLoadingChange={setIsLoading}
          sortType={sortType}
        />
      </div>

      {/* 底部抽屉详情 - 使用 Ant Design Drawer */}
      <Drawer
        title="角色详细信息"
        placement="bottom"
        height="70%"
        open={drawerOpen}
        onClose={handleCloseDrawer}
        maskClosable={true}
        className="chara-detail-drawer"
        styles={{
          body: { 
            padding: 0,
            height: 'calc(100% - 45px)',  /* 减去头部高度 */
            overflow: 'hidden'
          }
        }}
      >
        {selectedCard && <CharaDetailDrawer filePath={selectedCard} />}
      </Drawer>
    </div>
  )
})

CharaExplorer.displayName = 'CharaExplorer'

export default CharaExplorer
