import ipcUtils from '@renderer/logic/ipcUtils'
import { Divider, List, Space, Typography } from 'antd'
import { FC, useEffect, useMemo, useState } from 'react'

const { readAllCharaNames } = ipcUtils
const { Text } = Typography

interface IState {
  filePath: string
}

export const SceneInfo: FC<IState> = ({ filePath }) => {
  const [info, setInfo] = useState<string[]>()
  const data = useMemo(() => {
    if (info) {
      return [{ title: '名称', content: info }]
    }
    return undefined
  }, [info])
  useEffect(() => {
    readAllCharaNames(filePath).then((data) => setInfo(data))
  }, [filePath])
  return (
    <List
      header={
        <div style={{ padding: '6px 24px' }}>
          <Text strong>场景人物</Text>
        </div>
      }
      itemLayout="horizontal"
      dataSource={data}
      renderItem={(item) => (
        <List.Item style={{ padding: '16px 24px' }}>
          <Space split={<Divider type="vertical" />} wrap>
            {item.content?.map((p) => p)}
          </Space>
        </List.Item>
      )}
    />
  )
}
