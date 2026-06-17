/**
 * Minecraft world discovery for all common installation types.
 *
 * Each "finder" knows how to locate the saves/ directories for one
 * installer/launcher. discoverWorlds() collects them all.
 */

import { join } from 'path'
import { existsSync, readdirSync, statSync, readFileSync } from 'fs'
import { gunzipSync } from 'zlib'
import os from 'os'

// ─── Types ────────────────────────────────────────────────────────────────────

export interface WorldInfo {
  name: string          // LevelName from level.dat, or folder name
  folderName: string    // directory name under saves/
  path: string          // absolute path to the world root
  regionDir: string     // absolute path to <world>/region/
  lastModified: number  // mtime of level.dat in ms
  regionCount: number   // number of .mca files in region/
  source: string        // human-readable installation label
}

interface SavesSource {
  savesDir: string
  label: string         // e.g. "Snap (mc-installer)", "Official Launcher"
}

// ─── Filesystem helpers ───────────────────────────────────────────────────────

const HOME = os.homedir()

function isDir(p: string): boolean {
  try { return statSync(p).isDirectory() } catch { return false }
}

function listDirs(dir: string): string[] {
  try {
    return readdirSync(dir).filter((entry) => isDir(join(dir, entry)))
  } catch { return [] }
}

// ─── Snap ─────────────────────────────────────────────────────────────────────
//
// Layout: ~/snap/<name>/<revision>/.minecraft/saves/
//   <revision> is a number; "current" is always a symlink to the active one.
//
// Strategy: scan ~/snap/ for any directory that looks like a Minecraft install
// (contains .minecraft/saves/ under current/ or the highest numeric revision).

function snapSources(): SavesSource[] {
  const snapBase = join(HOME, 'snap')
  if (!isDir(snapBase)) return []

  const sources: SavesSource[] = []

  for (const snapName of listDirs(snapBase)) {
    const packageDir = join(snapBase, snapName)

    // Try the "current" symlink first — it always resolves to the active revision.
    const currentSaves = join(packageDir, 'current', '.minecraft', 'saves')
    if (isDir(currentSaves)) {
      sources.push({ savesDir: currentSaves, label: `Snap (${snapName})` })
      continue
    }

    // Fallback: pick the highest numeric revision (in case current/ is absent).
    const latestRev = listDirs(packageDir)
      .filter((d) => /^\d+$/.test(d))
      .sort((a, b) => Number(b) - Number(a))[0]

    if (latestRev) {
      const revSaves = join(packageDir, latestRev, '.minecraft', 'saves')
      if (isDir(revSaves)) {
        sources.push({ savesDir: revSaves, label: `Snap (${snapName} rev ${latestRev})` })
      }
    }
  }

  return sources
}

// ─── Flatpak ──────────────────────────────────────────────────────────────────
//
// Layout: ~/.var/app/<app-id>/data/minecraft/saves/

const FLATPAK_APP_IDS = [
  { id: 'com.mojang.Minecraft',       label: 'Flatpak (com.mojang.Minecraft)' },
  { id: 'com.mojangsales.Minecraft',  label: 'Flatpak (com.mojangsales.Minecraft)' },
]

function flatpakSources(): SavesSource[] {
  return FLATPAK_APP_IDS.flatMap(({ id, label }) => {
    const saves = join(HOME, '.var', 'app', id, 'data', 'minecraft', 'saves')
    return isDir(saves) ? [{ savesDir: saves, label }] : []
  })
}

// ─── Official launcher ────────────────────────────────────────────────────────

function officialSources(): SavesSource[] {
  const candidates: Array<{ savesDir: string; label: string }> = []

  if (process.platform === 'win32') {
    const appdata = process.env['APPDATA'] || join(HOME, 'AppData', 'Roaming')
    candidates.push({
      savesDir: join(appdata, '.minecraft', 'saves'),
      label: 'Official Launcher'
    })
  } else if (process.platform === 'darwin') {
    candidates.push({
      savesDir: join(HOME, 'Library', 'Application Support', 'minecraft', 'saves'),
      label: 'Official Launcher'
    })
  } else {
    candidates.push({
      savesDir: join(HOME, '.minecraft', 'saves'),
      label: 'Official Launcher'
    })
  }

  return candidates.filter(({ savesDir }) => isDir(savesDir))
}

// ─── Instance-based launchers (MultiMC / Prism / ATLauncher …) ───────────────
//
// Layout: <base>/instances/<instance-name>/.minecraft/saves/

interface InstanceLauncher {
  label: string
  base: string
}

