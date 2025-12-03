import { Play, Pause, SkipBack, SkipForward, Shuffle, Repeat, RepeatOnce, SpeakerHigh, SpeakerX } from '@phosphor-icons/react'
import { useAppStore } from '@/lib/store'
import { Button } from '@/components/ui/button'
import { Slider } from '@/components/ui/slider'
import { cn } from '@/lib/utils'

export function PlaybackControls() {
  const { playbackState, setPlaybackState, trackMetadata } = useAppStore()
  
  const togglePlay = () => {
    setPlaybackState({ isPlaying: !playbackState.isPlaying })
  }
  
  const toggleShuffle = () => {
    setPlaybackState({ shuffle: !playbackState.shuffle })
  }
  
  const cycleRepeat = () => {
    const modes: Array<'off' | 'one' | 'all'> = ['off', 'one', 'all']
    const currentIndex = modes.indexOf(playbackState.repeat)
    const nextMode = modes[(currentIndex + 1) % modes.length]
    setPlaybackState({ repeat: nextMode })
  }
  
  const toggleMute = () => {
    setPlaybackState({ isMuted: !playbackState.isMuted })
  }
  
  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60)
    const secs = Math.floor(seconds % 60)
    return `${mins}:${secs.toString().padStart(2, '0')}`
  }
  
  const RepeatIcon = playbackState.repeat === 'one' ? RepeatOnce : Repeat
  
  return (
    <div className="fixed bottom-0 left-0 right-0 bg-card border-t border-border z-50">
      <div className="h-1 bg-secondary">
        <div 
          className="h-full bg-primary transition-all duration-300"
          style={{ 
            width: playbackState.duration > 0 
              ? `${(playbackState.position / playbackState.duration) * 100}%` 
              : '0%' 
          }}
        />
      </div>
      
      <div className="px-6 py-4">
        <div className="flex items-center justify-between gap-8">
          <div className="flex-1 min-w-0">
            <div className="text-base font-semibold text-foreground truncate">
              {trackMetadata?.title || 'No Track Playing'}
            </div>
            <div className="text-sm text-muted-foreground truncate">
              {trackMetadata?.artist || '--'}
            </div>
          </div>
          
          <div className="flex items-center gap-3">
            <Button
              variant="ghost"
              size="icon"
              className={cn(
                'h-12 w-12',
                playbackState.shuffle && 'text-primary'
              )}
              onClick={toggleShuffle}
            >
              <Shuffle size={24} weight={playbackState.shuffle ? 'fill' : 'regular'} />
            </Button>
            
            <Button
              variant="ghost"
              size="icon"
              className="h-12 w-12"
              disabled
            >
              <SkipBack size={24} />
            </Button>
            
            <Button
              size="icon"
              className="h-16 w-16 rounded-full bg-primary hover:bg-primary/90"
              onClick={togglePlay}
            >
              {playbackState.isPlaying ? (
                <Pause size={32} weight="fill" />
              ) : (
                <Play size={32} weight="fill" />
              )}
            </Button>
            
            <Button
              variant="ghost"
              size="icon"
              className="h-12 w-12"
              disabled
            >
              <SkipForward size={24} />
            </Button>
            
            <Button
              variant="ghost"
              size="icon"
              className={cn(
                'h-12 w-12',
                playbackState.repeat !== 'off' && 'text-primary'
              )}
              onClick={cycleRepeat}
            >
              <RepeatIcon size={24} weight={playbackState.repeat !== 'off' ? 'fill' : 'regular'} />
            </Button>
          </div>
          
          <div className="flex-1 flex items-center gap-6 justify-end">
            <div className="flex items-center gap-3 w-48">
              <Button
                variant="ghost"
                size="icon"
                className="h-10 w-10 shrink-0"
                onClick={toggleMute}
              >
                {playbackState.isMuted ? (
                  <SpeakerX size={20} />
                ) : (
                  <SpeakerHigh size={20} />
                )}
              </Button>
              <Slider
                value={[playbackState.isMuted ? 0 : playbackState.volume]}
                onValueChange={([value]) => setPlaybackState({ volume: value, isMuted: false })}
                max={100}
                step={1}
                className="flex-1"
              />
            </div>
            
            <div className="text-sm text-muted-foreground tabular-nums">
              {formatTime(playbackState.position)} / {formatTime(playbackState.duration)}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
