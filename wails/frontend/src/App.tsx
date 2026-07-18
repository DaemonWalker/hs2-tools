import { Route, Routes } from 'react-router-dom'
import '../src/assets/main.css'
import { Suspense, lazy } from 'react'
import { Header } from './components/app/Header'
import { BackgroundComponent } from './components/app/BackgroundComponent'
import { ErrorBoundary } from './components/share/ErrorBoundary'
import { SkipLink } from './components/share/SkipLink'
import { Spin } from 'antd'

// Lazy load pages for code splitting
const Home = lazy(() => import('./pages/Home').then(m => ({ default: m.Home })))
const CharaExplorer = lazy(() => import('./pages/CharaExplorer').then(m => ({ default: m.CharaExplorer })))
const Scenes = lazy(() => import('./pages/Scenes').then(m => ({ default: m.Scenes })))

const Mods = lazy(() => import('./pages/Mods').then(m => ({ default: m.Mods })))
const BetterRepack = lazy(() => import('./pages/BetterRepack').then(m => ({ default: m.BetterRepack })))
const CardExplorer = lazy(() => import('./pages/CardExplorer').then(m => ({ default: m.CardExplorer })))
const SystemSettings = lazy(() => import('./pages/SystemSettings').then(m => ({ default: m.SystemSettings })))
const Download = lazy(() => import('./pages/Download').then(m => ({ default: m.Download })))

// Loading fallback component
const PageLoading = () => (
  <div style={{ 
    height: '100%', 
    display: 'flex', 
    alignItems: 'center', 
    justifyContent: 'center',
    flexDirection: 'column',
    gap: 16
  }}>
    <Spin size="large" />
    <span style={{ color: 'var(--text-secondary)' }}>页面加载中...</span>
  </div>
)

function App(): JSX.Element {
  return (
    <ErrorBoundary>
      <SkipLink />
      <BackgroundComponent />
      <Header />
      <div id="container" role="main" aria-label="主内容区域" tabIndex={-1}>
        <Suspense fallback={<PageLoading />}>
          <Routes>
            <Route path="/character/*" element={<CharaExplorer />} />
            <Route path="/scene/*" element={<Scenes />} />

            <Route path="/mods-local/*" element={<Mods />} />
            <Route path="/mods-sideload/*" element={<BetterRepack />} />
            <Route path="/explorer/*" element={<CardExplorer />} />
            <Route path="/settings/*" element={<SystemSettings />} />
            <Route path="/download" element={<Download />} />
            <Route path="/" element={<Home />} />
          </Routes>
        </Suspense>
      </div>
    </ErrorBoundary>
  )
}

export default App
