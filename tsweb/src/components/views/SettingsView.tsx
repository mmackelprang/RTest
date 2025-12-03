import { useState, useEffect } from 'react'
import { Card } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Slider } from '@/components/ui/slider'
import { Switch } from '@/components/ui/switch'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAppStore } from '@/lib/store'
import { useKV } from '@github/spark/hooks'
import { Eye, EyeSlash, Trash, Plus, Download, ChartBar } from '@phosphor-icons/react'
import { applyTheme } from '@/lib/themes'
import type { ThemeName } from '@/types'

interface Secret {
  id: string
  key: string
  value: string
}

interface ConfigItem {
  id: string
  key: string
  value: string
}

interface LogEntry {
  timestamp: string
  level: 'info' | 'warning' | 'error'
  message: string
}

interface Preferences {
  ledColor: string
  brightness: number
  audioDucking: boolean
  autoPlay: boolean
}

interface UserPreferenceItem {
  id: string
  key: string
  value: string
}

interface MetricEntry {
  timestamp: number
  type: 'tts-alert' | 'audio-alert' | 'characters-ttsed' | 'audio-played'
  value: number
}

type TimeFilter = 'minute' | 'hour' | 'day' | 'week' | 'month'

const defaultPreferences: Preferences = {
  ledColor: 'amber',
  brightness: 80,
  audioDucking: false,
  autoPlay: true,
}

