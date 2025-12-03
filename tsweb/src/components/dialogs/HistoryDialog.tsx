import { useState, useMemo } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Button } from '@/components/ui/button'
import { MusicNote, SpotifyLogo, Radio, Folder, Disc, Plug, Fingerprint, FileAudio } from '@phosphor-icons/react'
import { useKV } from '@github/spark/hooks'
import type { HistoryEntry, AudioSource } from '@/types'
import { cn } from '@/lib/utils'

interface HistoryDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

type TimeFilter = 'all' | 'today' | 'week' | 'month'

export function HistoryDialog({ open, onOpenChange }: HistoryDialogProps) {
  const [history] = useKV<HistoryEntry[]>('playback-history', [])
  const [timeFilter, setTimeFilter] = useState<TimeFilter>('all')
  
  const getSourceIcon = (source: AudioSource) => {
    switch (source) {
      case 'spotify': return SpotifyLogo
      case 'radio': return Radio
      case 'files': return Folder
      case 'vinyl': return Disc
      case 'aux': return Plug
      default: return MusicNote
    }
  }
  
  const getMetadataSourceIcon = (metadataSource: string) => {
    switch (metadataSource) {
      case 'spotify': return SpotifyLogo
      case 'file': return FileAudio
      case 'fingerprinting': return Fingerprint
      default: return MusicNote
    }
  }
  
  const filteredHistory = useMemo(() => {
    if (!history) return []
    
    const now = Date.now()
    const oneDayMs = 24 * 60 * 60 * 1000
    
    switch (timeFilter) {
      case 'today':
        return history.filter(entry => now - entry.timePlayed < oneDayMs)
      case 'week':
        return history.filter(entry => now - entry.timePlayed < 7 * oneDayMs)
      case 'month':
        return history.filter(entry => now - entry.timePlayed < 30 * oneDayMs)
      default:
        return history
    }
  }, [history, timeFilter])
  
  const formatTimeAgo = (timestamp: number) => {
    const now = Date.now()
    const diff = now - timestamp
    const minutes = Math.floor(diff / 60000)
    const hours = Math.floor(diff / 3600000)
    const days = Math.floor(diff / 86400000)
    
    if (minutes < 60) return `${minutes}m ago`
    if (hours < 24) return `${hours}h ago`
    return `${days}d ago`
  }
  
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-5xl max-h-[85vh]">
        <DialogHeader>
          <DialogTitle>Playback History</DialogTitle>
        </DialogHeader>
        
        <div className="flex gap-2 mb-4">
          <Button
            variant={timeFilter === 'all' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setTimeFilter('all')}
          >
            All Time
          </Button>
          <Button
            variant={timeFilter === 'today' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setTimeFilter('today')}
          >
            Today
          </Button>
          <Button
            variant={timeFilter === 'week' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setTimeFilter('week')}
          >
            This Week
          </Button>
          <Button
            variant={timeFilter === 'month' ? 'default' : 'outline'}
            size="sm"
            onClick={() => setTimeFilter('month')}
          >
            This Month
          </Button>
        </div>
        
        <ScrollArea className="h-[500px]">
          <div className="space-y-1">
            <div className="grid grid-cols-12 gap-4 px-4 py-2 text-xs font-semibold text-muted-foreground border-b border-border">
              <div className="col-span-3">Title</div>
              <div className="col-span-2">Artist</div>
              <div className="col-span-2">Album</div>
              <div className="col-span-2">Source</div>
              <div className="col-span-2">Metadata</div>
              <div className="col-span-1">Time</div>
            </div>
            
            {!filteredHistory || filteredHistory.length === 0 ? (
              <div className="text-center text-muted-foreground py-12">
                <MusicNote size={48} className="mx-auto mb-2 opacity-50" />
                <div>No playback history yet</div>
              </div>
            ) : (
              filteredHistory.map((entry) => {
                const SourceIcon = getSourceIcon(entry.source)
                const MetadataIcon = getMetadataSourceIcon(entry.metadataSource)
                
                return (
                  <div
                    key={entry.id}
                    className="grid grid-cols-12 gap-4 px-4 py-3 hover:bg-secondary/50 rounded-lg transition-colors text-sm"
                  >
                    <div className="col-span-3 truncate font-medium">{entry.title}</div>
                    <div className="col-span-2 truncate text-muted-foreground">{entry.artist}</div>
                    <div className="col-span-2 truncate text-muted-foreground">{entry.album}</div>
                    <div className="col-span-2 flex items-center gap-2">
                      <SourceIcon size={16} className="text-primary" />
                      <span className="capitalize truncate">{entry.source}</span>
                    </div>
                    <div className="col-span-2 flex items-center gap-2">
                      <MetadataIcon size={16} className="text-accent" />
                      <span className="capitalize truncate">{entry.metadataSource}</span>
                    </div>
                    <div className="col-span-1 text-muted-foreground text-xs">
                      {formatTimeAgo(entry.timePlayed)}
                    </div>
                  </div>
                )
              })
            )}
          </div>
        </ScrollArea>
      </DialogContent>
    </Dialog>
  )
}
