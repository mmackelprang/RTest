import { create } from 'zustand'
import type { AudioSource, AudioOutput, PlaybackState, RadioState, SystemStats, TrackMetadata, VisualizerMode, ThemeName } from '@/types'

interface AppState {
  activeSource: AudioSource
  setActiveSource: (source: AudioSource) => void
  
  activeOutput: AudioOutput
  setActiveOutput: (output: AudioOutput) => void
  
  playbackState: PlaybackState
  setPlaybackState: (state: Partial<PlaybackState>) => void
  
  trackMetadata: TrackMetadata | null
  setTrackMetadata: (metadata: TrackMetadata | null) => void
  
  radioState: RadioState
  setRadioState: (state: Partial<RadioState>) => void
  
  systemStats: SystemStats
  setSystemStats: (stats: SystemStats) => void
  
  visualizerMode: VisualizerMode
  setVisualizerMode: (mode: VisualizerMode) => void
  
  currentView: 'dashboard' | 'radio' | 'spotify' | 'files' | 'visualizer' | 'settings'
  setCurrentView: (view: AppState['currentView']) => void
  
  showScreenDebugger: boolean
  setShowScreenDebugger: (show: boolean) => void
  
  theme: ThemeName
  setTheme: (theme: ThemeName) => void
}

export const useAppStore = create<AppState>((set) => ({
  activeSource: 'radio',
  setActiveSource: (source) => set({ activeSource: source }),
  
  activeOutput: 'soundbar',
  setActiveOutput: (output) => set({ activeOutput: output }),
  
  playbackState: {
    isPlaying: false,
    position: 0,
    duration: 0,
    volume: 75,
    balance: 0,
    isMuted: false,
    shuffle: false,
    repeat: 'off',
    isSeekable: true,
  },
  setPlaybackState: (state) => set((prev) => ({
    playbackState: { ...prev.playbackState, ...state }
  })),
  
  trackMetadata: null,
  setTrackMetadata: (metadata) => set({ trackMetadata: metadata }),
  
  radioState: {
    frequency: 101.5,
    band: 'fm' as const,
    signalStrength: 85,
    isStereo: true,
    stepSize: 0.1,
    eqMode: 'off',
  },
  setRadioState: (state) => set((prev) => ({
    radioState: { ...prev.radioState, ...state }
  })),
  
  systemStats: {
    cpuUsage: 12,
    ramUsage: 38,
    threadCount: 24,
  },
  setSystemStats: (stats) => set({ systemStats: stats }),
  
  visualizerMode: 'spectrum',
  setVisualizerMode: (mode) => set({ visualizerMode: mode }),
  
  currentView: 'dashboard',
  setCurrentView: (view) => set({ currentView: view }),
  
  showScreenDebugger: true,
  setShowScreenDebugger: (show) => set({ showScreenDebugger: show }),
  
  theme: 'dark',
  setTheme: (theme) => set({ theme }),
}))
