# Security policy for the Intel macOS port

This policy applies to the unofficial Intel macOS builds published by [FarMC666/ImageGlass-macOS](https://github.com/FarMC666/ImageGlass-macOS/releases). For official ImageGlass releases, follow the [upstream security policy](https://github.com/d2phap/ImageGlass/security/policy).

## Supported builds

Use the newest published Intel x64 DMG for macOS 15 or later. Security fixes for the port ship in that release channel and apply to both Classic and Pro. The `10.0.6.906-mac-x64.2` prerelease updates the decoder for the [ImageMagick TIFF advisory](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-gr4q-6v79-qj3g) and is the first build with update checks enabled. If you have `.1`, use the [manual upgrade steps](README.md#install-or-update-on-an-intel-mac).

## Report a vulnerability privately

Sign in to GitHub and choose **Report a vulnerability** in the [Intel port’s Security tab](https://github.com/FarMC666/ImageGlass-macOS/security/advisories/new). Use the private form for issues in the Intel app, its bundled dependencies, the DMG or its update channel. Keep security reports and sensitive samples out of public issues and discussions.

Include the exact Intel release tag, macOS version, expected and observed behavior, and any logs or files needed to understand the issue. Clearly label malformed image samples and remove private information from logs. Coordinate disclosure through the private report.

Use [Intel port Issues](https://github.com/FarMC666/ImageGlass-macOS/issues) for ordinary bug reports.

## Update check privacy

Intel builds check public GitHub release metadata without a GitHub token or ImageGlass usage tokens. Image contents and local file paths are not sent. The automatic check can be turned off under **Settings > General > App update**. Manual checks are still available from **Help > Check for update**.
