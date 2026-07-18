import { FC } from 'react'
import { Modal, Progress, Typography, Space, Tag, Divider } from 'antd'
import { useDownloadStore } from '@renderer/store/downloadStore'
import { formatBytes, formatSpeed, formatTime, estimateRemainingTime } from '@renderer/utils/format'
import { DownloadOutlined, FileOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons'

const { Text } = Typography

/**
 * 进度弹窗组件
 * 显示批量下载的总体进度和当前文件的详细进度
 */
export const ProgressModal: FC = () => {
  const { currentIndex, totalCount, isBatchDownloading, currentTask, tasks } = useDownloadStore()

  // 是否显示弹窗
  const isVisible = isBatchDownloading && currentIndex < totalCount

  // 计算总体进度
  const overallPercent = totalCount > 0 ? (currentIndex / totalCount) * 100 : 0

  // 当前任务的进度文本
  const currentProgressText = currentTask
    ? `${formatBytes(currentTask.downloaded)} / ${currentTask.total > 0 ? formatBytes(currentTask.total) : 'Unknown'}`
    : ''

  // 预计剩余时间
  const remainingTime =
    currentTask && currentTask.total > 0 && currentTask.speed > 0
      ? estimateRemainingTime(currentTask.downloaded, currentTask.total, currentTask.speed)
      : -1

  // 获取任务状态图标
  const getTaskStatusIcon = (status: string) => {
    switch (status) {
      case 'completed':
        return <CheckCircleOutlined style={{ color: 'var(--color-success)' }} />
      case 'error':
        return <CloseCircleOutlined style={{ color: 'var(--color-error)' }} />
      case 'downloading':
        return <DownloadOutlined style={{ color: 'var(--theme-primary)' }} />
      default:
        return <FileOutlined />
    }
  }

  // 获取任务状态标签
  const getTaskStatusTag = (status: string) => {
    switch (status) {
      case 'completed':
        return <Tag color="success">完成</Tag>
      case 'error':
        return <Tag color="error">失败</Tag>
      case 'downloading':
        return <Tag color="processing">下载中</Tag>
      default:
        return <Tag>等待中</Tag>
    }
  }

  return (
    <Modal
      open={isVisible}
      title={
        <Space>
          <DownloadOutlined />
          <span>Mod 下载进度</span>
          <Tag color="blue">
            {currentIndex} / {totalCount}
          </Tag>
        </Space>
      }
      footer={null}
      closable={false}
      maskClosable={false}
      width={560}
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        {/* 总体进度 */}
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
            <Text strong>总体进度</Text>
            <Text type="secondary">
              已完成 {currentIndex} / {totalCount} 个文件
            </Text>
          </div>
          <Progress
            percent={Number(overallPercent.toFixed(1))}
            strokeColor={{ from: 'var(--theme-primary)', to: 'var(--color-success)' }}
            showInfo={true}
            format={(percent) => `${percent?.toFixed(0)}%`}
          />
        </div>

        <Divider style={{ margin: '12px 0' }} />

        {/* 当前文件进度 */}
        {currentTask && (
          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
              <Space>
                <Text strong>当前文件</Text>
                {getTaskStatusTag(currentTask.status)}
              </Space>
              {currentTask.speed > 0 && (
                <Text type="secondary">速度: {formatSpeed(currentTask.speed)}</Text>
              )}
            </div>

            <div
              style={{
                background: 'var(--color-gray-100)',
                padding: '12px',
                borderRadius: '8px',
                marginBottom: 12
              }}
            >
              <Text
                ellipsis={{ tooltip: currentTask.name }}
                style={{ display: 'block', marginBottom: 8, fontFamily: 'monospace' }}
              >
                {getTaskStatusIcon(currentTask.status)} {currentTask.name}
              </Text>

              <Progress
                percent={Number(currentTask.percent.toFixed(1))}
                status={currentTask.status === 'error' ? 'exception' : 'active'}
                strokeColor={currentTask.status === 'completed' ? 'var(--color-success)' : undefined}
                format={(percent) => `${percent?.toFixed(1)}%`}
              />

              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  marginTop: 8,
                  fontSize: '12px'
                }}
              >
                <Text type="secondary">{currentProgressText}</Text>
                {remainingTime > 0 && (
                  <Text type="secondary">剩余时间: {formatTime(remainingTime)}</Text>
                )}
              </div>
            </div>
          </div>
        )}

        {/* 错误信息 */}
        {currentTask?.status === 'error' && currentTask.message && (
          <div style={{ background: 'var(--color-error-bg)', padding: '12px', borderRadius: '8px' }}>
            <Text type="danger" style={{ fontSize: '12px' }}>
              错误: {currentTask.message}
            </Text>
          </div>
        )}

        {/* 已完成任务统计 */}
        {Object.keys(tasks).length > 0 && (
          <div>
            <Text type="secondary" style={{ fontSize: '12px' }}>
              已处理: {Object.values(tasks).filter((t) => t.status === 'completed').length} 成功,{' '}
              {Object.values(tasks).filter((t) => t.status === 'error').length} 失败
            </Text>
          </div>
        )}
      </Space>
    </Modal>
  )
}

export default ProgressModal
