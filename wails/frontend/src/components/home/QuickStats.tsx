import { useModStore } from '@renderer/store/modStore'
import { Space, Button } from 'antd'
import {
  DatabaseOutlined,
  FileImageOutlined,
  UserOutlined,
  ArrowRightOutlined,
  LinkOutlined,
  CheckCircleOutlined,
  ExclamationCircleOutlined
} from '@ant-design/icons'
import { FC, useMemo } from 'react'
import { useNavigate } from 'react-router-dom'

export const QuickStats: FC = () => {
  const { mods, useage } = useModStore()
  const navigate = useNavigate()

  const modCount = Object.keys(mods).length
  const useageCount = Object.keys(useage).length
  const totalRefs = useMemo(() =>
    Object.values(useage).reduce((sum, count) => sum + (count || 0), 0),
  [useage])

  return (
    <Space direction="vertical" className="w-full" size="large">
      {/* 统计项 - 使用更有层次的设计 */}
      <div className="flex flex-col gap-[var(--spacing-sm)]">
        {/* 本地 Mods */}
        <div className="flex items-center p-[var(--spacing-md)] bg-[var(--bg-tertiary)] rounded-[var(--radius-sm)] border border-[var(--border-color)]">
          <div className="w-10 h-10 bg-[rgba(34,197,94,0.15)] rounded-[var(--radius-sm)] flex items-center justify-center mr-[var(--spacing-md)]">
            <DatabaseOutlined className="text-xl text-[var(--color-success)]" />
          </div>
          <div className="flex-1">
            <div className="text-2xl font-bold text-[var(--text-primary)] leading-tight">
              {modCount.toLocaleString()}
            </div>
            <div className="text-sm text-[var(--text-secondary)]">
              本地 Mods
            </div>
          </div>
          <CheckCircleOutlined className="text-base text-[var(--color-success)]" />
        </div>

        {/* 被引用 Mods */}
        <div className="flex items-center p-[var(--spacing-md)] bg-[var(--bg-tertiary)] rounded-[var(--radius-sm)] border border-[var(--border-color)]">
          <div className="w-10 h-10 bg-[rgba(59,130,246,0.15)] rounded-[var(--radius-sm)] flex items-center justify-center mr-[var(--spacing-md)]">
            <LinkOutlined className="text-xl text-[var(--color-info)]" />
          </div>
          <div className="flex-1">
            <div className="text-2xl font-bold text-text-primary leading-tight">
              {useageCount.toLocaleString()}
            </div>
            <div className="text-sm text-text-secondary">
              被引用 Mods
            </div>
          </div>
          {useageCount > 0 ? (
            <CheckCircleOutlined className="text-base text-[var(--color-success)]" />
          ) : (
            <ExclamationCircleOutlined className="text-base text-[var(--text-muted)]" />
          )}
        </div>

        {/* 总引用次数 */}
        <div className="p-[var(--spacing-md)] bg-[var(--primary-color)] rounded-[var(--radius-sm)] relative overflow-hidden">
          {/* 装饰性背景 */}
          <div className="absolute -top-5 -right-5 w-20 h-20 bg-[rgba(0,0,0,0.1)] rounded-full" />

          <div className="relative z-10">
            <div className="text-sm font-medium text-[rgba(0,0,0,0.7)] mb-[var(--spacing-xs)]">
              总引用次数
            </div>
            <div className="text-4xl font-extrabold text-[#000] leading-none">
              {totalRefs.toLocaleString()}
            </div>
          </div>
        </div>
      </div>

      {/* 快捷导航 */}
      <Space direction="vertical" className="w-full" size="small">
        <Button
          type="default"
          block
          onClick={() => navigate('/mods-local')}
          icon={<DatabaseOutlined />}
          className="h-11 text-sm font-medium"
        >
          管理 Mods <ArrowRightOutlined />
        </Button>
        <Button
          type="default"
          block
          onClick={() => navigate('/scene')}
          icon={<FileImageOutlined />}
          className="h-11 text-sm font-medium"
        >
          管理场景 <ArrowRightOutlined />
        </Button>
        <Button
          type="default"
          block
          onClick={() => navigate('/character')}
          icon={<UserOutlined />}
          className="h-11 text-sm font-medium"
        >
          管理角色 <ArrowRightOutlined />
        </Button>
      </Space>
    </Space>
  )
}

export default QuickStats
