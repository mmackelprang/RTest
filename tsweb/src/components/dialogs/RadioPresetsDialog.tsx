import { useState, useEffect } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Radio, Trash, FloppyDisk, Keyboard as KeyboardIcon } from '@phosphor-icons/react'
import { useKV } from '@github/spark/hooks'
import { useAppStore } from '@/lib/store'
import { KeyboardDialog } from '@/components/dialogs/KeyboardDialog'
import type { RadioBand } from '@/types'

interface RadioStation {
  id: string
  name: string
  frequency: number
  band: RadioBand
}

interface RadioPresetsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSelectStation: (frequency: number, band: RadioBand) => void
}

export function RadioPresetsDialog({ open, onOpenChange, onSelectStation }: RadioPresetsDialogProps) {
  const [stations, setStations] = useKV<RadioStation[]>('radio-presets', [])
  const [stationName, setStationName] = useState('')
  const [showKeyboard, setShowKeyboard] = useState(false)
  const radioState = useAppStore((state) => state.radioState)
  
  useEffect(() => {
    if (!open) {
      setStationName('')
    }
  }, [open])
  
  const handleSaveCurrent = () => {
    if (stationName.trim()) {
      setStations((current) => [
        ...(current || []),
        {
          id: Date.now().toString(),
          name: stationName.trim(),
          frequency: radioState.frequency,
          band: radioState.band,
        },
      ])
      setStationName('')
    }
  }
  
  const handleKeyboardSubmit = (value: string) => {
    setStationName(value)
    setShowKeyboard(false)
  }
  
  const handleDelete = (id: string) => {
    setStations((current) => (current || []).filter((s) => s.id !== id))
  }
  
  const handleSelect = (station: RadioStation) => {
    onSelectStation(station.frequency, station.band)
    onOpenChange(false)
  }
  
  const formatFrequency = (freq: number, band: RadioBand) => {
    if (band === 'fm' || band === 'vhf' || band === 'weather' || band === 'air') {
      return `${freq.toFixed(band === 'fm' ? 1 : 3)} MHz`
    } else {
      return `${freq} kHz`
    }
  }
  
  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="max-w-2xl max-h-[80vh]">
          <DialogHeader>
            <DialogTitle>Radio Station Presets</DialogTitle>
          </DialogHeader>
          
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-4">
              <div className="text-sm font-semibold">Saved Stations</div>
              <ScrollArea className="h-[400px] pr-4">
                <div className="space-y-2">
                  {!stations || stations.length === 0 ? (
                    <div className="text-center text-muted-foreground py-8">
                      No stations saved yet
                    </div>
                  ) : (
                    stations.map((station) => (
                      <div
                        key={station.id}
                        className="flex items-center justify-between p-3 bg-secondary rounded-lg hover:bg-secondary/80 cursor-pointer"
                        onClick={() => handleSelect(station)}
                      >
                        <div className="flex items-center gap-3">
                          <Radio size={20} className="text-primary" />
                          <div>
                            <div className="font-medium">{station.name}</div>
                            <div className="text-xs text-muted-foreground">
                              {formatFrequency(station.frequency, station.band)} • {station.band.toUpperCase()}
                            </div>
                          </div>
                        </div>
                        <Button
                          variant="ghost"
                          size="icon"
                          onClick={(e) => {
                            e.stopPropagation()
                            handleDelete(station.id)
                          }}
                        >
                          <Trash size={18} />
                        </Button>
                      </div>
                    ))
                  )}
                </div>
              </ScrollArea>
            </div>
            
            <div className="space-y-3">
              <div>
                <Label htmlFor="station-name">Station Name</Label>
                <div className="flex gap-2">
                  <Input
                    id="station-name"
                    placeholder="Enter station name"
                    value={stationName}
                    onChange={(e) => setStationName(e.target.value)}
                    className="flex-1"
                  />
                  <Button
                    variant="outline"
                    size="icon"
                    onClick={() => setShowKeyboard(true)}
                  >
                    <KeyboardIcon size={20} />
                  </Button>
                </div>
              </div>
              
              <Button 
                onClick={handleSaveCurrent} 
                className="w-full gap-2"
                disabled={!stationName.trim()}
              >
                <FloppyDisk size={20} />
                Save Current
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
      
      <KeyboardDialog
        open={showKeyboard}
        onOpenChange={setShowKeyboard}
        onSubmit={handleKeyboardSubmit}
        initialValue={stationName}
        title="Enter Station Name"
      />
    </>
  )
}
