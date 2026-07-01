// Block name → [r, g, b] color lookup

type RGB = [number, number, number]

const BLOCK_COLORS: Record<string, RGB> = {
  // Air / transparent
  'minecraft:air': [0, 0, 0],
  'minecraft:cave_air': [30, 30, 50],
  'minecraft:void_air': [0, 0, 0],
  'minecraft:water': [32, 100, 220],
  'minecraft:lava': [220, 80, 20],

  // Terrain
  'minecraft:grass_block': [86, 156, 55],
  'minecraft:dirt': [134, 96, 67],
  'minecraft:coarse_dirt': [115, 82, 57],
  'minecraft:podzol': [100, 68, 30],
  'minecraft:mycelium': [108, 96, 100],
  'minecraft:grass_path': [147, 116, 59],
  'minecraft:dirt_path': [147, 116, 59],
  'minecraft:farmland': [120, 85, 45],
  'minecraft:stone': [110, 110, 110],
  'minecraft:deepslate': [70, 70, 78],
  'minecraft:cobblestone': [120, 118, 114],
  'minecraft:mossy_cobblestone': [100, 118, 80],
  'minecraft:bedrock': [50, 50, 55],
  'minecraft:gravel': [130, 126, 118],
  'minecraft:sand': [220, 210, 150],
  'minecraft:red_sand': [190, 100, 50],
  'minecraft:sandstone': [210, 200, 130],
  'minecraft:red_sandstone': [180, 95, 45],
  'minecraft:clay': [160, 166, 180],

  // Grass / plants
  'minecraft:grass': [72, 140, 45],
  'minecraft:short_grass': [72, 140, 45],
  'minecraft:tall_grass': [72, 140, 45],
  'minecraft:fern': [60, 120, 40],
  'minecraft:large_fern': [60, 120, 40],
  'minecraft:dead_bush': [130, 100, 50],
  'minecraft:seagrass': [40, 130, 80],
  'minecraft:kelp': [30, 110, 60],
  'minecraft:kelp_plant': [30, 110, 60],
  'minecraft:vine': [50, 115, 40],
  'minecraft:leaf_litter': [95, 135, 55],
  'minecraft:bamboo': [95, 165, 60],

  // Flowers
  'minecraft:dandelion': [240, 220, 20],
  'minecraft:poppy': [200, 30, 30],
  'minecraft:blue_orchid': [60, 130, 220],
  'minecraft:allium': [150, 80, 200],
  'minecraft:sunflower': [230, 200, 30],
  'minecraft:pink_petals': [240, 180, 205],

  // Snow / ice
  'minecraft:snow': [240, 245, 255],
  'minecraft:snow_block': [235, 240, 255],
  'minecraft:ice': [170, 210, 240],
  'minecraft:packed_ice': [140, 185, 230],
  'minecraft:blue_ice': [100, 150, 220],
  'minecraft:frosted_ice': [160, 200, 235],
  'minecraft:powder_snow': [235, 240, 250],

  // Wood / logs / leaves
  'minecraft:oak_log': [120, 90, 55],
  'minecraft:spruce_log': [90, 65, 35],
  'minecraft:birch_log': [200, 195, 160],
  'minecraft:jungle_log': [100, 80, 45],
  'minecraft:acacia_log': [110, 85, 50],
  'minecraft:dark_oak_log': [60, 45, 25],
  'minecraft:mangrove_log': [95, 60, 40],
  'minecraft:oak_leaves': [60, 130, 30],
  'minecraft:spruce_leaves': [40, 90, 40],
  'minecraft:birch_leaves': [100, 160, 60],
  'minecraft:jungle_leaves': [50, 140, 30],
  'minecraft:acacia_leaves': [80, 150, 35],
  'minecraft:dark_oak_leaves': [45, 110, 25],
  'minecraft:mangrove_leaves': [55, 125, 30],
  'minecraft:azalea_leaves': [75, 135, 45],
  'minecraft:flowering_azalea_leaves': [90, 130, 60],
  'minecraft:cherry_leaves': [235, 170, 200],

  // Stone variants / ores
  'minecraft:coal_ore': [50, 50, 55],
  'minecraft:deepslate_coal_ore': [45, 45, 50],
  'minecraft:iron_ore': [170, 130, 110],
  'minecraft:deepslate_iron_ore': [140, 110, 100],
  'minecraft:copper_ore': [145, 105, 75],
  'minecraft:deepslate_copper_ore': [120, 90, 70],
  'minecraft:gold_ore': [220, 195, 40],
  'minecraft:deepslate_gold_ore': [190, 170, 35],
  'minecraft:redstone_ore': [180, 30, 20],
  'minecraft:deepslate_redstone_ore': [155, 25, 18],
  'minecraft:lapis_ore': [30, 60, 180],
  'minecraft:deepslate_lapis_ore': [25, 50, 155],
  'minecraft:diamond_ore': [50, 220, 210],
  'minecraft:deepslate_diamond_ore': [40, 190, 180],
  'minecraft:emerald_ore': [30, 200, 80],
  'minecraft:deepslate_emerald_ore': [25, 170, 65],
  'minecraft:nether_quartz_ore': [200, 200, 200],
  'minecraft:nether_gold_ore': [210, 170, 30],
  'minecraft:ancient_debris': [130, 80, 60],

  // Processed stone
  'minecraft:andesite': [140, 140, 142],
  'minecraft:granite': [165, 110, 85],
  'minecraft:diorite': [195, 195, 195],
  'minecraft:calcite': [225, 225, 220],
  'minecraft:tuff': [100, 100, 90],
  'minecraft:dripstone_block': [140, 120, 100],

  // Nether
  'minecraft:netherrack': [120, 30, 30],
  'minecraft:nether_bricks': [90, 25, 25],
  'minecraft:soul_sand': [80, 60, 45],
  'minecraft:soul_soil': [75, 58, 42],
  'minecraft:basalt': [65, 65, 70],
  'minecraft:blackstone': [40, 38, 45],
  'minecraft:magma_block': [180, 80, 15],
  'minecraft:glowstone': [220, 190, 90],
  'minecraft:shroomlight': [230, 150, 50],
  'minecraft:warped_nylium': [30, 130, 110],
  'minecraft:crimson_nylium': [170, 40, 40],

  // End
  'minecraft:end_stone': [220, 220, 160],
  'minecraft:end_stone_bricks': [205, 205, 145],
  'minecraft:purpur_block': [160, 100, 160],
  'minecraft:obsidian': [20, 15, 30],
  'minecraft:crying_obsidian': [35, 18, 55],

  // Water / aquatic
  'minecraft:coral_block': [200, 80, 120],

  // Misc structural
  'minecraft:oak_planks': [160, 130, 70],
  'minecraft:spruce_planks': [120, 90, 50],
  'minecraft:birch_planks': [210, 195, 140],
  'minecraft:jungle_planks': [130, 100, 65],
  'minecraft:acacia_planks': [170, 95, 55],
  'minecraft:dark_oak_planks': [65, 45, 30],
  'minecraft:mangrove_planks': [115, 55, 50],
  'minecraft:cherry_planks': [215, 175, 165],
  'minecraft:bamboo_planks': [195, 175, 95],
  'minecraft:crimson_planks': [110, 55, 75],
  'minecraft:warped_planks': [45, 110, 105],
  'minecraft:crimson_stem': [100, 30, 45],
  'minecraft:warped_stem': [40, 85, 85],
  'minecraft:cherry_log': [150, 100, 100],
  'minecraft:bamboo_block': [180, 165, 70],
  'minecraft:stone_bricks': [125, 125, 125],
  'minecraft:mossy_stone_bricks': [100, 125, 85],
  'minecraft:cracked_stone_bricks': [115, 115, 115],
  'minecraft:chiseled_stone_bricks': [120, 120, 120],
  'minecraft:smooth_stone': [130, 130, 130],
  'minecraft:bricks': [155, 95, 75],
  'minecraft:red_nether_bricks': [70, 15, 18],
  'minecraft:mud': [70, 65, 60],
  'minecraft:mud_bricks': [145, 120, 90],
  'minecraft:packed_mud': [130, 100, 65],
  'minecraft:cobbled_deepslate': [75, 75, 80],
  'minecraft:polished_deepslate': [65, 65, 72],
  'minecraft:deepslate_bricks': [68, 68, 75],
  'minecraft:deepslate_tiles': [60, 60, 66],
  'minecraft:chiseled_deepslate': [72, 72, 78],
  'minecraft:polished_blackstone': [55, 50, 58],
  'minecraft:polished_blackstone_bricks': [48, 44, 52],
  'minecraft:prismarine': [95, 160, 145],
  'minecraft:prismarine_bricks': [80, 170, 150],
  'minecraft:dark_prismarine': [50, 95, 85],
  'minecraft:quartz_block': [230, 225, 215],
  'minecraft:smooth_quartz': [235, 230, 220],
  'minecraft:quartz_pillar': [225, 220, 210],
  'minecraft:chiseled_quartz_block': [230, 225, 218],
  'minecraft:iron_block': [220, 220, 220],
  'minecraft:gold_block': [245, 220, 65],
  'minecraft:diamond_block': [100, 220, 210],
  'minecraft:emerald_block': [40, 200, 90],
  'minecraft:lapis_block': [30, 65, 165],
  'minecraft:coal_block': [25, 25, 28],
  'minecraft:redstone_block': [180, 30, 20],
  'minecraft:netherite_block': [75, 65, 65],
  'minecraft:copper_block': [195, 120, 90],
  'minecraft:exposed_copper': [165, 125, 100],
  'minecraft:weathered_copper': [110, 150, 120],
  'minecraft:oxidized_copper': [75, 155, 125],
  'minecraft:raw_iron_block': [200, 165, 135],
  'minecraft:raw_gold_block': [220, 180, 60],
  'minecraft:raw_copper_block': [190, 120, 85],
  'minecraft:ladder': [140, 105, 60],
  'minecraft:scaffolding': [190, 160, 100],
  'minecraft:terracotta': [165, 110, 80],
  'minecraft:glass': [180, 210, 230],
  'minecraft:sea_lantern': [200, 230, 235],
  'minecraft:torch': [240, 200, 60],
  'minecraft:chest': [180, 140, 60],
}

