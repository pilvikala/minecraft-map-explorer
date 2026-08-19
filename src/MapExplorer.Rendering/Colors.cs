namespace MapExplorer.Rendering;

// Full port of the Electron app's src/renderer/src/core/colors.ts (block name
// -> RGB lookup, ~150 entries, plus dye/shape-suffix fallback resolution).
public static class Colors
{
    // Exposed publicly (not just via GetBlockColor) so the edit-mode material palette can enumerate
    // every block this app knows how to render/paint — see EditViewModel.AllMaterials. Kept as a
    // private, concrete Dictionary internally (dictionary-initializer syntax needs a constructible
    // type) with a read-only public view over it, so external code can enumerate but not mutate it.
    private static readonly Dictionary<string, Rgb> BlockColorsMap = new()
    {
        // Air / transparent
        ["minecraft:air"] = new Rgb(0, 0, 0),
        ["minecraft:cave_air"] = new Rgb(30, 30, 50),
        ["minecraft:void_air"] = new Rgb(0, 0, 0),
        ["minecraft:water"] = new Rgb(32, 100, 220),
        ["minecraft:lava"] = new Rgb(220, 80, 20),

        // Terrain
        ["minecraft:grass_block"] = new Rgb(86, 156, 55),
        ["minecraft:dirt"] = new Rgb(134, 96, 67),
        ["minecraft:coarse_dirt"] = new Rgb(115, 82, 57),
        ["minecraft:podzol"] = new Rgb(100, 68, 30),
        ["minecraft:mycelium"] = new Rgb(108, 96, 100),
        ["minecraft:grass_path"] = new Rgb(147, 116, 59),
        ["minecraft:dirt_path"] = new Rgb(147, 116, 59),
        ["minecraft:farmland"] = new Rgb(120, 85, 45),
        ["minecraft:stone"] = new Rgb(110, 110, 110),
        ["minecraft:deepslate"] = new Rgb(70, 70, 78),
        ["minecraft:cobblestone"] = new Rgb(120, 118, 114),
        ["minecraft:mossy_cobblestone"] = new Rgb(100, 118, 80),
        ["minecraft:bedrock"] = new Rgb(50, 50, 55),
        ["minecraft:gravel"] = new Rgb(130, 126, 118),
        ["minecraft:sand"] = new Rgb(220, 210, 150),
        ["minecraft:red_sand"] = new Rgb(190, 100, 50),
        ["minecraft:sandstone"] = new Rgb(210, 200, 130),
        ["minecraft:red_sandstone"] = new Rgb(180, 95, 45),
        ["minecraft:clay"] = new Rgb(160, 166, 180),

        // Grass / plants
        ["minecraft:grass"] = new Rgb(72, 140, 45),
        ["minecraft:short_grass"] = new Rgb(72, 140, 45),
        ["minecraft:tall_grass"] = new Rgb(72, 140, 45),
        ["minecraft:fern"] = new Rgb(60, 120, 40),
        ["minecraft:large_fern"] = new Rgb(60, 120, 40),
        ["minecraft:dead_bush"] = new Rgb(130, 100, 50),
        ["minecraft:seagrass"] = new Rgb(40, 130, 80),
        ["minecraft:kelp"] = new Rgb(30, 110, 60),
        ["minecraft:kelp_plant"] = new Rgb(30, 110, 60),
        ["minecraft:vine"] = new Rgb(50, 115, 40),
        ["minecraft:leaf_litter"] = new Rgb(95, 135, 55),
        ["minecraft:bamboo"] = new Rgb(95, 165, 60),

        // Flowers
        ["minecraft:dandelion"] = new Rgb(240, 220, 20),
        ["minecraft:poppy"] = new Rgb(200, 30, 30),
        ["minecraft:blue_orchid"] = new Rgb(60, 130, 220),
        ["minecraft:allium"] = new Rgb(150, 80, 200),
        ["minecraft:sunflower"] = new Rgb(230, 200, 30),
        ["minecraft:pink_petals"] = new Rgb(240, 180, 205),

        // Snow / ice
        ["minecraft:snow"] = new Rgb(240, 245, 255),
        ["minecraft:snow_block"] = new Rgb(235, 240, 255),
        ["minecraft:ice"] = new Rgb(170, 210, 240),
        ["minecraft:packed_ice"] = new Rgb(140, 185, 230),
        ["minecraft:blue_ice"] = new Rgb(100, 150, 220),
        ["minecraft:frosted_ice"] = new Rgb(160, 200, 235),
        ["minecraft:powder_snow"] = new Rgb(235, 240, 250),

        // Wood / logs / leaves
        ["minecraft:oak_log"] = new Rgb(120, 90, 55),
        ["minecraft:spruce_log"] = new Rgb(90, 65, 35),
        ["minecraft:birch_log"] = new Rgb(200, 195, 160),
        ["minecraft:jungle_log"] = new Rgb(100, 80, 45),
        ["minecraft:acacia_log"] = new Rgb(110, 85, 50),
        ["minecraft:dark_oak_log"] = new Rgb(60, 45, 25),
        ["minecraft:mangrove_log"] = new Rgb(95, 60, 40),
        ["minecraft:oak_leaves"] = new Rgb(60, 130, 30),
        ["minecraft:spruce_leaves"] = new Rgb(40, 90, 40),
        ["minecraft:birch_leaves"] = new Rgb(100, 160, 60),
        ["minecraft:jungle_leaves"] = new Rgb(50, 140, 30),
        ["minecraft:acacia_leaves"] = new Rgb(80, 150, 35),
        ["minecraft:dark_oak_leaves"] = new Rgb(45, 110, 25),
        ["minecraft:mangrove_leaves"] = new Rgb(55, 125, 30),
        ["minecraft:azalea_leaves"] = new Rgb(75, 135, 45),
        ["minecraft:flowering_azalea_leaves"] = new Rgb(90, 130, 60),
        ["minecraft:cherry_leaves"] = new Rgb(235, 170, 200),

        // Stone variants / ores
        ["minecraft:coal_ore"] = new Rgb(50, 50, 55),
        ["minecraft:deepslate_coal_ore"] = new Rgb(45, 45, 50),
        ["minecraft:iron_ore"] = new Rgb(170, 130, 110),
        ["minecraft:deepslate_iron_ore"] = new Rgb(140, 110, 100),
        ["minecraft:copper_ore"] = new Rgb(145, 105, 75),
        ["minecraft:deepslate_copper_ore"] = new Rgb(120, 90, 70),
        ["minecraft:gold_ore"] = new Rgb(220, 195, 40),
        ["minecraft:deepslate_gold_ore"] = new Rgb(190, 170, 35),
        ["minecraft:redstone_ore"] = new Rgb(180, 30, 20),
        ["minecraft:deepslate_redstone_ore"] = new Rgb(155, 25, 18),
        ["minecraft:lapis_ore"] = new Rgb(30, 60, 180),
        ["minecraft:deepslate_lapis_ore"] = new Rgb(25, 50, 155),
        ["minecraft:diamond_ore"] = new Rgb(50, 220, 210),
        ["minecraft:deepslate_diamond_ore"] = new Rgb(40, 190, 180),
        ["minecraft:emerald_ore"] = new Rgb(30, 200, 80),
        ["minecraft:deepslate_emerald_ore"] = new Rgb(25, 170, 65),
        ["minecraft:nether_quartz_ore"] = new Rgb(200, 200, 200),
        ["minecraft:nether_gold_ore"] = new Rgb(210, 170, 30),
        ["minecraft:ancient_debris"] = new Rgb(130, 80, 60),

        // Processed stone
        ["minecraft:andesite"] = new Rgb(140, 140, 142),
        ["minecraft:granite"] = new Rgb(165, 110, 85),
        ["minecraft:diorite"] = new Rgb(195, 195, 195),
        ["minecraft:calcite"] = new Rgb(225, 225, 220),
        ["minecraft:tuff"] = new Rgb(100, 100, 90),
        ["minecraft:dripstone_block"] = new Rgb(140, 120, 100),

        // Nether
        ["minecraft:netherrack"] = new Rgb(120, 30, 30),
        ["minecraft:nether_bricks"] = new Rgb(90, 25, 25),
        ["minecraft:soul_sand"] = new Rgb(80, 60, 45),
        ["minecraft:soul_soil"] = new Rgb(75, 58, 42),
        ["minecraft:basalt"] = new Rgb(65, 65, 70),
        ["minecraft:blackstone"] = new Rgb(40, 38, 45),
        ["minecraft:magma_block"] = new Rgb(180, 80, 15),
        ["minecraft:glowstone"] = new Rgb(220, 190, 90),
        ["minecraft:shroomlight"] = new Rgb(230, 150, 50),
        ["minecraft:warped_nylium"] = new Rgb(30, 130, 110),
        ["minecraft:crimson_nylium"] = new Rgb(170, 40, 40),

        // End
        ["minecraft:end_stone"] = new Rgb(220, 220, 160),
        ["minecraft:end_stone_bricks"] = new Rgb(205, 205, 145),
        ["minecraft:purpur_block"] = new Rgb(160, 100, 160),
        ["minecraft:obsidian"] = new Rgb(20, 15, 30),
        ["minecraft:crying_obsidian"] = new Rgb(35, 18, 55),

        // Water / aquatic
        ["minecraft:coral_block"] = new Rgb(200, 80, 120),

        // Misc structural
        ["minecraft:oak_planks"] = new Rgb(160, 130, 70),
        ["minecraft:spruce_planks"] = new Rgb(120, 90, 50),
        ["minecraft:birch_planks"] = new Rgb(210, 195, 140),
        ["minecraft:jungle_planks"] = new Rgb(130, 100, 65),
        ["minecraft:acacia_planks"] = new Rgb(170, 95, 55),
        ["minecraft:dark_oak_planks"] = new Rgb(65, 45, 30),
        ["minecraft:mangrove_planks"] = new Rgb(115, 55, 50),
        ["minecraft:cherry_planks"] = new Rgb(215, 175, 165),
        ["minecraft:bamboo_planks"] = new Rgb(195, 175, 95),
        ["minecraft:crimson_planks"] = new Rgb(110, 55, 75),
        ["minecraft:warped_planks"] = new Rgb(45, 110, 105),
        ["minecraft:crimson_stem"] = new Rgb(100, 30, 45),
        ["minecraft:warped_stem"] = new Rgb(40, 85, 85),
        ["minecraft:cherry_log"] = new Rgb(150, 100, 100),
        ["minecraft:bamboo_block"] = new Rgb(180, 165, 70),
        ["minecraft:stone_bricks"] = new Rgb(125, 125, 125),
        ["minecraft:mossy_stone_bricks"] = new Rgb(100, 125, 85),
        ["minecraft:cracked_stone_bricks"] = new Rgb(115, 115, 115),
        ["minecraft:chiseled_stone_bricks"] = new Rgb(120, 120, 120),
        ["minecraft:smooth_stone"] = new Rgb(130, 130, 130),
        ["minecraft:bricks"] = new Rgb(155, 95, 75),
        ["minecraft:red_nether_bricks"] = new Rgb(70, 15, 18),
        ["minecraft:mud"] = new Rgb(70, 65, 60),
        ["minecraft:mud_bricks"] = new Rgb(145, 120, 90),
        ["minecraft:packed_mud"] = new Rgb(130, 100, 65),
        ["minecraft:cobbled_deepslate"] = new Rgb(75, 75, 80),
        ["minecraft:polished_deepslate"] = new Rgb(65, 65, 72),
        ["minecraft:deepslate_bricks"] = new Rgb(68, 68, 75),
        ["minecraft:deepslate_tiles"] = new Rgb(60, 60, 66),
        ["minecraft:chiseled_deepslate"] = new Rgb(72, 72, 78),
        ["minecraft:polished_blackstone"] = new Rgb(55, 50, 58),
        ["minecraft:polished_blackstone_bricks"] = new Rgb(48, 44, 52),
        ["minecraft:prismarine"] = new Rgb(95, 160, 145),
        ["minecraft:prismarine_bricks"] = new Rgb(80, 170, 150),
        ["minecraft:dark_prismarine"] = new Rgb(50, 95, 85),
        ["minecraft:quartz_block"] = new Rgb(230, 225, 215),
        ["minecraft:smooth_quartz"] = new Rgb(235, 230, 220),
        ["minecraft:quartz_pillar"] = new Rgb(225, 220, 210),
        ["minecraft:chiseled_quartz_block"] = new Rgb(230, 225, 218),
        ["minecraft:iron_block"] = new Rgb(220, 220, 220),
        ["minecraft:gold_block"] = new Rgb(245, 220, 65),
        ["minecraft:diamond_block"] = new Rgb(100, 220, 210),
        ["minecraft:emerald_block"] = new Rgb(40, 200, 90),
        ["minecraft:lapis_block"] = new Rgb(30, 65, 165),
        ["minecraft:coal_block"] = new Rgb(25, 25, 28),
        ["minecraft:redstone_block"] = new Rgb(180, 30, 20),
        ["minecraft:netherite_block"] = new Rgb(75, 65, 65),
        ["minecraft:copper_block"] = new Rgb(195, 120, 90),
        ["minecraft:exposed_copper"] = new Rgb(165, 125, 100),
        ["minecraft:weathered_copper"] = new Rgb(110, 150, 120),
        ["minecraft:oxidized_copper"] = new Rgb(75, 155, 125),
        ["minecraft:raw_iron_block"] = new Rgb(200, 165, 135),
        ["minecraft:raw_gold_block"] = new Rgb(220, 180, 60),
        ["minecraft:raw_copper_block"] = new Rgb(190, 120, 85),
        ["minecraft:ladder"] = new Rgb(140, 105, 60),
        ["minecraft:scaffolding"] = new Rgb(190, 160, 100),
        ["minecraft:terracotta"] = new Rgb(165, 110, 80),
        ["minecraft:glass"] = new Rgb(180, 210, 230),
        ["minecraft:sea_lantern"] = new Rgb(200, 230, 235),
        ["minecraft:torch"] = new Rgb(240, 200, 60),
        ["minecraft:chest"] = new Rgb(180, 140, 60),
    };

