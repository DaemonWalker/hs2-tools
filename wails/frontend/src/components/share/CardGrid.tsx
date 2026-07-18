import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { Spin, Badge, Divider, Empty } from 'antd'
import { Announcer } from './Announcer'
import {
  forwardRef,
  useImperativeHandle,
  useMemo,
  useRef,
  useCallback,
  memo,
  FC,
  useState,
  useEffect
} from 'react'
import { HeartOutlined, HeartFilled, PictureOutlined } from '@ant-design/icons'
import { useLocalStorageState, useRequest, useInfiniteScroll } from 'ahooks'

const { getAllFiles, readPngPageDataBatch } = ipcUtils

export type SortType = 'nameAsc' | 'nameDesc' | 'path' | 'favorite'

interface IProps {
  type: 'chara' | 'scene'
  onCardClick?: (path: string) => void
  selectedPath?: string | null
  onTotalChange?: (total: number) => void
  onLoadingChange?: (loading: boolean) => void
  sortType?: SortType
  searchText?: string
}

export interface CardGridRef {
  reload: () => void
  total: number
}

interface CardData {
  path: string
  names: string[]
  imageData: string
}

interface InfiniteScrollData {
  list: CardData[]
}

const FAVORITE_KEY = 'chara_favorites'
const PAGE_SIZE = 24

const normalizePath = (path: string): string => {
  return path.replace(/\\/g, '/').toLowerCase()
}

