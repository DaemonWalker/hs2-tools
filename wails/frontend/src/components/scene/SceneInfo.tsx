import ipcUtils from '@renderer/logic/ipcUtils'
import { List, Typography } from 'antd'
import { FC, useEffect, useState } from 'react'

const { readAllCharaNames } = ipcUtils
const { Text } = Typography

interface IState {
  filePath: string
}

export const SceneInfo: FC<IState> = ({ filePath }) => {
  const [charaNames, setCharaNames] = useState<string[]>([])

  useEffect(() => {
    readAllCharaNames(filePath).then((data) => {
      setCharaNames(data || [])
    })
  }, [filePath])

  return (
    <List
      header={
        <div style={{ padding: '8px 16px' }}>
          <Text strong>场景人物</Text>
          <Text type="secondary" style={{ marginLeft: 8 }}>({charaNames.length} 个)</Text>
        </div>
      }
      style={{ marginTop: 0 }}
      size="small"
      dataSource={charaNames}
      renderItem={(name, index) => (
        <List.Item style={{ padding: '8px 16px', marginTop: 0 }}>
          <Text ellipsis style={{ flex: 1 }} title={name}>
            {index + 1}. {name}
          </Text>
        </List.Item>
      )}
      locale={{ emptyText: '未检测到场景人物' }}
    />
  )
}
