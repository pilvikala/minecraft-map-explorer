import React, { useState } from 'react'
import { WorldPicker, WorldStatus, useWorldLoader } from './components/WorldLoader'
import MapCanvas from './components/MapCanvas'
import LayerPanel from './components/LayerPanel'
import { useStore } from './store'

export default function App(): React.ReactElement {
  const { regionDir } = useStore()
  const [phase, setPhase] = useState<'pick' | 'map'>('pick')
  const loadWorld = useWorldLoader()

  async function handleWorldSelected(regionPath: string): Promise<void> {
    setPhase('map')
    await loadWorld(regionPath)
  }

  if (phase === 'pick') {
    return <WorldPicker onLoad={handleWorldSelected} />
  }

  return (
    <div style={styles.root}>
      <div style={styles.topBar}>
        <span style={styles.title}>Minecraft Map Explorer</span>
        <WorldStatus />
        <button style={styles.backBtn} onClick={() => setPhase('pick')}>
          ← Worlds
        </button>
      </div>
      <div style={styles.body}>
        <MapCanvas />
        <LayerPanel />
      </div>
    </div>
  )
}

const styles: Record<string, React.CSSProperties> = {
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    background: '#111',
    color: '#eee'
  },
  topBar: {
    display: 'flex',
    alignItems: 'center',
    gap: 16,
    padding: '6px 12px',
    background: '#1a1a2e',
    borderBottom: '1px solid #333',
    flexShrink: 0
  },
  title: {
    fontWeight: 700,
    fontSize: 15,
    color: '#8bc34a',
    whiteSpace: 'nowrap'
  },
  backBtn: {
    marginLeft: 'auto',
    background: 'transparent',
    border: '1px solid #444',
    color: '#aaa',
    borderRadius: 4,
    padding: '3px 10px',
    cursor: 'pointer',
    fontSize: 12
  },
  body: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden'
  }
}
