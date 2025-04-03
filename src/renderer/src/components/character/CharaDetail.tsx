import { FC } from 'react'
import { ModList } from '../share/ModList'
import { PngViewer } from '../share/PngViewer'
import { CharaInfo } from './CharaInfo'

interface IProps {
  filePath: string
}

export const CharaDetail: FC<IProps> = ({ filePath }) => {
  return (
    <div className="chara-card">
      <div style={{ minHeight: 0 }}>
        <PngViewer filePath={filePath} />
      </div>
      <div className="chara-card-list-container">
        <div className="chara-card-chara-info ">
          <CharaInfo filePath={filePath} />
        </div>
        <div className="chara-card-mod-list">
          <ModList filePath={filePath} />
        </div>
      </div>
    </div>
  )
}
