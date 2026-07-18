# Mod 管理

本文档介绍本地 Mod 管理和统计功能。

## 功能概述

Mod 管理功能允许用户：

- 浏览本地已安装的 Mod
- 查看 Mod 详细信息（GUID、名称、版本、路径）
- 统计 Mod 使用次数（被角色卡/场景卡引用次数）
- 筛选未使用的 Mod（便于清理）

## 页面路由

| 路由 | 页面 | 说明 |
|------|------|------|
| `/mods-local` | 本地 Mod | 显示已安装的 Mod 列表和统计信息 |
| `/mods-sideload` | BetterRepack | 显示 sideload 数据源，支持缺失 Mod 下载 |

## 数据结构

### Mod 数据模型

```typescript
// wails/frontend/src/shared/src/models/modModel.ts

// 单个 Mod 信息
interface ModInfo {
  name: string           // Mod 名称
  version: string        // 版本号
  path: string          // 文件路径
}

// Mod 集合（GUID 为键）
interface ModModel {
  [guid: string]: ModInfo
}

// Mod 使用统计（GUID 被引用的次数）
interface ModUseageModel {
  [guid: string]: number
}
```

## 页面布局

```
┌─────────────────────────────────────────────────────────────────┐
│  📦 本地 Mod 管理                                                 │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                        │
│  │ 总模组数  │  │  已使用  │  │  未使用  │                        │
│  │   156    │  │   134    │  │   22     │                        │
│  └──────────┘  └──────────┘  └──────────┘                        │
├─────────────────────────────────────────────────────────────────┤
│  [● 所有模组 156] [ 未使用的 22]                 [刷新模组列表]     │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │ Mod GUID                      │ 名称      │ 版本 │ 使用 │ 路径 │
│  ├───────────────────────────────┼───────────┼──────┼──────┼──────┤ │
│  │ author.modname.v1             │ Mod 名称1 │ 1.0  │  15  │ ...  │
│  │ another.author.mod.v2         │ Mod 名称2 │ 2.0  │   8  │ ...  │
│  │ unused.mod.v1                 │ Mod 名称3 │ 1.2  │   0  │ ...  │
│  │ ...                           │ ...       │ ...  │ ...  │ ...  │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  第 1/8 页  [10 ▼]  [首页] [上页] [1] [2] [3] [下页] [尾页] 前往 [] 页 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## 核心功能实现

### 1. 本地 Mod 列表组件 (LocalModList.tsx)

```typescript
// wails/frontend/src/components/mods/LocalMods.tsx
export const LocalModList: FC = () => {
  const { mods, useage, setMods } = useModStore()
  const [filter, setFilter] = useState<string>('1')  // '1' = 所有, '2' = 未使用的
  const [scanning, setScanning] = useState(false)

  // 扫描本地 Mods
  const scan = async () => {
    setScanning(true)
    const mods = await scanMods()
    setMods(mods)
    setScanning(false)
  }

  // 使用 useMemo 创建表格数据
  const data = useMemo(() => {
    if (!mods) return undefined

    const usageMap = createUsageMap(useage)
    const result: TableDataType[] = []

    for (const key in mods) {
      const mod = mods[key]
      if (!mod) continue

      const used = getUsageCount(usageMap, key)

      // 筛选：如果筛选未使用的且此 mod 被使用，则跳过
      if (filter === '2' && used !== 0) continue

      result.push({ guid: key, ...mod, used })
    }

    // 排序：按 GUID、版本、路径排序
    result.sort((a, b) => {
      const guidCompare = a.guid.localeCompare(b.guid)
      if (guidCompare !== 0) return guidCompare

      const versionCompare = (a.version || '').localeCompare(b.version || '')
      if (versionCompare !== 0) return versionCompare

      return (a.path || '').localeCompare(b.path || '')
    })

    return result
  }, [mods, useage, filter])

  // 统计数据
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
      <Row gutter={16}>
        <Col span={8}>
          <Card>
            <Statistic title="总模组数" value={stats.total} />
          </Card>
        </Col>
        <Col span={8}>
          <Card>
            <Statistic title="已使用" value={stats.used} />
          </Card>
        </Col>
        <Col span={8}>
          <Card>
            <Statistic title="未使用" value={stats.unused} />
          </Card>
        </Col>
      </Row>

      {/* 筛选和操作栏 */}
      <Card>
        <Radio.Group onChange={(e) => setFilter(e.target.value)} value={filter}>
          <Radio.Button value="1">所有模组</Radio.Button>
          <Radio.Button value="2">未使用的</Radio.Button>
        </Radio.Group>

        <Button type="primary" onClick={scan} loading={scanning}>
          刷新模组列表
        </Button>
      </Card>

      {/* 表格 */}
      <Table
        dataSource={data}
        columns={columns}
        pagination={{
          showQuickJumper: true,
          showSizeChanger: true,
          defaultPageSize: 20,
          pageSizeOptions: ['10', '20', '50', '100']
        }}
      />
    </div>
  )
}
```

### 2. 表格列定义

```typescript
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
```

### 3. 使用次数统计

创建大小写不敏感的 usage Map，用于 O(1) 查找：

```typescript
// 创建 usage Map
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

