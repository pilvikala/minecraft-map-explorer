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
  'minecraft:tall_grass': [72, 140, 45],
  'minecraft:fern': [60, 120, 40],
  'minecraft:large_fern': [60, 120, 40],
  'minecraft:dead_bush': [130, 100, 50],
  'minecraft:seagrass': [40, 130, 80],
  'minecraft:kelp': [30, 110, 60],
  'minecraft:kelp_plant': [30, 110, 60],

  // Flowers
  'minecraft:dandelion': [240, 220, 20],
  'minecraft:poppy': [200, 30, 30],
  'minecraft:blue_orchid': [60, 130, 220],
  'minecraft:allium': [150, 80, 200],
  'minecraft:sunflower': [230, 200, 30],

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
  'minecraft:stone_bricks': [125, 125, 125],
  'minecraft:mossy_stone_bricks': [100, 125, 85],
  'minecraft:smooth_stone': [130, 130, 130],
  'minecraft:brick': [155, 95, 75],
  'minecraft:terracotta': [165, 110, 80],
  'minecraft:glass': [180, 210, 230],
  'minecraft:sea_lantern': [200, 230, 235],
  'minecraft:torch': [240, 200, 60],
  'minecraft:chest': [180, 140, 60],
}

const FALLBACK_COLOR: RGB = [128, 128, 128]

export function getBlockColor(blockName: string): RGB {
  return BLOCK_COLORS[blockName] ?? FALLBACK_COLOR
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