function instanceLaunchers(): InstanceLauncher[] {
  return [
    { label: 'Prism Launcher',          base: join(HOME, '.local', 'share', 'PrismLauncher', 'instances') },
    { label: 'Prism Launcher (Flatpak)', base: join(HOME, '.var', 'app', 'org.prismlauncher.PrismLauncher', 'data', 'PrismLauncher', 'instances') },
    { label: 'MultiMC',                 base: join(HOME, '.local', 'share', 'multimc', 'instances') },
    { label: 'MultiMC (Flatpak)',       base: join(HOME, '.var', 'app', 'org.multimc.MultiMC', 'data', 'multimc', 'instances') },
    { label: 'ATLauncher',              base: join(HOME, '.local', 'share', 'ATLauncher', 'instances') },
    { label: 'GDLauncher Carbon',       base: join(HOME, '.local', 'share', 'gdlauncher_carbon', 'instances') },
  ]
}

function instanceSources(): SavesSource[] {
  const sources: SavesSource[] = []
  for (const { label, base } of instanceLaunchers()) {
    if (!isDir(base)) continue
    for (const instance of listDirs(base)) {
      const saves = join(base, instance, '.minecraft', 'saves')
      if (isDir(saves)) {
        sources.push({ savesDir: saves, label: `${label} — ${instance}` })
      }
    }
  }
  return sources
}

// ─── level.dat name extraction ────────────────────────────────────────────────

function readLevelName(worldPath: string, fallback: string): string {
  try {
    const compressed = readFileSync(join(worldPath, 'level.dat'))
    const nbt = gunzipSync(compressed)
    const needle = 'LevelName'
    for (let i = 0; i < nbt.length - needle.length - 5; i++) {
      if (nbt[i] !== 8) continue // TAG_String
      const nameLen = (nbt[i + 1] << 8) | nbt[i + 2]
      if (nameLen !== needle.length) continue
      if (nbt.slice(i + 3, i + 3 + nameLen).toString('utf8') !== needle) continue
      const valStart = i + 3 + nameLen
      const valLen = (nbt[valStart] << 8) | nbt[valStart + 1]
      return nbt.slice(valStart + 2, valStart + 2 + valLen).toString('utf8')
    }
  } catch { /* ignore */ }
  return fallback
}

// ─── Region directory detection ───────────────────────────────────────────────
//
// Two world layouts exist:
//   Classic (≤1.17):  <world>/region/*.mca
//   Dimensions (1.18+): <world>/dimensions/<namespace>/<dim>/region/*.mca
//
// We always return the overworld region dir, preferring classic then modern.

function hasMcaFiles(dir: string): boolean {
  try { return readdirSync(dir).some((f) => f.endsWith('.mca')) } catch { return false }
}

function countMcaFiles(dir: string): number {
  try { return readdirSync(dir).filter((f) => f.endsWith('.mca')).length } catch { return 0 }
}

function findOverworldRegionDir(worldPath: string): string | null {
  // 1. Classic layout: <world>/region/
  const classic = join(worldPath, 'region')
  if (isDir(classic) && hasMcaFiles(classic)) return classic

  // 2. Standard dimensions layout: <world>/dimensions/minecraft/overworld/region/
  const modern = join(worldPath, 'dimensions', 'minecraft', 'overworld', 'region')
  if (isDir(modern) && hasMcaFiles(modern)) return modern

  // 3. Any namespace/dimension with region files (custom dimensions / modded)
  const dimBase = join(worldPath, 'dimensions')
  if (isDir(dimBase)) {
    for (const ns of listDirs(dimBase)) {
      for (const dim of listDirs(join(dimBase, ns))) {
        const r = join(dimBase, ns, dim, 'region')
        if (isDir(r) && hasMcaFiles(r)) return r
      }
    }
  }

  return null
}

// ─── Public API ───────────────────────────────────────────────────────────────

/** Collect saves/ directories from all known installation types on this OS. */
export function findSavesDirs(): SavesSource[] {
  if (process.platform === 'linux') {
    return [
      ...officialSources(),
      ...snapSources(),
      ...flatpakSources(),
      ...instanceSources(),
    ]
  }
  return [
    ...officialSources(),
    ...instanceSources(),
  ]
}

/** Scan all discovered saves/ directories and return a flat, sorted world list. */
export function discoverWorlds(): WorldInfo[] {
  const worlds: WorldInfo[] = []
  const seen = new Set<string>()

  for (const { savesDir, label } of findSavesDirs()) {
    for (const folderName of listDirs(savesDir)) {
      const worldPath = join(savesDir, folderName)
      if (seen.has(worldPath)) continue
      seen.add(worldPath)

      const regionDir = findOverworldRegionDir(worldPath)
      if (!regionDir) continue

      const levelDatPath = join(worldPath, 'level.dat')
      const lastModified = existsSync(levelDatPath)
        ? statSync(levelDatPath).mtimeMs
        : statSync(worldPath).mtimeMs

      worlds.push({
        name: readLevelName(worldPath, folderName),
        folderName,
        path: worldPath,
        regionDir,
        lastModified,
        regionCount: countMcaFiles(regionDir),
        source: label,
      })
    }
  }

  return worlds.sort((a, b) => b.lastModified - a.lastModified)
}
