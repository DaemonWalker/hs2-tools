import { useModStore } from '@renderer/store/modStore'
import { Button } from 'antd'
import { FC, useMemo, useState } from 'react'
import { RunningProgress } from '../share/RunningProgress'
import { scanFemale, scanMods, scanScene } from '@renderer/logic/scanLogic'

export const Scan: FC = () => {
  const { setMods, setUseage } = useModStore()
  const [current, setCurrent] = useState<number>(0)
  const [info, setInfo] = useState<{ mod: string; scene: string; chara: string }>({
    mod: '',
    scene: '',
    chara: ''
  })
  const [scanning, setScanning] = useState<boolean>(false)

  const steps = useMemo(
    () => [
      { title: '扫描Mods' },
      { title: '分析Mods', description: info.mod },
      { title: '扫描场景' },
      { title: '分析场景', description: info.scene },
      { title: '扫描角色' },
      { title: '分析角色', description: info.chara }
    ],
    [info]
  )

  const scan = async () => {
    setScanning(true)
    const mods = await scanMods(
      () => setCurrent(current + 1),
      (t) => setInfo({ ...info, mod: t })
    )
    const scene = await scanScene(
      () => setCurrent(current + 1),
      (t) => setInfo({ ...info, scene: t })
    )
    const female = await scanFemale(
      () => setCurrent(current + 1),
      (t) => setInfo({ ...info, chara: t })
    )

    setMods(mods)
    setUseage({ ...scene, ...female })
    setScanning(false)
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <Button loading={scanning} onClick={scan}>
        开始分析
      </Button>
      <RunningProgress current={current} items={steps}></RunningProgress>
    </div>
  )
}