    public static IReadOnlyDictionary<string, Rgb> BlockColors => BlockColorsMap;

    // 16 standard dye colors, used for wool/concrete/terracotta/glass/carpet/etc.
    private static readonly Dictionary<string, Rgb> DyeColors = new()
    {
        ["white"] = new Rgb(225, 225, 220),
        ["orange"] = new Rgb(220, 120, 35),
        ["magenta"] = new Rgb(190, 70, 190),
        ["light_blue"] = new Rgb(65, 150, 220),
        ["yellow"] = new Rgb(230, 200, 30),
        ["lime"] = new Rgb(110, 190, 40),
        ["pink"] = new Rgb(230, 150, 170),
        ["gray"] = new Rgb(65, 65, 70),
        ["light_gray"] = new Rgb(150, 150, 140),
        ["cyan"] = new Rgb(30, 130, 140),
        ["purple"] = new Rgb(110, 50, 160),
        ["blue"] = new Rgb(45, 55, 155),
        ["brown"] = new Rgb(95, 65, 40),
        ["green"] = new Rgb(90, 110, 35),
        ["red"] = new Rgb(140, 40, 40),
        ["black"] = new Rgb(20, 20, 25),
    };

    private static readonly string[] DyeBlockSuffixes =
    [
        "_wool", "_concrete", "_concrete_powder", "_terracotta", "_glazed_terracotta",
        "_stained_glass", "_stained_glass_pane", "_carpet", "_bed", "_shulker_box",
        "_banner", "_candle"
    ];

