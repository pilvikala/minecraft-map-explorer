import React from 'react'
import { useStore } from '../store'
import { LayerMode } from '../core/renderer'
import { ORE_BLOCKS } from '../core/colors'

const MODES: { key: LayerMode; label: string }[] = [
  { key: 'surface', label: 'Surface' },
  { key: 'heightmap', label: 'Heightmap' },
  { key: 'cave', label: 'Caves' },
  { key: 'slice', label: 'Y-Slice' },
  { key: 'biome', label: 'Biome' }
]

const ORE_LABELS: Record<string, string> = {
  'minecraft:diamond_ore': 'Diamond',
  'minecraft:deepslate_diamond_ore': 'Diamond (deep)',
  'minecraft:emerald_ore': 'Emerald',
  'minecraft:deepslate_emerald_ore': 'Emerald (deep)',
  'minecraft:gold_ore': 'Gold',
  'minecraft:deepslate_gold_ore': 'Gold (deep)',
  'minecraft:iron_ore': 'Iron',
  'minecraft:deepslate_iron_ore': 'Iron (deep)',
  'minecraft:copper_ore': 'Copper',
  'minecraft:deepslate_copper_ore': 'Copper (deep)',
  'minecraft:lapis_ore': 'Lapis',
  'minecraft:deepslate_lapis_ore': 'Lapis (deep)',
  'minecraft:redstone_ore': 'Redstone',
  'minecraft:deepslate_redstone_ore': 'Redstone (deep)',
  'minecraft:coal_ore': 'Coal',
  'minecraft:deepslate_coal_ore': 'Coal (deep)',
  'minecraft:ancient_debris': 'Ancient Debris',
  'minecraft:nether_quartz_ore': 'Quartz',
  'minecraft:nether_gold_ore': 'Nether Gold'
}

function toHex(rgb: [number, number, number]): string {
  return `#${rgb.map((v) => v.toString(16).padStart(2, '0')).join('')}`
}

export default function LayerPanel(): React.ReactElement {
  const { layerConfig, setLayerMode, setSliceY, toggleOre, setOreOverlay, hoveredBlock } = useStore()

  return (
    <div style={styles.panel}>
      <div style={styles.section}>
        <div style={styles.sectionTitle}>View Mode</div>
        {MODES.map(({ key, label }) => (
          <button
            key={key}
            style={{ ...styles.modeBtn, ...(layerConfig.mode === key ? styles.modeBtnActive : {}) }}
            onClick={() => setLayerMode(key)}
          >
            {label}
          </button>
        ))}
      </div>

      {layerConfig.mode === 'slice' && (
        <div style={styles.section}>
          <div style={styles.sectionTitle}>Y Level: {layerConfig.sliceY}</div>
          <input
            type="range"
            min={-64}
            max={319}
            value={layerConfig.sliceY}
            onChange={(e) => setSliceY(parseInt(e.target.value))}
            style={styles.slider}
          />
          <div style={styles.rangeLabels}>
            <span>-64</span>
            <span>319</span>
          </div>
        </div>
      )}

      <div style={styles.section}>
        <label style={styles.checkRow}>
          <input
            type="checkbox"
            checked={layerConfig.oreOverlay}
            onChange={(e) => setOreOverlay(e.target.checked)}
          />
          <span style={{ marginLeft: 6 }}>Ore Overlay</span>
        </label>
      </div>

      {layerConfig.oreOverlay && (
        <div style={styles.section}>
          <div style={styles.sectionTitle}>Ores</div>
          {Object.entries(ORE_LABELS).map(([blockName, label]) => {
            const color = toHex(ORE_BLOCKS[blockName])
            return (
              <label key={blockName} style={styles.checkRow}>
                <input
                  type="checkbox"
                  checked={layerConfig.oreFilter.has(blockName)}
                  onChange={() => toggleOre(blockName)}
                />
                <span
                  style={{
                    display: 'inline-block',
                    width: 10,
                    height: 10,
                    background: color,
                    marginLeft: 6,
                    marginRight: 4,
                    borderRadius: 2
                  }}
                />
                <span style={{ fontSize: 12 }}>{label}</span>
              </label>
            )
          })}
        </div>
      )}

      {hoveredBlock && (
        <div style={styles.section}>
          <div style={styles.sectionTitle}>Cursor</div>
          <div style={styles.hoverInfo}>
            X: {hoveredBlock.x} Z: {hoveredBlock.z}
          </div>
          <div style={{ ...styles.hoverInfo, fontSize: 11, color: '#aaa', wordBreak: 'break-word' }}>
            {hoveredBlock.name}
          </div>
        </div>
      )}
    </div>
  )
}

const styles: Record<string, React.CSSProperties> = {
  panel: {
    width: 180,
    minWidth: 180,
    background: '#1e1e2e',
    borderLeft: '1px solid #333',
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    gap: 0
  },
  section: {
    padding: '10px 12px',
    borderBottom: '1px solid #2a2a3a',
    display: 'flex',
    flexDirection: 'column',
    gap: 4
  },
  sectionTitle: {
    fontSize: 11,
    fontWeight: 700,
    textTransform: 'uppercase',
    color: '#888',
    marginBottom: 4,
    letterSpacing: 0.5
  },
  modeBtn: {
    background: '#2a2a3a',
    color: '#ccc',
    border: '1px solid #444',
    borderRadius: 4,
    padding: '5px 8px',
    cursor: 'pointer',
    fontSize: 13,
    textAlign: 'left'
  },
  modeBtnActive: {
    background: '#3a7d44',
    color: '#fff',
    borderColor: '#3a7d44'
  },
  slider: {
    width: '100%',
    accentColor: '#3a7d44'
  },
  rangeLabels: {
    display: 'flex',
    justifyContent: 'space-between',
    fontSize: 10,
    color: '#666'
  },
  checkRow: {
    display: 'flex',
    alignItems: 'center',
    cursor: 'pointer',
    fontSize: 13,
    color: '#ccc'
  },
  hoverInfo: {
    fontSize: 12,
    color: '#ddd',
    fontFamily: 'monospace'
  }
}
