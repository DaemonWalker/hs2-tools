import { SmartCardLayout } from '@renderer/components/share/SmartCardLayout'
import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { Button, Input, Segmented, Empty } from 'antd'
import { UserOutlined, BookOutlined, FolderOpenOutlined, InboxOutlined } from '@ant-design/icons'
import { FC, useEffect, useState, useCallback, memo } from 'react'
import { CHARA_FEMALE_PATH, SCENE_PATH } from '@shared/constants'

const { fileExists, dirExists, openFileSelector } = ipcUtils

export const CardExplorer: FC = memo(() => {
  const [type, setType] = useState<'chara' | 'scene'>('chara')
  const [filePath, setFilePath] = useState('')
  const [show, setShow] = useState(false)

  // 获取游戏路径
  const gamePath = useSettingStore((state) => state.settings.path)

  useEffect(() => {
    fileExists(filePath).then((show) => setShow(show))
  }, [filePath])

  const selectFile = useCallback(async () => {
    const subPath = type === 'chara' ? CHARA_FEMALE_PATH : SCENE_PATH
    const targetPath = gamePath
      ? `${gamePath.replace(/\\+$/, '')}/${subPath}`.replaceAll('/', '\\')
      : undefined

    let defaultPath: string | undefined
    if (targetPath) {
      const exists = await dirExists(targetPath)
      defaultPath = exists ? targetPath : undefined
    }

    openFileSelector(defaultPath).then((selectedPath) => {
      if (selectedPath) {
        setFilePath(selectedPath)
      }
    })
  }, [type, gamePath])

  // 清空选择
  const handleClear = useCallback(() => {
    setFilePath('')
    setShow(false)
  }, [])

  return (
    <div className="flex flex-col gap-3 min-h-0 h-full">
      {/* 工具栏 */}
      <div className="flex gap-3 flex-shrink-0 items-center">
        {/* 类型选择 */}
        <Segmented
          value={type}
          onChange={(value) => {
            setType(value as 'chara' | 'scene')
            setFilePath('')  // 清空文件路径
            setShow(false)   // 清空显示
          }}
          options={[
            { label: '人物卡', value: 'chara', icon: <UserOutlined /> },
            { label: '场景卡', value: 'scene', icon: <BookOutlined /> }
          ]}
        />

        {/* 文件选择区域 */}
        <div
          className="flex-1 relative rounded-md border-2 border-dashed border-[var(--border-color)] bg-[var(--bg-secondary)] transition-all duration-200 ease-in-out cursor-pointer hover:border-[var(--primary-color)]"
          onClick={selectFile}
        >
          <Input
            value={filePath}
            onChange={(e) => setFilePath(e.target.value)}
            placeholder="点击选择 PNG 文件"
            variant="borderless"
            className="bg-transparent cursor-pointer"
            suffix={
              filePath ? (
                <Button
                  type="text"
                  size="small"
                  onClick={(e) => {
                    e.stopPropagation()
                    handleClear()
                  }}
                >
                  清除
                </Button>
              ) : (
                <FolderOpenOutlined className="text-[var(--text-secondary)]" />
              )
            }
            onClick={(e) => e.stopPropagation()}
          />
        </div>

        {/* 浏览按钮 */}
        <Button type="primary" icon={<FolderOpenOutlined />} onClick={selectFile}>
          浏览
        </Button>
      </div>

      {/* 内容区域 */}
      {show ? (
        <div className="flex-1 min-h-0 overflow-hidden">
          <SmartCardLayout filePath={filePath} type={type} />
        </div>
      ) : (
        <div className="flex-1 flex items-center justify-center bg-[var(--bg-secondary)] rounded-lg border border-dashed border-[var(--border-color)]">
          <Empty
            image={<InboxOutlined className="text-[64px] text-[var(--text-secondary)]" />}
            description={
              <div className="text-[var(--text-secondary)]">
                <div className="text-base mb-2">请选择一个 PNG 卡片文件</div>
                <div className="text-sm opacity-80">
                  点击上方区域或"浏览"按钮选择文件
                </div>
              </div>
            }
          />
        </div>
      )}
    </div>
  )
})

CardExplorer.displayName = 'CardExplorer'

export default CardExplorer
