import React, { useMemo } from 'react'
import { Menu } from 'antd'
import {
  AppstoreOutlined,
  HomeOutlined,
  UserOutlined,
  BookOutlined,
  BackwardOutlined,
  SearchOutlined,
  DownloadOutlined,
  SettingOutlined
} from '@ant-design/icons'
import { useNavigate, useLocation } from 'react-router-dom'
import { ThemeSwitcher } from './ThemeSwitcher'

const menuItems = [
  {
    label: '后退',
    key: 'back',
    icon: <BackwardOutlined />,
  },
  {
    label: '首页',
    key: 'home',
    icon: <HomeOutlined />,
  },
  {
    label: '模组',
    key: 'mods',
    icon: <AppstoreOutlined />,
    children: [
      { label: '本地模组', key: 'localMods' },
      { label: 'BetterRepack', key: 'info' }
    ]
  },
  {
    label: '人物',
    key: 'chara',
    icon: <UserOutlined />,
  },
  {
    label: '场景',
    key: 'scene',
    icon: <BookOutlined />,
  },
  {
    label: '查看',
    key: 'explorer',
    icon: <SearchOutlined />,
  },
  {
    label: '下载',
    key: 'download',
    icon: <DownloadOutlined />,
  },
  {
    label: '设置',
    key: 'settings',
    icon: <SettingOutlined />,
  }
]

// 定义 Header 组件，返回一个横向的 Menu 组件
export const Header: React.FC = () => {
  const navigate = useNavigate()
  const location = useLocation()

  // 根据当前路径确定选中的菜单项
  const selectedKeys = useMemo(() => {
    const path = location.pathname
    if (path === '/') return ['home']
    if (path.startsWith('/character')) return ['chara']
    if (path.startsWith('/scene')) return ['scene']
    if (path.startsWith('/mods-local')) return ['mods']
    if (path.startsWith('/explorer')) return ['explorer']
    if (path.startsWith('/settings')) return ['settings']
    if (path.startsWith('/download')) return ['download']
    return []
  }, [location.pathname])

  // 处理菜单点击
  const handleClick = (key: string) => {
    switch (key) {
      case 'back':
        navigate(-1)
        break
      case 'home':
        navigate('/')
        break
      case 'chara':
        navigate('/character')
        break
      case 'scene':
        navigate('/scene')
        break
      case 'localMods':
        navigate('/mods-local/')
        break
      case 'info':
        navigate('/mods-sideload/')
        break
      case 'explorer':
        navigate('/explorer')
        break
      case 'download':
        navigate('/download')
        break
      case 'settings':
        navigate('/settings')
        break
    }
  }

  return (
    <header
      className="app-header"
      role="banner"
      aria-label="应用主导航"
      style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        width: '100%',
        padding: '0 8px'
      }}
    >
      <nav aria-label="主导航菜单">
        <Menu
          mode="horizontal"
          style={{
            width: 'auto',
            flex: 1,
            background: 'transparent'
          }}
          selectedKeys={selectedKeys}
          items={menuItems}
          onClick={({ key }) => handleClick(key)}
        />
      </nav>
      <ThemeSwitcher />
    </header>
  )
}
