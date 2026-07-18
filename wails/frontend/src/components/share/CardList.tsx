import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { Pagination, Button } from 'antd'
import { FC, useEffect, useMemo, useState, useCallback } from 'react'
import { ReloadOutlined } from '@ant-design/icons'

const { getAllFiles, readPngPageDataBatch } = ipcUtils

interface IProps {
  columnCount: number
  type: 'chara' | 'scene'
}

// 单个卡片的数据
interface CardData {
  path: string
  names: string[]
  imageData: string
}

export const CardList: FC<IProps> = ({ columnCount, type }) => {
  const { settings, scenePath, charaFemalePath } = useSettingStore()
  
  const charaPath = useMemo(() => {
    const path = type === 'chara' ? charaFemalePath() : scenePath()
    return settings.path ? path : undefined
  }, [settings.path, type])

  const [current, setCurrent] = useState(1)
  const [pageSize, setPageSize] = useState(20)
  const [allCards, setAllCards] = useState<string[]>([])
  const [pageData, setPageData] = useState<Map<string, CardData>>(new Map())
  const [loading, setLoading] = useState(false)

  // 当前页的文件列表
  const currentPageFiles = useMemo(() => {
    return allCards.slice((current - 1) * pageSize, current * pageSize)
  }, [allCards, current, pageSize])

  // 加载文件列表
  const loadFiles = useCallback(async () => {
    if (!charaPath) {
      setAllCards([])
      return
    }
    setLoading(true)
    try {
      const files = await getAllFiles(charaPath, { targetExtension: ['.png'] })
      setAllCards(files)
      // 重置到第一页
      setCurrent(1)
      // 清空已加载的页面数据
      setPageData(new Map())
    } finally {
      setLoading(false)
    }
  }, [charaPath])

  // 加载当前页的数据（批量获取名称和缩略图）
  const loadPageData = useCallback(async () => {
    if (currentPageFiles.length === 0) return

    // 过滤掉已缓存的文件
    const filesToLoad = currentPageFiles.filter((path) => !pageData.has(path))
    if (filesToLoad.length === 0) return

    try {
      // 批量获取当前页所有数据（名称 + 缩略图）
      const results = await readPngPageDataBatch(filesToLoad)
      
      // 更新缓存
      setPageData((prev) => {
        const next = new Map(prev)
        results.forEach((item) => {
          next.set(item.path, {
            path: item.path,
            names: item.names,
            imageData: item.imageData
          })
        })
        return next
      })
    } catch (e) {
      console.error('Failed to load page data:', e)
    }
  }, [currentPageFiles, pageData])

  // 首次加载文件列表
  useEffect(() => {
    loadFiles()
  }, [loadFiles])

  // 当前页变化时加载数据
  useEffect(() => {
    loadPageData()
  }, [loadPageData])

  // 处理分页变化
  const handlePageChange = (page: number, newPageSize: number) => {
    setCurrent(page)
    setPageSize(newPageSize)
  }

  // 渲染卡片网格
  const renderCardGrid = () => {
    return (
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: `repeat(${columnCount}, 1fr)`,
          gap: 16,
          overflow: 'auto',
          flex: 1,
          padding: '8px 4px',
          alignContent: 'start'
        }}
      >
        {currentPageFiles.map((filePath, index) => {
          const data = pageData.get(filePath)
          return (
            <CardItem
              key={filePath}
              filePath={filePath}
              data={data}
              index={index}
              type={type}
            />
          )
        })}
      </div>
    )
  }

  if (!charaPath) {
    return (
      <div style={{ height: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <div>请先设置游戏路径</div>
      </div>
    )
  }

  return (
    <div
      style={{
        height: '100%',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: 10
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'flex-end', padding: '0 10px' }}>
        <Button icon={<ReloadOutlined />} onClick={loadFiles} loading={loading} size="small">
          刷新
        </Button>
      </div>

      {allCards.length === 0 ? (
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <div>暂无数据</div>
        </div>
      ) : (
        <>
          {renderCardGrid()}
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Pagination
              showQuickJumper
              current={current}
              total={allCards.length}
              onChange={handlePageChange}
              onShowSizeChange={handlePageChange}
              pageSize={pageSize}
              pageSizeOptions={[12, 20, 40]}
            />
          </div>
        </>
      )}
    </div>
  )
}

// 单个卡片组件
interface CardItemProps {
  filePath: string
  data?: CardData
  index: number
  type: 'chara' | 'scene'
}

import { useNavigate } from 'react-router-dom'
import { Spin } from 'antd'

const CardItem: FC<CardItemProps> = ({ filePath, data, index }) => {
  const navigate = useNavigate()
  const displayName = data?.names?.[0] || ''
  const imageData = data?.imageData

  return (
    <div
      className="animate-fadeIn"
      style={{
        cursor: 'pointer',
        animationDelay: `${(index % 8) * 0.05}s`
      }}
      onClick={() => navigate(encodeURI(filePath))}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          navigate(encodeURI(filePath))
        }
      }}
      tabIndex={0}
      role="button"
      aria-label={`查看角色: ${displayName || '未知角色'}`}
    >
      <div className="png-thumbnail">
        <div className="png-thumbnail-header">{displayName}</div>
        {imageData ? (
          <img
            src={`data:image/png;base64,${imageData}`}
            style={{ maxWidth: '100%', borderRadius: 8 }}
            alt={displayName}
          />
        ) : (
          <div style={{ display: 'flex', justifyContent: 'center', padding: 20 }}>
            <Spin size="small" />
          </div>
        )}
      </div>
    </div>
  )
}
