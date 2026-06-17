import { create } from 'zustand'
import { ChunkData } from '../core/chunk'
import { LayerConfig, LayerMode } from '../core/renderer'

export interface Viewport {
  offsetX: number   // world-block offset of canvas center
  offsetZ: number
  zoom: number      // pixels per block
}

interface WorldState {
  regionDir: string | null
  chunks: Map<string, ChunkData>         // key: "cx,cz"
  loadingRegions: Set<string>
  totalRegions: number
  loadedRegions: number
  viewport: Viewport
  layerConfig: LayerConfig
  hoveredBlock: { x: number; z: number; name: string } | null

  setRegionDir: (dir: string) => void
  addChunk: (chunk: ChunkData) => void
  setLoadingRegion: (name: string, loading: boolean) => void
  setTotalRegions: (n: number) => void
  setViewport: (v: Partial<Viewport>) => void
  setLayerMode: (mode: LayerMode) => void
  setSliceY: (y: number) => void
  toggleOre: (oreName: string) => void
  setOreOverlay: (v: boolean) => void
  setHoveredBlock: (b: { x: number; z: number; name: string } | null) => void
}

export const useStore = create<WorldState>((set) => ({
  regionDir: null,
  chunks: new Map(),
  loadingRegions: new Set(),
  totalRegions: 0,
  loadedRegions: 0,
  viewport: { offsetX: 0, offsetZ: 0, zoom: 2 },
  layerConfig: {
    mode: 'surface',
    sliceY: 64,
    oreFilter: new Set(),
    oreOverlay: false
  },
  hoveredBlock: null,

  setRegionDir: (dir) => set({ regionDir: dir, chunks: new Map(), loadedRegions: 0, totalRegions: 0 }),

  addChunk: (chunk) =>
    set((s) => {
      const key = `${chunk.chunkX},${chunk.chunkZ}`
      const next = new Map(s.chunks)
      next.set(key, chunk)
      return { chunks: next }
    }),

  setLoadingRegion: (name, loading) =>
    set((s) => {
      const next = new Set(s.loadingRegions)
      if (loading) {
        next.add(name)
        return { loadingRegions: next }
      } else {
        next.delete(name)
        return { loadingRegions: next, loadedRegions: s.loadedRegions + 1 }
      }
    }),

  setTotalRegions: (n) => set({ totalRegions: n }),

  setViewport: (v) => set((s) => ({ viewport: { ...s.viewport, ...v } })),

  setLayerMode: (mode) =>
    set((s) => ({ layerConfig: { ...s.layerConfig, mode } })),

  setSliceY: (y) =>
    set((s) => ({ layerConfig: { ...s.layerConfig, sliceY: y } })),

  toggleOre: (oreName) =>
    set((s) => {
      const next = new Set(s.layerConfig.oreFilter)
      if (next.has(oreName)) next.delete(oreName)
      else next.add(oreName)
      return { layerConfig: { ...s.layerConfig, oreFilter: next } }
    }),

  setOreOverlay: (v) =>
    set((s) => ({ layerConfig: { ...s.layerConfig, oreOverlay: v } })),

  setHoveredBlock: (b) => set({ hoveredBlock: b })
}))
