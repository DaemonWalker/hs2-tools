import { FC } from 'react'
import { CharaThumbnail } from '../character/CharaThumbnail'
import { Empty } from 'antd'
import { InboxOutlined } from '@ant-design/icons'

interface IProps {
  data?: string[]
  columnCount: number
  type: 'chara' | 'scene'
}

export const ResponsiveContainer: FC<IProps> = ({ data, columnCount }) => {
  if (!data || data.length === 0) {
    return (
      <div style={{ 
        flex: 1, 
        display: 'flex', 
        alignItems: 'center', 
        justifyContent: 'center' 
      }}>
        <Empty
          image={<InboxOutlined style={{ fontSize: 64, opacity: 0.3 }} />}
          description="暂无数据"
        />
      </div>
    )
  }

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: `repeat(${columnCount}, 1fr)`,
        gap: 16,
        overflow: 'auto',
        flex: 1,
        padding: '8px 4px',
        alignContent: 'start'
      }}
    >
      {data?.map((item, index) => (
        <div
          className="animate-fadeIn"
          style={{
            cursor: 'pointer',
            animationDelay: `${(index % 8) * 0.05}s`
          }}
          key={item}
        >
          <CharaThumbnail filePath={item} />
        </div>
      ))}
    </div>
  )
}
