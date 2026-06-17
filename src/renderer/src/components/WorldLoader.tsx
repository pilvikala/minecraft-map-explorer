import React, { useEffect, useState, useCallback } from 'react'
import { useStore } from '../store'
import { parseRegionFile } from '../core/region'
import { decodeChunk } from '../core/chunk'

interface WorldInfo {
  name: string
  folderName: string
  path: string
  regionDir: string
  lastModified: number
  regionCount: number
  source: string
}

declare global {
  interface Window {
    minecraft: {
      findWorlds: () => Promise<WorldInfo[]>
      openWorldDialog: () => Promise<string | null>
      listRegions: (dir: string) => Promise<string[]>
      readRegion: (path: string) => Promise<ArrayBuffer>
    }
  }
}

function pathJoin(...parts: string[]): string {
  return parts.join('/').replace(/\/+/g, '/')
}

function formatAge(ms: number): string {
  const diff = Date.now() - ms
  const mins = Math.floor(diff / 60000)
  const hours = Math.floor(diff / 3600000)
  const days = Math.floor(diff / 86400000)
  if (days > 0) return `${days} day${days > 1 ? 's' : ''} ago`
  if (hours > 0) return `${hours} hour${hours > 1 ? 's' : ''} ago`
  if (mins > 0) return `${mins} min ago`
  return 'just now'
}

interface WorldPickerProps {
  onLoad: (regionDir: string, displayName: string) => void
}

export function WorldPicker({ onLoad }: WorldPickerProps): React.ReactElement {
  const [worlds, setWorlds] = useState<WorldInfo[]>([])
  const [scanning, setScanning] = useState(true)

  useEffect(() => {
    if (!window.minecraft) { setScanning(false); return }
    window.minecraft.findWorlds().then((found) => {
      setWorlds(found)
      setScanning(false)
    })
  }, [])

  async function browseForFolder(): Promise<void> {
    if (!window.minecraft) return
    const dir = await window.minecraft.openWorldDialog()
    if (!dir) return
    // Check if selected dir has a region/ subfolder or is a region folder itself
    let regionDir = dir
    const subRegions = await window.minecraft.listRegions(pathJoin(dir, 'region'))
    if (subRegions.length > 0) regionDir = pathJoin(dir, 'region')
    onLoad(regionDir, dir.split('/').pop() ?? dir)
  }

  return (
    <div style={styles.picker}>
      <div style={styles.pickerHeader}>
        <h1 style={styles.pickerTitle}>Minecraft Map Explorer</h1>
        <p style={styles.pickerSub}>Choose a world to explore</p>
      </div>

      {scanning ? (
        <div style={styles.scanning}>Scanning for worlds…</div>
      ) : worlds.length === 0 ? (
        <div style={styles.emptyState}>
          <div style={styles.emptyIcon}>🗺</div>
          <p>No Minecraft worlds found in the default save location.</p>
          <p style={{ color: '#888', fontSize: 13, marginTop: 4 }}>
            Use "Browse…" below to open a world folder manually.
          </p>
        </div>
      ) : (
        <div style={styles.grid}>
          {worlds.map((w) => (
            <button key={w.path} style={styles.worldCard} onClick={() => onLoad(w.regionDir, w.name)}>
              <div style={styles.worldIcon}>🌍</div>
              <div style={styles.worldName}>{w.name}</div>
              <div style={styles.worldSource}>{w.source}</div>
              <div style={styles.worldMeta}>
                {w.regionCount} region{w.regionCount !== 1 ? 's' : ''}
              </div>
              <div style={styles.worldAge}>{formatAge(w.lastModified)}</div>
            </button>
          ))}
        </div>
      )}

      <button style={styles.browseBtn} onClick={browseForFolder}>
        Browse for world folder…
      </button>
    </div>
  )
}

interface LoadingBarProps {
  loaded: number
  total: number
}

export function LoadingBar({ loaded, total }: LoadingBarProps): React.ReactElement {
  const pct = total > 0 ? Math.round((loaded / total) * 100) : 0
  return (
    <div style={styles.loadingWrap}>
      <div style={styles.loadingBar}>
        <div style={{ ...styles.loadingFill, width: `${pct}%` }} />
      </div>
      <div style={styles.loadingLabel}>Loading regions… {loaded}/{total}</div>
    </div>
  )
}

