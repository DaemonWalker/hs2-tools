import { Scan } from '@renderer/components/home/Scan'
import { SideloadInit } from '@renderer/components/home/SideloadInit'
import { SideloaderUpdate } from '@renderer/components/home/SideloaderUpdate'
import { QuickStats } from '@renderer/components/home/QuickStats'
import { PathSetting } from '@renderer/components/home/PathSetting'
import { GameLauncher } from '@renderer/components/home/GameLauncher'
import { Card, Typography, Space, Divider, Row, Col } from 'antd'
import {
  RocketOutlined,
  DatabaseOutlined,
  ThunderboltOutlined,
  SettingOutlined,
  BarChartOutlined,
  FolderOpenOutlined
} from '@ant-design/icons'
import { FC, memo } from 'react'

const { Title, Text } = Typography

export const Home: FC = memo(() => {
  return (
    <div className="home h-full p-[var(--spacing-lg)] overflow-auto bg-[var(--bg-primary)]">
      {/* 英雄区域 - 大胆的视觉焦点 */}
      <div className="relative overflow-hidden mb-[var(--spacing-xl)] p-[var(--spacing-xl)] p-[var(--spacing-lg)] bg-[var(--bg-secondary)] rounded-[var(--radius-lg)] border border-[var(--border-color)]">
        {/* 装饰性背景元素 */}
        <div
          className="absolute -top-12 -right-12 w-[200px] h-[200px] rounded-full pointer-events-none"
          style={{
            background: 'radial-gradient(circle, rgba(245, 158, 11, 0.1) 0%, transparent 70%)'
          }}
        />

        <div className="relative z-10">
          <Space align="start" size="large">
            <div
              className="w-16 h-16 bg-[var(--primary-color)] rounded-[var(--radius-md)] flex items-center justify-center"
              style={{ boxShadow: '0 8px 24px rgba(245, 158, 11, 0.25)' }}
            >
              <FolderOpenOutlined className="text-[32px] text-[#000]" />
            </div>

            <div className="flex-1">
              <Title level={2} className="!m-0 !mb-[var(--spacing-sm)] !text-[32px] !font-bold !tracking-tight">
                HS2 角色信息查看器
              </Title>
              <Text className="text-base text-[var(--text-secondary)] block max-w-[600px]">
                一站式管理你的 Honey Select 2 角色卡、场景和模组
              </Text>
            </div>
          </Space>
        </div>
      </div>

      {/* 三列布局 - 顶部对齐 */}
      <Row gutter={[24, 24]} className="flex items-stretch">
        {/* 左列：快速操作 */}
        <Col xs={24} sm={24} md={8} className="flex">
          <div className="flex flex-col gap-[var(--spacing-md)] w-full">
            <Card
              className="theme-card flex-1"
              title={
                <Space>
                  <ThunderboltOutlined className="text-xl text-[var(--primary-color)]" />
                  <span className="font-semibold">数据分析</span>
                </Space>
              }
              size="small"
              headStyle={{ borderBottom: '1px solid var(--border-color)' }}
            >
              <Scan />
            </Card>

            <Card
              className="theme-card"
              title={
                <Space>
                  <SettingOutlined className="text-xl text-[var(--text-secondary)]" />
                  <span className="font-semibold">路径设置</span>
                </Space>
              }
              size="small"
              headStyle={{ borderBottom: '1px solid var(--border-color)' }}
            >
              <PathSetting />
            </Card>

            <Card
              className="theme-card"
              title={
                <Space>
                  <RocketOutlined className="text-xl text-[var(--color-success)]" />
                  <span className="font-semibold">快速启动</span>
                </Space>
              }
              size="small"
              headStyle={{ borderBottom: '1px solid var(--border-color)' }}
            >
              <GameLauncher />
            </Card>
          </div>
        </Col>

        {/* 中列：数据初始化 */}
        <Col xs={24} sm={24} md={8} className="flex">
          <Card
            className="theme-card w-full"
            title={
              <Space>
                <DatabaseOutlined className="text-xl text-[var(--accent-color)]" />
                <span className="font-semibold">数据初始化</span>
              </Space>
            }
            size="small"
            headStyle={{ borderBottom: '1px solid var(--border-color)' }}
          >
            <SideloaderUpdate />

            <Divider className="!my-[var(--spacing-md)]" />

            <SideloadInit />

            <Divider className="!my-spacing-md" />

            <div className="py-[var(--spacing-xs)]">
              <Text type="secondary" className="text-[13px] leading-relaxed">
                <strong className="text-[var(--text-primary)]">提示：</strong>
                <br />• 点击「更新 Sideload 数据」获取最新的 Mod 数据库
                <br />• 点击「补全缺失 Mods」自动下载角色卡和场景所需的依赖
                <br />• 请确保已完成数据分析，系统会自动检测缺失的 Mods
              </Text>
            </div>
          </Card>
        </Col>

        {/* 右列：数据概览 */}
        <Col xs={24} sm={24} md={8} className="flex">
          <Card
            className="theme-card w-full"
            title={
              <Space>
                <BarChartOutlined className="text-xl text-primary-DEFAULT" />
                <span className="font-semibold">数据概览</span>
              </Space>
            }
            size="small"
            headStyle={{ borderBottom: '1px solid var(--border-color)' }}
          >
            <QuickStats />
          </Card>
        </Col>
      </Row>

      {/* 底部提示 */}
      <div className="text-center mt-[var(--spacing-xl)] py-[var(--spacing-md)]">
        <Text type="secondary" className="text-[13px]">
          首次使用请先在「路径设置」中选择游戏目录，然后执行「数据分析」
        </Text>
      </div>
    </div>
  )
})

Home.displayName = 'Home'

export default Home