// 16 standard dye colors, used for wool/concrete/terracotta/glass/carpet/etc.
const DYE_COLORS: Record<string, RGB> = {
  white: [225, 225, 220],
  orange: [220, 120, 35],
  magenta: [190, 70, 190],
  light_blue: [65, 150, 220],
  yellow: [230, 200, 30],
  lime: [110, 190, 40],
  pink: [230, 150, 170],
  gray: [65, 65, 70],
  light_gray: [150, 150, 140],
  cyan: [30, 130, 140],
  purple: [110, 50, 160],
  blue: [45, 55, 155],
  brown: [95, 65, 40],
  green: [90, 110, 35],
  red: [140, 40, 40],
  black: [20, 20, 25]
}

const DYE_BLOCK_SUFFIXES = [
  '_wool',
  '_concrete',
  '_concrete_powder',
  '_terracotta',
  '_glazed_terracotta',
  '_stained_glass',
  '_stained_glass_pane',
  '_carpet',
  '_bed',
  '_shulker_box',
  '_banner',
  '_candle'
]

// Suffixes for block "shapes" (stairs/slabs/etc.) that should inherit their base material's color
const SHAPE_SUFFIXES = [
  '_stairs',
  '_slab',
  '_wall',
  '_fence_gate',
  '_fence',
  '_door',
  '_trapdoor',
  '_pressure_plate',
  '_button',
  '_pane',
  '_hanging_sign',
  '_wall_sign',
  '_sign'
]

