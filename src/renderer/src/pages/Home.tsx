import { Settings } from '@renderer/components/app/Settings'
import { Scan } from '@renderer/components/home/Scan'
import { SideloadInit } from '@renderer/components/home/SideloadInit'
import { FC } from 'react'

export const Home: FC = () => {
  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <h1>欢迎使用 HS2 角色信息查看器</h1>
      <div style={{ flex: 1, display: 'flex', gap: 10 }}>
        <div style={{ flex: 1 }}>
          <Scan />
        </div>
        <div style={{ flex: 1 }}>
          <SideloadInit />
        </div>
      </div>
      <Settings />
    </div>
  )
}
