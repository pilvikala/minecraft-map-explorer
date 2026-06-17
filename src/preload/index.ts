import { contextBridge, ipcRenderer } from 'electron'
import type { WorldInfo } from '../main/index'

contextBridge.exposeInMainWorld('minecraft', {
  findWorlds: (): Promise<WorldInfo[]> => ipcRenderer.invoke('find-worlds'),
  openWorldDialog: (): Promise<string | null> => ipcRenderer.invoke('open-world-dialog'),
  listRegions: (regionDir: string): Promise<string[]> => ipcRenderer.invoke('list-regions', regionDir),
  readRegion: (filePath: string): Promise<ArrayBuffer> => ipcRenderer.invoke('read-region', filePath)
})
