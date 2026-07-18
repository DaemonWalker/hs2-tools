import { scanMods } from '@renderer/logic/scanLogic'
import { useModStore } from '@renderer/store/modStore'
import { ModModel, ModUseageModel } from '@shared/models/modModel'
import { Button, Radio, Table, TableProps, Card, Space, Badge, Statistic, Row, Col } from 'antd'
import { FC, useMemo, useState } from 'react'
import { 
  ReloadOutlined, 
  AppstoreOutlined,
  DeleteOutlined,
  CheckCircleOutlined 
} from '@ant-design/icons'

// 创建大小写不敏感的 usage Map，用于 O(1) 查找
const createUsageMap = (useage: ModUseageModel): Map<string, number> => {
  const map = new Map<string, number>()
  if (!useage || typeof useage !== 'object') {
    return map
  }
  for (const [key, value] of Object.entries(useage)) {
    map.set(key.toLowerCase(), value)
  }
  return map
}

// 使用 Map 进行 O(1) 查找
const getUsageCount = (usageMap: Map<string, number>, guid: string): number => {
  // 首先尝试精确匹配（小写）
  const lowerGuid = guid.toLowerCase()
  if (usageMap.has(lowerGuid)) {
    return usageMap.get(lowerGuid)!
  }
  return 0
}

type TableDataType = { guid: string } & ModModel[string] & { used: number }

const columns: TableProps<TableDataType>['columns'] = [
  {
    title: 'GUID',
    dataIndex: 'guid',
    key: 'guid',
    width: 280,
    ellipsis: true
  },
  {
    title: '名称',
    dataIndex: 'name',
    key: 'name',
    width: 200,
    ellipsis: true
  },
  {
    title: '版本',
    dataIndex: 'version',
    key: 'version',
    width: 100
  },
  {
    title: '使用次数',
    dataIndex: 'used',
    key: 'used',
    width: 100,
    sorter: (a, b) => a.used - b.used,
    render: (used) => (
      <Badge 
        count={used} 
        style={{ 
          backgroundColor: used > 0 ? 'var(--color-success)' : 'var(--color-gray-200)'
        }} 
      />
    )
  },
  {
    title: '路径',
    dataIndex: 'path',
    key: 'path',
    ellipsis: true
  }
]

export const LocalModList: FC = () => {
  const { mods, useage, setMods } = useModStore()
  const [filter, setFilter] = useState<string>('1')
  const [scanning, setScanning] = useState(false)

  const scan = async () => {
    setScanning(true)
    const mods = await scanMods()
    setMods(mods)
    setScanning(false)
  }

  // 在 useMemo 内部创建 usageMap，确保数据一致性
  const data: TableDataType[] | undefined = useMemo(() => {
    if (!mods) {
      return undefined
    }
    
    // 直接在这里创建 usageMap，确保与 useage 同步
    const usageMap = createUsageMap(useage)
    
    const result: TableDataType[] = []
    
    // 使用 for...in 代替 Object.keys().map()，避免创建中间数组
    for (const key in mods) {
      const mod = mods[key]
      if (!mod) continue
      
      const used = getUsageCount(usageMap, key)
      
      // 提前过滤：如果筛选未使用的且此 mod 被使用，则跳过
      if (filter === '2' && used !== 0) {
        continue
      }
      
      result.push({ guid: key, ...mod, used })
    }
    
    // 排序：使用 localeCompare 进行更高效的字符串比较
    result.sort((a, b) => {
      const guidCompare = a.guid.localeCompare(b.guid)
      if (guidCompare !== 0) return guidCompare
      
      const versionCompare = (a.version || '').localeCompare(b.version || '')
      if (versionCompare !== 0) return versionCompare
      
      return (a.path || '').localeCompare(b.path || '')
    })
    
    return result
  }, [mods, useage, filter])

  const stats = useMemo(() => {
    if (!data) return { total: 0, used: 0, unused: 0 }
    const used = data.filter(item => item.used > 0).length
    return {
      total: data.length,
      used,
      unused: data.length - used
    }
  }, [data])

  return (
    <div className="local-mods">
      {/* 统计卡片 */}
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={8}>
          <Card className="theme-card" size="small">
            <Statistic
              title="总模组数"
              value={stats.total}
              prefix={<AppstoreOutlined />}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card className="theme-card" size="small">
            <Statistic
              title="已使用"
              value={stats.used}
              valueStyle={{ color: 'var(--color-success)' }}
              prefix={<CheckCircleOutlined />}
            />
          </Card>
        </Col>
        <Col span={8}>
          <Card className="theme-card" size="small">
            <Statistic
              title="未使用"
              value={stats.unused}
              valueStyle={{ color: stats.unused > 0 ? 'var(--color-warning)' : 'var(--color-gray-200)' }}
              prefix={<DeleteOutlined />}
            />
          </Card>
        </Col>
      </Row>

      {/* 筛选和操作栏 */}
      <Card className="theme-card" size="small" style={{ marginBottom: 16 }}>
        <div style={{ 
          display: 'flex', 
          alignItems: 'center', 
          justifyContent: 'space-between'
        }}>
          <Radio.Group 
            onChange={(e) => setFilter(e.target.value)} 
            value={filter}
          >
            <Radio.Button value="1">
              <Space>
                <AppstoreOutlined />
                所有模组
                <Badge count={stats.total} style={{ backgroundColor: 'var(--theme-primary)' }} />
              </Space>
            </Radio.Button>
            <Radio.Button value="2">
              <Space>
                <DeleteOutlined />
                未使用的
                <Badge count={stats.unused} style={{ backgroundColor: 'var(--color-warning)' }} />
              </Space>
            </Radio.Button>
          </Radio.Group>

          <Button 
            type="primary" 
            icon={<ReloadOutlined />}
            onClick={scan} 
            loading={scanning}
          >
            刷新模组列表
          </Button>
        </div>
      </Card>

      {/* 表格 */}
      <div className="local-mods-list">
        <Card className="theme-card" size="small">
          <Table<TableDataType>
            dataSource={data}
            columns={columns}
            pagination={{ 
              showQuickJumper: true,
              showSizeChanger: true,
              defaultPageSize: 20,
              pageSizeOptions: ['10', '20', '50', '100']
            }}
            rowKey={(record) => record.guid}
            size="small"
            scroll={{ x: 'max-content' }}
          />
        </Card>
      </div>
    </div>
  )
}