    // Suffixes for block "shapes" (stairs/slabs/etc.) that should inherit their base material's color
    private static readonly string[] ShapeSuffixes =
    [
        "_stairs", "_slab", "_wall", "_fence_gate", "_fence", "_door", "_trapdoor",
        "_pressure_plate", "_button", "_pane", "_hanging_sign", "_wall_sign", "_sign"
    ];

    private static readonly string[] WoodTypes =
    [
        "oak", "spruce", "birch", "jungle", "acacia", "dark_oak",
        "mangrove", "cherry", "bamboo", "crimson", "warped"
    ];

    // Maps a shape's stripped base name to the block whose color it should inherit,
    // for cases where the shape name doesn't match the base block name directly
    // (e.g. "oak_door" -> "oak" -> "oak_planks", "stone_brick_wall" -> "stone_brick" -> "stone_bricks")
    private static readonly Dictionary<string, string> ShapeBaseOverrides = BuildShapeBaseOverrides();

    private static Dictionary<string, string> BuildShapeBaseOverrides()
    {
        var overrides = new Dictionary<string, string>
        {
            ["minecraft:quartz"] = "minecraft:quartz_block",
            ["minecraft:purpur"] = "minecraft:purpur_block"
        };
        foreach (var wood in WoodTypes)
        {
            overrides[$"minecraft:{wood}"] = $"minecraft:{wood}_planks";
        }
        return overrides;
    }