const WOOD_TYPES = [
  'oak', 'spruce', 'birch', 'jungle', 'acacia', 'dark_oak',
  'mangrove', 'cherry', 'bamboo', 'crimson', 'warped'
]

// Maps a shape's stripped base name to the block whose color it should inherit,
// for cases where the shape name doesn't match the base block name directly
// (e.g. "oak_door" -> "oak" -> "oak_planks", "stone_brick_wall" -> "stone_brick" -> "stone_bricks")
const SHAPE_BASE_OVERRIDES: Record<string, string> = {
  'minecraft:quartz': 'minecraft:quartz_block',
  'minecraft:purpur': 'minecraft:purpur_block'
}
for (const wood of WOOD_TYPES) {
  SHAPE_BASE_OVERRIDES[`minecraft:${wood}`] = `minecraft:${wood}_planks`
}

function tryDyeColor(blockName: string): RGB | undefined {
  for (const color of Object.keys(DYE_COLORS)) {
    const prefix = `minecraft:${color}`
    if (blockName.startsWith(prefix) && DYE_BLOCK_SUFFIXES.includes(blockName.slice(prefix.length))) {
      return DYE_COLORS[color]
    }
  }
  return undefined
}

function tryShapeColor(blockName: string): RGB | undefined {
  for (const suffix of SHAPE_SUFFIXES) {
    if (!blockName.endsWith(suffix)) continue
    const base = blockName.slice(0, -suffix.length)
    if (BLOCK_COLORS[base]) return BLOCK_COLORS[base]
    if (SHAPE_BASE_OVERRIDES[base]) return BLOCK_COLORS[SHAPE_BASE_OVERRIDES[base]]
    // e.g. "stone_brick" -> "stone_bricks", "deepslate_tile" -> "deepslate_tiles"
    if (BLOCK_COLORS[base + 's']) return BLOCK_COLORS[base + 's']
    return undefined
  }
  return undefined
}

