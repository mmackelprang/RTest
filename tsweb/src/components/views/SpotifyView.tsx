import { useState, useRef } from 'react'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { ScrollArea } from '@/components/ui/scroll-area'
import { MagnifyingGlass, Play, MusicNote, Playlist as PlaylistIcon, Disc, User, Microphone, Books, CaretLeft, CaretRight } from '@phosphor-icons/react'
import { NowPlayingCard } from '@/components/NowPlayingCard'
import { SpectrumVisualizer } from '@/components/SpectrumVisualizer'
import { useKV } from '@github/spark/hooks'
import { useAppStore } from '@/lib/store'
import { cn } from '@/lib/utils'
import type { SpotifySearchFilter, SpotifySearchResult } from '@/types'

const FILTERS: Array<{ value: SpotifySearchFilter; label: string; icon: any }> = [
  { value: 'all', label: 'All', icon: MagnifyingGlass },
  { value: 'track', label: 'Music', icon: MusicNote },
  { value: 'playlist', label: 'Playlists', icon: PlaylistIcon },
  { value: 'album', label: 'Albums', icon: Disc },
  { value: 'artist', label: 'Artists', icon: User },
  { value: 'show', label: 'Podcasts', icon: Microphone },
  { value: 'audiobook', label: 'Audiobooks', icon: Books },
]

