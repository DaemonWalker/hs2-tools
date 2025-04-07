import { useDownloadStore } from '@renderer/store/downloadStore'
import { Progress } from 'antd'
import { FC, useMemo } from 'react'

export const Download: FC = () => {
  const { tasks } = useDownloadStore()
  const data = useMemo(() => Object.entries(tasks).map(([_, info]) => info), [tasks])

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {data &&
        data.length > 0 &&
        data.map((info, index) => (
          <div key={index} style={{ display: 'flex', gap: 10 }}>
            <div
              style={{
                flex: 1,
                display: 'flex',
                maxWidth: 200,
                textWrap: 'nowrap',
                textOverflow: 'ellipsis',
                justifyContent: 'end',
                alignItems: 'end'
              }}
            >
              {info.name}
            </div>
            <Progress
              style={{ flex: 4 }}
              percent={info.percent}
              showInfo={false}
              strokeColor={info.percent >= 100 ? 'green' : 'blue'}
            ></Progress>
          </div>
        ))}
    </div>
  )
}
