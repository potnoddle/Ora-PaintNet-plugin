# OpenRaster FileType Plugin for Paint.NET

A filetype plugin for [Paint.NET](https://getpaint.net/) that enables loading and saving layered images in the OpenRaster (`.ora`) format.

## Overview

[OpenRaster](https://www.openraster.org/) is an open, cross-application file format designed for raster image editing with layer preservation. It is supported by applications including Krita, MyPaint, and GIMP. This plugin integrates native `.ora` read and write support directly into Paint.NET.

## Key Features

- **Layer Preservation**: Loads and exports multi-layered images with full position offsets (`x`, `y` coordinates).
- **Blend Modes**: Translates Paint.NET blend modes to OpenRaster SVG composite operations (`svg:src-over`, `svg:multiply`, `svg:screen`, `svg:overlay`, `svg:darken`, `svg:lighten`, `svg:color-dodge`, `svg:color-burn`, `svg:plus`, `svg:difference`, `svg:xor`, etc.) and vice versa.
- **Opacity & Visibility**: Preserves per-layer opacity levels and visibility flags.
- **Specification Compliance**:
  - Uncompressed `mimetype` header (`image/openraster`) placed at the start of the archive.
  - Generates standard `stack.xml` metadata conforming to OpenRaster v0.0.5.
  - Embeds full flattened composite image (`mergedimage.png`).
  - Generates proportional thumbnail previews (`Thumbnails/thumbnail.png`) up to 256x256 px.
- **Tool Interoperability**: Retains MyPaint stroke map data and background tile attachments across edit sessions.
- **DPI Handling**: Preserves resolution settings across inch, centimeter, and pixel units.

## System Requirements

- Paint.NET 5.x or later
- Windows 10 / 11 (x64 / ARM64)
- .NET 9.0 Windows Desktop Runtime

## Installation

### Automated via PowerShell (Recommended)

1. Build the solution (or download `OpenRasterFileType.dll`).
2. Run `install.ps1` in PowerShell (or right-click and select **Run with PowerShell**):
   ```powershell
   .\install.ps1
   ```
   *Note: The script will automatically request Administrator elevation to copy the file into Program Files.*

### Manual Installation

1. Close Paint.NET if running.
2. Download or build `OpenRasterFileType.dll`.
3. Copy `OpenRasterFileType.dll` into the Paint.NET FileTypes directory:
   ```text
   C:\Program Files\Paint.NET\FileTypes\
   ```
4. Restart Paint.NET. The OpenRaster option (`*.ora`) will now appear in the Open and Save dialogs.

## Building from Source

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download)
- Paint.NET installed in default location (`C:\Program Files\Paint.NET\`) for reference assemblies:
  - `PaintDotNet.Base.dll`
  - `PaintDotNet.Core.dll`
  - `PaintDotNet.Data.dll`
  - `PaintDotNet.Effects.dll`
  - `PaintDotNet.Primitives.dll`
  - `PaintDotNet.ComponentModel.dll`

### Build Command

```powershell
dotnet build "OpenRaster Filetype.csproj" -c Release
```

A post-build step will automatically copy the resulting assembly to your local Paint.NET `FileTypes` directory if present.

## Architecture

```text
OpenRaster Archive (.ora)
├── mimetype                 # Uncompressed "image/openraster" identifier
├── stack.xml                # Document root, layer definitions, blend modes, coords
├── mergedimage.png          # Flattened preview of the composite canvas
├── Thumbnails/
│   └── thumbnail.png        # 256x256 scaled preview
└── data/
    ├── layer0.png           # Individual raster layers (top-to-bottom order)
    ├── layer1.png
    └── ...
```

## Contributing

Contributions, bug reports, and pull requests are welcome. Feel free to open an issue or submit a pull request.
