import { cn } from '@/lib/utils'

interface LEDDisplayProps {
  value: string
  color?: 'amber' | 'cyan' | 'green'
  size?: 'sm' | 'md' | 'lg' | 'xl'
  className?: string
  glow?: boolean
  variant?: 'default' | 'frequency'
}

export function LEDDisplay({ 
  value, 
  color = 'amber', 
  size = 'md', 
  className,
  glow = true,
  variant = 'default'
}: LEDDisplayProps) {
  const colorClasses = {
    amber: 'text-[var(--led-amber)]',
    cyan: 'text-[var(--led-cyan)]',
    green: 'text-[var(--led-green)]',
  }
  
  const sizeClasses = {
    sm: 'text-lg',
    md: 'text-2xl',
    lg: 'text-4xl',
    xl: 'text-5xl',
  }
  
  return (
    <div 
      className={cn(
        variant === 'frequency' ? 'tracking-wider font-bold' : 'font-mono tracking-wider font-bold',
        colorClasses[color],
        sizeClasses[size],
        glow && 'led-glow',
        className
      )}
      style={{ 
        fontVariantNumeric: 'tabular-nums',
        fontFamily: variant === 'frequency' ? 'DSEG14Modern, monospace' : undefined
      }}
    >
      {value}
    </div>
  )
}
