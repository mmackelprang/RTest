import { useEffect } from 'react'
import { NavigationBar } from '@/components/NavigationBar'
import { PlaybackControls } from '@/components/PlaybackControls'
import { ScreenDebugger } from '@/components/ScreenDebugger'
import { Dashboard } from '@/components/views/Dashboard'
import { RadioView } from '@/components/views/RadioView'
import { SpotifyView } from '@/components/views/SpotifyView'
import { FilesView } from '@/components/views/FilesView'
import { VisualizerView } from '@/components/views/VisualizerView'
import { SettingsView } from '@/components/views/SettingsView'
import { useAppStore } from '@/lib/store'
import { Toaster } from '@/components/ui/sonner'
import { applyTheme } from '@/lib/themes'

function App() {
  const currentView = useAppStore((state) => state.currentView)
  const activeSource = useAppStore((state) => state.activeSource)
  const setCurrentView = useAppStore((state) => state.setCurrentView)
  const setTrackMetadata = useAppStore((state) => state.setTrackMetadata)
  const setPlaybackState = useAppStore((state) => state.setPlaybackState)
  const theme = useAppStore((state) => state.theme)
  
  useEffect(() => {
    applyTheme(theme)
  }, [theme])
  
  useEffect(() => {
    const initializeView = () => {
      if (activeSource === 'radio') {
        setCurrentView('radio')
      } else if (activeSource === 'spotify') {
        setCurrentView('spotify')
      } else if (activeSource === 'files') {
        setCurrentView('files')
      } else {
        setCurrentView('dashboard')
      }
    }
    
    initializeView()
  }, [])
  
  useEffect(() => {
    const loadDemoData = async () => {
      const demoTrack = await window.spark.kv.get<any>('demo-track')
      if (demoTrack) {
        setTrackMetadata(demoTrack)
        setPlaybackState({ 
          isPlaying: true, 
          position: 145, 
          duration: 382,
          volume: 75 
        })
      }
    }
    
    loadDemoData()
    
    const progressInterval = setInterval(() => {
      const state = useAppStore.getState().playbackState
      if (state.isPlaying && state.position < state.duration) {
        setPlaybackState({ position: state.position + 1 })
      }
    }, 1000)
    
    return () => clearInterval(progressInterval)
  }, [setTrackMetadata, setPlaybackState])
  
  const renderView = () => {
    switch (currentView) {
      case 'dashboard':
        return <Dashboard />
      case 'radio':
        return <RadioView />
      case 'spotify':
        return <SpotifyView />
      case 'files':
        return <FilesView />
      case 'visualizer':
        return <VisualizerView />
      case 'settings':
        return <SettingsView />
      default:
        return <Dashboard />
    }
  }
  
  return (
    <div className="h-screen w-screen overflow-hidden bg-background text-foreground flex flex-col">
      <NavigationBar />
      <ScreenDebugger />
      
      <main className="flex-1 overflow-hidden px-6 py-4" style={{ marginTop: '64px', marginBottom: '112px' }}>
        {renderView()}
      </main>
      
      <PlaybackControls />
      <Toaster />
    </div>
  )
}

export default App