const FALLBACK_COLOR: RGB = [128, 128, 128]

export function getBlockColor(blockName: string): RGB {
  return (
    BLOCK_COLORS[blockName] ??
    tryDyeColor(blockName) ??
    tryShapeColor(blockName) ??
    FALLBACK_COLOR
  )
}

// Biome name → [r, g, b]
const BIOME_COLORS: Record<string, RGB> = {
  'minecraft:ocean': [0, 48, 140],
  'minecraft:deep_ocean': [0, 30, 110],
  'minecraft:cold_ocean': [50, 80, 180],
  'minecraft:lukewarm_ocean': [40, 120, 200],
  'minecraft:warm_ocean': [30, 150, 210],
  'minecraft:frozen_ocean': [150, 185, 220],
  'minecraft:river': [40, 100, 200],
  'minecraft:frozen_river': [140, 175, 215],
  'minecraft:beach': [210, 205, 145],
  'minecraft:stony_shore': [130, 130, 120],
  'minecraft:snowy_beach': [220, 225, 210],
  'minecraft:forest': [30, 115, 30],
  'minecraft:flower_forest': [70, 140, 50],
  'minecraft:birch_forest': [140, 185, 90],
  'minecraft:old_growth_birch_forest': [120, 165, 80],
  'minecraft:dark_forest': [25, 80, 25],
  'minecraft:jungle': [30, 145, 30],
  'minecraft:sparse_jungle': [60, 155, 50],
  'minecraft:bamboo_jungle': [20, 155, 40],
  'minecraft:taiga': [60, 120, 70],
  'minecraft:snowy_taiga': [155, 180, 165],
  'minecraft:old_growth_pine_taiga': [55, 100, 65],
  'minecraft:old_growth_spruce_taiga': [50, 95, 60],
  'minecraft:plains': [80, 165, 60],
  'minecraft:sunflower_plains': [100, 175, 50],
  'minecraft:snowy_plains': [200, 215, 215],
  'minecraft:ice_spikes': [160, 200, 230],
  'minecraft:desert': [215, 200, 115],
  'minecraft:savanna': [155, 175, 60],
  'minecraft:savanna_plateau': [140, 160, 55],
  'minecraft:windswept_savanna': [145, 170, 55],
  'minecraft:badlands': [195, 100, 45],
  'minecraft:eroded_badlands': [200, 95, 40],
  'minecraft:wooded_badlands': [175, 115, 55],
  'minecraft:swamp': [65, 120, 70],
  'minecraft:mangrove_swamp': [60, 115, 75],
  'minecraft:meadow': [100, 185, 80],
  'minecraft:grove': [150, 185, 165],
  'minecraft:snowy_slopes': [190, 205, 205],
  'minecraft:frozen_peaks': [180, 200, 220],
  'minecraft:jagged_peaks': [175, 185, 185],
  'minecraft:stony_peaks': [155, 160, 150],
  'minecraft:lush_caves': [50, 165, 80],
  'minecraft:dripstone_caves': [130, 115, 95],
  'minecraft:deep_dark': [15, 18, 25],
  'minecraft:nether_wastes': [105, 25, 25],
  'minecraft:soul_sand_valley': [75, 55, 40],
  'minecraft:crimson_forest': [155, 35, 35],
  'minecraft:warped_forest': [25, 120, 100],
  'minecraft:basalt_deltas': [60, 60, 65],
  'minecraft:the_end': [210, 210, 155],
  'minecraft:end_highlands': [195, 195, 140],
  'minecraft:end_midlands': [200, 200, 145],
  'minecraft:end_barrens': [185, 185, 135],
  'minecraft:small_end_islands': [200, 200, 155],
  'minecraft:the_void': [10, 10, 15],
}