export const CardGrid = forwardRef<CardGridRef, IProps>(({
  type,
  onCardClick,
  selectedPath,
  onTotalChange,
  onLoadingChange,
  sortType = 'favorite',
  searchText = ''
}, ref) => {
  const { settings, scenePath, charaFemalePath } = useSettingStore()
  const containerRef = useRef<HTMLDivElement>(null)

  // 使用 ahooks 管理收藏
  const [favorites, setFavorites] = useLocalStorageState<string[]>(FAVORITE_KEY, {
    defaultValue: [],
  })

  const charaPath = type === 'chara' ? charaFemalePath() : scenePath()

  // 使用 ahooks useRequest 加载文件列表
  const { data: allPaths = [], loading, error, refresh } = useRequest(
    async () => {
      if (!charaPath || !settings.path) return []
      return getAllFiles(charaPath, { targetExtension: ['.png'] })
    },
    {
      refreshDeps: [charaPath, settings.path],
    }
  )

  // 过滤和排序后的路径
  const sortedPaths = useMemo(() => {
    let paths = [...allPaths]
    const normalizedFavorites = (favorites || []).map(normalizePath)

    // 搜索过滤
    if (searchText.trim()) {
      const searchLower = searchText.toLowerCase()
      paths = paths.filter(p => {
        const fileName = p.split(/[\\/]/).pop()?.replace(/\.png$/i, '').toLowerCase() || ''
        return fileName.includes(searchLower)
      })
    }

    switch (sortType) {
      case 'nameAsc':
        return paths.sort((a, b) => {
          const nameA = a.split(/[\\/]/).pop()?.replace(/\.png$/i, '') || ''
          const nameB = b.split(/[\\/]/).pop()?.replace(/\.png$/i, '') || ''
          return nameA.localeCompare(nameB, 'zh-CN')
        })

      case 'nameDesc':
        return paths.sort((a, b) => {
          const nameA = a.split(/[\\/]/).pop()?.replace(/\.png$/i, '') || ''
          const nameB = b.split(/[\\/]/).pop()?.replace(/\.png$/i, '') || ''
          return nameB.localeCompare(nameA, 'zh-CN')
        })

      case 'path':
        return paths.sort((a, b) => a.localeCompare(b, 'zh-CN'))

      default: {
        const favoritesList: string[] = []
        const nonFavoritesList: string[] = []

        paths.forEach(p => {
          if (normalizedFavorites.includes(normalizePath(p))) {
            favoritesList.push(p)
          } else {
            nonFavoritesList.push(p)
          }
        })

        const sortByName = (a: string, b: string) => {
          const nameA = a.split(/[\\/]/).pop()?.replace(/\.png$/i, '') || ''
          const nameB = b.split(/[\\/]/).pop()?.replace(/\.png$/i, '') || ''
          return nameA.localeCompare(nameB, 'zh-CN')
        }

        favoritesList.sort(sortByName)
        nonFavoritesList.sort(sortByName)

        return [...favoritesList, ...nonFavoritesList]
      }
    }
  }, [allPaths, favorites, sortType, searchText])

  // 通知总数变化 - 使用 useEffect 执行副作用
  useEffect(() => {
    onTotalChange?.(sortedPaths.length)
  }, [sortedPaths.length, onTotalChange])

  // 通知加载状态 - 使用 useEffect 执行副作用
  useEffect(() => {
    onLoadingChange?.(loading)
  }, [loading, onLoadingChange])

  // 延迟隐藏的 loading 状态
  const [showLoadingMore, setShowLoadingMore] = useState(false)

  // 使用 ahooks useInfiniteScroll 实现无限滚动
  const { data, loading: loadingMore, reload: reloadItems, loadMore } = useInfiniteScroll<InfiniteScrollData>(
    async (currentData) => {
      const startIndex = currentData?.list.length || 0
      const batch = sortedPaths.slice(startIndex, startIndex + PAGE_SIZE)

      if (batch.length === 0) return { list: [] }

      const results = await readPngPageDataBatch(batch)
      const newItems = results.map(item => ({
        path: item.path,
        names: item.names,
        imageData: item.imageData
      }))

      return { list: newItems }
    },
    {
      target: containerRef,
      isNoMore: (data) => (data?.list.length || 0) >= sortedPaths.length,
      reloadDeps: [sortedPaths],
      manual: true, // 手动控制，等 sortedPaths 准备好后再加载
      threshold: 200,
    }
  )

  // sortedPaths 准备好后，自动加载第一页
  useEffect(() => {
    if (sortedPaths.length > 0 && (!data || data.list.length === 0)) {
      loadMore()
    }
  }, [sortedPaths, data, loadMore])

  const items = data?.list || []

  // 控制显示/隐藏 loading（初始加载无延迟，滚动加载延迟隐藏）
  useEffect(() => {
    if (loadingMore) {
      // 立即显示
      setShowLoadingMore(true)
    }
  }, [loadingMore, items.length])

  // 暴露方法
  useImperativeHandle(ref, () => ({
    reload: () => {
      reloadItems()
      return refresh()
    },
    total: sortedPaths.length
  }), [refresh, sortedPaths.length, reloadItems])

  // 切换收藏
  const toggleFavorite = useCallback((path: string) => {
    const normPath = normalizePath(path)
    const exists = (favorites || []).some(p => normalizePath(p) === normPath)
    const newFavorites = exists
      ? (favorites || []).filter(p => normalizePath(p) !== normPath)
      : [path, ...(favorites || [])]
    setFavorites(newFavorites)
  }, [favorites, setFavorites])

  const normalizedFavorites = (favorites || []).map(normalizePath)

  // 空状态
  if (!settings.path || !charaPath) {
    return (
      <div className="h-full flex items-center justify-center p-6 text-center">
        <Empty description="请先设置游戏路径" image={Empty.PRESENTED_IMAGE_SIMPLE} />
      </div>
    )
  }

  if (error) {
    return (
      <div className="h-full flex items-center justify-center p-6">
        <Empty description="加载文件列表失败" image={Empty.PRESENTED_IMAGE_SIMPLE} />
      </div>
    )
  }

  if (loading && items.length === 0) {
    return (
      <div className="h-full flex flex-col items-center justify-center gap-4">
        <Spin size="large" />
        <span className="text-[var(--text-secondary)]">正在加载{type === 'scene' ? '场景' : '角色'}列表...</span>
      </div>
    )
  }

  if (sortedPaths.length === 0) {
    return (
      <div className="h-full flex items-center justify-center p-6">
        <Empty description="暂无数据" image={Empty.PRESENTED_IMAGE_SIMPLE} />
      </div>
    )
  }

  return (
    <div
      ref={containerRef}
      className="h-full w-full overflow-auto px-2"
    >
      <Announcer message={`共 ${sortedPaths.length} 个${type === 'scene' ? '场景' : '角色'}`} />
      <div className="card-grid">
        {items.map((item, index) => (
          <CardItem
            key={item.path}
            data={item}
            index={index}
            selected={selectedPath === item.path}
            isFavorite={normalizedFavorites.includes(normalizePath(item.path))}
            onClick={onCardClick}
            onToggleFavorite={toggleFavorite}
            type={type}
          />
        ))}
      </div>

      {showLoadingMore && (
        <div className="text-center py-6 flex flex-col items-center justify-center gap-3">
          <Spin size="default" />
          <span className="text-sm text-[var(--text-secondary)]">
            加载中... ({items.length}/{sortedPaths.length})
          </span>
        </div>
      )}

      {items.length >= sortedPaths.length && items.length > 0 && (
        <Divider plain className="!my-3 text-xs">
          已加载全部 {sortedPaths.length} 个{type === 'scene' ? '场景' : '角色'}
        </Divider>
      )}
    </div>
  )
})

