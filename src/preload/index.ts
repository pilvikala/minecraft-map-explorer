import { contextBridge, ipcRenderer } from 'electron'

contextBridge.exposeInMainWorld('minecraft', {
  openWorldDialog: (): Promise<string | null> => ipcRenderer.invoke('open-world-dialog'),
  listRegions: (regionDir: string): Promise<string[]> => ipcRenderer.invoke('list-regions', regionDir),
  readRegion: (filePath: string): Promise<ArrayBuffer> => ipcRenderer.invoke('read-region', filePath)
})
