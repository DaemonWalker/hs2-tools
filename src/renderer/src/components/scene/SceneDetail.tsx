import { FC } from 'react'
import { PngViewer } from '../share/PngViewer'
import { ModList } from '../share/ModList'
import { SceneInfo } from './SceneInfo'

interface IProps {
  filePath: string
}

export const SceneDetail: FC<IProps> = ({ filePath }) => {
  return (
    <div className="scene-card">
      <div className="scene-card-left">
        <PngViewer filePath={filePath} style={{ width: '100%' }} />
        <SceneInfo filePath={filePath} />
      </div>
      <div className="scene-card-mods">
        <ModList filePath={filePath} />
      </div>
    </div>
  )
}
