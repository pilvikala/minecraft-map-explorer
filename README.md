# Minecraft Map Explorer

An easy-to-use desktop app to explore and visualize your Minecraft worlds in different ways. See your terrain from new perspectives, find valuable ores, explore caves, and understand the structure of your world.

## Getting Started

### What You Need

- Your computer
- Node.js installed (download from https://nodejs.org/)
- A Minecraft world saved on your computer

### How to Run

1. **Open a terminal or command prompt** in the app folder
2. **Install dependencies** (only needed the first time):
   ```
   npm install
   ```
3. **Start the app**:
   ```
   npm run dev
   ```
4. **A window will open** – select your Minecraft world's region folder when prompted
5. **Explore!** Switch between different views using the buttons on the right panel

> **Tip**: On Windows, Minecraft worlds are usually in `%APPDATA%\.minecraft\saves`. On Mac, they're in `~/Library/Application Support/minecraft/saves`. On Linux, they're in `~/.minecraft/saves`.

## The Views

The app shows your world in 5 different ways. Click the buttons on the right side to switch between them.

### Surface Map
![Surface map](images/surface.png)

The top view of your world – what it looks like from high above. This is the most intuitive view showing terrain, water, grass, and structures.

### Heightmap
![Heightmap](images/height-map.png)

A colorized version showing terrain elevation. Brighter colors are higher up, darker colors are lower down. This helps you visualize the topography of your world at a glance.

### Caves
![Cave map](images/caves.png)

Reveals underground caverns and tunnels. This view shows all the empty space underground where caves naturally form. Perfect for finding good locations for mining or base building below the surface.

### Y-Slice (Horizontal Slice)
![Horizontal slice](images/horizonta-slice.png)

A thin horizontal layer through your world at a specific height. Use the +/- buttons to move up and down through the world (from Y level -64 to 319). Great for finding ore deposits at specific depths or planning underground construction.

### Biome Map
![Biomes](images/biomes.png)

Shows the different biomes in your world, each with its own color. Biomes include deserts, forests, mountains, oceans, and more. Use this to find specific biome types for building or gathering biome-specific materials.

## Features

- **Ore Highlighting**: Toggle specific ore types on and off to find valuable resources
- **Y-Level Control**: In the Y-Slice view, adjust the height level to explore different depths
- **Smooth Navigation**: Click and drag to pan around the map, scroll to zoom in and out
- **Multiple Worlds**: Switch between different saved worlds by clicking the "← Worlds" button

## Tips & Tricks

- **Finding Diamonds**: Switch to Y-Slice view and set it to levels 5-16 where diamonds are most common
- **Cave Exploring**: Use Caves view to find interesting cave systems to explore
- **Building Locations**: Use Heightmap to find flat areas or Biome view to find the perfect biome for your next project
- **Mining Planning**: Y-Slice is great for finding good mining heights for specific ores

## Troubleshooting

**The app won't open**: Make sure Node.js is installed. Open a terminal and type `node --version` to check.

**The app opens but crashes when loading a world**: Make sure you selected a valid Minecraft world region folder (it should contain .mca files).

**The map is empty**: The world might still be loading – wait a moment. If nothing appears, try zooming out with your mouse scroll wheel.

**I don't see ores highlighted**: Ore highlighting is a toggle in the layer panel on the right – check that the ores you're looking for are enabled.

---

Enjoy exploring your Minecraft worlds! 🗺️