    private static Rgb? TryDyeColor(string blockName)
    {
        foreach (var (colorName, rgb) in DyeColors)
        {
            var prefix = $"minecraft:{colorName}";
            if (blockName.StartsWith(prefix, StringComparison.Ordinal) &&
                DyeBlockSuffixes.Contains(blockName[prefix.Length..]))
            {
                return rgb;
            }
        }
        return null;
    }

    private static Rgb? TryShapeColor(string blockName)
    {
        foreach (var suffix in ShapeSuffixes)
        {
            if (!blockName.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var basename = blockName[..^suffix.Length];
            if (BlockColorsMap.TryGetValue(basename, out var direct)) return direct;
            if (ShapeBaseOverrides.TryGetValue(basename, out var overrideName) && BlockColorsMap.TryGetValue(overrideName, out var overrideColor))
                return overrideColor;
            // e.g. "stone_brick" -> "stone_bricks", "deepslate_tile" -> "deepslate_tiles"
            if (BlockColorsMap.TryGetValue(basename + "s", out var plural)) return plural;
            return null;
        }
        return null;
    }

    private static readonly Rgb FallbackColor = new(128, 128, 128);

    public static Rgb GetBlockColor(string blockName) =>
        BlockColorsMap.TryGetValue(blockName, out var direct) ? direct
        : TryDyeColor(blockName) ?? TryShapeColor(blockName) ?? FallbackColor;

    // Biome name -> RGB
    private static readonly Dictionary<string, Rgb> BiomeColors = new()
    {
        ["minecraft:ocean"] = new Rgb(0, 48, 140),
        ["minecraft:deep_ocean"] = new Rgb(0, 30, 110),
        ["minecraft:cold_ocean"] = new Rgb(50, 80, 180),
        ["minecraft:lukewarm_ocean"] = new Rgb(40, 120, 200),
        ["minecraft:warm_ocean"] = new Rgb(30, 150, 210),
        ["minecraft:frozen_ocean"] = new Rgb(150, 185, 220),
        ["minecraft:river"] = new Rgb(40, 100, 200),
        ["minecraft:frozen_river"] = new Rgb(140, 175, 215),
        ["minecraft:beach"] = new Rgb(210, 205, 145),
        ["minecraft:stony_shore"] = new Rgb(130, 130, 120),
        ["minecraft:snowy_beach"] = new Rgb(220, 225, 210),
        ["minecraft:forest"] = new Rgb(30, 115, 30),
        ["minecraft:flower_forest"] = new Rgb(70, 140, 50),
        ["minecraft:birch_forest"] = new Rgb(140, 185, 90),
        ["minecraft:old_growth_birch_forest"] = new Rgb(120, 165, 80),
        ["minecraft:dark_forest"] = new Rgb(25, 80, 25),
        ["minecraft:jungle"] = new Rgb(30, 145, 30),
        ["minecraft:sparse_jungle"] = new Rgb(60, 155, 50),
        ["minecraft:bamboo_jungle"] = new Rgb(20, 155, 40),
        ["minecraft:taiga"] = new Rgb(60, 120, 70),
        ["minecraft:snowy_taiga"] = new Rgb(155, 180, 165),
        ["minecraft:old_growth_pine_taiga"] = new Rgb(55, 100, 65),
        ["minecraft:old_growth_spruce_taiga"] = new Rgb(50, 95, 60),
        ["minecraft:plains"] = new Rgb(80, 165, 60),
        ["minecraft:sunflower_plains"] = new Rgb(100, 175, 50),
        ["minecraft:snowy_plains"] = new Rgb(200, 215, 215),
        ["minecraft:ice_spikes"] = new Rgb(160, 200, 230),
        ["minecraft:desert"] = new Rgb(215, 200, 115),
        ["minecraft:savanna"] = new Rgb(155, 175, 60),
        ["minecraft:savanna_plateau"] = new Rgb(140, 160, 55),
        ["minecraft:windswept_savanna"] = new Rgb(145, 170, 55),
        ["minecraft:badlands"] = new Rgb(195, 100, 45),
        ["minecraft:eroded_badlands"] = new Rgb(200, 95, 40),
        ["minecraft:wooded_badlands"] = new Rgb(175, 115, 55),
        ["minecraft:swamp"] = new Rgb(65, 120, 70),
        ["minecraft:mangrove_swamp"] = new Rgb(60, 115, 75),
        ["minecraft:meadow"] = new Rgb(100, 185, 80),
        ["minecraft:grove"] = new Rgb(150, 185, 165),
        ["minecraft:snowy_slopes"] = new Rgb(190, 205, 205),
        ["minecraft:frozen_peaks"] = new Rgb(180, 200, 220),
        ["minecraft:jagged_peaks"] = new Rgb(175, 185, 185),
        ["minecraft:stony_peaks"] = new Rgb(155, 160, 150),
        ["minecraft:lush_caves"] = new Rgb(50, 165, 80),
        ["minecraft:dripstone_caves"] = new Rgb(130, 115, 95),
        ["minecraft:deep_dark"] = new Rgb(15, 18, 25),
        ["minecraft:nether_wastes"] = new Rgb(105, 25, 25),
        ["minecraft:soul_sand_valley"] = new Rgb(75, 55, 40),
        ["minecraft:crimson_forest"] = new Rgb(155, 35, 35),
        ["minecraft:warped_forest"] = new Rgb(25, 120, 100),
        ["minecraft:basalt_deltas"] = new Rgb(60, 60, 65),
        ["minecraft:the_end"] = new Rgb(210, 210, 155),
        ["minecraft:end_highlands"] = new Rgb(195, 195, 140),
        ["minecraft:end_midlands"] = new Rgb(200, 200, 145),
        ["minecraft:end_barrens"] = new Rgb(185, 185, 135),
        ["minecraft:small_end_islands"] = new Rgb(200, 200, 155),
        ["minecraft:the_void"] = new Rgb(10, 10, 15),
    };

    private static readonly Rgb FallbackBiomeColor = new(100, 150, 100);

    public static Rgb GetBiomeColor(string biomeName) =>
        BiomeColors.GetValueOrDefault(biomeName, FallbackBiomeColor);

    // Heightmap gradient: dark blue (low) -> green (mid) -> white (high)
    public static Rgb GetHeightColor(int y)
    {
        // y range: -64 to 320 -> normalise to 0..1
        double t = Math.Max(0, Math.Min(1, (y + 64) / 384.0));
        if (t < 0.25)
        {
            // deep underground: very dark gray
            byte v = Rgb.ClampByte(30 + t * 4 * 60);
            return new Rgb(v, v, v);
        }
        if (t < 0.5)
        {
            // underground -> surface: dark to green
            double s = (t - 0.25) / 0.25;
            return new Rgb(
                Rgb.ClampByte(50 * (1 - s)),
                Rgb.ClampByte(90 + s * 70),
                Rgb.ClampByte(50 * (1 - s)));
        }
        if (t < 0.75)
        {
            // surface -> hills: green -> yellow-green
            double s = (t - 0.5) / 0.25;
            return new Rgb(
                Rgb.ClampByte(s * 120),
                Rgb.ClampByte(160 - s * 30),
                20);
        }
        else
        {
            // mountains -> peaks: yellow -> white
            double s = (t - 0.75) / 0.25;
            return new Rgb(
                Rgb.ClampByte(120 + s * 135),
                Rgb.ClampByte(130 + s * 125),
                Rgb.ClampByte(20 + s * 235));
        }
    }

    public static readonly Dictionary<string, Rgb> OreBlocks = new()
    {
        ["minecraft:diamond_ore"] = new Rgb(50, 220, 210),
        ["minecraft:deepslate_diamond_ore"] = new Rgb(40, 190, 180),
        ["minecraft:emerald_ore"] = new Rgb(30, 200, 80),
        ["minecraft:deepslate_emerald_ore"] = new Rgb(25, 170, 65),
        ["minecraft:gold_ore"] = new Rgb(220, 195, 40),
        ["minecraft:deepslate_gold_ore"] = new Rgb(190, 170, 35),
        ["minecraft:iron_ore"] = new Rgb(200, 160, 130),
        ["minecraft:deepslate_iron_ore"] = new Rgb(170, 135, 115),
        ["minecraft:copper_ore"] = new Rgb(180, 125, 80),
        ["minecraft:deepslate_copper_ore"] = new Rgb(155, 105, 70),
        ["minecraft:lapis_ore"] = new Rgb(30, 60, 180),
        ["minecraft:deepslate_lapis_ore"] = new Rgb(25, 50, 155),
        ["minecraft:redstone_ore"] = new Rgb(220, 40, 30),
        ["minecraft:deepslate_redstone_ore"] = new Rgb(190, 30, 22),
        ["minecraft:coal_ore"] = new Rgb(50, 50, 55),
        ["minecraft:deepslate_coal_ore"] = new Rgb(45, 45, 50),
        ["minecraft:ancient_debris"] = new Rgb(160, 100, 70),
        ["minecraft:nether_quartz_ore"] = new Rgb(220, 215, 215),
        ["minecraft:nether_gold_ore"] = new Rgb(210, 175, 35),
    };

    // Stable ordering of OreBlocks' keys, fixed once at startup — lets OreSummary store a compact
    // byte index per ore hit instead of repeating the block name string per occurrence.
    public static readonly string[] OreBlockNames = OreBlocks.Keys.ToArray();

    public static readonly Dictionary<string, byte> OreBlockIndex = BuildOreBlockIndex();

    private static Dictionary<string, byte> BuildOreBlockIndex()
    {
        var index = new Dictionary<string, byte>();
        for (int i = 0; i < OreBlockNames.Length; i++) index[OreBlockNames[i]] = (byte)i;
        return index;
    }

    public static readonly HashSet<string> AirBlocks = ["minecraft:air", "minecraft:cave_air", "minecraft:void_air"];

    public static readonly HashSet<string> TransparentBlocks =
    [
        "minecraft:air", "minecraft:cave_air", "minecraft:void_air",
        "minecraft:water", "minecraft:grass", "minecraft:tall_grass", "minecraft:fern",
        "minecraft:large_fern", "minecraft:dandelion", "minecraft:poppy", "minecraft:blue_orchid",
        "minecraft:allium", "minecraft:sunflower", "minecraft:dead_bush", "minecraft:seagrass",
        "minecraft:kelp", "minecraft:torch",
    ];
}
