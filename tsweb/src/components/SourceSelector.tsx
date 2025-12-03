import { Radio, SpotifyLogo, Folder, Disc, Plug } from '@phosphor-icons/react'
import { useAppStore } from '@/lib/store'
import { Card } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import type { AudioSource } from '@/types'

const sources = [
  { id: 'spotify' as const, icon: SpotifyLogo, label: 'Spotify', color: 'text-green-500' },
  { id: 'radio' as const, icon: Radio, label: 'Radio', color: 'text-primary' },
  { id: 'files' as const, icon: Folder, label: 'Files', color: 'text-blue-500' },
  { id: 'vinyl' as const, icon: Disc, label: 'Vinyl', color: 'text-purple-500' },
  { id: 'aux' as const, icon: Plug, label: 'AUX', color: 'text-orange-500' },
]

export function SourceSelector() {
  const activeSource = useAppStore((state) => state.activeSource)
  const setActiveSource = useAppStore((state) => state.setActiveSource)
  const setCurrentView = useAppStore((state) => state.setCurrentView)
  
  const handleSourceSelect = (sourceId: AudioSource) => {
    setActiveSource(sourceId)
    
    if (sourceId === 'radio') {
      setCurrentView('radio')
    } else if (sourceId === 'spotify') {
      setCurrentView('spotify')
    } else if (sourceId === 'files') {
      setCurrentView('files')
    } else {
      setCurrentView('dashboard')
    }
  }
  
  return (
    <div className="grid grid-cols-5 gap-3">
      {sources.map((source) => (
        <Card
          key={source.id}
          className={cn(
            'p-3 flex flex-col items-center justify-center gap-2 cursor-pointer transition-all hover:scale-105',
            activeSource === source.id 
              ? 'border-primary border-2 bg-primary/10' 
              : 'hover:border-primary/50'
          )}
          onClick={() => handleSourceSelect(source.id)}
        >
          <source.icon 
            size={32} 
            weight={activeSource === source.id ? 'fill' : 'regular'}
            className={cn(activeSource === source.id && source.color)}
          />
          <div className={cn(
            'text-sm font-medium',
            activeSource === source.id && 'text-primary'
          )}>
            {source.label}
          </div>
        </Card>
      ))}
    </div>
  )
}
