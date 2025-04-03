import { FC } from 'react'
import { CharaThumbnail } from '../character/CharaThumbnail'

interface IProps {
  data?: string[]
  columnCount: number
  type: 'chara' | 'scene'
}

export const ResponsiveContainer: FC<IProps> = ({ data, columnCount }) => {
  return (
    <div
      style={{
        display: 'flex',
        width: '100%',
        gap: 10,
        overflow: 'auto',
        flexWrap: 'wrap',
        flex: 1
      }}
    >
      {data?.map((item) => (
        <div
          style={{
            width: `calc(${100 / columnCount}% - ${(10 * (columnCount - 1)) / columnCount}px)`,
            cursor: 'pointer'
          }}
          key={item}
        >
          <CharaThumbnail filePath={item} />
        </div>
      ))}
    </div>
  )
}
