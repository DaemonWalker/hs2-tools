import { useModStore } from '@renderer/store/modStore'
import { useTaskStore } from '@renderer/store/taskStore'
import { Button, Space, Typography } from 'antd'
import { FC, useMemo } from 'react'
import { RunningProgress } from '../share/RunningProgress'
import { scanFemale, scanMods, scanScene } from '@renderer/logic/scanLogic'
import { PlayCircleOutlined, CheckCircleOutlined } from '@ant-design/icons'

const { Text } = Typography

export const Scan: FC = () => {
  const { setMods, setUseage } = useModStore()
  const {
    scanStatus,
    scanProgress,
    setScanStatus,
    setScanProgress
  } = useTaskStore()
  
  const { current, info } = scanProgress
  const scanning = scanStatus === 'running'

  const steps = useMemo(
    () => [
      { title: '分析 Mods', description: info.mod },
      { title: '分析场景', description: info.scene },
      { title: '分析角色', description: info.chara }
    ],
    [info]
  )
  const increaseCurrent = () => setScanProgress({ current: current + 1 })

  const scan = async () => {
    setScanStatus('running')
    setScanProgress({ current: 0 })
    try {
      const mods = await scanMods(
        increaseCurrent,
        (t) => setScanProgress({ info: { ...info, mod: t } })
      )
      const scene = await scanScene(
        increaseCurrent,
        (t) => setScanProgress({ info: { ...info, scene: t } })
      )
      const female = await scanFemale(
        increaseCurrent,
        (t) => setScanProgress({ info: { ...info, chara: t } })
      )

      const mergedUseage = { ...scene, ...female }
      await setMods(mods)
      await setUseage(mergedUseage)
      increaseCurrent()
      setScanStatus('complete')
    } catch (error) {
      console.error('Scan failed:', error)
      setScanStatus('error')
    }
  }

  const isComplete = scanStatus === 'complete' || current >= steps.length

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Button 
        type="primary"
        size="large"
        loading={scanning} 
        onClick={scan}
        icon={isComplete ? <CheckCircleOutlined /> : <PlayCircleOutlined />}
        block
        style={{ height: 44 }}
      >
        {scanning ? '分析中...' : isComplete ? '重新分析' : '开始分析'}
      </Button>
      
      {(scanning || current >= 0) && (
        <div style={{ marginTop: 8 }}>
          <RunningProgress current={current} items={steps} />
        </div>
      )}
      
      {!scanning && current < 0 && (
        <Text type="secondary" style={{ fontSize: 12 }}>
          首次使用或数据更新后，建议先执行分析
        </Text>
      )}
    </Space>
  )
}

export default Scan
