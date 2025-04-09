import ipcUtils from '@renderer/logic/ipcUtils'
import { List, Typography } from 'antd'
import { FC, useEffect, useMemo, useState } from 'react'

const { readAllCharaNames } = ipcUtils
const { Text } = Typography

interface IProps {
  filePath: string
}
export const CharaInfo: FC<IProps> = ({ filePath }) => {
  const [info, setInfo] = useState<(string | undefined)[]>()
  const data = useMemo(() => {
    if (info) {
      return [{ title: '名称', content: info[0] }]
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
          <Text strong>人物信息</Text>
        </div>
      }
      itemLayout="horizontal"
      dataSource={data}
      renderItem={(item) => (
        <List.Item style={{ padding: '16px 24px' }}>
          <Typography.Text mark>{item.title}</Typography.Text> {item.content}
        </List.Item>
      )}
    />
  )
}
