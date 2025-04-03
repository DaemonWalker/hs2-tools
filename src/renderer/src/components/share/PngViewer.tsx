import { readPngForShow } from '@renderer/logic/ipcUtils'
import { Spin } from 'antd'
import { CSSProperties, FC, useEffect, useState } from 'react'

interface IState {
  filePath: string
  style?: CSSProperties
}

export const PngViewer: FC<IState> = ({ filePath, style }) => {
  const [base64, setBase64] = useState<string>()
  useEffect(() => {
    readPngForShow(filePath).then((data) => {
      setBase64(data)
    })
  }, [filePath])
  return base64 ? (
    <img src={`data:image/png;base64,${base64}`} style={{ ...style, borderRadius: 8 }} />
  ) : (
    <Spin></Spin>
  )
}
