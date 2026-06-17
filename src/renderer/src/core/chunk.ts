import { parseNbt, NbtCompound, NbtValue } from './nbt'

export interface ChunkData {
  chunkX: number
  chunkZ: number
  // blocks[localX][y + 64][localZ] = block name string index in palette
  blocks: Uint16Array  // 16 * 384 * 16 = 98304 entries; y offset by 64
  palette: string[]    // block names, index 0 = air
  // biomes[biomeX][biomeY][biomeZ] = biome name, 4x4x4 grid per section, 4x24x4 total
  biomes: string[][]   // flat [biomeIdx] per (bx,by,bz) mapped via getBiome()
  biomePalette: string[]
  biomeIndices: Uint8Array // 4*96*4 entries (4 biome cells per 16 blocks * 384 height / 4)
}

const CHUNK_HEIGHT = 384  // -64 to +319
const Y_OFFSET = 64       // shift so Y=-64 maps to index 0

function asNumber(v: NbtValue): number { return v as number }
function asCompound(v: NbtValue): NbtCompound { return v as NbtCompound }
function asList(v: NbtValue): NbtValue[] { return v as NbtValue[] }
function asBigIntArray(v: NbtValue): bigint[] { return v as bigint[] }
function asString(v: NbtValue): string { return v as string }

function createEmptyChunk(chunkX: number, chunkZ: number): ChunkData {
  return {
    chunkX,
    chunkZ,
    blocks: new Uint16Array(16 * CHUNK_HEIGHT * 16),
    palette: ['minecraft:air'],
    biomePalette: ['minecraft:plains'],
    biomeIndices: new Uint8Array(4 * (CHUNK_HEIGHT / 4) * 4),
    biomes: []
  }
}

function decodePacked(
  data: bigint[],
  size: number,
  bitsPerEntry: number
): number[] {
  const valuesPerLong = Math.floor(64 / bitsPerEntry)
  const mask = (1n << BigInt(bitsPerEntry)) - 1n
  const result = new Array(size).fill(0)
  for (let i = 0; i < size; i++) {
    const longIdx = Math.floor(i / valuesPerLong)
    const bitIdx = (i % valuesPerLong) * bitsPerEntry
    if (longIdx < data.length) {
      result[i] = Number((data[longIdx] >> BigInt(bitIdx)) & mask)
    }
  }
  return result
}

