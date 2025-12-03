import { useEffect, useState } from 'react'
import { House, Queue, Radio, ChartBar, Gear, ClockCounterClockwise, Cpu, Database, CirclesThree, SpotifyLogo, Folder, Disc, Plug, SpeakerHigh, GoogleChromeLogo, BluetoothConnected, CaretDown, Screencast } from '@phosphor-icons/react'
import { LEDDisplay } from '@/components/LEDDisplay'
import { useAppStore } from '@/lib/store'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { HistoryDialog } from '@/components/dialogs/HistoryDialog'
import type { AudioSource, AudioOutput } from '@/types'

export function NavigationBar() {
  const [currentTime, setCurrentTime] = useState(new Date())
  const [showCastDevices, setShowCastDevices] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const { systemStats, currentView, setCurrentView, activeSource, setActiveSource, activeOutput, setActiveOutput } = useAppStore()
  
  useEffect(() => {
    const interval = setInterval(() => {
      setCurrentTime(new Date())
    }, 1000)
    return () => clearInterval(interval)
  }, [])
  
  const timeString = currentTime.toLocaleTimeString('en-US', { 
    hour: '2-digit', 
    minute: '2-digit',
    hour12: false 
  })
  
  const dateString = currentTime.toLocaleDateString('en-US', { 
    month: 'short', 
    day: 'numeric' 
  })
  
  const inputSources = [
    { id: 'spotify' as const, icon: SpotifyLogo, label: 'Spotify' },
    { id: 'radio' as const, icon: Radio, label: 'Radio' },
    { id: 'files' as const, icon: Folder, label: 'Files' },
    { id: 'vinyl' as const, icon: Disc, label: 'Vinyl' },
    { id: 'aux' as const, icon: Plug, label: 'Aux' },
  ]
  
  const outputDevices = [
    { id: 'soundbar' as const, icon: SpeakerHigh, label: 'Soundbar' },
    { id: 'googlecast' as const, icon: GoogleChromeLogo, label: 'Google Cast' },
    { id: 'bluetooth' as const, icon: BluetoothConnected, label: 'Bluetooth' },
  ]
  
  const handleInputSelect = (sourceId: AudioSource) => {
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
  
  const activeInputLabel = inputSources.find(s => s.id === activeSource)?.label || 'Input'
  const activeOutputLabel = outputDevices.find(d => d.id === activeOutput)?.label || 'Output'
  
  const navItems = [
    { icon: House, label: 'Home', view: 'dashboard' as const, show: true, onClick: undefined },
    { icon: Queue, label: 'Queue', view: 'files' as const, show: activeSource === 'files', onClick: undefined },
    { icon: ClockCounterClockwise, label: 'History', view: null, show: true, onClick: () => setShowHistory(true) },
    { icon: Radio, label: 'Radio', view: 'radio' as const, show: activeSource === 'radio', onClick: undefined },
    { icon: ChartBar, label: 'Visualizer', view: 'visualizer' as const, show: true, onClick: undefined },
    { icon: Gear, label: 'Settings', view: 'settings' as const, show: true, onClick: undefined },
  ]
  
  const castDevices = [
    { id: '1', name: 'Living Room Speaker' },
    { id: '2', name: 'Kitchen Display' },
    { id: '3', name: 'Bedroom TV' },
  ]

  return (
    <div className="fixed top-0 left-0 right-0 h-16 bg-card border-b border-border flex items-center justify-between px-6 z-50">
      <div className="flex items-center gap-6">
        <div className="flex items-center gap-4">
          <LEDDisplay value={timeString} size="lg" color="amber" glow={false} />
          <LEDDisplay value={dateString} size="lg" color="amber" glow={false} />
        </div>
        
        <div className="flex items-center gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button 
                variant="outline" 
                className="h-8 px-3 rounded-full bg-primary/10 border-primary/30 hover:bg-primary/20 hover:border-primary/50 transition-all min-w-[100px]"
              >
                <span className="text-sm font-medium">{activeInputLabel}</span>
                <CaretDown size={14} className="ml-1" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-40">
              {inputSources.map((source) => (
                <DropdownMenuItem 
                  key={source.id}
                  onClick={() => handleInputSelect(source.id)}
                  className={cn(
                    'cursor-pointer',
                    activeSource === source.id && 'bg-primary/10 text-primary'
                  )}
                >
                  <source.icon size={18} className="mr-2" weight={activeSource === source.id ? 'fill' : 'regular'} />
                  {source.label}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
          
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button 
                variant="outline" 
                className="h-8 px-3 rounded-full bg-accent/10 border-accent/30 hover:bg-accent/20 hover:border-accent/50 transition-all min-w-[120px]"
              >
                <span className="text-sm font-medium">{activeOutputLabel}</span>
                <CaretDown size={14} className="ml-1" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-44">
              {outputDevices.map((device) => (
                <DropdownMenuItem 
                  key={device.id}
                  onClick={() => setActiveOutput(device.id)}
                  className={cn(
                    'cursor-pointer',
                    activeOutput === device.id && 'bg-accent/10 text-accent'
                  )}
                >
                  <device.icon size={18} className="mr-2" weight={activeOutput === device.id ? 'fill' : 'regular'} />
                  {device.label}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>
          
          <div className="w-10 h-8 flex items-center justify-center">
            {activeOutput === 'googlecast' && (
              <DropdownMenu open={showCastDevices} onOpenChange={setShowCastDevices}>
                <DropdownMenuTrigger asChild>
                  <Button 
                    variant="ghost" 
                    size="icon"
                    className="h-8 w-8 text-accent hover:text-accent hover:bg-accent/20"
                  >
                    <Screencast size={20} weight="fill" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="start" className="w-56">
                  <div className="px-2 py-1.5 text-sm font-semibold">Cast Devices</div>
                  {castDevices.map((device) => (
                    <DropdownMenuItem 
                      key={device.id}
                      className="cursor-pointer"
                    >
                      <GoogleChromeLogo size={18} className="mr-2" />
                      {device.name}
                    </DropdownMenuItem>
                  ))}
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </div>
        </div>
      </div>
      
      <div className="flex items-center gap-8">
        <div className="flex items-center gap-6">
          <div className="flex items-center gap-2">
            <Cpu size={20} className="text-[var(--led-cyan)]" />
            <LEDDisplay 
              value={`${systemStats.cpuUsage}%`} 
              size="sm" 
              color="cyan"
              glow={false}
            />
          </div>
          <div className="flex items-center gap-2">
            <Database size={20} className="text-[var(--led-cyan)]" />
            <LEDDisplay 
              value={`${systemStats.ramUsage}%`} 
              size="sm" 
              color="cyan"
              glow={false}
            />
          </div>
          <div className="flex items-center gap-2">
            <CirclesThree size={20} className="text-[var(--led-cyan)]" />
            <LEDDisplay 
              value={`${systemStats.threadCount}`} 
              size="sm" 
              color="cyan"
              glow={false}
            />
          </div>
        </div>
      </div>
      
      <div className="flex items-center gap-2">
        {navItems.filter(item => item.show).map((item, index) => (
          <Button
            key={item.view || `action-${index}`}
            variant="ghost"
            size="icon"
            className={cn(
              'h-12 w-12 rounded-lg transition-all',
              currentView === item.view && item.view && 'bg-primary/20 text-primary'
            )}
            onClick={() => item.onClick ? item.onClick() : item.view && setCurrentView(item.view)}
            aria-label={item.label}
          >
            <item.icon size={28} weight={currentView === item.view && item.view ? 'fill' : 'regular'} />
          </Button>
        ))}
      </div>
      
      <HistoryDialog open={showHistory} onOpenChange={setShowHistory} />
    </div>
  )
}