const FALLBACK_BIOME_COLOR: RGB = [100, 150, 100]

export function getBiomeColor(biomeName: string): RGB {
  return BIOME_COLORS[biomeName] ?? FALLBACK_BIOME_COLOR
}

// Heightmap gradient: dark blue (low) → green (mid) → white (high)
export function getHeightColor(y: number): RGB {
  // y range: -64 to 320 → normalise to 0..1
  const t = Math.max(0, Math.min(1, (y + 64) / 384))
  if (t < 0.25) {
    // deep underground: very dark gray
    const v = Math.round(30 + t * 4 * 60)
    return [v, v, v]
  } else if (t < 0.5) {
    // underground → surface: dark to green
    const s = (t - 0.25) / 0.25
    return [Math.round(50 * (1 - s)), Math.round(90 + s * 70), Math.round(50 * (1 - s))]
  } else if (t < 0.75) {
    // surface → hills: green → yellow-green
    const s = (t - 0.5) / 0.25
    return [Math.round(s * 120), Math.round(160 - s * 30), Math.round(20)]
  } else {
    // mountains → peaks: yellow → white
    const s = (t - 0.75) / 0.25
    return [Math.round(120 + s * 135), Math.round(130 + s * 125), Math.round(20 + s * 235)]
  }
}

export const ORE_BLOCKS: Record<string, RGB> = {
  'minecraft:diamond_ore': [50, 220, 210],
  'minecraft:deepslate_diamond_ore': [40, 190, 180],
  'minecraft:emerald_ore': [30, 200, 80],
  'minecraft:deepslate_emerald_ore': [25, 170, 65],
  'minecraft:gold_ore': [220, 195, 40],
  'minecraft:deepslate_gold_ore': [190, 170, 35],
  'minecraft:iron_ore': [200, 160, 130],
  'minecraft:deepslate_iron_ore': [170, 135, 115],
  'minecraft:copper_ore': [180, 125, 80],
  'minecraft:deepslate_copper_ore': [155, 105, 70],
  'minecraft:lapis_ore': [30, 60, 180],
  'minecraft:deepslate_lapis_ore': [25, 50, 155],
  'minecraft:redstone_ore': [220, 40, 30],
  'minecraft:deepslate_redstone_ore': [190, 30, 22],
  'minecraft:coal_ore': [50, 50, 55],
  'minecraft:deepslate_coal_ore': [45, 45, 50],
  'minecraft:ancient_debris': [160, 100, 70],
  'minecraft:nether_quartz_ore': [220, 215, 215],
  'minecraft:nether_gold_ore': [210, 175, 35],
}

export const AIR_BLOCKS = new Set(['minecraft:air', 'minecraft:cave_air', 'minecraft:void_air'])
export const TRANSPARENT_BLOCKS = new Set([
  ...AIR_BLOCKS,
  'minecraft:water',
  'minecraft:grass',
  'minecraft:tall_grass',
  'minecraft:fern',
  'minecraft:large_fern',
  'minecraft:dandelion',
  'minecraft:poppy',
  'minecraft:blue_orchid',
  'minecraft:allium',
  'minecraft:sunflower',
  'minecraft:dead_bush',
  'minecraft:seagrass',
  'minecraft:kelp',
  'minecraft:torch',
])
