import { SceneDetail } from '@renderer/components/scene/SceneDetail'
import { FC } from 'react'
import { useParams } from 'react-router-dom'

export const SceneCard: FC = () => {
  const { path } = useParams()
  return <SceneDetail filePath={decodeURI(path!)} />
}