/** Top-bar compact status shown while a world is open */
export function WorldStatus(): React.ReactElement {
  const { regionDir, totalRegions, loadedRegions } = useStore()
  const isLoading = loadedRegions < totalRegions && totalRegions > 0
  const worldName = regionDir?.split('/').slice(-2, -1)[0] ?? ''

  return (
    <div style={styles.statusRow}>
      <span style={styles.worldNameSmall}>{worldName}</span>
      {isLoading && (
        <div style={styles.miniProgress}>
          <div style={{ ...styles.miniBar, width: `${Math.round((loadedRegions / totalRegions) * 100)}%` }} />
        </div>
      )}
    </div>
  )
}

/** Loads a region directory into the store */
export function useWorldLoader() {
  const { setRegionDir, addChunk, setLoadingRegion, setTotalRegions } = useStore()

  const loadWorld = useCallback(async (regionDir: string) => {
    setRegionDir(regionDir)
    const regionFiles = await window.minecraft.listRegions(regionDir)
    setTotalRegions(regionFiles.length)

    const BATCH = 4
    for (let i = 0; i < regionFiles.length; i += BATCH) {
      const batch = regionFiles.slice(i, i + BATCH)
      await Promise.all(
        batch.map(async (filename) => {
          setLoadingRegion(filename, true)
          try {
            const buffer = await window.minecraft.readRegion(pathJoin(regionDir, filename))
            const rawChunks = parseRegionFile(buffer, filename)
            for (const raw of rawChunks) {
              try {
                addChunk(decodeChunk(raw.data, raw.chunkX, raw.chunkZ))
              } catch { /* skip malformed chunks */ }
            }
          } catch { /* skip unreadable regions */ }
          finally {
            setLoadingRegion(filename, false)
          }
        })
      )
    }
  }, [setRegionDir, addChunk, setLoadingRegion, setTotalRegions])

  return loadWorld
}

const styles: Record<string, React.CSSProperties> = {
  picker: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    padding: '40px 24px',
    background: '#111',
    overflowY: 'auto',
    gap: 24
  },
  pickerHeader: {
    textAlign: 'center'
  },
  pickerTitle: {
    fontSize: 28,
    fontWeight: 800,
    color: '#8bc34a',
    margin: 0
  },
  pickerSub: {
    color: '#888',
    marginTop: 6,
    fontSize: 14
  },
  scanning: {
    color: '#888',
    fontSize: 14
  },
  emptyState: {
    textAlign: 'center',
    color: '#ccc',
    fontSize: 15
  },
  emptyIcon: {
    fontSize: 48,
    marginBottom: 12
  },
  grid: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: 16,
    justifyContent: 'center',
    maxWidth: 900,
    width: '100%'
  },
  worldCard: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: 4,
    background: '#1e1e2e',
    border: '1px solid #333',
    borderRadius: 8,
    padding: '18px 20px',
    cursor: 'pointer',
    width: 160,
    transition: 'background 0.15s, border-color 0.15s',
    color: '#eee'
  },
  worldIcon: {
    fontSize: 36,
    marginBottom: 4
  },
  worldName: {
    fontWeight: 600,
    fontSize: 14,
    textAlign: 'center',
    wordBreak: 'break-word'
  },
  worldSource: {
    fontSize: 10,
    color: '#5a7a5a',
    textAlign: 'center',
    wordBreak: 'break-word'
  },
  worldMeta: {
    fontSize: 11,
    color: '#888'
  },
  worldAge: {
    fontSize: 11,
    color: '#666'
  },
  browseBtn: {
    background: 'transparent',
    border: '1px solid #444',
    color: '#aaa',
    borderRadius: 6,
    padding: '8px 18px',
    cursor: 'pointer',
    fontSize: 13
  },
  loadingWrap: {
    display: 'flex',
    flexDirection: 'column',
    gap: 4,
    width: 240
  },
  loadingBar: {
    height: 6,
    background: '#222',
    borderRadius: 3,
    overflow: 'hidden'
  },
  loadingFill: {
    height: '100%',
    background: '#3a7d44',
    borderRadius: 3,
    transition: 'width 0.2s'
  },
  loadingLabel: {
    fontSize: 11,
    color: '#888',
    textAlign: 'center'
  },
  statusRow: {
    display: 'flex',
    alignItems: 'center',
    gap: 10
  },
  worldNameSmall: {
    fontSize: 13,
    color: '#ccc',
    fontWeight: 500
  },
  miniProgress: {
    width: 100,
    height: 4,
    background: '#222',
    borderRadius: 2,
    overflow: 'hidden'
  },
  miniBar: {
    height: '100%',
    background: '#3a7d44',
    transition: 'width 0.2s'
  }
}