export function decodeChunk(raw: Uint8Array, chunkX: number, chunkZ: number): ChunkData {
  let nbt: NbtCompound
  try {
    nbt = parseNbt(raw)
  } catch (e) {
    console.error(`Failed to parse NBT for chunk (${chunkX}, ${chunkZ}):`, e)
    return createEmptyChunk(chunkX, chunkZ)
  }

  const blockPalette: string[] = ['minecraft:air']
  const paletteIndex = new Map<string, number>()
  paletteIndex.set('minecraft:air', 0)

  const blocks = new Uint16Array(16 * CHUNK_HEIGHT * 16)

  const biomePaletteArr: string[] = ['minecraft:plains']
  const biomeIndex = new Map<string, number>()
  biomeIndex.set('minecraft:plains', 0)
  const biomeData = new Uint8Array(4 * (CHUNK_HEIGHT / 4) * 4)

  // Handle both flat structure (1.18+) and wrapped Level structure (older versions)
  let chunkData: NbtCompound = nbt
  
  // Try to find the actual chunk data
  if (!nbt['sections'] && nbt['Level']) {
    chunkData = asCompound(nbt['Level'])
  }
  
  // If still no sections, try looking for it differently
  if (!chunkData['sections']) {
    console.warn(`Chunk (${chunkX}, ${chunkZ}) has no 'sections'. Root keys: ${Object.keys(nbt).join(', ')}, ChunkData keys: ${Object.keys(chunkData).join(', ')}`)
    return createEmptyChunk(chunkX, chunkZ)
  }

  const sections = asList(chunkData['sections'])

  for (const sectionVal of sections) {
    const section = asCompound(sectionVal)
    const sectionY = asNumber(section['Y'])
    const yBase = (sectionY * 16) + Y_OFFSET

    if (yBase < 0 || yBase >= CHUNK_HEIGHT) continue

    // --- block states ---
    const blockStates = section['block_states']
    if (blockStates) {
      const bs = asCompound(blockStates)
      const rawPalette = asList(bs['palette'] ?? [])
      
      if (rawPalette.length === 0) {
        console.warn(`Section Y=${sectionY} in chunk (${chunkX}, ${chunkZ}) has empty palette`)
        continue
      }

      const sectionPalette: number[] = rawPalette.map((entry, idx) => {
        try {
          const e = asCompound(entry)
          const name = asString(e['Name'])
          let pidx = paletteIndex.get(name)
          if (pidx === undefined) {
            pidx = blockPalette.length
            blockPalette.push(name)
            paletteIndex.set(name, pidx)
          }
          return pidx
        } catch (err) {
          console.error(`Failed to parse palette entry ${idx} in section Y=${sectionY}:`, entry, err)
          return 0
        }
      })

      if (rawPalette.length === 1) {
        // entire section is this block
        const globalIdx = sectionPalette[0]
        for (let y = 0; y < 16; y++) {
          for (let z = 0; z < 16; z++) {
            for (let x = 0; x < 16; x++) {
              blocks[x * CHUNK_HEIGHT * 16 + (yBase + y) * 16 + z] = globalIdx
            }
          }
        }
      } else if (bs['data']) {
        const dataArr = asBigIntArray(bs['data'])
        const bitsPerBlock = Math.max(4, Math.ceil(Math.log2(rawPalette.length)))
        const decoded = decodePacked(dataArr, 4096, bitsPerBlock)
        // Minecraft packs blocks as index = y*256 + z*16 + x
        for (let i = 0; i < 4096; i++) {
          const bx = i & 15
          const bz = (i >> 4) & 15
          const by = (i >> 8) & 15
          blocks[bx * CHUNK_HEIGHT * 16 + (yBase + by) * 16 + bz] = sectionPalette[decoded[i]] ?? 0
        }
      } else {
        console.warn(`Section Y=${sectionY} in chunk (${chunkX}, ${chunkZ}) has palette but no data`)
      }
    } else {
      console.debug(`Section Y=${sectionY} in chunk (${chunkX}, ${chunkZ}) has no block_states`)
    }

    // --- biomes ---
    const biomesTag = section['biomes']
    if (biomesTag) {
      const bm = asCompound(biomesTag)
      const rawBiomePalette = asList(bm['palette'] ?? [])
      const sectionBiomePalette: number[] = rawBiomePalette.map((b) => {
        const name = asString(b)
        let idx = biomeIndex.get(name)
        if (idx === undefined) {
          idx = biomePaletteArr.length
          biomePaletteArr.push(name)
          biomeIndex.set(name, idx)
        }
        return idx
      })

      // 4x4x4 biome cells per section; sectionY=-4 → biomeBase=0
      const biomeBase = (sectionY + 4) * 4
      if (rawBiomePalette.length === 1) {
        for (let by = 0; by < 4; by++) {
          for (let bz = 0; bz < 4; bz++) {
            for (let bx = 0; bx < 4; bx++) {
              const idx = bx * (CHUNK_HEIGHT / 4) * 4 + (biomeBase + by) * 4 + bz
              if (idx >= 0 && idx < biomeData.length) {
                biomeData[idx] = sectionBiomePalette[0]
              }
            }
          }
        }
      } else if (bm['data']) {
        const dataArr = asBigIntArray(bm['data'])
        const bitsPerBiome = Math.max(1, Math.ceil(Math.log2(rawBiomePalette.length)))
        const decoded = decodePacked(dataArr, 64, bitsPerBiome)
        // Minecraft biome index = y*16 + z*4 + x
        for (let i = 0; i < 64; i++) {
          const bx = i & 3
          const bz = (i >> 2) & 3
          const by = (i >> 4) & 3
          const idx = bx * (CHUNK_HEIGHT / 4) * 4 + (biomeBase + by) * 4 + bz
          if (idx >= 0 && idx < biomeData.length) {
            biomeData[idx] = sectionBiomePalette[decoded[i]] ?? 0
          }
        }
      }
    }
  }

  return {
    chunkX,
    chunkZ,
    blocks,
    palette: blockPalette,
    biomePalette: biomePaletteArr,
    biomeIndices: biomeData,
    biomes: []
  }
}

export function getBlock(chunk: ChunkData, lx: number, y: number, lz: number): string {
  const yi = y + Y_OFFSET
  if (lx < 0 || lx > 15 || yi < 0 || yi >= CHUNK_HEIGHT || lz < 0 || lz > 15) return 'minecraft:air'
  return chunk.palette[chunk.blocks[lx * CHUNK_HEIGHT * 16 + yi * 16 + lz]] ?? 'minecraft:air'
}

export function getBiome(chunk: ChunkData, lx: number, y: number, lz: number): string {
  const biomeY = Math.floor((y + Y_OFFSET) / 4)
  const bx = Math.floor(lx / 4)
  const bz = Math.floor(lz / 4)
  const totalBiomeY = CHUNK_HEIGHT / 4
  const idx = bx * totalBiomeY * 4 + biomeY * 4 + bz
  if (idx < 0 || idx >= chunk.biomeIndices.length) return 'minecraft:plains'
  return chunk.biomePalette[chunk.biomeIndices[idx]] ?? 'minecraft:plains'
}

export { CHUNK_HEIGHT, Y_OFFSET }