export function SpotifyView() {
  const [searchQuery, setSearchQuery] = useState('')
  const [activeFilter, setActiveFilter] = useState<SpotifySearchFilter>('all')
  const [searchResults, setSearchResults] = useKV<SpotifySearchResult[]>('spotify-search-results', [])
  const [isSearchFocused, setIsSearchFocused] = useState(false)
  const [playlist] = useKV<Array<{ id: string; name: string; artist: string; duration: number }>>('spotify-playlist', [
    { id: '1', name: 'Bohemian Rhapsody', artist: 'Queen', duration: 354 },
    { id: '2', name: 'Stairway to Heaven', artist: 'Led Zeppelin', duration: 482 },
    { id: '3', name: 'Hotel California', artist: 'Eagles', duration: 391 },
    { id: '4', name: 'Imagine', artist: 'John Lennon', duration: 183 },
    { id: '5', name: 'Smells Like Teen Spirit', artist: 'Nirvana', duration: 301 },
  ])
  const filtersScrollRef = useRef<HTMLDivElement>(null)
  const setTrackMetadata = useAppStore((state) => state.setTrackMetadata)
  const setPlaybackState = useAppStore((state) => state.setPlaybackState)
  
  const handleSearch = async () => {
    if (!searchQuery.trim()) return
    
    const mockResults: SpotifySearchResult[] = [
      {
        id: '1',
        type: 'track',
        name: `${searchQuery} - Track Result`,
        artist: 'Artist Name',
        album: 'Album Name',
        duration: 240,
      },
      {
        id: '2',
        type: 'album',
        name: `${searchQuery} Album`,
        artist: 'Artist Name',
      },
      {
        id: '3',
        type: 'playlist',
        name: `Best of ${searchQuery}`,
      },
      {
        id: '4',
        type: 'artist',
        name: `${searchQuery} Artist`,
      },
    ]
    
    setSearchResults(mockResults)
  }
  
  const handlePlayResult = (result: SpotifySearchResult) => {
    if (result.type === 'track') {
      setTrackMetadata({
        title: result.name,
        artist: result.artist || 'Unknown Artist',
        album: result.album || 'Unknown Album',
      })
      setPlaybackState({
        isPlaying: true,
        position: 0,
        duration: result.duration || 180,
      })
    }
  }
  
  const getFilteredResults = () => {
    if (!searchResults) return []
    if (activeFilter === 'all') return searchResults
    return searchResults.filter(r => r.type === activeFilter)
  }
  
  const getResultIcon = (type: SpotifySearchFilter) => {
    const filter = FILTERS.find(f => f.value === type)
    return filter?.icon || MusicNote
  }
  
  const scrollFilters = (direction: 'left' | 'right') => {
    if (filtersScrollRef.current) {
      const scrollAmount = 200
      filtersScrollRef.current.scrollBy({
        left: direction === 'left' ? -scrollAmount : scrollAmount,
        behavior: 'smooth'
      })
    }
  }
  
  const formatDuration = (seconds: number) => {
    const mins = Math.floor(seconds / 60)
    const secs = seconds % 60
    return `${mins}:${secs.toString().padStart(2, '0')}`
  }
  
  return (
    <div className="h-full flex gap-4">
      <Card className="w-[360px] p-4 flex flex-col overflow-hidden">
        <div className="space-y-3 mb-4">
          <div className="relative">
            <Input
              placeholder="Search for songs, artists, albums..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              onFocus={() => setIsSearchFocused(true)}
              onBlur={() => setIsSearchFocused(false)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  handleSearch()
                }
              }}
              className="pr-10"
            />
            <Button
              size="icon"
              variant="ghost"
              className="absolute right-1 top-1/2 -translate-y-1/2 h-8 w-8"
              onClick={handleSearch}
            >
              <MagnifyingGlass size={20} />
            </Button>
          </div>
          
          <div className="flex items-center gap-2">
            <Button
              size="icon"
              variant="ghost"
              className="h-8 w-8 flex-shrink-0"
              onClick={() => scrollFilters('left')}
            >
              <CaretLeft size={20} weight="bold" />
            </Button>
            
            <div 
              ref={filtersScrollRef}
              className="flex gap-2 overflow-x-auto scrollbar-hide flex-1"
              style={{ scrollbarWidth: 'none', msOverflowStyle: 'none' }}
            >
              {FILTERS.map((filter) => {
                const Icon = filter.icon
                return (
                  <Button
                    key={filter.value}
                    variant={activeFilter === filter.value ? 'default' : 'outline'}
                    size="sm"
                    onClick={() => setActiveFilter(filter.value)}
                    className="gap-2 whitespace-nowrap flex-shrink-0"
                  >
                    <Icon size={16} />
                    {filter.label}
                  </Button>
                )
              })}
            </div>
            
            <Button
              size="icon"
              variant="ghost"
              className="h-8 w-8 flex-shrink-0"
              onClick={() => scrollFilters('right')}
            >
              <CaretRight size={20} weight="bold" />
            </Button>
          </div>
        </div>
        
        <ScrollArea className="flex-1">
          {!searchResults || searchResults.length === 0 ? (
            <div className="space-y-4">
              <div className="text-sm font-semibold text-muted-foreground px-2">Current Playlist</div>
              <div className="space-y-2 pr-4">
                {(playlist || []).map((track, index) => (
                  <div
                    key={track.id}
                    className="flex items-center gap-3 p-3 bg-secondary rounded-lg hover:bg-secondary/80 cursor-pointer"
                    onClick={() => {
                      setTrackMetadata({
                        title: track.name,
                        artist: track.artist,
                        album: 'Playlist',
                      })
                      setPlaybackState({
                        isPlaying: true,
                        position: 0,
                        duration: track.duration,
                      })
                    }}
                  >
                    <div className="text-sm text-muted-foreground w-6 text-right">{index + 1}</div>
                    <MusicNote size={24} className="text-primary flex-shrink-0" />
                    <div className="flex-1 min-w-0">
                      <div className="font-medium truncate">{track.name}</div>
                      <div className="text-xs text-muted-foreground truncate">{track.artist}</div>
                    </div>
                    <div className="text-xs text-muted-foreground">{formatDuration(track.duration)}</div>
                    <Button
                      size="icon"
                      variant="ghost"
                      onClick={(e) => {
                        e.stopPropagation()
                        setTrackMetadata({
                          title: track.name,
                          artist: track.artist,
                          album: 'Playlist',
                        })
                        setPlaybackState({
                          isPlaying: true,
                          position: 0,
                          duration: track.duration,
                        })
                      }}
                    >
                      <Play size={20} weight="fill" />
                    </Button>
                  </div>
                ))}
              </div>
            </div>
          ) : (
            <div className="space-y-2 pr-4">
              {getFilteredResults().map((result) => {
                const Icon = getResultIcon(result.type)
                return (
                  <div
                    key={result.id}
                    className="flex items-center justify-between p-3 bg-secondary rounded-lg hover:bg-secondary/80"
                  >
                    <div className="flex items-center gap-3 flex-1 min-w-0">
                      <Icon size={24} className="text-primary flex-shrink-0" />
                      <div className="min-w-0">
                        <div className="font-medium truncate">{result.name}</div>
                        {result.artist && (
                          <div className="text-xs text-muted-foreground truncate">
                            {result.artist}{result.album ? ` • ${result.album}` : ''}
                          </div>
                        )}
                        <div className="text-xs text-muted-foreground capitalize">
                          {result.type}
                        </div>
                      </div>
                    </div>
                    {result.type === 'track' && (
                      <Button
                        size="icon"
                        variant="ghost"
                        onClick={() => handlePlayResult(result)}
                      >
                        <Play size={20} weight="fill" />
                      </Button>
                    )}
                  </div>
                )
              })}
            </div>
          )}
        </ScrollArea>
      </Card>
      
      <div className="w-[360px]">
        <NowPlayingCard />
      </div>
      
      <Card className="flex-1 p-4">
        <div className="h-full">
          <SpectrumVisualizer />
        </div>
      </Card>
    </div>
  )
}
