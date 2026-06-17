import React from 'react'
import { useStore } from '../store'
import { parseRegionFile } from '../core/region'
import { decodeChunk } from '../core/chunk'
import { join } from 'path'

declare global {
  interface Window {
    minecraft: {
      openWorldDialog: () => Promise<string | null>
      listRegions: (dir: string) => Promise<string[]>
      readRegion: (path: string) => Promise<ArrayBuffer>
    }
  }
}

// Node path.join isn't available in renderer; implement a simple join
function pathJoin(...parts: string[]): string {
  return parts.join('/').replace(/\/+/g, '/')
}

export default function WorldLoader(): React.ReactElement {
  const { regionDir, setRegionDir, addChunk, setLoadingRegion, setTotalRegions, loadedRegions, totalRegions } = useStore()

  async function openWorld(): Promise<void> {
    const dir = await window.minecraft.openWorldDialog()
    if (!dir) return

    // The user may select the world root or the region folder directly
    // Try to find a "region" subdirectory
    let regionFolder = dir
    const files = await window.minecraft.listRegions(dir)
    if (files.length === 0) {
      // Try appending /region
      const sub = pathJoin(dir, 'region')
      const subFiles = await window.minecraft.listRegions(sub)
      if (subFiles.length > 0) regionFolder = sub
    }

    setRegionDir(regionFolder)
    const regionFiles = await window.minecraft.listRegions(regionFolder)
    setTotalRegions(regionFiles.length)

    // Load all regions concurrently in batches of 4
    const BATCH = 4
    for (let i = 0; i < regionFiles.length; i += BATCH) {
      const batch = regionFiles.slice(i, i + BATCH)
      await Promise.all(
        batch.map(async (filename) => {
          setLoadingRegion(filename, true)
          try {
            const filePath = pathJoin(regionFolder, filename)
            const buffer = await window.minecraft.readRegion(filePath)
            const rawChunks = parseRegionFile(buffer, filename)
            for (const raw of rawChunks) {
              try {
                const chunk = decodeChunk(raw.data, raw.chunkX, raw.chunkZ)
                addChunk(chunk)
              } catch {
                // skip malformed chunks
              }
            }
          } catch {
            // skip unreadable regions
          } finally {
            setLoadingRegion(filename, false)
          }
        })
      )
    }
  }

  return (
    <div style={styles.container}>
      <button style={styles.button} onClick={openWorld}>
        Open Minecraft World
      </button>
      {totalRegions > 0 && (
        <div style={styles.progress}>
          <div
            style={{
              ...styles.bar,
              width: `${Math.round((loadedRegions / totalRegions) * 100)}%`
            }}
          />
          <span style={styles.label}>
            {loadedRegions} / {totalRegions} regions
          </span>
        </div>
      )}
    </div>
  )
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: 8,
    padding: '8px 12px',
    borderBottom: '1px solid #333'
  },
  button: {
    background: '#3a7d44',
    color: '#fff',
    border: 'none',
    borderRadius: 4,
    padding: '6px 14px',
    cursor: 'pointer',
    fontWeight: 600,
    fontSize: 13
  },
  progress: {
    position: 'relative',
    height: 16,
    background: '#222',
    borderRadius: 4,
    overflow: 'hidden'
  },
  bar: {
    position: 'absolute',
    inset: 0,
    background: '#3a7d44',
    transition: 'width 0.2s'
  },
  label: {
    position: 'relative',
    fontSize: 11,
    color: '#ccc',
    padding: '0 6px',
    lineHeight: '16px'
  }
}
