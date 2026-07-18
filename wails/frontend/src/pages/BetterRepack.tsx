import { useSideloadStore } from '@renderer/store/sideloadStore'
import { useModStore } from '@renderer/store/modStore'
import { Card, Table, Typography, Tag, Statistic, Row, Col, Input, Button, Tooltip, Empty, message } from 'antd'
import { FC, useEffect, useMemo, useState, useCallback, useRef } from 'react'
import { ReloadOutlined, DatabaseOutlined, CloudDownloadOutlined, DownloadOutlined, CheckCircleOutlined } from '@ant-design/icons'
import { useShallow } from 'zustand/shallow'
import ipcUtils from '@renderer/logic/ipcUtils'

const { Title, Text } = Typography

interface SideloadItem {
  guid: string
  url: string
  existsLocally: boolean
}

export const BetterRepack: FC = () => {
  const { sideload, init, isLoading } = useSideloadStore(
    useShallow((state) => ({
      sideload: state.sideload,
      init: state.init,
      isLoading: state.isLoading
    }))
  )
  const { mods, init: initMods } = useModStore(
    useShallow((state) => ({
      mods: state.mods,
      init: state.init
    }))
  )
  const [inputText, setInputText] = useState('')
  const [searchText, setSearchText] = useState('')
  const [downloadingGuids, setDownloadingGuids] = useState<Set<string>>(new Set())
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // 组件挂载时自动加载数据
  useEffect(() => {
    init()
    initMods()
  }, [init, initMods])

  // 防抖处理输入
  const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const value = e.target.value
    setInputText(value)
    
    // 清除之前的定时器
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current)
    }
    
    // 设置新的定时器，300ms 后更新搜索文本
    debounceTimerRef.current = setTimeout(() => {
      setSearchText(value)
    }, 300)
  }, [])

  // 组件卸载时清除定时器
  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current)
      }
    }
  }, [])

  const data: SideloadItem[] = useMemo(() => {
    const items = Object.entries(sideload).map(([guid, url]) => ({
      guid,
      url,
      existsLocally: !!mods[guid]
    }))
    
    // 按搜索文本过滤
    if (searchText) {
      const lowerSearch = searchText.toLowerCase()
      return items.filter(
        (item) =>
          item.guid.toLowerCase().includes(lowerSearch) ||
          item.url.toLowerCase().includes(lowerSearch)
      )
    }
    return items
  }, [sideload, mods, searchText])

  // 统计数据
  const stats = useMemo(() => {
    const total = Object.keys(sideload).length
    const existing = Object.keys(sideload).filter(guid => mods[guid]).length
    const missing = total - existing
    
    return {
      total,
      existing,
      missing,
      filtered: data.length
    }
  }, [sideload, mods, data.length])

  // 处理下载
  const handleDownload = useCallback(async (guid: string, url: string) => {
    if (downloadingGuids.has(guid)) return
    
    setDownloadingGuids(prev => new Set(prev).add(guid))
    try {
      await ipcUtils.triggerDownload({ name: guid, url })
      message.success(`已开始下载: ${guid}`)
    } catch (error) {
      message.error(`下载失败: ${guid}`)
      console.error('Download error:', error)
    } finally {
      setDownloadingGuids(prev => {
        const next = new Set(prev)
        next.delete(guid)
        return next
      })
    }
  }, [downloadingGuids])

  const columns = [
    {
      title: 'GUID',
      dataIndex: 'guid',
      key: 'guid',
      width: '50%',
      ellipsis: {
        showTitle: false
      },
      render: (guid: string) => (
        <Tooltip title={guid}>
          <Text code copyable={{ text: guid }}>
            {guid}
          </Text>
        </Tooltip>
      )
    },
    {
      title: '下载链接',
      dataIndex: 'url',
      key: 'url',
      width: '35%',
      ellipsis: {
        showTitle: false
      },
      render: (url: string) => (
        <Tooltip title={url}>
          <Text
            type="secondary"
            style={{ fontSize: 12 }}
            copyable={{ text: url }}
          >
            {url.length > 40 ? url.substring(0, 40) + '...' : url}
          </Text>
        </Tooltip>
      )
    },
    {
      title: '本地存在',
      dataIndex: 'existsLocally',
      key: 'existsLocally',
      width: '15%',
      align: 'center' as const,
      render: (existsLocally: boolean, record: SideloadItem) => {
        if (existsLocally) {
          return (
            <Tag color="success" icon={<CheckCircleOutlined />}>
              已存在
            </Tag>
          )
        }
        return (
          <Button
            type="primary"
            size="small"
            icon={<DownloadOutlined />}
            loading={downloadingGuids.has(record.guid)}
            onClick={() => handleDownload(record.guid, record.url)}
          >
            下载
          </Button>
        )
      }
    }
  ]

  return (
    <div style={{ padding: '24px', height: '100%', overflow: 'auto' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
        {/* 标题栏 */}
        <Row justify="space-between" align="middle">
          <Col>
            <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
              <DatabaseOutlined style={{ fontSize: 24, color: 'var(--color-primary)' }} />
              <Title level={3} style={{ margin: 0 }}>
                BetterRepack Sideload 数据
              </Title>
            </div>
          </Col>
          <Col>
            <Button
              icon={<ReloadOutlined />}
              onClick={() => {
                init()
                initMods()
              }}
              loading={isLoading}
            >
              刷新数据
            </Button>
          </Col>
        </Row>

        {/* 统计卡片 */}
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={8}>
            <Card>
              <Statistic
                title="总 Mod 数量"
                value={stats.total}
                suffix="个"
                valueStyle={{ color: 'var(--color-primary)' }}
              />
            </Card>
          </Col>
          <Col xs={24} sm={8}>
            <Card>
              <Statistic
                title="本地已存在"
                value={stats.existing}
                suffix="个"
                valueStyle={{ color: 'var(--color-success)' }}
              />
            </Card>
          </Col>
          <Col xs={24} sm={8}>
            <Card>
              <Statistic
                title="缺失 Mods"
                value={stats.missing}
                suffix="个"
                valueStyle={{ color: 'var(--color-error)' }}
              />
            </Card>
          </Col>
        </Row>

        {/* 搜索和表格 */}
        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            <Input
              placeholder="搜索 GUID"
              onChange={handleInputChange}
              value={inputText}
              style={{ width: '100%' }}
            />
            
            <Table
              columns={columns}
              dataSource={data}
              rowKey="guid"
              loading={isLoading}
              pagination={{
                pageSize: 20,
                showSizeChanger: true,
                showTotal: (total) => `共 ${total} 条记录`,
                pageSizeOptions: [10, 20, 50, 100]
              }}
              scroll={{ x: 600 }}
              size="middle"
              locale={{
                emptyText: stats.total === 0 ? (
                  <Empty
                    image={<CloudDownloadOutlined style={{ fontSize: 64, color: '#d9d9d9' }} />}
                    description={
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                        <Text type="secondary">暂无 Sideload 数据</Text>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          请点击上方"刷新数据"按钮或前往首页更新数据
                        </Text>
                      </div>
                    }
                  />
                ) : undefined
              }}
            />
          </div>
        </Card>

        {/* 说明文字 */}
        <Text type="secondary" style={{ fontSize: 12 }}>
          Sideload 数据用于自动补全角色和场景所需的缺失 Mods。数据来源于 sideload.betterrepack.com。
        </Text>
      </div>
    </div>
  )
}

export default BetterRepack
