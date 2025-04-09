import React from 'react'
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
import { useNavigate } from 'react-router-dom'

// 定义 Header 组件，返回一个横向的 Menu 组件
export const Header: React.FC = () => {
  const navigate = useNavigate()
  return (
    <Menu
      mode="horizontal"
      style={{ width: '100%' }}
      items={[
        { label: '后退', key: 'back', icon: <BackwardOutlined />, onClick: () => navigate(-1) },
        { label: '首页', key: 'home', icon: <HomeOutlined />, onClick: () => navigate('/') },
        {
          label: '模组',
          key: 'localMods',
          icon: <AppstoreOutlined />,
          children: [
            { label: '本地模组', key: 'list', onClick: () => navigate('/mods-local/') },
            { label: 'BatterRepack', key: 'info', onClick: () => navigate('/mods-sideload/') }
          ]
        },
        {
          label: '人物',
          key: 'chara',
          icon: <UserOutlined />,
          onClick: () => navigate('/character')
        },
        {
          label: '场景',
          key: 'scene',
          icon: <BookOutlined />,
          children: [
            { label: '场景列表', key: 'list', onClick: () => navigate('/scene') },
            {
              label: '场景整理',
              key: 'group',
              onClick: () => navigate('/scene-group')
            },
            {
              label: '场景统计',
              key: 'statistics',
              onClick: () => navigate('/scene-statistics')
            }
          ]
        },
        {
          label: '查看',
          key: 'explorer',
          icon: <SearchOutlined />,
          onClick: () => navigate('/explorer')
        },
        {
          label: '下载',
          key: 'download',
          icon: <DownloadOutlined />,
          onClick: () => navigate('/download')
        },
        {
          label: '设置',
          key: 'settings',
          icon: <SettingOutlined />,
          onClick: () => navigate('/settings')
        }
      ]}
    ></Menu>
  )
}
