# WorldLabs Gaussian Splatting

A Unity package for generating and rendering 3D Gaussian Splatting scenes using the WorldLabs API.

## Preview

https://github.com/user-attachments/assets/13204a7d-cffc-4f7a-9c95-dfd9597af439

## Overview

This package combines:
- **WorldLabs API Client** — Generate 3D scenes from text prompts using WorldLabs' AI
- **Gaussian Splatting Renderer** — Real-time rendering of 3D Gaussian Splat assets
- **Runtime Browser UI** — In-game VR/screen-space world browser and creator
- **Editor Importer** — Unity Editor window for browsing and importing worlds as project assets

The Gaussian Splatting implementation is based on [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting), extended with runtime loading, splat layer support, and WorldLabs API integration.

---

## Requirements

- **Unity Version:** 6000.2.10f1 and 6000.3.14f1 (Recommended/Tested)
- **Render Pipeline:** Universal Render Pipeline (URP) is **required**
- **Dependencies:** Burst 1.8.8+, Collections 2.1.4+, Mathematics 1.2.6+ (installed automatically)

---

## Installation

### Via Package Manager (Git URL)

1. Open Unity and go to **Window > Package Manager**
2. Click the `+` button and select **Add package from git URL…**
3. Enter:
   ```
   https://github.com/nigelhartm/worldlabs_unity.git
   ```
4. Click **Add**

---

## Configuration

### 1. API Key Setup

