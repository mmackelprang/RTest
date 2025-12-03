import { useState, useEffect } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Backspace, ArrowLeft, Check } from '@phosphor-icons/react'

interface KeyboardDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (value: string) => void
  initialValue?: string
  title?: string
}

const KEYBOARD_ROWS = [
  ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0'],
  ['Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P'],
  ['A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L'],
  ['Z', 'X', 'C', 'V', 'B', 'N', 'M', '.', '-'],
]

export function KeyboardDialog({ open, onOpenChange, onSubmit, initialValue = '', title = 'Enter Text' }: KeyboardDialogProps) {
  const [value, setValue] = useState(initialValue)
  const [isShift, setIsShift] = useState(false)
  
  useEffect(() => {
    if (open) {
      setValue(initialValue)
    }
  }, [open, initialValue])
  
  const handleKeyPress = (key: string) => {
    setValue(prev => prev + (isShift ? key.toUpperCase() : key.toLowerCase()))
    setIsShift(false)
  }
  
  const handleBackspace = () => {
    setValue(prev => prev.slice(0, -1))
  }
  
  const handleSpace = () => {
    setValue(prev => prev + ' ')
  }
  
  const handleSubmit = () => {
    onSubmit(value)
  }
  
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>
        
        <div className="space-y-4">
          <Input
            value={value}
            onChange={(e) => setValue(e.target.value)}
            className="text-lg h-12"
            placeholder="Type here..."
          />
          
          <div className="space-y-2">
            {KEYBOARD_ROWS.map((row, rowIndex) => (
              <div key={rowIndex} className="flex gap-1 justify-center">
                {row.map((key) => (
                  <Button
                    key={key}
                    variant="outline"
                    className="h-12 min-w-[3rem] text-base font-semibold"
                    onClick={() => handleKeyPress(key)}
                  >
                    {isShift ? key.toUpperCase() : key}
                  </Button>
                ))}
              </div>
            ))}
            
            <div className="flex gap-1 justify-center">
              <Button
                variant="outline"
                className="h-12 px-6"
                onClick={() => setIsShift(!isShift)}
              >
                <ArrowLeft size={20} weight={isShift ? 'fill' : 'regular'} className="mr-2" />
                Shift
              </Button>
              
              <Button
                variant="outline"
                className="h-12 flex-1 max-w-md"
                onClick={handleSpace}
              >
                Space
              </Button>
              
              <Button
                variant="outline"
                className="h-12 px-6"
                onClick={handleBackspace}
              >
                <Backspace size={20} />
              </Button>
            </div>
            
            <div className="flex justify-center pt-2">
              <Button
                className="h-12 px-12 gap-2"
                onClick={handleSubmit}
              >
                <Check size={20} weight="bold" />
                Done
              </Button>
            </div>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
