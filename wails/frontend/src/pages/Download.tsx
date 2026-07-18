import { FC, useState, useEffect, useRef } from 'react'
import { Tabs, Button, Badge, Typography, message } from 'antd'
import type { TabsProps } from 'antd'
import {
  PauseCircleOutlined,
  ClearOutlined,
  DownloadOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  AppstoreOutlined,
  SyncOutlined
} from '@ant-design/icons'
import { useDownloadManager } from '@renderer/hooks/useDownloadManager'
import { StatisticsBar } from '@renderer/components/download/StatisticsBar'
import { TaskList } from '@renderer/components/download/TaskList'
import { formatSpeed } from '@renderer/store/downloadStore'

const { Text } = Typography

/**
 * 下载管理页面
 * 占满整个父容器，紧凑布局
 */
export const Download: FC = () => {
  const {
    tasks,
    activeTasks,
    completedTasks,
    failedTasks,
    totalCount,
    activeCount,
    completedCount,
    failedCount,
    totalSpeed,
    cancel,
    cancelAll,
    retry,
    clearCompleted
  } = useDownloadManager()

  const [activeTab, setActiveTab] = useState('all')
  
  // 用于追踪之前是否有活跃任务
  const prevActiveCountRef = useRef(activeCount)

  // 监听所有下载任务完成
  useEffect(() => {
    const prevActiveCount = prevActiveCountRef.current
    
    // 如果之前有活跃任务，现在没有了，且已完成任务数大于0，说明全部完成
    if (prevActiveCount > 0 && activeCount === 0 && completedCount > 0) {
      message.success('所有下载任务已经完成')
    }
    
    prevActiveCountRef.current = activeCount
  }, [activeCount, completedCount])

  // 判断是否有可清除的任务
  const hasClearable = completedCount > 0 || failedTasks.some(t => t.status === 'cancelled')

  // Tab 配置
  const tabItems: TabsProps['items'] = [
    {
      key: 'all',
      label: (
        <span>
          <AppstoreOutlined style={{ marginRight: 4 }} />
          全部
          {totalCount > 0 && (
            <Badge count={totalCount} style={{ marginLeft: 4, backgroundColor: '#8c8c8c' }} />
          )}
        </span>
      ),
      children: (
        <TaskList
          tasks={tasks}
          onCancel={cancel}
          onRetry={retry}
          emptyText="暂无下载任务"
        />
      )
    },
    {
      key: 'active',
      label: (
        <span>
          {activeCount > 0 ? <SyncOutlined spin style={{ marginRight: 4 }} /> : <DownloadOutlined style={{ marginRight: 4 }} />}
          下载中
          {activeCount > 0 && (
            <Badge count={activeCount} style={{ marginLeft: 4 }} color="processing" />
          )}
        </span>
      ),
      children: (
        <TaskList
          tasks={activeTasks}
          onCancel={cancel}
          emptyText="没有正在下载的任务"
        />
      )
    },
    {
      key: 'completed',
      label: (
        <span>
          <CheckCircleOutlined style={{ marginRight: 4 }} />
          已完成
          {completedCount > 0 && (
            <Badge count={completedCount} style={{ marginLeft: 4 }} color="success" />
          )}
        </span>
      ),
      children: (
        <TaskList
          tasks={completedTasks}
          emptyText="没有已完成的任务"
          showActions={false}
        />
      )
    },
    {
      key: 'failed',
      label: (
        <span>
          <CloseCircleOutlined style={{ marginRight: 4 }} />
          失败
          {failedCount > 0 && (
            <Badge count={failedCount} style={{ marginLeft: 4 }} color="error" />
          )}
        </span>
      ),
      children: (
        <TaskList
          tasks={failedTasks}
          onRetry={retry}
          emptyText="没有失败的任务"
        />
      )
    }
  ]

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        padding: '12px 16px',
        boxSizing: 'border-box',
        gap: 12
      }}
    >
      {/* 顶部栏：状态 + 操作按钮 */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexShrink: 0
        }}
      >
        <StatisticsBar
          activeCount={activeCount}
          failedCount={failedCount}
          totalSpeed={totalSpeed}
        />

        {/* 操作按钮 */}
        <div style={{ display: 'flex', gap: 8 }}>
          {activeCount > 0 && (
            <Button
              size="small"
              danger
              icon={<PauseCircleOutlined />}
              onClick={cancelAll}
            >
              全部取消
            </Button>
          )}
          {hasClearable && (
            <Button
              size="small"
              icon={<ClearOutlined />}
              onClick={clearCompleted}
            >
              清除已完成
            </Button>
          )}
        </div>
      </div>

      {/* 任务列表 Tabs */}
      <div
        style={{
          flex: 1,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: 'var(--ant-color-bg-container)',
          borderRadius: 8,
          border: '1px solid var(--ant-color-border)'
        }}
      >
        <Tabs
          activeKey={activeTab}
          onChange={setActiveTab}
          items={tabItems}
          className="download-tabs"
          size="small"
          style={{ height: '100%', display: 'flex', flexDirection: 'column' }}
        />
      </div>

      {/* 底部信息栏 */}
      <div
        style={{
          flexShrink: 0,
          padding: '8px 12px',
          backgroundColor: 'var(--ant-color-bg-layout)',
          borderRadius: 6,
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center'
        }}
      >
        <Text type="secondary" style={{ fontSize: 12 }}>
          下载目录: mods/hs2-tool-download
        </Text>
        {activeCount > 0 && (
          <Text style={{ fontSize: 12, color: '#1890ff' }}>
            总速度: {formatSpeed(totalSpeed)}
          </Text>
        )}
      </div>
    </div>
  )
}

export default Download
