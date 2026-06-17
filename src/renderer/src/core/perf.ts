const PERF_ENABLED = import.meta.env.DEV

type DetailFactory = () => Record<string, unknown>

interface PerfTrackerOptions {
  logEvery?: number
  warnAboveMs?: number
}

export function perfEnabled(): boolean {
  return PERF_ENABLED
}

export function createPerfTracker(name: string, options: PerfTrackerOptions = {}) {
  const logEvery = options.logEvery ?? 20
  const warnAboveMs = options.warnAboveMs ?? Infinity

  let count = 0
  let total = 0
  let max = 0

  return {
    sample(ms: number, getDetails?: DetailFactory): void {
      if (!PERF_ENABLED) return

      count += 1
      total += ms
      if (ms > max) max = ms

      const shouldWarn = ms >= warnAboveMs
      const shouldLogSummary = count % logEvery === 0

      if (!shouldWarn && !shouldLogSummary) return

      const details = getDetails?.() ?? {}
      const prefix = shouldWarn ? `[perf][${name}][slow]` : `[perf][${name}]`
      console.log(prefix, {
        sample: count,
        ms: Number(ms.toFixed(2)),
        avgMs: Number((total / count).toFixed(2)),
        maxMs: Number(max.toFixed(2)),
        ...details
      })
    }
  }
}

export function roundMs(ms: number): number {
  return Number(ms.toFixed(2))
}