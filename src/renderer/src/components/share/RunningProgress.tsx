import { Steps } from 'antd'
import { FC } from 'react'

interface IProps {
  current: number
  items: { title: string; description?: string }[]
}

export const RunningProgress: FC<IProps> = ({ current, items }) => {
  return <Steps direction="vertical" current={current} items={items}></Steps>
}
