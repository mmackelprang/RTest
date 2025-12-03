import { useEffect, useRef } from 'react'
import { useAppStore } from '@/lib/store'

export function SpectrumVisualizer() {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const animationRef = useRef<number | undefined>(undefined)
  const playbackState = useAppStore((state) => state.playbackState)
  
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    
    const ctx = canvas.getContext('2d')
    if (!ctx) return
    
    const bars = 32
    const barData = new Array(bars).fill(0)
    
    const animate = () => {
      if (!playbackState.isPlaying) {
        barData.forEach((_, i) => {
          barData[i] *= 0.95
        })
      } else {
        barData.forEach((_, i) => {
          const target = Math.random() * 0.3 + Math.sin(Date.now() / 1000 + i * 0.5) * 0.7
          barData[i] = barData[i] * 0.8 + target * 0.2
        })
      }
      
      const width = canvas.width
      const height = canvas.height
      const barWidth = width / bars
      const gap = 2
      
      ctx.fillStyle = 'oklch(0.2 0.01 240)'
      ctx.fillRect(0, 0, width, height)
      
      const gradient = ctx.createLinearGradient(0, height, 0, 0)
      gradient.addColorStop(0, 'oklch(0.75 0.15 195)')
      gradient.addColorStop(0.5, 'oklch(0.8 0.18 75)')
      gradient.addColorStop(1, 'oklch(0.85 0.2 75)')
      
      barData.forEach((value, i) => {
        const barHeight = value * height * 0.9
        const x = i * barWidth
        const y = height - barHeight
        
        ctx.fillStyle = gradient
        ctx.fillRect(x + gap, y, barWidth - gap * 2, barHeight)
        
        if (value > 0.8) {
          ctx.shadowBlur = 20
          ctx.shadowColor = 'oklch(0.75 0.15 195)'
          ctx.fillRect(x + gap, y, barWidth - gap * 2, barHeight)
          ctx.shadowBlur = 0
        }
      })
      
      animationRef.current = requestAnimationFrame(animate)
    }
    
    animate()
    
    return () => {
      if (animationRef.current) {
        cancelAnimationFrame(animationRef.current)
      }
    }
  }, [playbackState.isPlaying])
  
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    
    const updateSize = () => {
      const rect = canvas.getBoundingClientRect()
      canvas.width = rect.width * window.devicePixelRatio
      canvas.height = rect.height * window.devicePixelRatio
    }
    
    updateSize()
    window.addEventListener('resize', updateSize)
    return () => window.removeEventListener('resize', updateSize)
  }, [])
  
  return (
    <canvas 
      ref={canvasRef}
      className="w-full h-full rounded-lg"
      style={{ imageRendering: 'crisp-edges' }}
    />
  )
}
