import ipcUtils from '@renderer/logic/ipcUtils'
import { useSettingStore } from '@renderer/store/settingStore'
import { Divider, Pagination, PaginationProps } from 'antd'
import { FC, useEffect, useMemo, useState } from 'react'
import { ResponsiveContainer } from './ResponsiveContainer'

const { getAllFiles } = ipcUtils

interface IProps {
  columnCount: number
  type: 'chara' | 'scene'
}

export const CardList: FC<IProps> = ({ columnCount, type }) => {
  const { settings, scenePath, charaFemalePath } = useSettingStore()
  const charaPath = useMemo(() => {
    const path = type === 'chara' ? charaFemalePath() : scenePath()
    return settings.path ? path : undefined
  }, [settings.path, type])
  const [current, setCurrent] = useState(1)
  const [cards, setCards] = useState<string[]>()
  const [pageSize, setPageSize] = useState(20)
  const data = useMemo(
    () => cards?.slice((current - 1) * pageSize, current * pageSize),
    [cards, current, pageSize]
  )
  useEffect(() => {
    if (!charaPath) {
      return
    }
    getAllFiles(charaPath, { targetExtension: ['.png'] }).then((files) => setCards(files))
  }, [charaPath])

  return (
    <div
      style={{
        height: '100%',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: 10
      }}
    >
      <ResponsiveContainer data={data} columnCount={columnCount} type={type} />
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <Pagination
          showQuickJumper
          defaultCurrent={2}
          total={cards?.length}
          onChange={(page, pageSize) => {
            setCurrent(page)
            setPageSize(pageSize)
          }}
          onShowSizeChange={(current, size) => {
            setCurrent(current)
            setPageSize(size)
          }}
          defaultPageSize={pageSize}
          pageSizeOptions={[12, 20, 40]}
        />
      </div>
    </div>
  )
}
