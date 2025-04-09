import { pathJoin } from '@renderer/logic/ioUtils'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useModStore } from '@renderer/store/modStore'
import { useSettingStore } from '@renderer/store/settingStore'
import { Button } from 'antd'
import { FC, useMemo, useState } from 'react'
import { RunningProgress } from '../share/RunningProgress'
import { ProgressInfo } from '@shared/models/progressInfo'
import { ModModel } from '@shared/models/modModel'

const { getAllFiles, readPngForMod, readZipMod } = ipcUtils

export const Scan: FC = () => {
  const { modsPath, scenePath, charaFemalePath } = useSettingStore()
  const { setMods } = useModStore()
  const [current, setCurrent] = useState<number>(0)
  const [info, setInfo] = useState<{ mod: ProgressInfo; scene: ProgressInfo; chara: ProgressInfo }>(
    {
      mod: { current: 0, total: 0 },
      scene: { current: 0, total: 0 },
      chara: { current: 0, total: 0 }
    }
  )
  const [scanning, setScanning] = useState<boolean>(false)

  const steps = useMemo(
    () => [
      { title: '扫描Mods' },
      { title: '分析Mods', description: `${info.mod.current}/${info.mod.total}` },
      { title: '扫描场景' },
      { title: '分析场景', description: `${info.scene.current}/${info.scene.total}` },
      { title: '扫描角色' },
      { title: '分析角色', description: `${info.chara.current}/${info.chara.total}` }
    ],
    [info]
  )

  const scanMods = async () => {
    setCurrent(0)
    const allLocalMods = await getAllFiles(modsPath(), { targetExtension: ['.zipmod'] })
    setInfo({ ...info, mod: { current: 0, total: allLocalMods.length } })

    setCurrent(1)
    const result: ModModel = {}
    for (let i = 0; i < allLocalMods.length; i++) {
      const mod = allLocalMods[i]
      const zipInfo = await readZipMod(mod)
      if (zipInfo) {
        const guid = Object.keys(zipInfo)[0]
        result[guid] = { ...zipInfo[guid]!, path: mod }
      }
      setInfo({ ...info, mod: { current: i + 1, total: allLocalMods.length } })
    }
    return result
  }

  const scanScene = async (result: ModModel) => {
    setCurrent(2)
    const scene = await getAllFiles(scenePath(), { targetExtension: ['.png'] })
    setInfo({ ...info, scene: { current: 0, total: scene.length } })

    setCurrent(3)
    for (let i = 0; i < scene.length; i++) {
      const chara = scene[i]
      const mods = await readPngForMod(chara)
      if (mods) {
        for (const mod of mods) {
          result[mod]
          if (result[mod]) {
            result[mod].used++
          }
        }
      }
      setInfo({ ...info, scene: { current: i + 1, total: scene.length } })
    }
    return result
  }

  const scanFemale = async (result: ModModel) => {
    setCurrent(4)
    const charas = await getAllFiles(charaFemalePath(), { targetExtension: ['.png'] })
    setInfo({ ...info, chara: { current: 0, total: charas.length } })

    setCurrent(5)
    for (let i = 0; i < charas.length; i++) {
      const chara = charas[i]
      const mods = await readPngForMod(chara)
      if (mods) {
        for (const mod of mods) {
          if (result[mod]) {
            result[mod].used++
          }
        }
      }
      setInfo({ ...info, chara: { current: i + 1, total: charas.length } })
    }
    return result
  }

  const scan = async () => {
    setScanning(true)
    let result = await scanMods()
    result = await scanScene(result)
    result = await scanFemale(result)
    setMods(result)
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
