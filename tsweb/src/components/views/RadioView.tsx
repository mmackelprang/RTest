import { useState } from 'react'
import { CaretUp, CaretDown, Hash, Faders, Equalizer, SpeakerHigh, SpeakerLow, Power } from '@phosphor-icons/react'
import { useAppStore } from '@/lib/store'
import { LEDDisplay } from '@/components/LEDDisplay'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { ScrollArea } from '@/components/ui/scroll-area'
import { SpectrumVisualizer } from '@/components/SpectrumVisualizer'
import { NowPlayingCard } from '@/components/NowPlayingCard'
import { FrequencyDialog } from '@/components/dialogs/FrequencyDialog'
import { RadioPresetsDialog } from '@/components/dialogs/RadioPresetsDialog'
import { cn } from '@/lib/utils'
import type { RadioBand } from '@/types'

const BAND_CONFIG = {
  am: { min: 520, max: 1710, defaultFreq: 1010, defaultStep: 9, decimals: 0 },
  fm: { min: 87.5, max: 108.0, defaultFreq: 101.5, defaultStep: 0.1, decimals: 1 },
  sw: { min: 2300, max: 26100, defaultFreq: 9600, defaultStep: 5, decimals: 0 },
  vhf: { min: 136, max: 174, defaultFreq: 150, defaultStep: 0.025, decimals: 3 },
  weather: { min: 162.4, max: 162.55, defaultFreq: 162.5, defaultStep: 0.025, decimals: 3 },
  air: { min: 118, max: 137, defaultFreq: 121.5, defaultStep: 0.025, decimals: 3 },
} as const

export function RadioView() {
  const radioState = useAppStore((state) => state.radioState)
  const setRadioState = useAppStore((state) => state.setRadioState)
  const [showFrequencyDialog, setShowFrequencyDialog] = useState(false)
  const [showPresetsDialog, setShowPresetsDialog] = useState(false)
  
  const adjustFrequency = (direction: 'up' | 'down') => {
    const step = radioState.stepSize
    const newFreq = direction === 'up' 
      ? radioState.frequency + step 
      : radioState.frequency - step
    
    const config = BAND_CONFIG[radioState.band]
    
    if (newFreq >= config.min && newFreq <= config.max) {
      setRadioState({ frequency: parseFloat(newFreq.toFixed(config.decimals)) })
    }
  }
  
  const cycleEQ = () => {
    const modes: Array<'off' | 'rock' | 'pop' | 'jazz' | 'classical' | 'speech'> = 
      ['off', 'rock', 'pop', 'jazz', 'classical', 'speech']
    const currentIndex = modes.indexOf(radioState.eqMode)
    const nextMode = modes[(currentIndex + 1) % modes.length]
    setRadioState({ eqMode: nextMode })
  }
  
  const cycleBand = () => {
    const bands: RadioBand[] = ['am', 'fm', 'sw', 'vhf', 'weather', 'air']
    const currentIndex = bands.indexOf(radioState.band)
    const nextBand = bands[(currentIndex + 1) % bands.length]
    const config = BAND_CONFIG[nextBand]
    setRadioState({ 
      band: nextBand, 
      frequency: config.defaultFreq, 
      stepSize: config.defaultStep 
    })
  }
  
  const formatFrequency = () => {
    const config = BAND_CONFIG[radioState.band]
    return radioState.frequency.toFixed(config.decimals)
  }
  
  const handleFrequencySet = (frequency: number) => {
    setRadioState({ frequency })
  }
  
  const handleSelectStation = (frequency: number, band: RadioBand) => {
    setRadioState({ frequency, band })
  }
  
  return (
    <div className="h-full flex gap-4">
      <Card className="w-[360px] p-4 flex flex-col overflow-hidden">
        <ScrollArea className="flex-1">
          <div className="pr-4">
            <div className="flex items-center justify-between mb-4">
              <div className="flex flex-col gap-2">
                <LEDDisplay 
                  value={formatFrequency()} 
                  size="xl" 
                  color="amber" 
                  className="text-5xl"
                  glow={false}
                  variant="frequency"
                />
                <div className="flex items-center gap-3">
                  <LEDDisplay 
                    value={radioState.band.toUpperCase()} 
                    size="sm" 
                    color="amber"
                    glow={false}
                    variant="frequency"
                  />
                  {radioState.isStereo && radioState.band === 'fm' && (
                    <span className="text-xs font-medium text-[var(--led-amber)]">
                      STEREO
                    </span>
                  )}
                  <span className="text-xs font-medium text-[var(--led-amber)]">
                    {radioState.eqMode === 'off' ? 'EQ OFF' : `EQ ${radioState.eqMode.toUpperCase()}`}
                  </span>
                </div>
              </div>
              
              <div className="flex flex-col gap-2">
                <div className="flex gap-1">
                  {[...Array(5)].map((_, i) => (
                    <div
                      key={i}
                      className={cn(
                        'w-2.5 h-6 rounded-sm transition-all',
                        i < Math.floor(radioState.signalStrength / 20)
                          ? 'bg-[var(--led-cyan)]'
                          : 'bg-secondary'
                      )}
                    />
                  ))}
                </div>
                <div className="text-xs text-center text-muted-foreground">Signal</div>
              </div>
            </div>
            
            <div className="flex items-center justify-center gap-3 mb-3">
              <Button
                size="icon"
                variant="outline"
                className="h-12 w-12"
                onClick={() => adjustFrequency('down')}
              >
                <CaretDown size={28} weight="bold" />
              </Button>
              
              <Button
                variant="outline"
                className="h-12 w-24 text-base font-semibold"
                onClick={() => setShowFrequencyDialog(true)}
              >
                <Hash size={24} weight="bold" />
              </Button>
              
              <Button
                size="icon"
                variant="outline"
                className="h-12 w-12"
                onClick={() => adjustFrequency('up')}
              >
                <CaretUp size={28} weight="bold" />
              </Button>
            </div>
            
            <div className="grid grid-cols-5 gap-2 mb-3">
              <Button
                variant="outline"
                className="h-10 px-3"
                onClick={cycleBand}
              >
                Band
              </Button>
              <Button
                variant="outline"
                className="h-10 px-2 gap-1"
                onClick={cycleEQ}
              >
                <Equalizer size={18} />
                EQ
              </Button>
              <Button
                size="icon"
                variant="outline"
                className="h-10"
                onClick={() => {}}
              >
                <Power size={20} weight="bold" />
              </Button>
              <Button
                size="icon"
                variant="outline"
                className="h-10"
                onClick={() => {}}
              >
                <SpeakerHigh size={20} weight="bold" />
              </Button>
              <Button
                size="icon"
                variant="outline"
                className="h-10"
                onClick={() => {}}
              >
                <SpeakerLow size={20} weight="bold" />
              </Button>
            </div>
            
            <div className="mb-3">
              <Button
                variant="outline"
                className="h-10 w-full gap-2"
                onClick={() => setShowPresetsDialog(true)}
              >
                <Faders size={18} />
                Presets
              </Button>
            </div>
          </div>
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
      
      <FrequencyDialog 
        open={showFrequencyDialog}
        onOpenChange={setShowFrequencyDialog}
        onSubmit={handleFrequencySet}
        band={radioState.band}
      />
      
      <RadioPresetsDialog 
        open={showPresetsDialog}
        onOpenChange={setShowPresetsDialog}
        onSelectStation={handleSelectStation}
      />
    </div>
  )
}