1. Obtain an API key from [WorldLabs](https://worldlabs.ai)
2. Create a `.env` file in the **project root** (next to the `Assets/` folder):
   ```
   WORLDLABS_API_KEY=your_worldlabs_key
   ```
   The key is read at runtime from `StreamingAssets/.env` (copied by the build process) and in the Editor from the project root.

> **Troubleshooting:** If the key does not load, open **Window > WorldLabs > WorldLabsUnityIntegration > Settings** and click **Reload API Key**.

### 2. Graphics API Settings

**D3D11 is NOT supported.** Go to **Project Settings > Player > Other Settings > Graphics APIs** and use:
- **Windows:** D3D12 or Vulkan
- **Mac:** Metal
- **Linux / Android (Meta Quest, Pico):** Vulkan

> **Meta Quest Warning:** Adding a Camera Rig from "Meta Building Blocks" may automatically force the project to D3D11. Switch it back to Vulkan manually and ignore the Meta Quest Project Setup Tool warning about this change.

### 3. URP Renderer Configuration

1. Locate your active URP Renderer Data asset (usually in `Assets/Settings/`)
2. Click **Add Renderer Feature** and select **GaussianSplatURPFeature**
3. Recommended mobile/XR settings:
   - **Depth Texture:** On
   - **HDR:** On
   - **MSAA:** Off (Disabled)

### 4. Render Graph (Required)

1. Go to **Project Settings > Graphics**
2. **Enable** the checkbox: **Compatibility Mode (Render Graph disabled)**

### 5. XR / OpenXR Settings

If building for VR:
1. Go to **Project Settings > XR Plugin Management > OpenXR**
2. Change **Render Mode** from *Single Pass Instanced* to **Multi-pass**

---

## Runtime Setup — WorldLabsWorldManager + WorldBrowserController

These two components provide a fully in-game, API-connected world browser and loader — useful for VR builds or any runtime scene.

### Quick Start with the Sample Prefab

1. In the Package Manager select **WorldLabs Gaussian Splatting → Samples → Sensai Sample → Import**
2. The import adds:
   - `sensai.asset` — a pre-processed Gaussian Splat you can use as a default placeholder
   - `Prefabs/WorldLabs_GUI.prefab` — a ready-wired prefab with both components
3. Drag `WorldLabs_GUI.prefab` into your scene
4. Select the prefab instance, and assign `sensai.asset` to **World Manager → Default Asset**
5. Enter Play Mode — the browser canvas appears and connects to your WorldLabs account

### Manual Setup

Add both components to the **same GameObject**:

| Component | Purpose |
|-----------|---------|
| `WorldLabsWorldManager` | Downloads, processes, and renders worlds at runtime |
| `WorldBrowserController` | Builds a world-space Canvas UI for browsing and loading worlds |

`WorldBrowserController` requires `WorldLabsWorldManager` on the same GameObject (enforced by `[RequireComponent]`).

---

### WorldLabsWorldManager Inspector

> [Screenshot — WorldLabsWorldManager Inspector]

| Field | Description |
|-------|-------------|
| **Quality** | GPU memory / fidelity tradeoff: VeryHigh → VeryLow |
| **Preferred Resolution** | SPZ resolution: `FullRes`, `500k` (default), `100k` |
| **World Parent** | Transform that spawned GameObjects are parented to (defaults to self) |
| **Default Asset** | Optional `GaussianSplatAsset` shown on Start and as a loading placeholder. Assign `sensai.asset` from the sample. |
| **Default Asset Inverted** | Apply −180° X rotation (required for WorldLabs worlds) |

Shaders and compute shaders are **auto-assigned** from the package path on reset and at runtime — no manual drag-and-drop needed.

---

### WorldBrowserController Inspector

> [Screenshot — WorldBrowserController Inspector]

| Field | Description |
|-------|-------------|
| **World Manager** | Auto-resolved from the same GameObject |
| **Creation Model** | Model for text-prompt generation: `Plus` (quality) or `Mini` (speed) |
| **Canvas Pixel Size** | Size of the auto-built world-space Canvas (default 420 × 600 px) |
| **Columns** | Card columns in the world grid (default 2) |

The full UI hierarchy (Canvas, scroll view, buttons, create panel) is **auto-built at Awake** when not pre-wired. Use the sample prefab and adjust there for a custom layout.

**UI Features:**
- Paginated grid of your WorldLabs worlds with panorama thumbnails
- Tap a card to load the world; tap again to unload it
- **➕ Create** button opens a text-prompt panel to generate new worlds inline
- Progress bar and status text during download and generation

---

## Editor Loading (separate workflow)

`WorldLabsWorldManager` and `WorldBrowserController` are **runtime-only** components that stream worlds on demand from the API. They do not create project assets.

For importing worlds into your project as Unity assets, use the Editor window:

```
Window → WorldLabs → WorldLabsUnityIntegration
```

This opens `WorldLabsEditorWindow`, which lets you:
- Browse and filter your WorldLabs worlds
- Generate worlds from text, image URL, image file, or video URL
- Import worlds as `GaussianSplatAsset` files with resolution and compression quality options
- Load imported assets directly into the active scene

---

## Loading Worlds with Your Own Code

You can drive `WorldLabsWorldManager` entirely from code without `WorldBrowserController`.

```csharp
using System.Collections.Generic;
using UnityEngine;
using WorldLabs.API;
using WorldLabs.Runtime;

public class MyWorldLoader : MonoBehaviour
{
    WorldLabsWorldManager _manager;

    async void Start()
    {
        _manager = GetComponent<WorldLabsWorldManager>();

        // Subscribe to events
        _manager.OnWorldLoaded       += (id, r)    => Debug.Log($"Loaded: {id}");
        _manager.OnWorldLoadFailed   += (id, err)  => Debug.LogError($"Failed: {err}");
        _manager.OnWorldLoadProgress += (id, prog) => Debug.Log($"{id}: {prog:P0}");

        // Fetch first page of your worlds
        List<World> worlds = await _manager.ListWorldsAsync(pageSize: 10);

        if (worlds.Count > 0)
            await _manager.LoadWorldAsync(worlds[0]);
    }

    void Unload(string worldId)     => _manager.UnloadWorld(worldId);
    void RestoreDefault()           => _manager.RestoreDefaultWorld();
}
```

### Available API

```csharp
// List worlds (paginated)
List<World> worlds = await manager.ListWorldsAsync(
    pageToken: null,              // pass manager.LastNextPageToken for next page
    pageSize:  20,
    status:    WorldStatus.SUCCEEDED,
    isPublic:  null);

// Load a world (download → GPU processing → spawn renderer)
GaussianSplatRenderer r = await manager.LoadWorldAsync(world);

// Unload one world
manager.UnloadWorld(worldId);

// Unload everything and restore the default asset placeholder
manager.RestoreDefaultWorld();

// Unload everything
manager.UnloadAllWorlds();

// Query state
bool loaded  = manager.IsWorldLoaded(worldId);
bool loading = manager.IsWorldLoading(worldId);
IReadOnlyList<World> cached = manager.CachedWorlds;
string nextPage = manager.LastNextPageToken;  // null = no more pages

// C# events
manager.OnWorldsListed      += (List<World> list)          => { };
manager.OnWorldLoadStarted  += (string id)                 => { };
manager.OnWorldLoadProgress += (string id, float progress) => { };
manager.OnWorldLoaded       += (string id, GaussianSplatRenderer r) => { };
manager.OnWorldLoadFailed   += (string id, string error)   => { };
manager.OnWorldUnloaded     += (string id)                 => { };
```

---

## Sample

Import **Sensai Sample** via Package Manager to get:

| File | Description |
|------|-------------|
| `sensai.asset` | Pre-processed `GaussianSplatAsset` — assign to **Default Asset** in `WorldLabsWorldManager` |
| `sensai_*.bytes` | Binary splat data referenced by `sensai.asset` |
| `Prefabs/WorldLabs_GUI.prefab` | Ready-wired prefab with `WorldLabsWorldManager` + `WorldBrowserController` and a world-space Canvas |

---

## Generating and Rendering Scenes (Editor)

1. Open **Window > WorldLabs > WorldLabsUnityIntegration**
2. Go to **Create World**, enter a text prompt (or provide an image/video URL)
3. Click **Generate** — the operation tracks in the **My Worlds** list
4. Once complete, click **Import** on the world card and choose a resolution
5. Add a `GaussianSplatRenderer` component to a GameObject and assign the imported asset

### Best Practices

- **Lighting:** Gaussian Splat scenes are pre-lit — removing Unity directional lights avoids double-lighting
- **Mobile/XR:** Keep models at **500k points / Medium Quality** for smooth framerates on standalone headsets

---

## License

MIT — see [LICENSE.md](LICENSE.md)

## Acknowledgements & Credits

Check out our other kits:
- [SensAI Kits](https://github.com/SensAIHackademy/SensAIKits) – context-aware AI templates  
- [World Model Kits](https://github.com/SensAIHackademy/SensAIWorldModelKits) – WebXR, Unity, Unreal templates  
- [PICO Kits](https://github.com/SensAIHackademy/SensAI-PICO-Kits) – world models + voice command templates  

- Explore [SensAI Hacks](https://sensaihack.com) and connect with a community of creators and innovators.   
- Visit our [Knowledge Hub](https://xrbootcamp.notion.site/SensAI-Knowledge-Hub-21f0095e34d880ec9826d9749ae56619) for curated resources.

Powered by **SensAI Hackademy**