// O(1) 查找使用次数
const getUsageCount = (usageMap: Map<string, number>, guid: string): number => {
  const lowerGuid = guid.toLowerCase()
  if (usageMap.has(lowerGuid)) {
    return usageMap.get(lowerGuid)!
  }
  return 0
}
```

## 状态管理

```typescript
// wails/frontend/src/store/modStore.ts
interface ModState {
  mods: ModModel
  useage: ModUseageModel

  init: () => Promise<void>
  setMods: (mods: ModModel) => void
  setUseage: (useage: ModUseageModel) => void
}

export const useModStore = create<ModState>((set) => ({
  mods: {},
  useage: {},

  init: async () => {
    const mods = await getLocalMods()
    const useage = await getModUseage()
    set(() => ({ mods, useage }))
  },

  setMods: async (mods) => {
    await saveLocalMods(mods)
    set(() => ({ mods }))
  },

  setUseage: async (useage) => {
    await saveModUseage(useage)
    set(() => ({ useage }))
  }
}))

// Selector hooks for better performance
export const useModsSelector = () => useModStore(useShallow(state => state.mods))
export const useModUsageSelector = () => useModStore(useShallow(state => state.useage))
```

## zipmod 文件解析

Mod 文件使用 `.zipmod` 扩展名，本质是 ZIP 文件：

```
zipmod 文件结构:
┌─────────────────────────────────────┐
│ manifest.xml           (必需)       │
│   - 包含 GUID、名称、版本信息        │
├─────────────────────────────────────┤
│ abdata/                             │
│   - 游戏资源文件                     │
├─────────────────────────────────────┤
│ ... 其他文件 ...                     │
└─────────────────────────────────────┘
```

### manifest.xml 示例

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest>
  <guid>author.modname.version</guid>
  <version>1.0.0</version>
  <name>Mod 名称</name>
  <description>Mod 描述</description>
  <author>作者名</author>
  <website>https://example.com</website>
</manifest>
</>
```

### 解析实现

```typescript
// 使用 Go Scanner 解析 zipmod
const parseZipMod = async (path: string): Promise<ModInfo | null> => {
  const result = await ipcUtils.readZipMod(path)
  return result ? Object.values(result)[0] : null
}
```

## 扫描逻辑

```typescript
// wails/frontend/src/logic/scanLogic.ts
export const scanMods = async (): Promise<ModModel> => {
  const modsPath = useSettingStore.getState().modsPath()

  // 获取所有 zipmod 文件
  const files = await ipcUtils.getAllFiles(modsPath, {
    targetExtension: ['.zipmod']
  })

  // 批量解析
  const modList: ModModel = {}
  for (const file of files) {
    const info = await ipcUtils.readZipMod(file)
    if (info) {
      Object.assign(modList, info)
    }
  }

  return modList
}
```

## BetterRepack Sideload 页面

用于展示和管理来自 BetterRepack 的 sideload 数据源。

### 功能特性

