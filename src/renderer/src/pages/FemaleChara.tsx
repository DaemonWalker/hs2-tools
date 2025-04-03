import { FC } from 'react'
import { Route, Routes } from 'react-router-dom'
import { CardList } from '@renderer/components/share/CardList'
import { CharaCard } from './CharaCard'

export const FemaleChara: FC = () => {
  return (
    <Routes>
      <Route path="/" element={<CardList columnCount={4} type="chara" />}></Route>
      <Route path=":path" element={<CharaCard />}></Route>
    </Routes>
  )
}
