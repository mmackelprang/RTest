export type AudioSource = 'spotify' | 'radio' | 'files' | 'vinyl' | 'aux'

export type AudioOutput = 'soundbar' | 'googlecast' | 'bluetooth'

export type VisualizerMode = 'spectrum' | 'waveform' | 'vu'

export type RepeatMode = 'off' | 'one' | 'all'

export type ThemeName = 'dark' | 'light' | 'nord' | 'dracula' | 'solarized' | 'monokai' | 'gruvbox'

export interface PlaybackState {
  isPlaying: boolean
  position: number
  duration: number
  volume: number
  balance: number
  isMuted: boolean
  shuffle: boolean
  repeat: RepeatMode
  isSeekable: boolean
}

export interface TrackMetadata {
  title: string
  artist: string
  album: string
  albumArt?: string
  genre?: string
  year?: number
}

export type RadioBand = 'am' | 'fm' | 'sw' | 'vhf' | 'weather' | 'air'

export interface RadioState {
  frequency: number
  band: RadioBand
  signalStrength: number
  isStereo: boolean
  stepSize: number
  eqMode: 'off' | 'rock' | 'pop' | 'jazz' | 'classical' | 'speech'
}

export type SpotifySearchFilter = 'all' | 'track' | 'playlist' | 'album' | 'artist' | 'show' | 'audiobook'

export interface SpotifySearchResult {
  id: string
  type: SpotifySearchFilter
  name: string
  artist?: string
  album?: string
  imageUrl?: string
  duration?: number
}

export interface SystemStats {
  cpuUsage: number
  ramUsage: number
  threadCount: number
}

export interface HistoryEntry {
  id: string
  title: string
  artist: string
  album: string
  source: AudioSource
  metadataSource: 'spotify' | 'file' | 'fingerprinting'
  timePlayed: number
}
