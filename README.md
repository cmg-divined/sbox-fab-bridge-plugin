# FabBridge for s&box

Import assets directly from [Fab](https://www.fab.com/) (Epic Games Marketplace) into s&box with a single click.

## What is this?

FabBridge is an s&box editor tool that acts as a bridge between the Epic Games Launcher and s&box. When you click "Add to Project" on any Fab asset and select the custom socket export option, FabBridge receives the asset data and automatically imports it into your s&box project.

This means you can browse Megascans, marketplace 3D models, and other Fab content and bring them straight into s&box without manual file copying or format conversion.

## Features

- **One-click import** - Export from Fab, asset appears in s&box
- **Automatic FBX to VMDL conversion** - Models are converted to s&box format automatically
- **Material generation** - Creates `.vmat` materials with proper PBR texture assignments
- **Texture handling** - Copies and organizes textures with correct naming conventions
- **Material assignment** - Generated materials are automatically assigned to imported models

## Installation

1. Clone or download this repository into your s&box addons/libraries folder
2. Open s&box and your project
3. The FabBridge library should be available in your project

## Usage

1. In s&box, go to **Tools > Open Fab Bridge** to open the bridge widget
2. The bridge starts a local server on port `24981` (configurable)
3. In the Epic Games Launcher, go to Fab and find an asset you want
4. Click **Add to My Library** on the asset
5. In the export dialog, select **Custom (socket port)** as the target and set 24981 as the setting (click the your settings blue button at the bottom).
6. Enter port `24981` (or whatever port you configured)
7. Click export - the asset will be imported into your s&box project under `Assets/fab_imports/`

## How it works

FabBridge runs a TCP socket server that listens for incoming connections from the Epic Games Launcher. When you export an asset from Fab:

1. Fab sends a JSON payload containing asset metadata, file paths, and texture information
2. FabBridge parses this data and identifies meshes, textures, and materials
3. Textures are copied to your project with standardized naming (`_color`, `_normal`, `_rough`, etc.)
4. A `.vmat` material is generated referencing the imported textures
5. FBX models are converted to `.vmdl` using s&box's built-in model compiler
6. The generated material is assigned as the model's default material

## Project Structure

```
fabbridge/
├── Editor/
│   ├── FabBridgeServer.cs      # TCP server for receiving Fab exports
│   ├── FabJsonProtocol.cs      # JSON parsing for Fab data format
│   ├── FabAssetData.cs         # Data models for Fab JSON structure
│   ├── FabImportHandler.cs     # Orchestrates the import process
│   ├── FabBridgeMenu.cs        # Editor menu integration
│   ├── UI/
│   │   └── FabBridgeWidget.cs  # Dockable editor UI
│   └── Converters/
│       ├── FabTextureConverter.cs  # Texture copying and VTEX generation
│       ├── FabModelConverter.cs    # FBX to VMDL conversion
│       └── FabMaterialConverter.cs # VMAT generation
```

## Supported Asset Types

- **3D Models** - FBX files are converted to VMDL
- **Textures** - JPG, PNG, TGA, EXR, TIF, BMP
- **PBR Materials** - Albedo, Normal, Roughness, Metalness, AO, Displacement, Opacity, Emissive

## Known Limitations

- Only tested with Megascans/Quixel assets from Fab
- LOD support is basic - imports LOD0 by default
- Some complex materials may need manual adjustment after import

## Requirements

- s&box
- Epic Games Launcher with Fab access

## License

Do whatever you want with this. No warranty, use at your own risk.

## Credits

Built for the s&box community.