export function SettingsView() {
  const { visualizerMode, setVisualizerMode, showScreenDebugger, setShowScreenDebugger, theme, setTheme } = useAppStore()
  const [secrets, setSecrets] = useKV<Secret[]>('system-secrets', [])
  const [config, setConfig] = useKV<ConfigItem[]>('system-config', [])
  const [userPreferences, setUserPreferences] = useKV<UserPreferenceItem[]>('user-preference-items', [])
  const [preferences, setPreferences] = useKV<Preferences>('user-preferences', defaultPreferences)
  const [logs] = useKV<LogEntry[]>('system-logs', [])
  const [metrics, setMetrics] = useKV<MetricEntry[]>('system-metrics', [])
  
  const [showSecrets, setShowSecrets] = useState<Record<string, boolean>>({})
  const [newSecretKey, setNewSecretKey] = useState('')
  const [newSecretValue, setNewSecretValue] = useState('')
  const [newConfigKey, setNewConfigKey] = useState('')
  const [newConfigValue, setNewConfigValue] = useState('')
  const [newUserPrefKey, setNewUserPrefKey] = useState('')
  const [newUserPrefValue, setNewUserPrefValue] = useState('')
  const [configType, setConfigType] = useState<'config' | 'user-prefs'>('config')
  const [timeFilter, setTimeFilter] = useState<TimeFilter>('day')
  
  useEffect(() => {
    applyTheme(theme)
  }, [theme])
  
  useEffect(() => {
    if (!metrics || metrics.length === 0) {
      const now = Date.now()
      const sampleMetrics: MetricEntry[] = [
        ...Array.from({ length: 50 }, (_, i) => ({
          timestamp: now - i * 3600000,
          type: 'tts-alert' as const,
          value: Math.floor(Math.random() * 10) + 1
        })),
        ...Array.from({ length: 30 }, (_, i) => ({
          timestamp: now - i * 3600000,
          type: 'audio-alert' as const,
          value: Math.floor(Math.random() * 5) + 1
        })),
        ...Array.from({ length: 50 }, (_, i) => ({
          timestamp: now - i * 3600000,
          type: 'characters-ttsed' as const,
          value: Math.floor(Math.random() * 500) + 100
        })),
        ...Array.from({ length: 40 }, (_, i) => ({
          timestamp: now - i * 3600000,
          type: 'audio-played' as const,
          value: Math.floor(Math.random() * 8) + 1
        }))
      ]
      setMetrics(sampleMetrics)
    }
  }, [])
  
  const getTimeFilterMs = (filter: TimeFilter): number => {
    const now = Date.now()
    switch (filter) {
      case 'minute':
        return 60 * 1000
      case 'hour':
        return 60 * 60 * 1000
      case 'day':
        return 24 * 60 * 60 * 1000
      case 'week':
        return 7 * 24 * 60 * 60 * 1000
      case 'month':
        return 30 * 24 * 60 * 60 * 1000
      default:
        return 24 * 60 * 60 * 1000
    }
  }
  
  const filterMetricsByTime = (type: MetricEntry['type']): number => {
    if (!metrics) return 0
    const now = Date.now()
    const filterMs = getTimeFilterMs(timeFilter)
    const cutoffTime = now - filterMs
    
    return metrics
      .filter(m => m.type === type && m.timestamp >= cutoffTime)
      .reduce((sum, m) => sum + m.value, 0)
  }
  
  const themeOptions: { value: ThemeName; label: string; description: string }[] = [
    { value: 'dark', label: 'Dark', description: 'Classic dark theme' },
    { value: 'light', label: 'Light', description: 'Clean light theme' },
    { value: 'nord', label: 'Nord', description: 'Arctic, north-inspired palette' },
    { value: 'dracula', label: 'Dracula', description: 'Dark theme with vibrant colors' },
    { value: 'solarized', label: 'Solarized', description: 'Precision colors for readability' },
    { value: 'monokai', label: 'Monokai', description: 'Warm and vibrant developer theme' },
    { value: 'gruvbox', label: 'Gruvbox', description: 'Retro groove color scheme' },
  ]
  
  const handleAddSecret = () => {
    if (newSecretKey && newSecretValue) {
      setSecrets((current) => [
        ...(current || []),
        { id: Date.now().toString(), key: newSecretKey, value: newSecretValue },
      ])
      setNewSecretKey('')
      setNewSecretValue('')
    }
  }
  
  const handleDeleteSecret = (id: string) => {
    setSecrets((current) => (current || []).filter((s) => s.id !== id))
  }
  
  const handleAddConfig = () => {
    if (newConfigKey && newConfigValue) {
      setConfig((current) => [
        ...(current || []),
        { id: Date.now().toString(), key: newConfigKey, value: newConfigValue },
      ])
      setNewConfigKey('')
      setNewConfigValue('')
    }
  }
  
  const handleDeleteConfig = (id: string) => {
    setConfig((current) => (current || []).filter((c) => c.id !== id))
  }
  
  const handleAddUserPref = () => {
    if (newUserPrefKey && newUserPrefValue) {
      setUserPreferences((current) => [
        ...(current || []),
        { id: Date.now().toString(), key: newUserPrefKey, value: newUserPrefValue },
      ])
      setNewUserPrefKey('')
      setNewUserPrefValue('')
    }
  }
  
  const handleDeleteUserPref = (id: string) => {
    setUserPreferences((current) => (current || []).filter((p) => p.id !== id))
  }
  
  const toggleSecretVisibility = (id: string) => {
    setShowSecrets((prev) => ({ ...prev, [id]: !prev[id] }))
  }
  
  const exportConfig = () => {
    const data = {
      config: config || [],
      preferences: preferences || {},
      timestamp: new Date().toISOString(),
    }
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `radio-config-${Date.now()}.json`
    a.click()
  }
  
  return (
    <div className="h-full overflow-hidden">
      <Tabs defaultValue="preferences" className="h-full flex flex-col">
        <TabsList className="grid grid-cols-6 w-full max-w-4xl flex-shrink-0">
          <TabsTrigger value="preferences">Preferences</TabsTrigger>
          <TabsTrigger value="config">Configuration</TabsTrigger>
          <TabsTrigger value="secrets">Secrets</TabsTrigger>
          <TabsTrigger value="logs">System Logs</TabsTrigger>
          <TabsTrigger value="system">System Info</TabsTrigger>
          <TabsTrigger value="metrics">System Metrics</TabsTrigger>
        </TabsList>
        
        <ScrollArea className="flex-1 mt-4">
          <div className="max-w-4xl pb-4">
            <TabsContent value="preferences" className="space-y-4">
              <Card className="p-6 space-y-6">
                <div className="space-y-4">
                  <div className="text-lg font-semibold">Theme</div>
                  
                  <div className="space-y-3">
                    <Label>Color Theme</Label>
                    <div className="grid grid-cols-2 gap-3">
                      {themeOptions.map((option) => (
                        <button
                          key={option.value}
                          onClick={() => setTheme(option.value)}
                          className={`p-4 rounded-lg border-2 text-left transition-all ${
                            theme === option.value
                              ? 'border-primary bg-primary/10'
                              : 'border-border hover:border-primary/50 hover:bg-secondary'
                          }`}
                        >
                          <div className="font-semibold mb-1">{option.label}</div>
                          <div className="text-xs text-muted-foreground">{option.description}</div>
                        </button>
                      ))}
                    </div>
                  </div>
                </div>
              </Card>
              
              <Card className="p-6 space-y-6">
                <div className="space-y-4">
                  <div className="text-lg font-semibold">Display Settings</div>
                  
                  <div className="flex items-center justify-between">
                    <div className="space-y-1">
                      <Label>Show Screen Size Debugger</Label>
                      <div className="text-sm text-muted-foreground">
                        Display current window dimensions
                      </div>
                    </div>
                    <Switch 
                      checked={showScreenDebugger}
                      onCheckedChange={setShowScreenDebugger}
                    />
                  </div>
                  
                  <div className="flex items-center justify-between">
                    <div className="space-y-1">
                      <Label>LED Color Theme</Label>
                      <div className="text-sm text-muted-foreground">
                        Choose frequency display color
                      </div>
                    </div>
                    <div className="flex gap-2">
                      <Button 
                        variant={(preferences?.ledColor || 'amber') === 'amber' ? 'default' : 'outline'} 
                        size="sm"
                        onClick={() => setPreferences((prev) => ({ ...(prev || defaultPreferences), ledColor: 'amber' }))}
                      >
                        Amber
                      </Button>
                      <Button 
                        variant={(preferences?.ledColor || 'amber') === 'green' ? 'default' : 'outline'} 
                        size="sm"
                        onClick={() => setPreferences((prev) => ({ ...(prev || defaultPreferences), ledColor: 'green' }))}
                      >
                        Green
                      </Button>
                      <Button 
                        variant={(preferences?.ledColor || 'amber') === 'cyan' ? 'default' : 'outline'} 
                        size="sm"
                        onClick={() => setPreferences((prev) => ({ ...(prev || defaultPreferences), ledColor: 'cyan' }))}
                      >
                        Cyan
                      </Button>
                    </div>
                  </div>
                  
                  <div className="flex items-center justify-between">
                    <div className="space-y-1">
                      <Label>Display Brightness</Label>
                      <div className="text-sm text-muted-foreground">
                        Adjust LED intensity
                      </div>
                    </div>
                    <div className="w-64">
                      <Slider 
                        value={[preferences?.brightness || 80]} 
                        max={100} 
                        step={1}
                        onValueChange={(val) => setPreferences((prev) => ({ ...(prev || defaultPreferences), brightness: val[0] }))}
                      />
                    </div>
                  </div>
                </div>
              </Card>
              
              <Card className="p-6 space-y-6">
                <div className="space-y-4">
                  <div className="text-lg font-semibold">Audio Settings</div>
                  
                  <div className="flex items-center justify-between">
                    <div className="space-y-1">
                      <Label>Visualizer Mode</Label>
                      <div className="text-sm text-muted-foreground">
                        Default visualization type
                      </div>
                    </div>
                    <div className="flex gap-2">
                      <Button 
                        variant={visualizerMode === 'spectrum' ? 'default' : 'outline'} 
                        size="sm"
                        onClick={() => setVisualizerMode('spectrum')}
                      >
                        Spectrum
                      </Button>
                      <Button 
                        variant={visualizerMode === 'waveform' ? 'default' : 'outline'} 
                        size="sm"
                        onClick={() => setVisualizerMode('waveform')}
                      >
                        Waveform
                      </Button>
                      <Button 
                        variant={visualizerMode === 'vu' ? 'default' : 'outline'} 
                        size="sm"
                        onClick={() => setVisualizerMode('vu')}
                      >
                        VU Meter
                      </Button>
                    </div>
                  </div>
                  
                  <div className="flex items-center justify-between">
                    <div className="space-y-1">
                      <Label>Audio Ducking</Label>
                      <div className="text-sm text-muted-foreground">
                        Lower volume during notifications
                      </div>
                    </div>
                    <Switch 
                      checked={preferences?.audioDucking || false}
                      onCheckedChange={(checked) => setPreferences((prev) => ({ ...(prev || defaultPreferences), audioDucking: checked }))}
                    />
                  </div>
                  
                  <div className="flex items-center justify-between">
                    <div className="space-y-1">
                      <Label>Auto-Play on Source Change</Label>
                      <div className="text-sm text-muted-foreground">
                        Automatically play when switching sources
                      </div>
                    </div>
                    <Switch 
                      checked={preferences?.autoPlay || false}
                      onCheckedChange={(checked) => setPreferences((prev) => ({ ...(prev || defaultPreferences), autoPlay: checked }))}
                    />
                  </div>
                </div>
              </Card>
            </TabsContent>
            
            <TabsContent value="config" className="space-y-4">
              <Card className="p-6">
                <div className="flex items-center justify-between mb-4">
                  <div className="text-lg font-semibold">
                    {configType === 'config' ? 'Configuration Variables' : 'User Preferences'}
                  </div>
                  <Tabs value={configType} onValueChange={(v) => setConfigType(v as 'config' | 'user-prefs')}>
                    <TabsList>
                      <TabsTrigger value="config">Configuration</TabsTrigger>
                      <TabsTrigger value="user-prefs">User Preferences</TabsTrigger>
                    </TabsList>
                  </Tabs>
                </div>
                
                {configType === 'config' ? (
                  <div className="space-y-4">
                    <div className="grid grid-cols-2 gap-3">
                      <Input 
                        placeholder="Key (e.g., API_URL)"
                        value={newConfigKey}
                        onChange={(e) => setNewConfigKey(e.target.value)}
                      />
                      <Input 
                        placeholder="Value"
                        value={newConfigValue}
                        onChange={(e) => setNewConfigValue(e.target.value)}
                      />
                    </div>
                    <Button onClick={handleAddConfig} className="w-full gap-2">
                      <Plus size={20} />
                      Add Configuration
                    </Button>
                    
                    <div className="space-y-2 mt-4">
                      {(!config || config.length === 0) ? (
                        <div className="text-center text-muted-foreground py-8">
                          No configuration variables set
                        </div>
                      ) : (
                        config.map((item) => (
                          <div key={item.id} className="flex items-center justify-between p-3 bg-secondary rounded-lg">
                            <div className="flex-1 grid grid-cols-2 gap-4">
                              <div className="font-mono text-sm">{item.key}</div>
                              <div className="text-sm text-muted-foreground truncate">{item.value}</div>
                            </div>
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => handleDeleteConfig(item.id)}
                            >
                              <Trash size={18} />
                            </Button>
                          </div>
                        ))
                      )}
                    </div>
                  </div>
                ) : (
                  <div className="space-y-4">
                    <div className="grid grid-cols-2 gap-3">
                      <Input 
                        placeholder="Key (e.g., DEFAULT_VOLUME)"
                        value={newUserPrefKey}
                        onChange={(e) => setNewUserPrefKey(e.target.value)}
                      />
                      <Input 
                        placeholder="Value"
                        value={newUserPrefValue}
                        onChange={(e) => setNewUserPrefValue(e.target.value)}
                      />
                    </div>
                    <Button onClick={handleAddUserPref} className="w-full gap-2">
                      <Plus size={20} />
                      Add User Preference
                    </Button>
                    
                    <div className="space-y-2 mt-4">
                      {(!userPreferences || userPreferences.length === 0) ? (
                        <div className="text-center text-muted-foreground py-8">
                          No user preferences set
                        </div>
                      ) : (
                        userPreferences.map((item) => (
                          <div key={item.id} className="flex items-center justify-between p-3 bg-secondary rounded-lg">
                            <div className="flex-1 grid grid-cols-2 gap-4">
                              <div className="font-mono text-sm">{item.key}</div>
                              <div className="text-sm text-muted-foreground truncate">{item.value}</div>
                            </div>
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => handleDeleteUserPref(item.id)}
                            >
                              <Trash size={18} />
                            </Button>
                          </div>
                        ))
                      )}
                    </div>
                  </div>
                )}
              </Card>
            </TabsContent>
            
            <TabsContent value="secrets" className="space-y-4">
              <Card className="p-6">
                <div className="text-lg font-semibold mb-4">Secret Management</div>
                <div className="space-y-4">
                  <div className="grid grid-cols-2 gap-3">
                    <Input 
                      placeholder="Key (e.g., SPOTIFY_TOKEN)"
                      value={newSecretKey}
                      onChange={(e) => setNewSecretKey(e.target.value)}
                    />
                    <Input 
                      type="password"
                      placeholder="Secret Value"
                      value={newSecretValue}
                      onChange={(e) => setNewSecretValue(e.target.value)}
                    />
                  </div>
                  <Button onClick={handleAddSecret} className="w-full gap-2">
                    <Plus size={20} />
                    Add Secret
                  </Button>
                  
                  <div className="space-y-2 mt-4">
                    {(!secrets || secrets.length === 0) ? (
                      <div className="text-center text-muted-foreground py-8">
                        No secrets stored
                      </div>
                    ) : (
                      secrets.map((secret) => (
                        <div key={secret.id} className="flex items-center justify-between p-3 bg-secondary rounded-lg">
                          <div className="flex-1 grid grid-cols-2 gap-4">
                            <div className="font-mono text-sm">{secret.key}</div>
                            <div className="text-sm font-mono">
                              {showSecrets[secret.id] ? secret.value : '••••••••'}
                            </div>
                          </div>
                          <div className="flex gap-1">
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => toggleSecretVisibility(secret.id)}
                            >
                              {showSecrets[secret.id] ? <EyeSlash size={18} /> : <Eye size={18} />}
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon"
                              onClick={() => handleDeleteSecret(secret.id)}
                            >
                              <Trash size={18} />
                            </Button>
                          </div>
                        </div>
                      ))
                    )}
                  </div>
                </div>
              </Card>
            </TabsContent>
            
            <TabsContent value="logs" className="space-y-4">
              <Card className="p-6">
                <div className="flex items-center justify-between mb-4">
                  <div className="text-lg font-semibold">System Logs</div>
                  <Button variant="outline" size="sm">Clear Logs</Button>
                </div>
                
                <ScrollArea className="h-[500px]">
                  <div className="space-y-2 font-mono text-xs">
                    {(!logs || logs.length === 0) ? (
                      <div className="text-center text-muted-foreground py-8">
                        No system logs available
                      </div>
                    ) : (
                      logs.map((log, idx) => (
                        <div 
                          key={idx}
                          className="p-2 bg-secondary rounded flex items-start gap-3"
                        >
                          <span className="text-muted-foreground">{log.timestamp}</span>
                          <span className={
                            log.level === 'error' ? 'text-destructive' :
                            log.level === 'warning' ? 'text-yellow-500' :
                            'text-primary'
                          }>
                            [{log.level.toUpperCase()}]
                          </span>
                          <span className="flex-1">{log.message}</span>
                        </div>
                      ))
                    )}
                  </div>
                </ScrollArea>
              </Card>
            </TabsContent>
            
            <TabsContent value="system" className="space-y-4">
              <Card className="p-6 space-y-6">
                <div className="space-y-4">
                  <div className="text-lg font-semibold">System Information</div>
                  
                  <div className="grid grid-cols-2 gap-4 text-sm">
                    <div className="flex justify-between p-3 bg-secondary rounded-lg">
                      <span className="text-muted-foreground">Device</span>
                      <span className="font-semibold">Raspberry Pi 5</span>
                    </div>
                    <div className="flex justify-between p-3 bg-secondary rounded-lg">
                      <span className="text-muted-foreground">Display</span>
                      <span className="font-semibold">1920×720</span>
                    </div>
                    <div className="flex justify-between p-3 bg-secondary rounded-lg">
                      <span className="text-muted-foreground">Version</span>
                      <span className="font-semibold">1.0.0</span>
                    </div>
                    <div className="flex justify-between p-3 bg-secondary rounded-lg">
                      <span className="text-muted-foreground">Uptime</span>
                      <span className="font-semibold">2d 14h 32m</span>
                    </div>
                  </div>
                </div>
              </Card>
              
              <div className="flex gap-4">
                <Button variant="destructive" className="flex-1">
                  Restart System
                </Button>
                <Button variant="outline" className="flex-1 gap-2" onClick={exportConfig}>
                  <Download size={20} />
                  Export Configuration
                </Button>
              </div>
            </TabsContent>
            
            <TabsContent value="metrics" className="space-y-4">
              <Card className="p-6 space-y-6">
                <div className="flex items-center justify-between">
                  <div className="text-lg font-semibold flex items-center gap-2">
                    <ChartBar size={24} className="text-primary" />
                    System Metrics
                  </div>
                  <div className="flex gap-2">
                    <Button 
                      variant={timeFilter === 'minute' ? 'default' : 'outline'} 
                      size="sm"
                      onClick={() => setTimeFilter('minute')}
                    >
                      Last Minute
                    </Button>
                    <Button 
                      variant={timeFilter === 'hour' ? 'default' : 'outline'} 
                      size="sm"
                      onClick={() => setTimeFilter('hour')}
                    >
                      Last Hour
                    </Button>
                    <Button 
                      variant={timeFilter === 'day' ? 'default' : 'outline'} 
                      size="sm"
                      onClick={() => setTimeFilter('day')}
                    >
                      Last Day
                    </Button>
                    <Button 
                      variant={timeFilter === 'week' ? 'default' : 'outline'} 
                      size="sm"
                      onClick={() => setTimeFilter('week')}
                    >
                      Last Week
                    </Button>
                    <Button 
                      variant={timeFilter === 'month' ? 'default' : 'outline'} 
                      size="sm"
                      onClick={() => setTimeFilter('month')}
                    >
                      Last Month
                    </Button>
                  </div>
                </div>
                
                <div className="rounded-lg border border-border overflow-hidden">
                  <Table>
                    <TableHeader>
                      <TableRow className="bg-muted/50">
                        <TableHead className="font-semibold">Metric</TableHead>
                        <TableHead className="font-semibold text-right">Count</TableHead>
                        <TableHead className="font-semibold">Description</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      <TableRow>
                        <TableCell className="font-medium">TTS Alerts</TableCell>
                        <TableCell className="text-right font-mono text-lg text-primary font-semibold">
                          {filterMetricsByTime('tts-alert').toLocaleString()}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          Text-to-speech alerts triggered
                        </TableCell>
                      </TableRow>
                      <TableRow>
                        <TableCell className="font-medium">Audio File Alerts</TableCell>
                        <TableCell className="text-right font-mono text-lg text-accent font-semibold">
                          {filterMetricsByTime('audio-alert').toLocaleString()}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          Pre-recorded audio alerts played
                        </TableCell>
                      </TableRow>
                      <TableRow>
                        <TableCell className="font-medium">Characters TTSed</TableCell>
                        <TableCell className="text-right font-mono text-lg text-primary font-semibold">
                          {filterMetricsByTime('characters-ttsed').toLocaleString()}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          Total characters converted to speech
                        </TableCell>
                      </TableRow>
                      <TableRow>
                        <TableCell className="font-medium">Audio Files Played</TableCell>
                        <TableCell className="text-right font-mono text-lg text-accent font-semibold">
                          {filterMetricsByTime('audio-played').toLocaleString()}
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          Total audio files played from library
                        </TableCell>
                      </TableRow>
                    </TableBody>
                  </Table>
                </div>
                
                <div className="pt-4 border-t border-border">
                  <div className="text-sm text-muted-foreground">
                    Metrics are collected in real-time and stored persistently. 
                    Use the time filters above to view different time ranges.
                  </div>
                </div>
              </Card>
            </TabsContent>
          </div>
        </ScrollArea>
      </Tabs>
    </div>
  )
}
