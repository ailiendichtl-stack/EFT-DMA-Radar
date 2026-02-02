# Twilight PVE Radar

A feature-rich DMA-based radar for Escape from Tarkov, forked from [Lone EFT DMA Radar](https://github.com/lone-dma/Lone-EFT-DMA-Radar) with additional enhancements.

**Note:** This project is primarily developed and tested for **PVE mode**. Most features should work in PVP as well, but PVP compatibility is not actively tested.

## Features

### Radar & Tracking
- Real-time player tracking with team/group detection
- PMC, Scav, Boss, and Raider identification
- Player gear value estimation
- Corpse and death marker tracking
- Exfil point display with status indicators
- Quest location markers and objective tracking
- Hazard zone visualization (minefields, sniper zones)

### Loot System
- Loose loot scanning with price filtering
- Container contents scanning (PVE/Offline)
- Custom loot filters with color coding
- Quest item highlighting
- Hideout upgrade item tracking
- Corpse loot inspection

### Widgets & UI
- Aimview widget with configurable FOV
- Player info widget (health, gear, distance)
- Freelook mode with smooth panning
- Multi-map support with auto-detection
- Configurable draw distances and entity visibility
- Modular floating panel UI with drag, resize, and collapse
- Auto-collapsing sidebar navigation
- SkiaSharp-powered radar rendering

### ESP & Overlay
- DX9 Fuser overlay with mini-radar
- Box ESP, skeleton ESP, distance markers
- Health bars and player names
- Loot ESP with filtering

### Memory Features
- Device aimbot (KMBox support)
- Silent aim
- No recoil / no sway
- Infinite stamina
- Thermal/NVG toggle
- Loot through walls

*Memory write features are from the original Moulman fork and have not been tested by the current maintainer.*

### Web Radar
- Remote web-based radar access
- Real-time data streaming
- Mobile-friendly interface

## Requirements

- Windows 11 (tested on 23H2/25H2)
- DMA hardware (FPGA-based)
- .NET 8.0 Runtime
- 1920x1080 resolution recommended

## Common Issues

### DX Overlay/D3DX Errors

If you see errors like:
```
DX overlay init failed
ESP DX init failed: System.DllNotFoundException: Unable to load DLL 'd3dx943.dll'
```

**Fix:** Download and install the [DirectX End-User Runtime (June 2010)](https://www.microsoft.com/en-us/download/details.aspx?id=8109) from Microsoft.

Do **not** download DLL files from third-party sites.

### Performance Issues

Running both radar and fuser overlay simultaneously may impact performance on lower-end hardware. Consider disabling the overlay if experiencing issues.

## Building

```bash
dotnet build -c Release
```

Output will be in `bin/Release/net8.0-windows/`

## Credits

- [Lone DMA](https://github.com/lone-dma) - Original Lone EFT DMA Radar
- [Moulman](https://github.com/Moulman) - ESP, aimbot, and memory-write features

## Contributing

Contributions welcome. Fork the repository and submit pull requests for features or fixes.

## Disclaimer

For educational purposes only. Use at your own risk.
