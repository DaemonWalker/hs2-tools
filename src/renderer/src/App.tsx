import { Route, Routes } from 'react-router-dom'
import '../src/assets/main.css'
import { Header } from './components/app/Header'
import { Home } from './pages/Home'
import { AppInitialization } from './components/app/AppInitialization'
import { Mods } from './pages/Mods'
import { FemaleChara } from './pages/FemaleChara'
import { Scenes } from './pages/Scenes'
import { SceneGroup } from './components/scene/SceneGroup'
import { CardExplorer } from './pages/CardExplorer'

function App(): JSX.Element {
  const {}=useDownload
  return (
    <>
      <AppInitialization />
      <Header />
      <div id="container">
        <Routes>
          <Route path="/character/*" element={<FemaleChara />} />
          <Route path="/scene/*" element={<Scenes />} />
          <Route path="/scene-group/*" element={<SceneGroup />} />
          <Route path="/mods/*" element={<Mods />} />
          <Route path="/explorer" element={<CardExplorer />} />
          <Route path="/" element={<Home />} />
        </Routes>
      </div>
    </>
  )
}

export default App
