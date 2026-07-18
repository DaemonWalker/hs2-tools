import { FC, useEffect, useState } from 'react'

interface AnnouncerProps {
  message: string
  assertive?: boolean
  clearAfter?: number
}

/**
 * 屏幕阅读器通知组件
 * 用于向屏幕阅读器用户宣布动态内容变化
 * 
 * @param message - 要宣布的消息
 * @param assertive - 是否使用 assertive（立即打断），默认 polite（等待空闲）
 * @param clearAfter - 自动清除时间（毫秒），默认 1000
 */
export const Announcer: FC<AnnouncerProps> = ({ 
  message, 
  assertive = false,
  clearAfter = 1000 
}) => {
  const [announcement, setAnnouncement] = useState(message)

  useEffect(() => {
    setAnnouncement(message)
    
    if (clearAfter > 0) {
      const timer = setTimeout(() => {
        setAnnouncement('')
      }, clearAfter)
      
      return () => clearTimeout(timer)
    }
    return undefined
  }, [message, clearAfter])

  return (
    <div
      aria-live={assertive ? 'assertive' : 'polite'}
      aria-atomic="true"
      style={{
        position: 'absolute',
        width: 1,
        height: 1,
        padding: 0,
        margin: -1,
        overflow: 'hidden',
        clip: 'rect(0, 0, 0, 0)',
        whiteSpace: 'nowrap',
        border: 0,
      }}
    >
      {announcement}
    </div>
  )
}

/**
 * 全局 announcer 容器
 * 应用启动时挂载一次，通过全局状态或事件系统触发通知
 */
export const AnnouncerContainer: FC = () => {
  const [message, setMessage] = useState('')
  const [assertive, setAssertive] = useState(false)

  useEffect(() => {
    // 监听自定义事件来触发通知
    const handleAnnounce = (e: CustomEvent<{ message: string; assertive?: boolean }>) => {
      setMessage(e.detail.message)
      setAssertive(e.detail.assertive || false)
    }

    window.addEventListener('announce', handleAnnounce as EventListener)
    return () => window.removeEventListener('announce', handleAnnounce as EventListener)
  }, [])

  return <Announcer message={message} assertive={assertive} />
}

/**
 * 触发屏幕阅读器通知的辅助函数
 */
export const announce = (message: string, assertive = false) => {
  window.dispatchEvent(new CustomEvent('announce', { 
    detail: { message, assertive } 
  }))
}
