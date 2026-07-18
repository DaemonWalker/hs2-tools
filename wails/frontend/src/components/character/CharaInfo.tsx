import ipcUtils from '@renderer/logic/ipcUtils'
import { Card, Descriptions, Tag, Spin, Empty } from 'antd'
import { FC, useEffect, useState } from 'react'
import { 
  UserOutlined, 
  InfoCircleOutlined 
} from '@ant-design/icons'

const { readAllCharaNames } = ipcUtils

interface IProps {
  filePath: string
}

export const CharaInfo: FC<IProps> = ({ filePath }) => {
  const [info, setInfo] = useState<(string | undefined)[]>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setError(null)
    readAllCharaNames(filePath)
      .then((data) => {
        setInfo(data)
        setLoading(false)
      })
      .catch((err) => {
        console.error('Failed to read character info:', err)
        setError('读取角色信息失败')
        setLoading(false)
      })
  }, [filePath])

  if (loading) {
    return (
      <Card className="theme-card" size="small">
        <div style={{ padding: 24, textAlign: 'center' }}>
          <Spin />
        </div>
      </Card>
    )
  }

  if (error) {
    return (
      <Card className="theme-card" size="small">
        <Empty 
          description={error} 
          image={Empty.PRESENTED_IMAGE_SIMPLE}
        />
      </Card>
    )
  }

  if (!info || info.length === 0) {
    return (
      <Card className="theme-card" size="small">
        <Empty description="无法读取角色信息" />
      </Card>
    )
  }

  return (
    <Card 
      className="theme-card" 
      size="small"
      title={
        <span>
          <UserOutlined style={{ marginRight: 8 }} />
          角色信息
        </span>
      }
    >
      <Descriptions 
        column={1} 
        size="small"
        labelStyle={{ 
          fontWeight: 500,
          width: 80
        }}
      >
        <Descriptions.Item label="名称">
          <Tag color="blue" icon={<InfoCircleOutlined />}>
            {info[0] || '未知'}
          </Tag>
        </Descriptions.Item>
        {info[1] && (
          <Descriptions.Item label="描述">
            {info[1]}
          </Descriptions.Item>
        )}
      </Descriptions>
    </Card>
  )
}
