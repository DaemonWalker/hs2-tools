import { FC } from 'react'
import { Empty } from 'antd'
import { DownloadTask } from '@renderer/managers/DownloadManager'
import { TaskItem } from './TaskItem'

interface TaskListProps {
  tasks: DownloadTask[]
  onCancel?: (taskId: string) => void
  onRetry?: (taskId: string) => void
  emptyText?: string
  showActions?: boolean
}

/**
 * 任务列表组件
 * 紧凑列表设计
 */
export const TaskList: FC<TaskListProps> = ({
  tasks,
  onCancel,
  onRetry,
  emptyText = '暂无任务',
  showActions = true
}) => {
  if (tasks.length === 0) {
    return (
      <div style={{
        height: '100%',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center'
      }}>
        <Empty
          description={emptyText}
          image={Empty.PRESENTED_IMAGE_SIMPLE}
        />
      </div>
    )
  }

  // 按状态和时间排序：下载中优先，然后按开始时间倒序
  const sortedTasks = [...tasks].sort((a, b) => {
    // 下载中的任务排在前面
    if (a.status === 'downloading' && b.status !== 'downloading') return -1
    if (a.status !== 'downloading' && b.status === 'downloading') return 1
    // 然后按开始时间倒序（最新的在前）
    return b.startTime - a.startTime
  })

  return (
    <div style={{
      height: '100%',
      overflowY: 'auto',
      overflowX: 'hidden'
    }}>
      {sortedTasks.map((task) => (
        <TaskItem
          key={task.id}
          task={task}
          onCancel={onCancel}
          onRetry={onRetry}
          showActions={showActions}
        />
      ))}
    </div>
  )
}

export default TaskList
