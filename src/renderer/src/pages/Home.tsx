import { Settings } from '@renderer/components/app/Settings'
import { Scan } from '@renderer/components/home/Scan'
import { FC } from 'react'

export const Home: FC = () => {
  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <h1>欢迎使用 HS2 角色信息查看器</h1>
      <p>请在上方菜单中选择要查看的内容</p>
      <div style={{ flex: 1 }}>
        <Scan />
      </div>
      <Settings />
    </div>
  )
}
