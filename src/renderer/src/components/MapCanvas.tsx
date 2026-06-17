import React, { useRef, useEffect, useCallback, useState } from 'react'
import { useStore } from '../store'
import { renderChunk } from '../core/renderer'
import { getBlock } from '../core/chunk'

const CHUNK_SIZE = 16

export default function MapCanvas(): React.ReactElement {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const { chunks, viewport, layerConfig, setViewport, setHoveredBlock } = useStore()

  // Track canvas size
  const [size, setSize] = useState({ w: 800, h: 600 })

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

  // Tile cache: key "cx,cz,mode,sliceY,oreOverlay,ores" → ImageData
  const tileCache = useRef<Map<string, ImageData>>(new Map())

  // Invalidate cache when layer config changes
  const configKey = [
    layerConfig.mode,
    layerConfig.sliceY,
    layerConfig.oreOverlay,
    [...layerConfig.oreFilter].sort().join(',')
  ].join('|')

  useEffect(() => {
    tileCache.current.clear()
  }, [configKey])

  // Rendering
  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    canvas.width = size.w
    canvas.height = size.h

    ctx.fillStyle = '#111'
    ctx.fillRect(0, 0, size.w, size.h)

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

    for (let cz = startChunkZ; cz <= endChunkZ; cz++) {
      for (let cx = startChunkX; cx <= endChunkX; cx++) {
        const key = `${cx},${cz}`
        const chunk = chunks.get(key)
        if (!chunk) continue

        const tileKey = `${key}|${configKey}`
        let img = tileCache.current.get(tileKey)
        if (!img) {
          img = renderChunk(chunk, layerConfig)
          tileCache.current.set(tileKey, img)
        }

        // Convert chunk world coords → screen coords
        const screenX = Math.round((cx * CHUNK_SIZE - offsetX) * pixelsPerBlock + size.w / 2)
        const screenZ = Math.round((cz * CHUNK_SIZE - offsetZ) * pixelsPerBlock + size.h / 2)
        const chunkPx = Math.ceil(pixelsPerChunk)

        // Draw scaled tile
        const offscreen = new OffscreenCanvas(16, 16)
        const octx = offscreen.getContext('2d')!
        octx.putImageData(img, 0, 0)

        ctx.imageSmoothingEnabled = false
        ctx.drawImage(offscreen, screenX, screenZ, chunkPx, chunkPx)
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
      setViewport({
        offsetX: useStore.getState().viewport.offsetX - dx / useStore.getState().viewport.zoom,
        offsetZ: useStore.getState().viewport.offsetZ - dy / useStore.getState().viewport.zoom
      })
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
      const y = layerConfig.mode === 'slice' ? layerConfig.sliceY : 64
      const name = getBlock(chunk, lx, y, lz)
      setHoveredBlock({ x: blockX, z: blockZ, name })
    } else {
      setHoveredBlock(null)
    }
  }, [size, layerConfig, setHoveredBlock, setViewport])

  const onMouseUp = useCallback(() => { dragging.current = false }, [])
  const onMouseLeave = useCallback(() => { dragging.current = false; setHoveredBlock(null) }, [setHoveredBlock])

  const onWheel = useCallback((e: React.WheelEvent) => {
    e.preventDefault()
    const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15
    setViewport({ zoom: Math.max(0.5, Math.min(64, useStore.getState().viewport.zoom * factor)) })
  }, [setViewport])

  return (
    <div ref={containerRef} style={styles.container}>
      <canvas
        ref={canvasRef}
        style={styles.canvas}
        onMouseDown={onMouseDown}
        onMouseMove={onMouseMove}
        onMouseUp={onMouseUp}
        onMouseLeave={onMouseLeave}
        onWheel={onWheel}
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
