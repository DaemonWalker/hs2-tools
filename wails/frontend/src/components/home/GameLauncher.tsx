import { Button, Space } from 'antd'
import { FC } from 'react'
import { PlayCircleOutlined, BuildOutlined } from '@ant-design/icons'

export const GameLauncher: FC = () => {
  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Button 
        type="primary" 
        size="large" 
        icon={<PlayCircleOutlined style={{ fontSize: 18 }} />}
        block
        aria-label="启动 Honey Select 2 游戏"
        style={{ 
          height: 52,
          fontSize: 16,
          fontWeight: 600
        }}
      >
        开始游戏
      </Button>
      <Button 
        size="large" 
        icon={<BuildOutlined style={{ fontSize: 18 }} />}
        block
        aria-label="启动 Honey Select 2 工作室"
        style={{ 
          height: 52,
          fontSize: 16,
          fontWeight: 500,
          background: 'var(--bg-tertiary)',
          border: '1px solid var(--border-color)'
        }}
      >
        开始工作室
      </Button>
    </Space>
  )
}

export default GameLauncher
