import React, { LegacyRef, useRef, useState } from 'react'
import { Button, Input, InputRef, Progress, Steps } from 'antd'
import { checkTargetDir, getAllFiles, moveFile, getCardCharaNames } from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { pathJoin } from '@renderer/logic/ioUtils'

const { TextArea } = Input

export const SceneGroup: React.FC = () => {
  const { scenePath } = useSettingStore()

  const charaNames = useRef<HTMLTextAreaElement>()
  const dirName = useRef<InputRef>()
  const [running, setRunning] = useState(false)
  const [step, setStep] = useState(0)
  const [current, setCurrent] = useState(0)
  const [total, setTotal] = useState(0)

  const group = async () => {
    // @ts-ignore (define in dts)
    const chara = charaNames.current.resizableTextArea.textArea.value
    const dir = dirName.current?.input?.value
    if (!chara || !dir || chara.length < 1 || dir.length < 1) {
      return
    }

    const targetNames = `${chara}`.split('\n')
    const targetDir = pathJoin(scenePath(), `hs_tools_${dir}`)
    setStep(0)
    setCurrent(0)
    setTotal(0)

    setRunning(true)
    try {
      await checkTargetDir(targetDir)
      const scenes = await getAllFiles(scenePath(), { excludeDir: ['hs_tools_'] })
      console.log('scenes', scenes.length)
      setStep(1)
      setTotal(scenes.length)
      for (let i = 0; i < scenes.length; i++) {
        const scene = scenes[i]
        const names = await getCardCharaNames(scene)
        if (!names) {
          continue
        }
        for (const name of targetNames) {
          if (names.find((p) => p.indexOf(name) > -1)) {
            await moveFile(scene, targetDir)
            break
          }
        }
        setCurrent(i)
      }
    } catch (e) {
      console.error(e)
    } finally {
      setRunning(false)
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <TextArea
          placeholder="人物名称，每行一个"
          ref={charaNames as LegacyRef<HTMLTextAreaElement>}
          rows={6}
        />
        <Input placeholder="目标文件夹名称" ref={dirName as LegacyRef<InputRef>} />
      </div>
      <div style={{ flex: 1 }}>
        {running ? (
          <Steps
            current={step}
            items={[
              {
                title: '扫描文件'
              },
              {
                title: '分析/移动文件',
                description: `${current}/${total}`
              }
            ]}
          />
        ) : (
          <></>
        )}
      </div>
      <div
        style={{
          display: 'flex',
          flexDirection: 'row-reverse',
          gap: 10
        }}
      >
        <Button onClick={group} loading={running}>
          开始归档
        </Button>
      </div>
    </div>
  )
}
