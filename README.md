> [!IMPORTANT]
> **Unofficial community port for Intel Macs.** This port is maintained by FarMC666 and targets macOS 15 or later on Intel (x86_64). ImageGlass was created by Duong Dieu Phap. [Upstream ImageGlass](https://github.com/d2phap/ImageGlass)

<div align="center">

# 🖼️ ImageGlass

### A Fast, Seamless Photo Viewer

ImageGlass is a fast, modern, open-source image viewer built for Windows, macOS, and Linux. Designed for speed and efficiency, it delivers a smooth, immersive viewing experience by combining high-performance rendering with powerful tools for both everyday users and designers. ImageGlass provides seamless, quick navigation across 90+ image formats, including `WEBP`, `JXL`, `SVG`, `HEIC`, `AVIF`, `HDR`, and raw images.

<br/>

[![Total downloads](https://img.shields.io/github/downloads/d2phap/imageglass/total?color=%23d60068&label=Total%20downloads&)](https://imageglass.org/pricing)
[![Latest version downloads](https://img.shields.io/github/downloads/d2phap/imageglass/latest/total?color=%23e66700&label=Latest%20version&)](https://imageglass.org/pricing)

[![Discord](https://img.shields.io/discord/818852544859209748?label=chat&logo=discord&color=%233097B8&style=social)](https://discord.gg/tWjbynH2X8)
[![Twitter Follow](https://img.shields.io/twitter/follow/duongdieuphap?style=social)](https://twitter.com/duongdieuphap)
[![Crowdin](https://d322cqt584bo4o.cloudfront.net/imageglass/localized.svg)](https://crowdin.com/project/imageglass)

<br/>

[**🌐 Website**](https://imageglass.org) &nbsp;•&nbsp;
[**📥 Pricing**](https://imageglass.org/pricing) &nbsp;•&nbsp;
[**📚 Docs**](https://imageglass.org/docs) &nbsp;•&nbsp;
[**💬 Discord**](https://discord.gg/tWjbynH2X8) &nbsp;•&nbsp;
[**💖 Donate**](https://imageglass.org/donate)

<br/>

[![ImageGlass 10](https://github.com/user-attachments/assets/ca49c1be-4ab6-4714-b23b-8b094b5eda90)](https://imageglass.org/news/introducing-imageglass-10-rebuilt-for-speed-and-cross-platform-support-107)

</div>


<div align="center">

[**Intel Mac releases**](https://github.com/FarMC666/ImageGlass-macOS/releases) &nbsp;•&nbsp; [**Official ImageGlass downloads**](https://imageglass.org)

</div>

<br/>


## Install or update on an Intel Mac

Requires an Intel Mac running macOS 15 or later. The build was tested on Intel macOS 15.8.3. The app includes its .NET runtime.

1. Download `ImageGlass_<version>_mac-x64.dmg` from the [Intel port’s GitHub Releases](https://github.com/FarMC666/ImageGlass-macOS/releases). Run `shasum -a 256 "/path/to/the/downloaded.dmg"` and compare the result with the SHA-256 shown for the release asset.
2. Quit ImageGlass if it is open. Open the DMG, drag **ImageGlass Intel** to **Applications**, and choose **Replace** if prompted. Eject the DMG and open the installed app from **Applications**.
3. The preview uses an ad-hoc signature and is not notarized. If macOS blocks the first launch, verify the download and follow Apple’s instructions for [opening an app you trust](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unidentified-developer-mh40616/mac).

**If you’re using the `10.0.6.906-mac-x64.1` preview, install `10.0.6.906-mac-x64.2` manually.** The earlier preview had update checks disabled. Starting with `.2`, **Help > Check for update** checks this repository, and **Settings > General > App update** controls checks at startup, roughly once a week. Published Intel previews are included. An increase in the release sequence, such as `.2` to `.3`, counts as an update even when the application version is the same. An update opens the Intel x64 DMG download in your browser. Use the steps above to install it.

Report port bugs through [Intel port Issues](https://github.com/FarMC666/ImageGlass-macOS/issues). For security problems, use the [private reporting instructions](SECURITY.md).

## Features
Primarily a photo viewer, ImageGlass offers a wide array of features geared toward image viewing, along with some focused editing capabilities. Here are some of the key features:
- Supports [90+ image formats](https://imageglass.org/docs/supported-formats) out of the box
- Hardware-aware smart caching
- Super-fast image browsing with Turbo mode
- Slideshow with random interval and sound notification
- Native SVG and SVGZ vector rendering
- Animated GIF, WEBP, and SVG playback
- Color management and basic HDR support
- Different cursor-precise zoom modes with flexible image interpolations
- Different window modes: Frameless, Window Fit, Full screen
- Built-in Tools: Rotate, Flip, Crop, Resize, Color Picker, Frame Navigation, Lossless compression...
- EXIF metadata viewing through the [ExifGlass tool](https://github.com/d2phap/ExifGlass)
- Touch gestures for zoom and pan
- Themes, layouts, and custom hotkeys
- Custom action binding for toolbar, mouse...
- [Plugins](https://imageglass.org/plugins) and [external tools](https://imageglass.org/tools) support via [ImageGlass.SDK](https://github.com/ImageGlass/SDK)
- Windows: Explorer sort order support
- Windows: Custom file type icon pack (Not available for Store release)

👉 Visit https://imageglass.org/docs/features to explore all features of ImageGlass.

<br/>


## System Requirements
**Version 10**
- Windows 10/11 x64 or arm64, version 1809 (build 17763) or later
- macOS 14+, Apple Silicon arm64
- macOS 15+, Intel x86_64 for this community port. [Build instructions](CONTRIBUTING.md#intel-macos)
- Linux Desktop X11 x64

**Version 9**
- Windows 10/11 x64 or arm64, version 1809 (build 17763) or later
- Optional: [WebView2 Runtime 64-bit v119.0.2151 or later](https://go.microsoft.com/fwlink/p/?LinkId=2124703)

<br/>


## Development
The Intel port is maintained on `macos-x64`. See [CONTRIBUTING.md](CONTRIBUTING.md#syncing-an-upstream-release) for upstream sync and Intel release checks.

**Version 10**: Located in `source` folder
- Visual Studio 2026 for Windows build
- VS Code for macOS, Linux build

**Version 9**: Located in `v9` folder
- Visual Studio 2026 on Windows 11
- VS Code for `WebUI`

<br/>


## Roadmap

[![ImageGlass 2026 roadmap](https://github.com/user-attachments/assets/9cf2a8a4-18f4-4852-ad83-5df79239d93f)](https://github.com/d2phap/ImageGlass/discussions/2287)

<br/>


## Support upstream ImageGlass

You can support the original ImageGlass project by donating or purchasing ImageGlass Pro from its official website.

#### 👉 Explore the ways to support at [imageglass.org/donate](https://imageglass.org/donate).