- **数据展示** - 显示所有可用的 sideload Mod 数据（GUID、下载链接）
- **本地存在检测** - 自动检测哪些 Mods 已在本地安装
- **缺失 Mod 下载** - 对缺失的 Mod 提供一键下载功能
- **实时搜索** - 支持按 GUID 或 URL 搜索（300ms 防抖）
- **统计信息** - 显示总数、已存在数、缺失数

### 页面布局

```
┌─────────────────────────────────────────────────────────────────┐
│  BetterRepack Sideload 数据                          [刷新数据]   │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                        │
│  │ 总模组数  │  │本地已存在│  │ 缺失 Mods│                        │
│  │   156    │  │   100    │  │   56     │                        │
│  └──────────┘  └──────────┘  └──────────┘                        │
├─────────────────────────────────────────────────────────────────┤
│  [搜索 GUID 或 URL...                    ]                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │ Mod GUID                      │ 下载链接    │ 本地存在    │ │
│  ├───────────────────────────────┼─────────────┼────────────┤ │
│  │ author.modname.v1             │ https://... │ ✓ 已存在    │ │
│  │ another.author.mod.v2         │ https://... │ [下载] 按钮 │ │
│  │ ...                           │ ...         │ ...        │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  第 1/8 页  [10 ▼]  [首页] [上页] [1] [2] [3] [下页] [尾页]      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 核心实现

```typescript
// wails/frontend/src/pages/BetterRepack.tsx
interface SideloadItem {
  guid: string
  url: string
  existsLocally: boolean
}

export const BetterRepack: FC = () => {
  const { sideload, init, isLoading } = useSideloadStore(...)
  const { mods } = useModStore(...)
  const [inputText, setInputText] = useState('')
  const [searchText, setSearchText] = useState('')

  // 防抖处理输入（300ms）
  const handleInputChange = useCallback((e) => {
    const value = e.target.value
    setInputText(value)
    
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current)
    }
    
    debounceTimerRef.current = setTimeout(() => {
      setSearchText(value)
    }, 300)
  }, [])

  // 合并 sideload 数据和本地 Mod 状态
  const data: SideloadItem[] = useMemo(() => {
    return Object.entries(sideload).map(([guid, url]) => ({
      guid,
      url,
      existsLocally: !!mods[guid]  // 检测本地是否存在
    }))
  }, [sideload, mods])

  // 下载缺失的 Mod
  const handleDownload = async (guid: string, url: string) => {
    await ipcUtils.triggerDownload({ name: guid, url })
  }
}
```

### 表格列定义

```typescript
const columns = [
  {
    title: 'GUID',
    dataIndex: 'guid',
    width: '50%',
    render: (guid: string) => (
      <Text code copyable={{ text: guid }}>
        {guid}
      </Text>
    )
  },
  {
    title: '下载链接',
    dataIndex: 'url',
    width: '35%',
    render: (url: string) => (
      <Text copyable={{ text: url }}>
        {url.length > 40 ? url.substring(0, 40) + '...' : url}
      </Text>
    )
  },
  {
    title: '本地存在',
    dataIndex: 'existsLocally',
    width: '15%',
    align: 'center',
    render: (existsLocally: boolean, record) => {
      if (existsLocally) {
        return <Tag color="success" icon={<CheckCircleOutlined />}>已存在</Tag>
      }
      return (
        <Button 
          type="primary" 
          size="small" 
          icon={<DownloadOutlined />}
          onClick={() => handleDownload(record.guid, record.url)}
        >
          下载
        </Button>
      )
    }
  }
]
```

## 注意事项

1. **使用次数统计** - 通过扫描角色卡和场景卡计算 Mod 被引用的次数
2. **大小写不敏感** - GUID 比较时使用小写进行匹配
3. **性能优化** - 使用 Map 进行 O(1) 查找，避免遍历数组
4. **数据持久化** - Mod 数据存储在 `%APPDATA%/hs2-tools/localMods.json`，使用统计保存在运行时 Store
5. **未使用筛选** - 方便用户识别可以删除的 Mod 以节省空间
6. **防抖搜索** - BetterRepack 页面使用 300ms 防抖优化搜索性能
