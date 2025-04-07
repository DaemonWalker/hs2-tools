import { useSideloadStore } from '@renderer/store/sideloadStore'
import { Table } from 'antd'
import { FC, useMemo } from 'react'

const columns = [
  { title: 'GUID', dataIndex: 'guid', key: 'guid' },
  { title: 'URL', dataIndex: 'url', key: 'url' }
]

export const BatterRepack: FC = () => {
  const { sideload } = useSideloadStore()
  const data = useMemo(
    () => Object.entries(sideload).map(([guid, url]) => ({ guid, url })),
    [sideload]
  )
  return <Table columns={columns} dataSource={data} />
}
