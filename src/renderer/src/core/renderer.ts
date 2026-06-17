import { ChunkData, getBlock, getBiome, CHUNK_HEIGHT, Y_OFFSET } from './chunk'
import {
  getBlockColor,
  getBiomeColor,
  getHeightColor,
  ORE_BLOCKS,
  AIR_BLOCKS,
  TRANSPARENT_BLOCKS
} from './colors'

export type LayerMode = 'surface' | 'slice' | 'heightmap' | 'cave' | 'biome'

export interface LayerConfig {
  mode: LayerMode
  sliceY: number          // used for 'slice' mode
  oreFilter: Set<string>  // ore block names to highlight (empty = none)
  oreOverlay: boolean     // if true, overlay ores on top of current mode
}

/** Returns a 16×16 pixel ImageData for the given chunk under the current layer config */
export function renderChunk(chunk: ChunkData, config: LayerConfig): ImageData {
  const img = new ImageData(16, 16)
  const data = img.data

  for (let lz = 0; lz < 16; lz++) {
    for (let lx = 0; lx < 16; lx++) {
      const pixelIdx = (lz * 16 + lx) * 4
      const [r, g, b] = getChunkPixelColor(chunk, config, lx, lz)
      const a = 255

      data[pixelIdx] = r
      data[pixelIdx + 1] = g
      data[pixelIdx + 2] = b
      data[pixelIdx + 3] = a
    }
  }

  return img
}

export function renderChunkPreview(chunk: ChunkData, config: LayerConfig, size: number): ImageData {
  const img = new ImageData(size, size)
  const data = img.data
  const step = 16 / size

  for (let pz = 0; pz < size; pz++) {
    for (let px = 0; px < size; px++) {
      const sampleX = Math.min(15, Math.floor(px * step + step / 2))
      const sampleZ = Math.min(15, Math.floor(pz * step + step / 2))
      const pixelIdx = (pz * size + px) * 4
      const [r, g, b] = getChunkPixelColor(chunk, config, sampleX, sampleZ)
      data[pixelIdx] = r
      data[pixelIdx + 1] = g
      data[pixelIdx + 2] = b
      data[pixelIdx + 3] = 255
    }
  }

  return img
}

function getChunkPixelColor(chunk: ChunkData, config: LayerConfig, lx: number, lz: number): [number, number, number] {
  let r = 0
  let g = 0
  let b = 0

  const blockAtY = (y: number) => getBlock(chunk, lx, y, lz)

  if (config.mode === 'surface') {
    const [sr, sg, sb] = getSurfaceColor(chunk, lx, lz)
    r = sr; g = sg; b = sb

  } else if (config.mode === 'slice') {
    const name = blockAtY(config.sliceY)
    const [cr, cg, cb] = getBlockColor(name)
    r = cr; g = cg; b = cb

  } else if (config.mode === 'heightmap') {
    const hy = findSurfaceY(chunk, lx, lz)
    const [cr, cg, cb] = getHeightColor(hy)
    r = cr; g = cg; b = cb

  } else if (config.mode === 'cave') {
    const [cr, cg, cb] = getCaveColor(chunk, lx, lz)
    r = cr; g = cg; b = cb

  } else if (config.mode === 'biome') {
    const sy = findSurfaceY(chunk, lx, lz)
    const biomeName = getBiome(chunk, lx, sy, lz)
    const [cr, cg, cb] = getBiomeColor(biomeName)
    r = cr; g = cg; b = cb
  }

  if (config.oreOverlay && config.oreFilter.size > 0) {
    const surfaceY = findSurfaceY(chunk, lx, lz)
    for (let y = surfaceY; y >= -Y_OFFSET; y--) {
      const name = blockAtY(y)
      if (config.oreFilter.has(name) && ORE_BLOCKS[name]) {
        const [or, og, ob] = ORE_BLOCKS[name]
        r = Math.round(or * 0.8 + r * 0.2)
        g = Math.round(og * 0.8 + g * 0.2)
        b = Math.round(ob * 0.8 + b * 0.2)
        break
      }
    }
  }

  return [r, g, b]
  return img
}

function findSurfaceY(chunk: ChunkData, lx: number, lz: number): number {
  const maxY = CHUNK_HEIGHT - Y_OFFSET - 1
  for (let y = maxY; y >= -Y_OFFSET; y--) {
    const name = getBlock(chunk, lx, y, lz)
    if (!TRANSPARENT_BLOCKS.has(name) && !AIR_BLOCKS.has(name)) return y
  }
  return -Y_OFFSET
}

function getSurfaceColor(chunk: ChunkData, lx: number, lz: number): [number, number, number] {
  const surfaceY = findSurfaceY(chunk, lx, lz)
  const name = getBlock(chunk, lx, surfaceY, lz)
  // simple shading: blocks slightly above average get lighter
  const shade = Math.min(1.2, Math.max(0.5, 0.7 + (surfaceY / 200)))
  const [r, g, b] = getBlockColor(name)
  return [Math.round(r * shade), Math.round(g * shade), Math.round(b * shade)]
}

function getCaveColor(chunk: ChunkData, lx: number, lz: number): [number, number, number] {
  const surfaceY = findSurfaceY(chunk, lx, lz)
  // Find highest air pocket below the surface
  for (let y = surfaceY - 1; y >= -Y_OFFSET; y--) {
    const name = getBlock(chunk, lx, y, lz)
    if (AIR_BLOCKS.has(name) || name === 'minecraft:cave_air') {
      // Color the block just below the air (cave floor)
      const floorName = getBlock(chunk, lx, y - 1, lz)
      return getBlockColor(floorName) as [number, number, number]
    }
  }
  // No cave found — show surface
  const [r, g, b] = getBlockColor(getBlock(chunk, lx, surfaceY, lz))
  return [Math.round(r * 0.3), Math.round(g * 0.3), Math.round(b * 0.3)]
}
