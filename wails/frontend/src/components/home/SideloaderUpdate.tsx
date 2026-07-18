import { FC, useCallback, useEffect } from 'react'
import { Button, Space, Typography, Alert, Card, Modal } from 'antd'
import { ReloadOutlined, CheckCircleOutlined, CloseCircleOutlined, PauseCircleOutlined } from '@ant-design/icons'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useSideloadStore } from '@renderer/store/sideloadStore'
import { useTaskStore } from '@renderer/store/taskStore'

const { Text } = Typography

// 确认停止对话框
const showStopConfirm = (onConfirm: () => void): void => {
  Modal.confirm({
    title: '确认停止更新？',
    content: '停止后，当前的更新进度将丢失，下次需要重新开始。是否确认停止？',
    okText: '确认停止',
    okType: 'danger',
    cancelText: '继续更新',
    onOk: onConfirm
  })
}

interface SideloaderProgress {
  type: 'info' | 'progress' | 'complete' | 'error'
  message?: string
  current?: number
  total?: number
  percent?: number
}

/**
 * Sideloader 更新组件
 * 用于运行 sideloader.exe 更新 Mod 数据库
 */
export const SideloaderUpdate: FC = () => {
  const {
    sideloadUpdateStatus: status,
    sideloadUpdateProgress,
    setSideloadUpdateStatus: setStatus,
    setSideloadUpdateProgress
  } = useTaskStore()
  const { init } = useSideloadStore()
  
  const progress: SideloaderProgress | null = status === 'running' ? {
    type: 'progress',
    message: sideloadUpdateProgress.message,
    percent: sideloadUpdateProgress.percent
  } : null
  const error = sideloadUpdateProgress.error || ''

  // 监听进度事件
  useEffect(() => {
    const removeProgressListener = ipcUtils.onSideloaderProgress((event) => {
      // 只显示 Processing 消息（正在分析的目录）
      if (event.message?.includes('Processing:')) {
        setSideloadUpdateProgress({
          message: event.message
        })
      }
      if (event.type === 'error') {
        setStatus('error')
        setSideloadUpdateProgress({ error: event.message || 'Unknown error' })
      }
    })

    const removeCompleteListener = ipcUtils.onSideloaderComplete((event) => {
      if (event.success) {
        setStatus('success')
        // 更新 store 中的数据
        init()
      } else {
        setStatus('error')
        setSideloadUpdateProgress({ error: event.error || '更新失败' })
      }
    })

    const removeStoppedListener = ipcUtils.onSideloaderStopped(() => {
      // 进程已停止，回到初始状态
      setStatus('idle')
      setSideloadUpdateProgress({ message: '已停止', error: '' })
    })

    return () => {
      removeProgressListener()
      removeCompleteListener()
      removeStoppedListener()
    }
  }, [init, setStatus, setSideloadUpdateProgress])

  const handleUpdate = useCallback(async () => {
    // 如果正在停止中，忽略点击
    if (status === 'running' && sideloadUpdateProgress.message === '正在停止...') {
      return
    }

    // 如果正在运行，显示确认对话框
    if (status === 'running') {
      showStopConfirm(async () => {
        try {
          // 先显示停止中状态，等待进程真正停止
          setSideloadUpdateProgress({ message: '正在停止...' })
          await ipcUtils.stopSideloader()
          // 状态更新由 onSideloaderStopped 事件处理
        } catch (err) {
          console.error('Failed to stop sideloader:', err)
          setStatus('error')
          setSideloadUpdateProgress({ error: '停止失败，请重试' })
        }
      })
      return
    }

    setStatus('running')
    setSideloadUpdateProgress({ message: '正在启动...', percent: undefined, error: '' })

    try {
      await ipcUtils.runSideloader()
    } catch (err) {
      setStatus('error')
      setSideloadUpdateProgress({ error: err instanceof Error ? err.message : String(err) })
    }
  }, [status, setStatus, setSideloadUpdateProgress])

  const getStatusIcon = () => {
    switch (status) {
      case 'running':
        return <PauseCircleOutlined style={{ color: 'var(--color-error)' }} />
      case 'success':
        return <CheckCircleOutlined style={{ color: 'var(--color-success)' }} />
      case 'error':
        return <CloseCircleOutlined style={{ color: 'var(--color-error)'}} />
      default:
        return <ReloadOutlined />
    }
  }

  const getButtonText = () => {
    // 正在停止中
    if (status === 'running' && sideloadUpdateProgress.message === '正在停止...') {
      return '正在停止...'
    }
    switch (status) {
      case 'running':
        return '点击停止更新'
      case 'success':
        return '更新完成'
      case 'error':
        return '重试更新'
      default:
        return '更新 Sideload 数据'
    }
  }

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Button
        type={status === 'running' ? 'default' : 'primary'}
        danger={status === 'running'}
        onClick={handleUpdate}
        icon={getStatusIcon()}
        size="large"
        block
        style={{ height: 44 }}
      >
        {getButtonText()}
      </Button>

      {/* 进度显示 - 当前正在分析的目录 */}
      {status === 'running' && progress?.message && (
        <Card size="small" style={{ background: 'var(--color-success-bg)', borderColor: 'var(--color-success-border)' }}>
          <Space direction="vertical" style={{ width: '100%' }} size="small">
            <Text type="secondary" style={{ fontSize: 12 }}>正在分析:</Text>
            <Text style={{ fontSize: 12, wordBreak: 'break-all' }} ellipsis={{ tooltip: true }}>
              {progress.message.replace('[Sideloader] Processing: ', '')}
            </Text>
          </Space>
        </Card>
      )}

      {/* 已停止提示 */}
      {status === 'idle' && sideloadUpdateProgress.message === '已停止' && (
        <Alert
          message="已停止"
          description="更新任务已被取消"
          type="warning"
          showIcon
          closable
          onClose={() => {
            setSideloadUpdateProgress({})
          }}
        />
      )}

      {/* 成功提示 */}
      {status === 'success' && (
        <Alert
          message="更新成功"
          description="Sideload 数据已更新"
          type="success"
          showIcon
          closable
          onClose={() => {
            setStatus('idle')
            setSideloadUpdateProgress({})
          }}
        />
      )}

      {/* 错误提示 */}
      {status === 'error' && (
        <Alert
          message="更新失败"
          description={error}
          type="error"
          showIcon
          closable
          onClose={() => {
            setStatus('idle')
            setSideloadUpdateProgress({})
          }}
        />
      )}

      <Text type="secondary" style={{ fontSize: 12 }}>
        从 sideload.betterrepack.com 获取最新的 Mod 信息，用于自动补全缺失的 Mods
      </Text>
    </Space>
  )
}

export default SideloaderUpdate
