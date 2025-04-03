import { CharaDetail } from '@renderer/components/character/CharaDetail'
import { FC } from 'react'
import { useParams } from 'react-router-dom'

export const CharaCard: FC = () => {
  let { path } = useParams()
  return <CharaDetail filePath={decodeURI(path!)} />
}
