import { useEffect, useState } from 'react'
import { useAppStore } from '@/lib/store'

export function ScreenDebugger() {
  const [dimensions, setDimensions] = useState({ width: 0, height: 0 })
  const showScreenDebugger = useAppStore((state) => state.showScreenDebugger)
  
  useEffect(() => {
    const updateDimensions = () => {
      setDimensions({
        width: window.innerWidth,
        height: window.innerHeight
      })
    }
    
    updateDimensions()
    window.addEventListener('resize', updateDimensions)
    
    return () => window.removeEventListener('resize', updateDimensions)
  }, [])
  
  if (!showScreenDebugger) return null
  
  return (
    <div className="fixed top-20 left-6 z-50 bg-destructive/90 text-destructive-foreground px-3 py-2 rounded-lg text-xs font-mono shadow-lg border border-destructive">
      <div className="font-semibold">Screen Size</div>
      <div>{dimensions.width} × {dimensions.height}px</div>
    </div>
  )
}
