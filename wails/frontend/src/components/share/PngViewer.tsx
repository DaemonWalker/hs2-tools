import ipcUtils from '@renderer/logic/ipcUtils'
import { Spin } from 'antd'
import { PictureOutlined } from '@ant-design/icons'
import { CSSProperties, FC, useEffect, useState, memo } from 'react'

const { readPngForShow } = ipcUtils
interface IState {
  filePath: string
  style?: CSSProperties
}

export const PngViewer: FC<IState> = memo(({ filePath, style }) => {
  const [base64, setBase64] = useState<string>()
  const [imgError, setImgError] = useState(false)

  useEffect(() => {
    setImgError(false)
    readPngForShow(filePath).then((data) => {
      setBase64(data)
    })
  }, [filePath])

  // 区分三种状态：加载中(undefined)、无图片(空字符串)、有图片(非空字符串)
  const hasImage = base64 !== undefined && base64 !== ''
  const noImage = base64 === ''

  return (
    <div style={{ height: '100%', width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      {hasImage && !imgError ? (
        <img 
          src={`data:image/png;base64,${base64}`} 
          style={{ 
            maxHeight: '100%', 
            maxWidth: '100%', 
            objectFit: 'contain', 
            ...style, 
            borderRadius: 8,
            // Use content-visibility for better rendering performance
            contentVisibility: 'auto'
          }}
          onError={() => setImgError(true)}
          alt="角色卡预览"
          decoding="async"
        />
      ) : imgError || noImage ? (
        <div style={{ 
          display: 'flex', 
          flexDirection: 'column',
          alignItems: 'center', 
          justifyContent: 'center',
          height: '100%',
          maxWidth: '100%',
          aspectRatio: '2/3',
          background: 'var(--bg-tertiary)',
          borderRadius: 8,
          color: 'var(--text-secondary)'
        }}>
          <PictureOutlined style={{ fontSize: 64, marginBottom: 16 }} />
          <span style={{ fontSize: 14 }}>暂无预览</span>
        </div>
      ) : (
        <Spin aria-label="加载图片中" />
      )}
    </div>
  )
})

PngViewer.displayName = 'PngViewer'
