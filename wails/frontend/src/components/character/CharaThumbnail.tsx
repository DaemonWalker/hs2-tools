import ipcUtils from '@renderer/logic/ipcUtils'
import { FC, useEffect, useState, memo, useCallback } from 'react'
import { PngViewer } from '../share/PngViewer'
import { useNavigate } from 'react-router-dom'

const { readAllCharaNames } = ipcUtils

interface IProps {
  filePath: string
}

export const CharaThumbnail: FC<IProps> = memo(({ filePath }) => {
  const [info, setInfo] = useState<(string | undefined)[]>()
  const [error, setError] = useState(false)
  
  useEffect(() => {
    readAllCharaNames(filePath)
      .then((data) => setInfo(data))
      .catch((err) => {
        console.error('Failed to read character names:', err)
        setError(true)
      })
  }, [filePath])
  const navi = useNavigate()
  
  const handleClick = useCallback(() => {
    navi(encodeURI(filePath))
  }, [navi, filePath])
  
  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      navi(encodeURI(filePath))
    }
  }, [navi, filePath])
  
  if (error) {
    return (
      <div className="png-thumbnail" style={{ opacity: 0.6 }}>
        <div className="png-thumbnail-header">读取失败</div>
        <div style={{ 
          display: 'flex', 
          alignItems: 'center', 
          justifyContent: 'center',
          aspectRatio: '2/3',
          background: 'var(--bg-tertiary)'
        }}>
          <span style={{ fontSize: 12, color: 'var(--text-secondary)' }}>加载失败</span>
        </div>
      </div>
    )
  }

  return (
    <div 
      className="png-thumbnail" 
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      tabIndex={0}
      role="button"
      aria-label={`查看角色: ${info?.[0] || '未知角色'}`}
    >
      <div className="png-thumbnail-header">{info?.[0] || '\u00A0'}</div>
      <PngViewer filePath={filePath} style={{ maxWidth: '100%' }} />
    </div>
  )
})

CharaThumbnail.displayName = 'CharaThumbnail'
