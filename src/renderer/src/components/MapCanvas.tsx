import React, { useRef, useEffect, useCallback, useState } from 'react'
import { HoveredBlock, useStore } from '../store'
import { LayerConfig, renderChunk, renderChunkPreview, findDisplayY } from '../core/renderer'
import { ChunkData, getBlock } from '../core/chunk'
import { createPerfTracker, perfEnabled } from '../core/perf'

const CHUNK_SIZE = 16
const renderPerf = createPerfTracker('render', { logEvery: 20, warnAboveMs: 16 })

type TileSurface = OffscreenCanvas | HTMLCanvasElement

interface RenderStats {
  renderedTiles: number
  cacheHits: number
}

function createTileSurface(imageData: ImageData): TileSurface {
  const { width, height } = imageData

  if (typeof OffscreenCanvas !== 'undefined') {
    const canvas = new OffscreenCanvas(width, height)
    const ctx = canvas.getContext('2d')
    if (ctx) {
      ctx.putImageData(imageData, 0, 0)
      return canvas
    }
  }

  const canvas = document.createElement('canvas')
  canvas.width = width
  canvas.height = height
  const ctx = canvas.getContext('2d')
  if (ctx) {
    ctx.putImageData(imageData, 0, 0)
  }
  return canvas
}

function getChunkSpan(pixelsPerChunk: number): number {
  if (pixelsPerChunk <= 8) return 8
  if (pixelsPerChunk <= 16) return 4
  if (pixelsPerChunk <= 24) return 2
  return 1
}

function getChunkTileSurface(
  cx: number,
  cz: number,
  configKey: string,
  chunks: Map<string, ChunkData>,
  layerConfig: LayerConfig,
  tileCache: Map<string, TileSurface>,
  stats: RenderStats,
  previewSize?: number
): TileSurface | null {
  const key = `${cx},${cz}`
  const chunk = chunks.get(key)
  if (!chunk) return null

  const tileKey = previewSize ? `${key}|${configKey}|preview:${previewSize}` : `${key}|${configKey}`
  let tileSurface = tileCache.get(tileKey)
  if (!tileSurface) {
    tileSurface = createTileSurface(
      previewSize ? renderChunkPreview(chunk, layerConfig, previewSize) : renderChunk(chunk, layerConfig)
    )
    tileCache.set(tileKey, tileSurface)
    stats.renderedTiles += 1
  } else {
    stats.cacheHits += 1
  }

  return tileSurface
}

function createMacroTileSurface(span: number): TileSurface {
  const size = span * CHUNK_SIZE
  if (typeof OffscreenCanvas !== 'undefined') {
    return new OffscreenCanvas(size, size)
  }
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  return canvas
}

