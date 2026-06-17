import { app, BrowserWindow, ipcMain, dialog } from 'electron'
import { join } from 'path'
import { readFileSync, readdirSync } from 'fs'

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

ipcMain.handle('open-world-dialog', async () => {
  const result = await dialog.showOpenDialog({
    title: 'Select Minecraft region folder',
    properties: ['openDirectory']
  })
  if (result.canceled || result.filePaths.length === 0) return null
  return result.filePaths[0]
})

ipcMain.handle('list-regions', (_event, regionDir: string) => {
  try {
    const files = readdirSync(regionDir)
    return files.filter((f) => f.endsWith('.mca'))
  } catch {
    return []
  }
})

ipcMain.handle('read-region', (_event, filePath: string) => {
  const buf = readFileSync(filePath)
  return buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength)
})
