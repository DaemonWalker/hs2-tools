import ipcUtils from '@renderer/logic/ipcUtils'
import { FC, useEffect, useState } from 'react'
import { PngViewer } from '../share/PngViewer'
import { useNavigate } from 'react-router-dom'

const { readAllCharaNames } = ipcUtils

interface IProps {
  filePath: string
}

export const CharaThumbnail: FC<IProps> = ({ filePath }) => {
  const [info, setInfo] = useState<(string | undefined)[]>()
  useEffect(() => {
    readAllCharaNames(filePath).then((data) => setInfo(data))
  }, [])
  const navi = useNavigate()
  return (
    <div className="png-thumbnail" onClick={() => navi(encodeURI(filePath))}>
      <div className="png-thumbnail-header">{info?.[0]}</div>
      <PngViewer filePath={filePath} style={{ maxWidth: '100%' }} />
    </div>
  )
}
