import { Card } from '@/components/ui/card'
import { SpectrumVisualizer } from '@/components/SpectrumVisualizer'
import { Button } from '@/components/ui/button'
import { useAppStore } from '@/lib/store'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'

export function VisualizerView() {
  const { visualizerMode, setVisualizerMode } = useAppStore()
  
  return (
    <div className="h-full flex flex-col gap-4">
      <div className="flex items-center justify-end">
        <Tabs value={visualizerMode} onValueChange={(v) => setVisualizerMode(v as any)}>
          <TabsList>
            <TabsTrigger value="spectrum">Spectrum</TabsTrigger>
            <TabsTrigger value="waveform">Waveform</TabsTrigger>
            <TabsTrigger value="vu">VU Meter</TabsTrigger>
          </TabsList>
        </Tabs>
      </div>
      
      <Card className="flex-1 p-6">
        <SpectrumVisualizer />
      </Card>
    </div>
  )
}
