# WorldLabs Gaussian Splatting

A Unity package for generating and rendering 3D Gaussian Splatting scenes using the WorldLabs API.

## Overview

This package integrates the WorldLabs AI generation capabilities directly into Unity, allowing you to generate 3D scenes from text prompts and render them in real-time.

**Note:** The rendering implementation is based on [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting) but has been modified significantly for WorldLabs API integration, layer support, and custom asset workflows.

## Requirements

- **Unity Version:** 6000.2.10f1 (Recommended/Tested)
- **Render Pipeline:** Universal Render Pipeline (URP) is **required**.
- **Dependencies:** Burst 1.8.8+, Collections 2.1.4+, Mathematics 1.2.6+ (installed automatically).

## Installation

### 1. Install via Package Manager
1. Open Unity and go to **Window > Package Manager**.
2. Click the `+` button and select **Add package from git URL...**
3. Enter the following URL and click **Add**:

'''
https://github.com/nigelhartm/worldlabs_unity.git
'''

### 2. Install Samples (Optional)
To verify your setup, import the **Hanok Sample** (a traditional Korean Hanok scene) via the **Samples** tab in the Package Manager.

## Configuration

This package requires specific project settings to function correctly, particularly for Mobile/XR builds.

### 1. API Key Setup
1. Obtain an API key from [WorldLabs](https://worldlabs.ai).
2. Create a file named `.env` in the **root folder** of your Unity project.
3. Add your key to the file:

'''
WORLDLABS_API_KEY=your_worldlabs_key
'''

4. **Troubleshooting:** If the key does not load, open **Window > WorldLabs > Settings** (or the integration window) and click **Reload API Key**.

### 2. Graphics API Settings
Ensure you are using a supported Graphics API. **D3D11 is NOT supported.**

- **Go to:** `Project Settings > Player > Other Settings > Graphics APIs`
- **Windows:** Use **D3D12** or **Vulkan**.
- **Mac:** Use **Metal**.
- **Linux/Android:** Use **Vulkan**.

> **Warning for Meta Quest Developers:** Adding a Camera Rig from "Meta Building Blocks" may automatically force your project to **D3D11**. You must manually switch it back to a supported API (D3D12/Vulkan). Ignore any warnings from the Meta Quest Project Setup Tool regarding this change.

### 3. URP Renderer Configuration
1. Locate your active URP Renderer Data asset (usually in `Assets/Settings/`).
2. Click **Add Renderer Feature** and select **GaussianSplatURPFeature**.
3. **Mobile/XR Settings:**
- **Depth Texture:** On
- **HDR:** On
- **MSAA:** Off (Disabled)

### 4. Render Graph (Crucial)
1. Go to **Project Settings > Graphics**.
2. **Enable** the checkbox: `Compatibility Mode (Render Graph disabled)`.

### 5. XR Specifics (OpenXR)
If building for VR/XR:
1. Go to **Project Settings > XR Plugin Management > OpenXR**.
2. Change the **Render Mode** from *Single Pass Instanced* to **Multi-pass**.

## Usage

### Generating Scenes
1. Open **Window > WorldLabs > Generator**.
2. Enter a text prompt describing your desired scene.
3. Click **Generate**.
4. The system will create a `.ply` or `.splat` asset.

### Rendering Scenes
1. Create an empty GameObject.
2. Add the `GaussianSplatRenderer` component.
3. Assign your generated Asset to the component.

### Best Practices & Optimization
- **Lighting:** If your splat scene is fully captured, consider removing Unity light sources (Directional Lights, etc.) to save performance, as the splats are already lit.
- **Mobile/XR Budgets:** For standalone headsets (e.g., Quest) or mobile devices, keep models under **500k points** (Medium Quality) to ensure smooth framerates.

## Known Limitations

- **Runtime Loading:** The ability to load new worlds dynamically while inside the headset (runtime) is currently not supported.

## License

This package is released under the MIT License.
