import { Component, ErrorInfo, ReactNode } from 'react'
import { Result, Button } from 'antd'
import { ReloadOutlined } from '@ant-design/icons'

interface Props {
  children: ReactNode
  fallback?: ReactNode
  onReset?: () => void
}

interface State {
  hasError: boolean
  error: Error | null
  errorInfo: ErrorInfo | null
}

/**
 * 错误边界组件 - 捕获子组件的渲染错误
 * 防止整个应用因单个组件错误而崩溃
 */
export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false, error: null, errorInfo: null }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error, errorInfo: null }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('ErrorBoundary caught an error:', error, errorInfo)
    this.setState({ error, errorInfo })
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null, errorInfo: null })
    this.props.onReset?.()
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback
      }

      return (
        <Result
          status="error"
          title="组件渲染出错"
          subTitle={this.state.error?.message || '发生未知错误'}
          extra={
            <Button 
              type="primary" 
              icon={<ReloadOutlined />}
              onClick={this.handleReset}
              aria-label="重试加载组件"
            >
              重试
            </Button>
          }
        />
      )
    }

    return this.props.children
  }
}

/**
 * 小型错误边界 - 用于卡片等小组件
 */
export class CardErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props)
    this.state = { hasError: false, error: null, errorInfo: null }
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error, errorInfo: null }
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('CardErrorBoundary caught an error:', error, errorInfo)
    this.setState({ error, errorInfo })
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null, errorInfo: null })
    this.props.onReset?.()
  }

  render() {
    if (this.state.hasError) {
      return (
        <div 
          style={{ 
            padding: 16, 
            textAlign: 'center',
            border: '1px dashed var(--border-color)',
            borderRadius: 8,
            color: 'var(--text-secondary)'
          }}
        >
          <div style={{ fontSize: 12, marginBottom: 8 }}>加载失败</div>
          <Button size="small" onClick={this.handleReset} aria-label="重试加载">重试</Button>
        </div>
      )
    }

    return this.props.children
  }
}
