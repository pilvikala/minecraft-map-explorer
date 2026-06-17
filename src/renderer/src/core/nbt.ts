// Minimal synchronous NBT parser for Java Edition 1.18+ chunk data.
// Supports all tag types needed for chunk parsing.

export type NbtValue =
  | number
  | bigint
  | string
  | number[]
  | bigint[]
  | Uint8Array
  | NbtCompound
  | NbtValue[]

export type NbtCompound = Record<string, NbtValue>

const TAG = {
  END: 0,
  BYTE: 1,
  SHORT: 2,
  INT: 3,
  LONG: 4,
  FLOAT: 5,
  DOUBLE: 6,
  BYTE_ARRAY: 7,
  STRING: 8,
  LIST: 9,
  COMPOUND: 10,
  INT_ARRAY: 11,
  LONG_ARRAY: 12
} as const

class Reader {
  private view: DataView
  offset = 0

  constructor(buf: Uint8Array) {
    this.view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength)
  }

  byte(): number { return this.view.getUint8(this.offset++) }
  int8(): number { return this.view.getInt8(this.offset++) }
  int16(): number { const v = this.view.getInt16(this.offset, false); this.offset += 2; return v }
  int32(): number { const v = this.view.getInt32(this.offset, false); this.offset += 4; return v }
  int64(): bigint { const v = this.view.getBigInt64(this.offset, false); this.offset += 8; return v }
  float32(): number { const v = this.view.getFloat32(this.offset, false); this.offset += 4; return v }
  float64(): number { const v = this.view.getFloat64(this.offset, false); this.offset += 8; return v }

  string(): string {
    const len = this.view.getUint16(this.offset, false)
    this.offset += 2
    const bytes = new Uint8Array(this.view.buffer, this.view.byteOffset + this.offset, len)
    this.offset += len
    return new TextDecoder('utf-8').decode(bytes)
  }

  bytes(n: number): Uint8Array {
    const out = new Uint8Array(this.view.buffer, this.view.byteOffset + this.offset, n)
    this.offset += n
    return out
  }
}

function readPayload(r: Reader, type: number): NbtValue {
  switch (type) {
    case TAG.BYTE: return r.int8()
    case TAG.SHORT: return r.int16()
    case TAG.INT: return r.int32()
    case TAG.LONG: return r.int64()
    case TAG.FLOAT: return r.float32()
    case TAG.DOUBLE: return r.float64()
    case TAG.BYTE_ARRAY: {
      const len = r.int32()
      return r.bytes(len)
    }
    case TAG.STRING: return r.string()
    case TAG.LIST: {
      const elemType = r.byte()
      const len = r.int32()
      const arr: NbtValue[] = []
      for (let i = 0; i < len; i++) arr.push(readPayload(r, elemType))
      return arr
    }
    case TAG.COMPOUND: {
      const obj: NbtCompound = {}
      while (true) {
        const tagType = r.byte()
        if (tagType === TAG.END) break
        const name = r.string()
        obj[name] = readPayload(r, tagType)
      }
      return obj
    }
    case TAG.INT_ARRAY: {
      const len = r.int32()
      const arr: number[] = []
      for (let i = 0; i < len; i++) arr.push(r.int32())
      return arr
    }
    case TAG.LONG_ARRAY: {
      const len = r.int32()
      const arr: bigint[] = []
      for (let i = 0; i < len; i++) arr.push(r.int64())
      return arr
    }
    default: throw new Error(`Unknown NBT tag type: ${type}`)
  }
}

export function parseNbt(data: Uint8Array): NbtCompound {
  const r = new Reader(data)
  const rootType = r.byte()
  r.string() // root name (usually empty)
  return readPayload(r, rootType) as NbtCompound
}
