# Intel Magick.Native build

Build on Intel macOS 15. The initial toolchain is Apple clang 17.0.0
(clang-1700.0.13.5), macOS SDK 15.5, .NET SDK 10.0.404, GNU Autoconf 2.73,
Automake 1.20, GNU Libtool 2.6.2, CMake 4.5.0, GNU sed 4.10, NASM 3.02,
pkgconf 3.0.11, Meson 1.11.1, Ninja 1.13.0, and Ragel 6.11. Use the system
Xcode Command Line Tools and a tool-only prefix. For Meson and Ninja a
Python virtual environment works. Keep the installation of unrelated
codec libraries out of the tool path.

`sources.tsv` pins the upstream repositories, commits, and download
digests. The build restores the upstream macOS Intel entry and builds
static delegates before linking Magick.Native. OpenEXR comes from its
upstream 3.5.5 tag. ImageMagick uses its 7.1.2-36 release commit.

From this directory, with the specified tools in PATH, run:

```sh
python3 build-native.py /private/tmp/ig-codec-build-1 --downloads /private/tmp/ig-codec-downloads
python3 build-native.py /private/tmp/ig-codec-build-2 --downloads /private/tmp/ig-codec-downloads
```

Use a dedicated empty directory for each build. After a failed step,
`--resume` reuses the already built dependencies with the same toolchain
and source pins. The native dylib is under `Magick.Native/artifacts/`
in each build directory. Compare their SHA-256 digests before packaging.

The Intel package pins ImageMagick's [TIFF decoder fix][tiff] and later
[HEIC][heic], [EXR][exr], and [pixel export][pixels] fixes. OpenEXR 3.5.5
also carries OpenEXR's separate EXR parser fixes. The pinned delegates
include the libheif 1.23.7 and libjxl 0.13.0 security releases and
libtiff 4.7.2. The probe checks that native delegates are present and
decodes trusted TIFF, HEIC, AVIF, JXL, and RAW samples.

[tiff]: https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-gr4q-6v79-qj3g
[heic]: https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-g3c3-wcx5-7qrw
[exr]: https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-7pvc-jpr7-9wxq
[pixels]: https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-qw9c-mq27-w844

For `15.1.0-intel.1`, the two independently compiled Intel outputs had the
following SHA-256 values after normalizing their build paths:

| Output | SHA-256 |
| --- | --- |
| Native dylib before signing | `9a9a26895b65a59aec3b35a3fb5f7cb2a80401e071148e7d9eaefad8ec549c53` |
| Native NuGet package | `2603e6f6a97cdb510ffe3a260020d49f0fdf996d0761cf06b800733b9c387bc9` |

```sh
lib=/private/tmp/ig-codec-build-1/Magick.Native/artifacts/Magick.Native-Q16-HDRI-x64.dll.dylib
lipo -archs "$lib"
vtool -show-build "$lib"
otool -l "$lib"
otool -L "$lib"
nm -gjU "$lib" > /private/tmp/ig-codec-exports.txt
shasum -a 256 "$lib"
```

Use the verified dylib, its notices, and a local NuGet feed:

```sh
notices=/private/tmp/ig-codec-build-1/NOTICE.txt
feed="$PWD/feed"
python3 pack-native.py "$lib" "$notices" "$feed"
dotnet publish CodecProbe.csproj -c Release -r osx-x64 --self-contained true \
  -p:MagickVersion=15.1.0 -p:RestoreAdditionalProjectSources="$feed" \
  -p:PublishAot=true -p:PublishTrimmed=true -p:PublishSingleFile=true -p:PublishReadyToRun=false \
  -o "$probe"
"$probe/CodecProbe" 7.1.2-36 "$trusted_sample"
"$probe/CodecProbe" --check-exports "$managed_assembly" /private/tmp/ig-codec-exports.txt
```

For the official 14.17.2 Intel control, omit `-p:MagickVersion=15.1.0` and
expect version `7.1.2-32`. For the CoreCLR probe, use
`-p:PublishAot=false -p:PublishTrimmed=false` with a separate output directory.
