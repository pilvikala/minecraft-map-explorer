import pako from 'pako'

export interface RawChunk {
  regionX: number
  regionZ: number
  chunkX: number  // global chunk X
  chunkZ: number  // global chunk Z
  data: Uint8Array // decompressed NBT bytes
}

export function parseRegionFile(buffer: ArrayBuffer, filename: string): RawChunk[] {
  // filename like "r.0.0.mca" or "r.-1.2.mca"
  const parts = filename.replace('.mca', '').split('.')
  const regionX = parseInt(parts[1])
  const regionZ = parseInt(parts[2])

  if (!Number.isFinite(regionX) || !Number.isFinite(regionZ)) {
    console.error(`Invalid region filename: ${filename}`)
    return []
  }

  const view = new DataView(buffer)
  const chunks: RawChunk[] = []

  if (buffer.byteLength < 8192) {
    console.error(`Region file too small (${buffer.byteLength} bytes), expected at least 8192`)
    return []
  }

  for (let i = 0; i < 1024; i++) {
    const localX = i % 32
    const localZ = Math.floor(i / 32)
    const offsetEntry = view.getUint32(i * 4, false)
    const sectorOffset = (offsetEntry >> 8) & 0xffffff
    const sectorCount = offsetEntry & 0xff

    if (sectorOffset === 0 || sectorCount === 0) continue

    const byteOffset = sectorOffset * 4096
    
    // Validate that the offset is within the buffer
    if (byteOffset + 5 > buffer.byteLength) {
      console.warn(`Region offset out of bounds: sector ${sectorOffset} (offset ${byteOffset}) in region (${regionX}, ${regionZ})`)
      continue
    }

    const length = view.getUint32(byteOffset, false)
    const compression = view.getUint8(byteOffset + 4)

    // Validate length
    if (length < 1 || byteOffset + 5 + length > buffer.byteLength) {
      console.warn(`Invalid chunk length ${length} at sector ${sectorOffset} in region (${regionX}, ${regionZ})`)
      continue
    }

    const compressed = new Uint8Array(buffer, byteOffset + 5, length - 1)

    let decompressed: Uint8Array
    try {
      if (compression === 1) {
        decompressed = pako.ungzip(compressed)
      } else if (compression === 2) {
        decompressed = pako.inflate(compressed)
      } else if (compression === 3) {
        decompressed = compressed.slice()
      } else {
        console.warn(`Unknown compression type ${compression} for chunk at (${regionX * 32 + localX}, ${regionZ * 32 + localZ})`)
        continue
      }
    } catch (e) {
      console.warn(`Failed to decompress chunk at (${regionX * 32 + localX}, ${regionZ * 32 + localZ}): ${e}`)
      continue
    }

    chunks.push({
      regionX,
      regionZ,
      chunkX: regionX * 32 + localX,
      chunkZ: regionZ * 32 + localZ,
      data: decompressed
    })
  }

  return chunks
}
