import ipcUtils from '@renderer/logic/ipcUtils'
import { Spin } from 'antd'
import { PictureOutlined } from '@ant-design/icons'
import { FC, useEffect, useState, CSSProperties } from 'react'
import { ModList } from './ModList'
import { CharaInfo } from '../character/CharaInfo'
import { SceneInfo } from '../scene/SceneInfo'

const { readPngForShow } = ipcUtils

interface SmartCardLayoutProps {
  filePath: string
  type: 'chara' | 'scene'
}

type LayoutType = 'vertical' | 'horizontal' | 'loading'

interface ImageInfo {
  base64: string
}

/**
 * 智能卡片布局组件
 * 根据卡片类型切换布局：
 * - 人物卡 (chara)：左右布局，图片在左侧
 * - 场景卡 (scene)：上下布局，图片在上部
 */
export const SmartCardLayout: FC<SmartCardLayoutProps> = ({ filePath, type }) => {
  const [imageInfo, setImageInfo] = useState<ImageInfo | null>(null)
  const [layout, setLayout] = useState<LayoutType>('loading')
  const [imgError, setImgError] = useState(false)

  // 根据类型确定布局
  useEffect(() => {
    setLayout('loading')
    setImgError(false)
    setImageInfo(null)

    readPngForShow(filePath).then((base64) => {
      if (!base64) {
        setImgError(true)
      } else {
        setImageInfo({ base64 })
      }
      // 根据类型设置布局：人物卡=纵向，场景卡=横向
      setLayout(type === 'scene' ? 'horizontal' : 'vertical')
    })
  }, [filePath, type])

  // 渲染图片
  const renderImage = () => {
    if (layout === 'loading' && !imageInfo) {
      return (
        <div style={centerStyle}>
          <Spin aria-label="加载图片中" />
        </div>
      )
    }

    if (imgError || !imageInfo) {
      return (
        <div style={emptyStyle}>
          <PictureOutlined className="text-6xl mb-4" />
          <span className="text-sm">暂无预览</span>
        </div>
      )
    }

    return (
      <img
        src={`data:image/png;base64,${imageInfo.base64}`}
        className="h-full w-auto max-w-full object-contain rounded-lg"
        style={{ contentVisibility: 'auto' }}
        alt={type === 'chara' ? '人物卡预览' : '场景卡预览'}
        decoding="async"
      />
    )
  }

  // 渲染信息面板
  const renderInfo = () => {
    return type === 'chara' ? (
      <CharaInfo filePath={filePath} />
    ) : (
      <SceneInfo filePath={filePath} />
    )
  }

  // 渲染场景人物列表（场景卡专用）
  const renderSceneCharaList = () => {
    return <SceneInfo filePath={filePath} />
  }

  // 渲染模组列表
  const renderModList = () => {
    return <ModList filePath={filePath} />
  }

  // 纵向布局：左右分栏
  if (layout === 'vertical') {
    return (
      <div style={verticalLayoutStyle}>
        {/* 左侧：图片 */}
        <div style={verticalImageStyle}>{renderImage()}</div>
        {/* 右侧：信息 + 模组 */}
        <div style={verticalContentStyle}>
          <div style={infoSectionStyle}>{renderInfo()}</div>
          <div style={modListSectionStyle}>{renderModList()}</div>
        </div>
      </div>
    )
  }

  // 横向布局：左右两列（场景卡专用）
  // 左侧列：图片 + 场景人物列表
  // 右侧列：模组依赖
  if (layout === 'horizontal') {
    return (
      <div style={sceneLayoutStyle}>
        {/* 左侧列：图片（上）+ 场景人物（下） */}
        <div style={sceneLeftColumnStyle}>
          <div style={sceneImageStyle}>{renderImage()}</div>
          <div style={sceneCharaListStyle}>{renderSceneCharaList()}</div>
        </div>
        {/* 右侧列：模组列表 */}
        <div style={sceneModColumnStyle}>{renderModList()}</div>
      </div>
    )
  }

  // 加载中
  return (
    <div style={centerStyle}>
      <Spin size="large" aria-label="加载中" />
    </div>
  )
}

// 样式定义
const centerStyle: CSSProperties = {
  height: '100%',
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center'
}

const emptyStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  height: '100%',
  width: '100%',
  aspectRatio: '2/3',
  background: 'var(--bg-tertiary)',
  borderRadius: 8,
  color: 'var(--text-secondary)'
}

// 纵向布局样式（左右分栏）
const verticalLayoutStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  height: '100%',
  minHeight: 0,
  gap: 16
}

const verticalImageStyle: CSSProperties = {
  flex: '0 0 auto',  // 不伸缩，宽度由内容决定
  height: '100%',
  minHeight: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'flex-start',  // 左对齐，消除居中留白
  overflow: 'hidden'
}

const verticalContentStyle: CSSProperties = {
  flex: 1,
  minHeight: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 12
}

// 场景卡布局样式（左右两列）
// 左侧：图片（上）+ 场景人物列表（下）
// 右侧：模组依赖
const sceneLayoutStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  height: '100%',
  minHeight: 0,
  gap: 16
}

const sceneLeftColumnStyle: CSSProperties = {
  flex: '0 0 45%',  // 左侧占 45%
  minWidth: 0,
  height: '100%',
  display: 'flex',
  flexDirection: 'column',
  gap: 12
}

const sceneImageStyle: CSSProperties = {
  flex: '0 0 55%',  // 图片占左侧 55% 高度
  width: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  overflow: 'hidden',
  background: 'var(--bg-secondary)',
  borderRadius: 8
}

const sceneCharaListStyle: CSSProperties = {
  flex: 1,  // 场景人物列表占剩余空间
  minHeight: 0,
  overflow: 'auto',
  background: 'var(--bg-secondary)',
  borderRadius: 8,
  padding: 0,
  margin: 0
}

const sceneModColumnStyle: CSSProperties = {
  flex: 1,  // 右侧模组列表占剩余宽度
  minWidth: 0,
  height: '100%',
  overflow: 'auto'
}

const infoSectionStyle: CSSProperties = {
  flex: '0 0 auto',
  maxHeight: '30%',  // 信息区域最多占 30%，给模组列表更多空间
  overflow: 'auto'
}

const modListSectionStyle: CSSProperties = {
  flex: 1,
  minHeight: 0,
  overflow: 'auto',
  borderRadius: 8,
  background: 'var(--bg-secondary)'
}
