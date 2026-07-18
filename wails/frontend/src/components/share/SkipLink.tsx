import { FC } from 'react'

/**
 * 跳过导航链接 - 为键盘用户提供快速跳转到主内容的途径
 * 按 Tab 键首次聚焦时显示，点击后跳转到主内容区域
 */
export const SkipLink: FC = () => {
  const handleClick = (e: React.MouseEvent<HTMLAnchorElement>) => {
    e.preventDefault()
    const mainContent = document.getElementById('container')
    if (mainContent) {
      mainContent.focus()
      mainContent.scrollIntoView({ behavior: 'smooth' })
    }
  }

  return (
    <a
      href="#container"
      onClick={handleClick}
      style={{
        position: 'absolute',
        top: -48,
        left: 0,
        background: 'var(--theme-primary)',
        color: 'var(--text-primary)',
        padding: '10px 20px',
        zIndex: 9999,
        textDecoration: 'none',
        fontSize: 15,
        fontWeight: 500,
        borderRadius: '0 0 8px 0',
        transition: 'top 0.2s ease-out',
      }}
      onFocus={(e) => {
        e.currentTarget.style.top = '0'
      }}
      onBlur={(e) => {
        e.currentTarget.style.top = '-48px'
      }}
    >
      跳转到主内容
    </a>
  )
}
