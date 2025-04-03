import { pathJoin } from '@renderer/logic/ioUtils'
import { getAllFiles, getZipModInfo } from '@renderer/logic/ipcUtils'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore } from '@renderer/store/settingStore'
import { ModModel } from '@shared/models/modModel'
import { Button, InputNumber, Progress, Radio, Table, TableProps } from 'antd'
import { FC, useMemo, useState } from 'react'

type TableDataType = { guid: string } & ModModel[string]

const columns: TableProps<TableDataType>['columns'] = [
  {
    title: 'Guid',
    dataIndex: 'guid',
    key: 'Guid'
  },
  {
    title: '名称',
    dataIndex: 'name',
    key: 'name'
  },
  {
    title: '版本',
    dataIndex: 'version',
    key: 'version'
  },
  {
    title: '使用次数',
    dataIndex: 'used',
    key: 'used',
    sorter: (a, b) => a.used - b.used
  },
  {
    title: '路径',
    dataIndex: 'path',
    key: 'path'
  }
]

export const LocalModList: FC = () => {
  const { mods } = useModStore()
  const [filter, setFilter] = useState<string>('1')

  const data: TableDataType[] | undefined = useMemo(
    () =>
      mods
        ? Object.keys(mods)
            .map((key) => ({ guid: key, ...mods[key]! }))
            .filter((item) => {
              if (filter === '2') {
                return item.used === 0
              }
              return true
            })
            .sort((a, b) => {
              // 首先按 Guid 排序
              if (a.guid < b.guid) return -1
              if (a.guid > b.guid) return 1
              // 如果 Guid 相同，按 Version 排序
              if (a.version < b.version) return -1
              if (a.version > b.version) return 1
              // 如果 Version 也相同，按 Path 排序
              if (a.path < b.path) return -1
              if (a.path > b.path) return 1
              return 0
            })
        : undefined,
    [mods, filter]
  )

  return (
    <div className="local-mods">
      <div>
        <Radio.Group onChange={(e) => setFilter(e.target.value)}>
          <Radio value="1">
            <span>所有Mod</span>
          </Radio>
          <Radio value="2">
            <span>显示未使用的Mod</span>
          </Radio>
        </Radio.Group>
      </div>
      <div className="local-mods-list">
        <Table<TableDataType>
          dataSource={data}
          columns={columns}
          pagination={{ showQuickJumper: true }}
          rowKey={(record) => record.guid}
        />
      </div>
    </div>
  )
}
