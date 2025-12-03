import { useState } from 'react'
import { Card } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Folder, MusicNote, Play, FolderOpen, FolderPlus } from '@phosphor-icons/react'
import { useKV } from '@github/spark/hooks'
import { useAppStore } from '@/lib/store'
import { NowPlayingCard } from '@/components/NowPlayingCard'
import { SpectrumVisualizer } from '@/components/SpectrumVisualizer'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

interface AudioFile {
  id: string
  name: string
  path: string
  duration: number
  artist?: string
  album?: string
}

export function FilesView() {
  const [files, setFiles] = useKV<AudioFile[]>('audio-files', [])
  const [currentFolder, setCurrentFolder] = useState('/')
  const [showAddDialog, setShowAddDialog] = useState(false)
  const [newFileName, setNewFileName] = useState('')
  const [newFileArtist, setNewFileArtist] = useState('')
  const [newFileAlbum, setNewFileAlbum] = useState('')
  
  const setTrackMetadata = useAppStore((state) => state.setTrackMetadata)
  const setPlaybackState = useAppStore((state) => state.setPlaybackState)
  
  const handlePlayFile = (file: AudioFile) => {
    setTrackMetadata({
      title: file.name.replace(/\.[^/.]+$/, ''),
      artist: file.artist || 'Unknown Artist',
      album: file.album || 'Unknown Album',
    })
    setPlaybackState({
      isPlaying: true,
      position: 0,
      duration: file.duration,
    })
  }
  
  const handleAddFile = () => {
    if (newFileName) {
      const newFile: AudioFile = {
        id: Date.now().toString(),
        name: newFileName,
        path: `${currentFolder}${newFileName}`,
        duration: 180,
        artist: newFileArtist || 'Unknown Artist',
        album: newFileAlbum || 'Unknown Album',
      }
      
      setFiles((current) => [...(current || []), newFile])
      setNewFileName('')
      setNewFileArtist('')
      setNewFileAlbum('')
      setShowAddDialog(false)
    }
  }
  
  return (
    <div className="h-full flex gap-4">
      <Card className="w-[360px] p-4 flex flex-col">
        <div className="flex items-center justify-between mb-4">
          <div className="text-lg font-semibold">Audio Files</div>
          <Button 
            variant="outline" 
            size="sm" 
            className="gap-2"
            onClick={() => setShowAddDialog(true)}
          >
            <FolderPlus size={18} />
            Add Files
          </Button>
        </div>
        
        <ScrollArea className="flex-1">
          {!files || files.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full text-center py-8">
              <MusicNote size={64} className="text-muted-foreground mb-4" />
              <div className="text-lg font-semibold">No Audio Files</div>
              <div className="text-sm text-muted-foreground">
                Add audio files to get started
              </div>
            </div>
          ) : (
            <div className="space-y-2">
              {files.map((file) => (
                <div
                  key={file.id}
                  className="flex items-center justify-between p-3 bg-secondary rounded-lg hover:bg-secondary/80"
                >
                  <div className="flex items-center gap-3 flex-1 min-w-0">
                    <MusicNote size={24} className="text-primary flex-shrink-0" />
                    <div className="min-w-0">
                      <div className="font-medium truncate">{file.name}</div>
                      <div className="text-xs text-muted-foreground truncate">
                        {file.artist} • {file.album}
                      </div>
                    </div>
                  </div>
                  <Button
                    size="icon"
                    variant="ghost"
                    onClick={() => handlePlayFile(file)}
                  >
                    <Play size={20} weight="fill" />
                  </Button>
                </div>
              ))}
            </div>
          )}
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
      
      <Dialog open={showAddDialog} onOpenChange={setShowAddDialog}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Add Audio Files</DialogTitle>
            <DialogDescription>
              Select audio files to add to your playlist
            </DialogDescription>
          </DialogHeader>
          
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="filename">File Name</Label>
              <Input
                id="filename"
                placeholder="song.mp3"
                value={newFileName}
                onChange={(e) => setNewFileName(e.target.value)}
              />
            </div>
            
            <div className="space-y-2">
              <Label htmlFor="artist">Artist</Label>
              <Input
                id="artist"
                placeholder="Artist name"
                value={newFileArtist}
                onChange={(e) => setNewFileArtist(e.target.value)}
              />
            </div>
            
            <div className="space-y-2">
              <Label htmlFor="album">Album</Label>
              <Input
                id="album"
                placeholder="Album name"
                value={newFileAlbum}
                onChange={(e) => setNewFileAlbum(e.target.value)}
              />
            </div>
          </div>
          
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowAddDialog(false)}>
              Cancel
            </Button>
            <Button onClick={handleAddFile}>Add File</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
