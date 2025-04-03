import { FC, useEffect, useMemo, useState } from 'react'
import { List, Typography } from 'antd'
import { useModStore } from '@renderer/store/modStore'
import { getCardMods } from '@renderer/logic/ipcUtils'

interface IState {
  filePath: string
}

const { Text, Link } = Typography

export const ModList: FC<IState> = ({ filePath }) => {
  const [mods, setMods] = useState<string[] | undefined>(undefined)
  const { mods: localMods } = useModStore()
  useEffect(() => {
    getCardMods(filePath).then((data) => {
      setMods(data)
    })
  }, [filePath])
  const data = useMemo(
    () => mods?.map((p) => ({ name: p, isLocal: !!localMods?.[p] })),
    [mods, localMods]
  )
  return (
    <List
      header={
        <div style={{ padding: '6px 24px' }}>
          <Text strong>使用的Mods</Text>
        </div>
      }
      size="large"
      dataSource={data}
      renderItem={(item) => (
        <List.Item>
          <div style={{ width: '100%', display: 'flex' }}>
            <div style={{ flex: 1 }}>{item.name}</div>
            <div>
              {item.isLocal ? <Text style={{ color: 'green' }}>已在本地</Text> : <Link>下载</Link>}
            </div>
          </div>
        </List.Item>
      )}
    ></List>
  )
}
