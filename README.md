# 🔮 EnderBuoy

[![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Supported game](https://img.shields.io/badge/Big%20Walk%20%7C%20BepInEx%206-supported-6f42c1?style=flat-square)](https://store.steampowered.com/app/1478500/Big_Walk/)

**Enderpearl-style buoy teleportation for Big Walk.** Turn on a buoy, throw it across the island, and instantly blink to where it lands.

Buoys in Big Walk are bright, bouncy, and throwable. EnderBuoy turns any lit buoy into an instant traversal tool—complete with outfit-colored smoke clouds, firework spark bursts, spatial whoosh audio, and zero-allocation multiplayer synchronization so your modded friends see and hear every blink.

---

## ✨ Features

- **Throw to Teleport**: Toss any lit buoy (standard white or red pedestal bulb) to teleport immediately upon impact.
- **Smart & Safe Landings**: Automatic slope-alignment, wall-rebound clearance, and downward ground sweeps ensure you never spawn inside rocks or fall into geometry.
- **Outfit-Matching VFX**:
  - Billowing smoke clouds (lingering for 3.0 seconds) tinted directly to your character's shirt/torso color.
  - Outward bursts of high-speed flare & firework sparks dynamically keyed to your player's outfit palette and highlights.
- **3D Spatial Audio**: Plays atmospheric throw whoosh sounds at both departure and arrival points.
- **Multiplayer Sync for Modded Friends**: When other players in your lobby have EnderBuoy installed, they will see your personalized smoke/sparks and hear your 3D teleport sounds whenever you blink—and you will see and hear theirs!
- **Lit-Only Toggle (`RequireLit`)**: Buoys only teleport you when their lamp is turned **ON**. Turn the buoy off to throw it around as normal lighting or pass it to friends.
- **Performance Optimized**: Zero garbage collection (0 B allocated/frame) during active gameplay. Uses Big Walk's pre-cached player registry.
- **In-Game Mod Settings**: Fully integrated with [ModSettingsMenu](https://thunderstore.io/c/big-walk/p/Ice_Box_Studio_BigWalk/ModSettingsMenu/) (F7 toggle hotkey, distance limits, audio/visual toggles).

---

## ✨ How It Works

| State | Lamp Turned OFF | Lamp Turned ON |
|---|---|---|
| **Throw Action** | Normal physics toss. Buoy bounces and rolls normally. | **Ender blink active!** Player teleports to point of first solid impact. |
| **Visual Effects** | None. | Dual departure + arrival smoke clouds & spark bursts matching player clothing. |
| **Audio** | Normal buoy throw sounds. | 3D spatial whoosh at takeoff and landing. |
| **Friends' View** | Normal throw. | Friends with the mod see your custom-colored smoke and hear the whoosh. |

---

## 📦 Requirements

- **Big Walk** ([Steam](https://store.steampowered.com/app/1478500/Big_Walk/)) with **[BepInEx 6 (IL2CPP)](https://builds.bepinex.dev/projects/bepinex_be)** installed
- [**ModSettingsMenu**](https://thunderstore.io/c/big-walk/p/Ice_Box_Studio_BigWalk/ModSettingsMenu/) ≥ 1.1.0 (for in-game settings UI)

---

## 🚀 Install

1. Close the game. (The game locks plugin DLLs while running.)
2. Download or build `EnderBuoy.dll`.
3. Drop `EnderBuoy.dll` into your game's `BepInEx/plugins/EnderBuoy/` directory.
4. Launch Big Walk.
5. Pick up any buoy, press the interact button to turn its lamp **ON**, and throw!

---

## ⚙️ Settings

Configurable via **Mod Settings** in the pause or main menu (or in `BepInEx/config/walker.enderbuoy.cfg`):

### General
| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Master switch. When disabled, the mod is completely dormant. |

### Blink
| Setting | Default | What it does |
|---|---|---|
| `BlinkEnabled` | `true` | Enables/disables teleportation on throw. Can be toggled on-the-fly via hotkey. |
| `RequireLit` | `true` | When true, only lit buoys teleport you. Unlit buoys behave normally. |
| `MaxDistance` | `60.0` | Maximum allowed teleport range in meters (10m to 200m slider). Throws that travel farther will not teleport. |
| `Diagnostics` | `false` | Enables verbose tracking and physics logs in `LogOutput.log`. |

### Effects
| Setting | Default | What it does |
|---|---|---|
| `PlaySound` | `true` | Plays 3D spatial throw sound effects at departure and arrival coordinates. |
| `SpawnSmoke` | `true` | Spawns outfit-colored smoke puffs and spark bursts at departure and arrival. |

### Input
| Setting | Default | What it does |
|---|---|---|
| `ToggleKey` | `F7` | Keyboard shortcut to toggle `BlinkEnabled` on and off. |

*(Note: A safety fuse timeout of 10 seconds is also configurable directly inside `BepInEx/config/walker.enderbuoy.cfg` to discard lost or endless-falling props.)*

---

## 🔨 Build from Source

```bash
dotnet build
```

**Requirements**: .NET 6 SDK.
The project references the game's own interop assemblies directly from your local Steam install. If your game is installed in a non-default directory, update the `<GameDir>` property in `EnderBuoy.csproj`.

The build target automatically copies the compiled `EnderBuoy.dll` straight into `BepInEx/plugins/EnderBuoy/`.

---

## 🧠 Under the Hood

- **Throw Hooking**: Uses Harmony postfixes on `Prop.SetDropped` to detect intentional player throws (filtering out gentle drops and non-local player throws).
- **Hybrid Contact Detection**: Combines Unity `PlatformingBody` collision callbacks, forward `SphereCast` prediction along the buoy's velocity vector, and abrupt deceleration fallback checks to ensure 100% reliable impact registration across rocks, trees, water, and structures.
- **Safe Repositioning**: Feeds coordinates into the game's internal `player.grease.Teleport`, clearing falling states, resetting rigidbody velocities, and updating local mover kernals without rubberbanding.
- **Visual & Audio Extraction**: Dynamically clones particle systems from existing scene emitters, reading the local and remote players' `PlayerLooks.lookSet` to colorize smoke particles and flare spark fragments to match their shirts and hats.
- **Zero-Allocation Peer Tracking**: Leverages `PlayerCharacter.allPlayerCharacters` to track remote character displacements with zero heap allocations per frame, allowing seamless multiplayer effects without network RPC overhead.

---

## 📄 License

MIT License — feel free to modify, fork, or redistribute.
