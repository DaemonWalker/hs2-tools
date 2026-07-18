import { FC, useMemo } from 'react'
import { Button, Progress, Typography, Tooltip } from 'antd'
import {
  PauseCircleOutlined,
  RedoOutlined,
  CheckCircleFilled,
  ExclamationCircleFilled,
  MinusCircleFilled
} from '@ant-design/icons'
import { DownloadTask } from '@renderer/managers/DownloadManager'
import { formatBytes, formatSpeed, formatTime, estimateRemainingTime } from '@renderer/utils/format'

const { Text } = Typography

interface TaskItemProps {
  task: DownloadTask
  onCancel?: (taskId: string) => void
  onRetry?: (taskId: string) => void
  showActions?: boolean
}

/**
 * 单个任务项组件 - 紧凑设计
 */
export const TaskItem: FC<TaskItemProps> = ({ task, onCancel, onRetry, showActions = true }) => {
  // 状态图标和颜色
  const statusConfig = useMemo(() => {
    switch (task.status) {
      case 'downloading':
        return { icon: null, color: '#1890ff', bgColor: 'var(--ant-color-primary-bg)' }
      case 'completed':
        return { icon: <CheckCircleFilled className="text-[#52c41a]" />, color: '#52c41a', bgColor: 'var(--ant-color-success-bg)' }
      case 'error':
        return { icon: <ExclamationCircleFilled className="text-[#ff4d4f]" />, color: '#ff4d4f', bgColor: 'var(--ant-color-error-bg)' }
      case 'cancelled':
        return { icon: <MinusCircleFilled className="text-[#faad14]" />, color: '#faad14', bgColor: 'var(--ant-color-warning-bg)' }
      default:
        return { icon: null, color: '#8c8c8c', bgColor: 'transparent' }
    }
  }, [task.status])

  // 计算剩余时间
  const remainingTime = useMemo(() => {
    if (task.status !== 'downloading') return null
    const seconds = estimateRemainingTime(
      task.progress.downloaded,
      task.progress.total,
      task.progress.speed
    )
    if (seconds < 0) return null
    return formatTime(seconds)
  }, [task])

  // 状态文本
  const statusText = useMemo(() => {
    const { downloaded, total, percent, speed } = task.progress

    switch (task.status) {
      case 'downloading':
        const sizeText = total > 0
          ? `${formatBytes(downloaded)} / ${formatBytes(total)}`
          : formatBytes(downloaded)
        const timeText = remainingTime ? `· ${remainingTime}` : ''
        return `${sizeText} · ${percent.toFixed(0)}% · ${formatSpeed(speed)} ${timeText}`
      case 'completed':
        return total > 0 ? formatBytes(total) : '完成'
      case 'error':
        return task.error || '下载失败'
      case 'cancelled':
        return '已取消'
      default:
        return '等待中'
    }
  }, [task, remainingTime])

  // 操作按钮
  const actionButton = useMemo(() => {
    if (!showActions) return null

    switch (task.status) {
      case 'downloading':
        return (
          <Tooltip title="取消">
            <Button
              size="small"
              type="text"
              danger
              icon={<PauseCircleOutlined />}
              onClick={() => onCancel?.(task.id)}
            />
          </Tooltip>
        )
      case 'error':
      case 'cancelled':
        return (
          <Tooltip title="重试">
            <Button
              size="small"
              type="text"
              icon={<RedoOutlined />}
              onClick={() => onRetry?.(task.id)}
            />
          </Tooltip>
        )
      default:
        return null
    }
  }, [task.status, task.id, onCancel, onRetry, showActions])

  return (
    <div
      className="flex items-center px-4 py-2.5 gap-3"
      style={{
        borderBottom: '1px solid var(--ant-color-border)',
        backgroundColor: statusConfig.bgColor,
      }}
    >
      {/* 状态图标 */}
      <div className="flex-shrink-0 w-5 flex justify-center">
        {statusConfig.icon}
      </div>

      {/* 主要内容区 */}
      <div className="flex-1 min-w-0 flex flex-col gap-1">
        {/* 文件名 */}
        <Text
          strong
          ellipsis={{ tooltip: task.name }}
          className="text-sm leading-5"
        >
          {task.name}
        </Text>

        {/* 进度条 (仅下载中显示) */}
        {task.status === 'downloading' && (
          <Progress
            percent={Number(task.progress.percent.toFixed(1))}
            size={{ height: 4 }}
            status="active"
            showInfo={false}
            strokeColor={statusConfig.color}
          />
        )}

        {/* 状态文本 */}
        <Text
          type="secondary"
          className="text-xs leading-4"
          style={{
            color: task.status === 'error' ? '#ff4d4f' : undefined
          }}
          ellipsis
        >
          {statusText}
        </Text>
      </div>

      {/* 操作按钮 */}
      <div className="flex-shrink-0">
        {actionButton}
      </div>
    </div>
  )
}

export default TaskItem