interface CardItemProps {
  data: CardData
  index: number
  selected?: boolean
  isFavorite?: boolean
  onClick?: (path: string) => void
  onToggleFavorite?: (path: string) => void
  type?: 'chara' | 'scene'
}

const CardItem: FC<CardItemProps> = memo(({ data, index, selected, isFavorite, onClick, onToggleFavorite, type = 'chara' }) => {
  const [imgError, setImgError] = useState(false)
  const displayName = data.names?.[0] || ''

  const handleClick = useCallback(() => {
    onClick?.(data.path)
  }, [onClick, data.path])

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      onClick?.(data.path)
    }
  }, [onClick, data.path])

  const handleFavoriteClick = useCallback((e: React.MouseEvent) => {
    e.stopPropagation()
    onToggleFavorite?.(data.path)
  }, [onToggleFavorite, data.path])

  return (
    <div
      className={`card-item ${selected ? 'selected' : ''}`}
      style={{
        cursor: 'pointer',
        position: 'relative',
        animationDelay: index < 8 ? `${index * 0.05}s` : undefined
      }}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      tabIndex={0}
      role="button"
      aria-label={`查看${type === 'scene' ? '场景' : '角色'}: ${displayName || `未知${type === 'scene' ? '场景' : '角色'}`}`}
    >
      <div className="png-thumbnail relative">
        <div className="png-thumbnail-header">{displayName || '\u00A0'}</div>

        {/* 收藏按钮放在图片右上角 */}
        <button
          className="card-favorite-btn"
          onClick={handleFavoriteClick}
          style={{
            position: 'absolute',
            top: 32,
            right: -4,
            zIndex: 10,
            opacity: isFavorite ? 1 : 0,
            transition: 'opacity 0.2s'
          }}
          aria-label={isFavorite ? '取消收藏' : '添加收藏'}
          aria-pressed={isFavorite}
        >
          {isFavorite ? (
            <HeartFilled className="text-error text-base" />
          ) : (
            <HeartOutlined className="text-text-primary text-base" />
          )}
        </button>

        {data.imageData && !imgError ? (
          <img
            src={`data:image/png;base64,${data.imageData}`}
            className="max-w-full rounded-sm"
            style={{
              aspectRatio: type === 'scene' ? '16/9' : '2/3',
              objectFit: 'cover',
            }}
            alt={displayName || (type === 'scene' ? '场景卡预览' : '角色卡预览')}
            onError={() => setImgError(true)}
            decoding="async"
          />
        ) : (
          <div className="flex flex-col items-center justify-center bg-[var(--bg-tertiary)] rounded-sm text-[var(--text-secondary)]"
            style={{
              aspectRatio: type === 'scene' ? '16/9' : '2/3',
            }}
          >
            <PictureOutlined className="text-5xl mb-2" />
            <span className="text-xs">暂无预览</span>
          </div>
        )}
      </div>

      <div className="card-status-bar"><Badge status="success" text="完整" /></div>
    </div>
  )
}, (prevProps, nextProps) => {
  return (
    prevProps.data.path === nextProps.data.path &&
    prevProps.selected === nextProps.selected &&
    prevProps.isFavorite === nextProps.isFavorite &&
    prevProps.type === nextProps.type
  )
})

CardItem.displayName = 'CardItem'
