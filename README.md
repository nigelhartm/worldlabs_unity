# WorldLabs Gaussian Splatting

A Unity package for generating and rendering 3D Gaussian Splatting scenes using the WorldLabs API.

## Overview

This package combines:
- **WorldLabs API Client** - Generate 3D scenes from text prompts using WorldLabs' AI
- **Gaussian Splatting Renderer** - Real-time rendering of 3D Gaussian Splat assets

### About Gaussian Splatting

The Gaussian Splatting implementation in this package is based on [UnityGaussianSplatting](https://github.com/aras-p/UnityGaussianSplatting). Due to the significant modifications and extensions required for WorldLabs integration (including splat layer support, custom asset creation workflows, and API integration), the code is kept combined in this package rather than as a separate dependency.

## Installation

### Via Git URL (Recommended)

1. Open Unity Package Manager (`Window > Package Manager`)
2. Click the `+` button and select "Add package from git URL..."
3. Enter:
   ```
   https://github.com/nigelhartm/worldlabs_unity.git
   ```
4. Click Add

### Manual Installation

1. Download or clone this repository
2. Copy the `Package` folder contents to your project's `Packages/com.worldlabs.gaussian-splatting` folder

## Requirements

- Unity 2022.3 or later
- Burst 1.8.8+
- Collections 2.1.4+
- Mathematics 1.2.6+

These dependencies will be installed automatically via Package Manager.

## Getting Started

### 1. Configure WorldLabs API

1. Obtain an API key from [WorldLabs](https://worldlabs.ai)
2. Create a `.env` file in your project root with:
   ```
   WORLDLABS_API_KEY=your_api_key_here
   ```

### 2. Open the WorldLabs Editor

1. Go to `Window > WorldLabs > Generator`
2. Enter a text prompt describing your scene
3. Click "Generate" to create a 3D Gaussian Splat scene

### 3. Using Gaussian Splat Assets

1. Import a `.ply` or `.splat` file, or use WorldLabs to generate one
2. Add a `GaussianSplatRenderer` component to a GameObject
3. Assign your Gaussian Splat Asset

## Samples

Import the **Hanok Sample** via Package Manager to see an example scene with a traditional Korean Hanok in spring.

## Render Pipeline Support

- **Built-in Render Pipeline**: Fully supported
- **URP (Universal Render Pipeline)**: Supported via `GaussianSplatURPFeature`
- **HDRP (High Definition Render Pipeline)**: Supported via `GaussianSplatHDRPPass`

## License

This package is released under the MIT License.

