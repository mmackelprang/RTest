import { useState } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Hash, Backspace } from '@phosphor-icons/react'
import type { RadioBand } from '@/types'

const BAND_CONFIG = {
  am: { min: 520, max: 1710, placeholder: '----', label: '520 - 1710 kHz' },
  fm: { min: 87.5, max: 108.0, placeholder: '---.-', label: '87.5 - 108.0 MHz' },
  sw: { min: 2300, max: 26100, placeholder: '-----', label: '2.3 - 26.1 MHz' },
  vhf: { min: 136, max: 174, placeholder: '---.---', label: '136 - 174 MHz' },
  weather: { min: 162.4, max: 162.55, placeholder: '---.--', label: '162.4 - 162.55 MHz' },
  air: { min: 118, max: 137, placeholder: '---.---', label: '118 - 137 MHz' },
} as const

interface FrequencyDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (frequency: number) => void
  band: RadioBand
}

export function FrequencyDialog({ open, onOpenChange, onSubmit, band }: FrequencyDialogProps) {
  const [input, setInput] = useState('')
  
  const handleNumberClick = (num: string) => {
    if (input.length < 7) {
      setInput(input + num)
    }
  }
  
  const handleDecimal = () => {
    if (!input.includes('.') && input.length > 0) {
      setInput(input + '.')
    }
  }
  
  const handleBackspace = () => {
    setInput(input.slice(0, -1))
  }
  
  const handleClear = () => {
    setInput('')
  }
  
  const handleSubmit = () => {
    const freq = parseFloat(input)
    const config = BAND_CONFIG[band]
    if (!isNaN(freq) && freq >= config.min && freq <= config.max) {
      onSubmit(freq)
      setInput('')
      onOpenChange(false)
    }
  }
  
  const getDisplayValue = () => {
    if (!input) return BAND_CONFIG[band].placeholder
    return input
  }
  
  const needsDecimal = band !== 'am' && band !== 'sw'
  
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Enter Frequency - {band.toUpperCase()}</DialogTitle>
        </DialogHeader>
        
        <div className="space-y-4">
          <div className="bg-card border-2 border-primary/30 rounded-lg p-4 text-center">
            <div className="text-4xl font-mono font-bold text-primary tracking-wider">
              {getDisplayValue()}
            </div>
            <div className="text-sm text-muted-foreground mt-1">
              {BAND_CONFIG[band].label}
            </div>
          </div>
          
          <div className="grid grid-cols-3 gap-2">
            {[1, 2, 3, 4, 5, 6, 7, 8, 9].map((num) => (
              <Button
                key={num}
                variant="outline"
                size="lg"
                onClick={() => handleNumberClick(num.toString())}
                className="h-14 text-xl font-semibold"
              >
                {num}
              </Button>
            ))}
            
            {needsDecimal && (
              <Button
                variant="outline"
                size="lg"
                onClick={handleDecimal}
                className="h-14 text-xl font-semibold"
              >
                .
              </Button>
            )}
            
            <Button
              variant="outline"
              size="lg"
              onClick={() => handleNumberClick('0')}
              className={needsDecimal ? 'h-14 text-xl font-semibold' : 'h-14 text-xl font-semibold col-span-2'}
            >
              0
            </Button>
            
            <Button
              variant="outline"
              size="lg"
              onClick={handleBackspace}
              className="h-14"
            >
              <Backspace size={24} />
            </Button>
          </div>
          
          <div className="grid grid-cols-2 gap-2">
            <Button variant="outline" onClick={handleClear}>
              Clear
            </Button>
            <Button onClick={handleSubmit} className="gap-2">
              <Hash size={20} />
              Set Frequency
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
