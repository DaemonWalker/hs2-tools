import { CharaDetail } from '@renderer/components/character/CharaDetail'
import { SceneDetail } from '@renderer/components/scene/SceneDetail'
import ipcUtils from '@renderer/logic/ipcUtils'
import { Button, Input, Radio } from 'antd'
import { FC, useEffect, useState } from 'react'

const { fileExists, openFileSelector } = ipcUtils
export const CardExplorer: FC = () => {
  const [type, setType] = useState<'chara' | 'scene'>('chara')
  const [path, setPath] = useState('')
  const [show, setShow] = useState(false)
  useEffect(() => {
    fileExists(path).then((show) => setShow(show))
  }, [path])
  const selectFile = () => {
    openFileSelector().then((path) => (path ? setPath(path) : null))
  }
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', gap: 10 }}>
        <Radio.Group onChange={(e) => setType(e.target.value)} defaultValue={type}>
          <Radio.Button value="chara">人物卡</Radio.Button>
          <Radio.Button value="scene">场景卡</Radio.Button>
        </Radio.Group>
        <Input style={{ flex: 1 }} onChange={(e) => setPath(e.target.value)} value={path}></Input>
        <Button onClick={selectFile}>浏览</Button>
      </div>
      {show &&
        (type === 'chara' ? <CharaDetail filePath={path} /> : <SceneDetail filePath={path} />)}
    </div>
  )
}