export default function MapCanvas(): React.ReactElement {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const chunks = useStore((state) => state.chunks)
  const viewport = useStore((state) => state.viewport)
  const layerConfig = useStore((state) => state.layerConfig)
  const setViewport = useStore((state) => state.setViewport)
  const setHoveredBlock = useStore((state) => state.setHoveredBlock)

  // Track canvas size
  const [size, setSize] = useState({ w: 800, h: 600 })
  const hoverFrameRef = useRef<number | null>(null)
  const viewportFrameRef = useRef<number | null>(null)
  const pendingHoverRef = useRef<HoveredBlock | null>(null)
  const pendingViewportRef = useRef<typeof viewport | null>(null)
  const viewportRef = useRef(viewport)

  useEffect(() => {
    viewportRef.current = viewport
  }, [viewport])

  const flushHoverUpdate = useCallback(() => {
    hoverFrameRef.current = null
    setHoveredBlock(pendingHoverRef.current)
  }, [setHoveredBlock])

  const flushViewportUpdate = useCallback(() => {
    viewportFrameRef.current = null
    const nextViewport = pendingViewportRef.current
    if (!nextViewport) return
    pendingViewportRef.current = null
    viewportRef.current = nextViewport
    setViewport(nextViewport)
  }, [setViewport])

  const queueHoverUpdate = useCallback((nextHoveredBlock: HoveredBlock | null) => {
    pendingHoverRef.current = nextHoveredBlock
    if (hoverFrameRef.current !== null) return
    hoverFrameRef.current = window.requestAnimationFrame(flushHoverUpdate)
  }, [flushHoverUpdate])

  const queueViewportUpdate = useCallback((updater: (current: typeof viewport) => typeof viewport) => {
    const baseViewport = pendingViewportRef.current ?? viewportRef.current
    pendingViewportRef.current = updater(baseViewport)
    if (viewportFrameRef.current !== null) return
    viewportFrameRef.current = window.requestAnimationFrame(flushViewportUpdate)
  }, [flushViewportUpdate])

  useEffect(() => {
    const el = containerRef.current
    if (!el) return
    const obs = new ResizeObserver((entries) => {
      for (const e of entries) {
        setSize({ w: Math.round(e.contentRect.width), h: Math.round(e.contentRect.height) })
      }
    })
    obs.observe(el)
    return () => obs.disconnect()
  }, [])

  // Tile cache: key "cx,cz,mode,sliceY,oreOverlay,ores" → reusable canvas surface
  const tileCache = useRef<Map<string, TileSurface>>(new Map())
  const macroTileCache = useRef<Map<string, TileSurface>>(new Map())

  // Invalidate cache when layer config changes
  const configKey = [
    layerConfig.mode,
    layerConfig.sliceY,
    layerConfig.oreOverlay,
    [...layerConfig.oreFilter].sort().join(',')
  ].join('|')

  useEffect(() => {
    tileCache.current.clear()
    macroTileCache.current.clear()
  }, [configKey])

  useEffect(() => {
    macroTileCache.current.clear()
  }, [chunks])

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return

    const handleWheel = (event: WheelEvent) => {
      event.preventDefault()
      const factor = event.deltaY < 0 ? 1.15 : 1 / 1.15
      queueViewportUpdate((currentViewport) => ({
        ...currentViewport,
        zoom: Math.max(0.5, Math.min(64, currentViewport.zoom * factor))
      }))
    }

    canvas.addEventListener('wheel', handleWheel, { passive: false })
    return () => canvas.removeEventListener('wheel', handleWheel)
  }, [queueViewportUpdate])

  useEffect(() => () => {
    if (hoverFrameRef.current !== null) {
      window.cancelAnimationFrame(hoverFrameRef.current)
    }
    if (viewportFrameRef.current !== null) {
      window.cancelAnimationFrame(viewportFrameRef.current)
    }
  }, [])

  // Rendering
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    const startedAt = performance.now()

    if (canvas.width !== size.w) canvas.width = size.w
    if (canvas.height !== size.h) canvas.height = size.h

    ctx.fillStyle = '#111'
    ctx.fillRect(0, 0, size.w, size.h)
    ctx.imageSmoothingEnabled = false

    const { offsetX, offsetZ, zoom } = viewport
    const pixelsPerBlock = zoom
    const pixelsPerChunk = CHUNK_SIZE * pixelsPerBlock

    // Which chunks are visible?
    const startBlockX = Math.floor(offsetX - size.w / 2 / pixelsPerBlock)
    const startBlockZ = Math.floor(offsetZ - size.h / 2 / pixelsPerBlock)
    const endBlockX = Math.ceil(offsetX + size.w / 2 / pixelsPerBlock)
    const endBlockZ = Math.ceil(offsetZ + size.h / 2 / pixelsPerBlock)

    const startChunkX = Math.floor(startBlockX / CHUNK_SIZE)
    const startChunkZ = Math.floor(startBlockZ / CHUNK_SIZE)
    const endChunkX = Math.ceil(endBlockX / CHUNK_SIZE)
    const endChunkZ = Math.ceil(endBlockZ / CHUNK_SIZE)
    const chunkSpan = getChunkSpan(pixelsPerChunk)
    const drawChunkSize = Math.ceil(pixelsPerChunk * chunkSpan)
    const previewSize = chunkSpan >= 8 ? 2 : chunkSpan >= 4 ? 4 : undefined
    const stats: RenderStats = { renderedTiles: 0, cacheHits: 0 }

    let visibleChunks = 0
    let drawnTiles = 0

    if (chunkSpan === 1) {
      for (let cz = startChunkZ; cz <= endChunkZ; cz++) {
        for (let cx = startChunkX; cx <= endChunkX; cx++) {
          const tileSurface = getChunkTileSurface(
            cx,
            cz,
            configKey,
            chunks,
            layerConfig,
            tileCache.current,
            stats
          )
          if (!tileSurface) continue
          visibleChunks += 1
          drawnTiles += 1

          const screenX = Math.round((cx * CHUNK_SIZE - offsetX) * pixelsPerBlock + size.w / 2)
          const screenZ = Math.round((cz * CHUNK_SIZE - offsetZ) * pixelsPerBlock + size.h / 2)
          const chunkPx = Math.ceil(pixelsPerChunk)

          ctx.drawImage(tileSurface, screenX, screenZ, chunkPx, chunkPx)
        }
      }
    } else {
      const macroStartX = Math.floor(startChunkX / chunkSpan)
      const macroStartZ = Math.floor(startChunkZ / chunkSpan)
      const macroEndX = Math.floor(endChunkX / chunkSpan)
      const macroEndZ = Math.floor(endChunkZ / chunkSpan)

      for (let macroZ = macroStartZ; macroZ <= macroEndZ; macroZ++) {
        for (let macroX = macroStartX; macroX <= macroEndX; macroX++) {
          const baseChunkX = macroX * chunkSpan
          const baseChunkZ = macroZ * chunkSpan
          const macroKey = `${baseChunkX},${baseChunkZ},${chunkSpan}|${configKey}`
          let macroSurface = macroTileCache.current.get(macroKey)

          if (!macroSurface) {
            const chunkTiles: Array<{ tileSurface: TileSurface; localX: number; localZ: number }> = []
            let hasAnyChunk = false
            for (let localZ = 0; localZ < chunkSpan; localZ++) {
              for (let localX = 0; localX < chunkSpan; localX++) {
                const chunkX = baseChunkX + localX
                const chunkZ = baseChunkZ + localZ
                if (chunks.has(`${chunkX},${chunkZ}`)) {
                  visibleChunks += 1
                }
                const tileSurface = getChunkTileSurface(
                  chunkX,
                  chunkZ,
                  configKey,
                  chunks,
                  layerConfig,
                  tileCache.current,
                  stats,
                  previewSize
                )
                if (!tileSurface) continue
                hasAnyChunk = true
                chunkTiles.push({ tileSurface, localX, localZ })
              }
            }

            if (!hasAnyChunk) {
              continue
            }

            macroSurface = createMacroTileSurface(chunkSpan * (previewSize ? previewSize / CHUNK_SIZE : 1))
            const macroCtx = macroSurface.getContext('2d')
            if (!macroCtx) continue
            const tileStep = previewSize ?? CHUNK_SIZE
            macroCtx.clearRect(0, 0, chunkSpan * tileStep, chunkSpan * tileStep)
            macroCtx.imageSmoothingEnabled = false
            for (const chunkTile of chunkTiles) {
              macroCtx.drawImage(
                chunkTile.tileSurface,
                chunkTile.localX * tileStep,
                chunkTile.localZ * tileStep,
                tileStep,
                tileStep
              )
            }
            macroTileCache.current.set(macroKey, macroSurface)
          } else {
            stats.cacheHits += 1
            for (let localZ = 0; localZ < chunkSpan; localZ++) {
              for (let localX = 0; localX < chunkSpan; localX++) {
                if (chunks.has(`${baseChunkX + localX},${baseChunkZ + localZ}`)) {
                  visibleChunks += 1
                }
              }
            }
          }

          drawnTiles += 1
          const screenX = Math.round((baseChunkX * CHUNK_SIZE - offsetX) * pixelsPerBlock + size.w / 2)
          const screenZ = Math.round((baseChunkZ * CHUNK_SIZE - offsetZ) * pixelsPerBlock + size.h / 2)
          ctx.drawImage(macroSurface, screenX, screenZ, drawChunkSize, drawChunkSize)
        }
      }
    }

    // Grid lines at high zoom
    if (zoom >= 8) {
      ctx.strokeStyle = 'rgba(255,255,255,0.08)'
      ctx.lineWidth = 1
      for (let cx = startChunkX; cx <= endChunkX; cx++) {
        const sx = Math.round((cx * CHUNK_SIZE - offsetX) * pixelsPerBlock + size.w / 2)
        ctx.beginPath()
        ctx.moveTo(sx, 0)
        ctx.lineTo(sx, size.h)
        ctx.stroke()
      }
      for (let cz = startChunkZ; cz <= endChunkZ; cz++) {
        const sz = Math.round((cz * CHUNK_SIZE - offsetZ) * pixelsPerBlock + size.h / 2)
        ctx.beginPath()
        ctx.moveTo(0, sz)
        ctx.lineTo(size.w, sz)
        ctx.stroke()
      }
    }

    // Crosshair at origin
    ctx.strokeStyle = 'rgba(255,60,60,0.6)'
    ctx.lineWidth = 1
    const ox = Math.round((0 - offsetX) * pixelsPerBlock + size.w / 2)
    const oz = Math.round((0 - offsetZ) * pixelsPerBlock + size.h / 2)
    ctx.beginPath(); ctx.moveTo(ox - 8, oz); ctx.lineTo(ox + 8, oz); ctx.stroke()
    ctx.beginPath(); ctx.moveTo(ox, oz - 8); ctx.lineTo(ox, oz + 8); ctx.stroke()

    renderPerf.sample(performance.now() - startedAt, () => ({
      visibleChunks,
      drawnTiles,
      renderedTiles: stats.renderedTiles,
      cacheHits: stats.cacheHits,
      chunkSpan,
      zoom: Number(zoom.toFixed(2)),
      canvas: `${size.w}x${size.h}`,
      cacheSize: tileCache.current.size,
        macroCacheSize: macroTileCache.current.size,
        previewSize: previewSize ?? 16
    }))

    if (perfEnabled() && stats.renderedTiles > 0) {
      console.log('[perf][render-cache]', {
        renderedTiles: stats.renderedTiles,
        visibleChunks,
        drawnTiles,
        chunkSpan,
        previewSize: previewSize ?? 16,
        config: configKey
      })
    }

  }, [chunks, viewport, layerConfig, configKey, size])

  // Pan with mouse drag
  const dragging = useRef(false)
  const lastMouse = useRef({ x: 0, y: 0 })

  const onMouseDown = useCallback((e: React.MouseEvent) => {
    dragging.current = true
    lastMouse.current = { x: e.clientX, y: e.clientY }
  }, [])

  const onMouseMove = useCallback((e: React.MouseEvent) => {
    if (dragging.current) {
      const dx = e.clientX - lastMouse.current.x
      const dy = e.clientY - lastMouse.current.y
      lastMouse.current = { x: e.clientX, y: e.clientY }
      queueViewportUpdate((currentViewport) => ({
        ...currentViewport,
        offsetX: currentViewport.offsetX - dx / currentViewport.zoom,
        offsetZ: currentViewport.offsetZ - dy / currentViewport.zoom
      }))
    }

    // Hover info
    const rect = canvasRef.current?.getBoundingClientRect()
    if (!rect) return
    const { offsetX, offsetZ, zoom } = useStore.getState().viewport
    const blockX = Math.floor(offsetX + (e.clientX - rect.left - size.w / 2) / zoom)
    const blockZ = Math.floor(offsetZ + (e.clientY - rect.top - size.h / 2) / zoom)
    const chunkX = Math.floor(blockX / CHUNK_SIZE)
    const chunkZ = Math.floor(blockZ / CHUNK_SIZE)
    const chunk = useStore.getState().chunks.get(`${chunkX},${chunkZ}`)
    if (chunk) {
      const lx = ((blockX % CHUNK_SIZE) + CHUNK_SIZE) % CHUNK_SIZE
      const lz = ((blockZ % CHUNK_SIZE) + CHUNK_SIZE) % CHUNK_SIZE
      const y = findDisplayY(chunk, layerConfig, lx, lz)
      const name = getBlock(chunk, lx, y, lz)
      queueHoverUpdate({ x: blockX, y, z: blockZ, name })
    } else {
      queueHoverUpdate(null)
    }
  }, [size, layerConfig, queueHoverUpdate, queueViewportUpdate])

  const onMouseUp = useCallback(() => { dragging.current = false }, [])
  const onMouseLeave = useCallback(() => {
    dragging.current = false
    queueHoverUpdate(null)
  }, [queueHoverUpdate])

  return (
    <div ref={containerRef} style={styles.container}>
      <canvas
        ref={canvasRef}
        style={styles.canvas}
        onMouseDown={onMouseDown}
        onMouseMove={onMouseMove}
        onMouseUp={onMouseUp}
        onMouseLeave={onMouseLeave}
      />
      <div style={styles.coords}>
        {`zoom: ${viewport.zoom.toFixed(1)}x`}
      </div>
    </div>
  )
}

const styles: Record<string, React.CSSProperties> = {
  container: {
    flex: 1,
    position: 'relative',
    overflow: 'hidden',
    background: '#111'
  },
  canvas: {
    display: 'block',
    width: '100%',
    height: '100%',
    cursor: 'grab'
  },
  coords: {
    position: 'absolute',
    bottom: 8,
    right: 8,
    background: 'rgba(0,0,0,0.6)',
    color: '#aaa',
    fontSize: 11,
    padding: '2px 6px',
    borderRadius: 3,
    fontFamily: 'monospace',
    pointerEvents: 'none'
  }
}
