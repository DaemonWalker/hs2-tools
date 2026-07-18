import { FC, useMemo } from 'react'
import { Badge, Space } from 'antd'
import {
  CloseCircleOutlined,
  ClockCircleOutlined,
  SyncOutlined
} from '@ant-design/icons'
import { formatSpeed } from '@renderer/utils/format'

interface StatisticsBarProps {
  activeCount: number
  failedCount: number
  totalSpeed: number
}

/**
 * 紧凑统计栏组件
 * 显示当前状态和关键指标
 */
export const StatisticsBar: FC<StatisticsBarProps> = ({
  activeCount,
  failedCount,
  totalSpeed
}) => {
  // 状态显示
  const statusDisplay = useMemo(() => {
    if (activeCount > 0) {
      return (
        <Space size={4}>
          <SyncOutlined spin style={{ color: '#1890ff' }} />
          <span style={{ color: '#1890ff', fontWeight: 500 }}>
            下载中
          </span>
          <Badge count={activeCount} color="processing" />
          <span style={{ color: '#8c8c8c', fontSize: 12, marginLeft: 4 }}>
            {formatSpeed(totalSpeed)}
          </span>
        </Space>
      )
    }

    if (failedCount > 0) {
      return (
        <Space size={4}>
          <CloseCircleOutlined style={{ color: '#ff4d4f' }} />
          <span style={{ color: '#ff4d4f', fontWeight: 500 }}>
            有 {failedCount} 个任务失败
          </span>
        </Space>
      )
    }

    return (
      <Space size={4}>
        <ClockCircleOutlined style={{ color: '#8c8c8c' }} />
        <span style={{ color: '#8c8c8c' }}>暂无下载任务</span>
      </Space>
    )
  }, [activeCount, failedCount, totalSpeed])

  return (
    <div style={{ display: 'flex', alignItems: 'center' }}>
      {statusDisplay}
    </div>
  )
}

export default StatisticsBar
