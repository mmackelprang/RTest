import { MusicNote } from '@phosphor-icons/react'
import { useAppStore } from '@/lib/store'
import { SpectrumVisualizer } from '@/components/SpectrumVisualizer'
import { Card } from '@/components/ui/card'

export function Dashboard() {
  const trackMetadata = useAppStore((state) => state.trackMetadata)
  const activeSource = useAppStore((state) => state.activeSource)
  
  const sourceLabels = {
    spotify: 'Spotify',
    radio: 'FM/AM Radio',
    files: 'Audio Files',
    vinyl: 'Vinyl Record',
    aux: 'AUX Input',
  }
  
  return (
    <div className="h-full flex gap-4">
      <Card className="w-[35%] p-4 flex items-center gap-4">
        <div className="w-40 h-40 bg-secondary rounded-lg flex items-center justify-center flex-shrink-0">
          {trackMetadata?.albumArt ? (
            <img 
              src={trackMetadata.albumArt} 
              alt={trackMetadata.album}
              className="w-full h-full object-cover rounded-lg"
            />
          ) : (
            <MusicNote size={64} className="text-muted-foreground" weight="thin" />
          )}
        </div>
        
        <div className="flex-1 space-y-1 min-w-0">
          <div className="text-xl font-semibold text-foreground truncate">
            {trackMetadata?.title || 'No Track'}
          </div>
          <div className="text-base text-muted-foreground truncate">
            {trackMetadata?.artist || '--'}
          </div>
          <div className="text-sm text-muted-foreground truncate">
            {trackMetadata?.album || '--'}
          </div>
          
          <div className="flex items-center gap-2 pt-2 text-xs flex-wrap">
            {trackMetadata?.genre && (
              <span className="px-2 py-0.5 bg-secondary rounded-full">{trackMetadata.genre}</span>
            )}
            {trackMetadata?.year && (
              <span className="px-2 py-0.5 bg-secondary rounded-full">{trackMetadata.year}</span>
            )}
            <span className="px-2 py-0.5 bg-primary/20 text-primary rounded-full">
              {sourceLabels[activeSource]}
            </span>
          </div>
        </div>
      </Card>
      
      <Card className="flex-1 p-4">
        <div className="h-full">
          <SpectrumVisualizer />
        </div>
      </Card>
    </div>
  )
}
