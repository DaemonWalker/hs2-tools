import { Steps } from 'antd'
import { FC } from 'react'
import { 
  CheckCircleOutlined, 
  LoadingOutlined,
  ClockCircleOutlined 
} from '@ant-design/icons'

interface IProps {
  current: number
  items: { title: string; description?: string }[]
}

export const RunningProgress: FC<IProps> = ({ current, items }) => {
  const getIcon = (index: number) => {
    if (index < current) {
      return <CheckCircleOutlined style={{ color: 'var(--color-success)' }} />
    } else if (index === current) {
      return <LoadingOutlined style={{ color: 'var(--theme-primary)' }} />
    } else {
      return <ClockCircleOutlined style={{ opacity: 0.5 }} />
    }
  }

  const stepsItems = items.map((item, index) => ({
    ...item,
    icon: getIcon(index),
    status: (index < current ? 'finish' : index === current ? 'process' : 'wait') as 'finish' | 'process' | 'wait'
  }))

  return (
    <Steps 
      direction="vertical" 
      current={current} 
      items={stepsItems}
      size="small"
    />
  )
}
