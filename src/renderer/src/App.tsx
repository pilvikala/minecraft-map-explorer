import React from 'react'
import WorldLoader from './components/WorldLoader'
import MapCanvas from './components/MapCanvas'
import LayerPanel from './components/LayerPanel'

export default function App(): React.ReactElement {
  return (
    <div style={styles.root}>
      <div style={styles.topBar}>
        <span style={styles.title}>Minecraft Map Explorer</span>
        <WorldLoader />
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
  body: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden'
  }
}
