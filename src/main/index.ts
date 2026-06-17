import { app, BrowserWindow, ipcMain, dialog } from 'electron'
import { join } from 'path'
import { readFileSync, readdirSync } from 'fs'
import { discoverWorlds } from './find-worlds'
import type { WorldInfo } from './find-worlds'

export type { WorldInfo }

function createWindow(): void {
  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    webPreferences: {
      preload: join(__dirname, '../preload/index.js'),
      sandbox: false
    },
    title: 'Minecraft Map Explorer'
  })

  if (process.env['ELECTRON_RENDERER_URL']) {
    win.loadURL(process.env['ELECTRON_RENDERER_URL'])
  } else {
    win.loadFile(join(__dirname, '../renderer/index.html'))
  }
}

app.whenReady().then(() => {
  createWindow()
  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

ipcMain.handle('find-worlds', () => discoverWorlds())

ipcMain.handle('open-world-dialog', async () => {
  const result = await dialog.showOpenDialog({
    title: 'Select Minecraft world or region folder',
    properties: ['openDirectory']
  })
  if (result.canceled || result.filePaths.length === 0) return null
  return result.filePaths[0]
})

ipcMain.handle('list-regions', (_event, regionDir: string) => {
  try {
    return readdirSync(regionDir).filter((f) => f.endsWith('.mca'))
  } catch {
    return []
  }
})

ipcMain.handle('read-region', (_event, filePath: string) => {
  const buf = readFileSync(filePath)
  // Ensure we return a proper ArrayBuffer (not a Buffer view)
  const arrayBuffer = new ArrayBuffer(buf.length)
  const view = new Uint8Array(arrayBuffer)
  view.set(new Uint8Array(buf))
  return arrayBuffer
})
