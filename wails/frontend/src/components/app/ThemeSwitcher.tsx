import { useThemeStore, ThemeType } from '@renderer/store/themeStore'
import { Dropdown, Button, Space, Badge } from 'antd'
import { 
  BgColorsOutlined, 
  CheckOutlined,
  MoonOutlined,
  SunOutlined
} from '@ant-design/icons'
import { FC, useEffect } from 'react'

interface ThemeOption {
  key: ThemeType
  label: string
  icon: React.ReactNode
  description: string
  color: string
}

const themeOptions: ThemeOption[] = [
  {
    key: 'modern-dark',
    label: '赛博紫夜',
    icon: <MoonOutlined />,
    description: '深色背景 + 紫蓝渐变 + 毛玻璃效果',
    color: '#8b5cf6'
  },
  {
    key: 'clean-light',
    label: '简洁专业',
    icon: <SunOutlined />,
    description: '浅色背景 + 清新蓝色 + 简洁风格',
    color: '#1890ff'
  }
]

export const ThemeSwitcher: FC = () => {
  const { currentTheme, setTheme } = useThemeStore()

  // 初始化主题
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', currentTheme)
  }, [currentTheme])

  const currentOption = themeOptions.find(opt => opt.key === currentTheme)

  const menuItems = themeOptions.map(option => ({
    key: option.key,
    label: (
      <Space style={{ width: 220 }}>
        <span style={{ 
          fontSize: 18, 
          color: option.color,
          width: 24,
          display: 'inline-flex',
          justifyContent: 'center'
        }}>
          {option.icon}
        </span>
        <Space direction="vertical" size={0} style={{ flex: 1 }}>
          <span style={{ fontWeight: 500, fontSize: 14 }}>
            {option.label}
            {currentTheme === option.key && (
              <CheckOutlined style={{ 
                color: '#52c41a', 
                marginLeft: 8,
                fontSize: 12
              }} />
            )}
          </span>
          <span style={{ fontSize: 12, opacity: 0.6 }}>
            {option.description}
          </span>
        </Space>
      </Space>
    ),
    onClick: () => setTheme(option.key)
  }))

  return (
    <Dropdown
      menu={{ items: menuItems }}
      placement="bottomRight"
      arrow
      trigger={['click']}
    >
      <Button 
        type="text"
        icon={<BgColorsOutlined />}
        aria-label="切换主题"
        aria-haspopup="true"
        aria-expanded={undefined}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 6
        }}
      >
        <Space size={4}>
          <span style={{ 
            color: currentOption?.color,
            fontSize: 16
          }}>
            {currentOption?.icon}
          </span>
          <span style={{ fontSize: 13 }}>
            {currentOption?.label}
          </span>
          <Badge 
            dot 
            color={currentOption?.color}
            style={{ marginLeft: 4 }}
          />
        </Space>
      </Button>
    </Dropdown>
  )
}
