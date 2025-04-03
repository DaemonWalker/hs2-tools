import { CardList } from '@renderer/components/share/CardList'
import { FC } from 'react'
import { Route, Routes } from 'react-router-dom'
import { SceneCard } from './SceneCard'

export const Scenes: FC = () => {
  return (
    <Routes>
      <Route path="/" element={<CardList columnCount={4} type="scene" />}></Route>
      <Route path=":path" element={<SceneCard />}></Route>
    </Routes>
  )
}
