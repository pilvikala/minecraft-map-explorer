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

  const view = new DataView(buffer)
  const chunks: RawChunk[] = []

  for (let i = 0; i < 1024; i++) {
    const localX = i % 32
    const localZ = Math.floor(i / 32)
    const offsetEntry = view.getUint32(i * 4, false)
    const sectorOffset = (offsetEntry >> 8) & 0xffffff
    const sectorCount = offsetEntry & 0xff

    if (sectorOffset === 0 || sectorCount === 0) continue

    const byteOffset = sectorOffset * 4096
    const length = view.getUint32(byteOffset, false)
    const compression = view.getUint8(byteOffset + 4)

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
        continue
      }
    } catch {
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
