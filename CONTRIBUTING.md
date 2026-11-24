# Contributing to ImageGlass

## Introduction

First, thank you for considering contributing to ImageGlass! It's people like you that make the open source community such a great community! 😊

ImageGlass 10 is a full rewrite in C# / .NET 10 with Avalonia and SkiaSharp, running on **Windows, macOS, and Linux**. That means there is more to help with than ever: new platforms to test, new formats to verify, and a lot of UI to polish.

Any type of contribution is welcome, not only code:

- **QA**: file bug reports. The more detail the better (OS and version, ImageGlass version, the image file that triggers it, screenshots or a screen recording).
- **Code**: take a look at the [open issues](https://github.com/d2phap/ImageGlass/issues). Even if you can't write code, commenting on an issue and confirming that it affects you helps with triage.
- **Translation**: ImageGlass should be usable by anyone, even if they don't speak English. See [Translations](#translations) below.
- **Docs**: improvements to the [documentation](https://imageglass.org/docs), this file, or the in-app text are always welcome.
- **Community**: presenting the project at meetups, writing blog posts, answering questions on [Discord](https://discord.gg/tWjbynH2X8) or in [Discussions](https://github.com/d2phap/ImageGlass/discussions).
- **Money**: ImageGlass is built and maintained by one person. You can support it through [GitHub Sponsors](https://github.com/sponsors/d2phap), [PayPal](https://www.paypal.me/d2phap), [donate](https://imageglass.org/donate), or by buying [ImageGlass Pro](https://imageglass.org/pricing).


## Repository layout

| Path | What it is |
| --- | --- |
| [source/](source/) | ImageGlass 10 (.NET 10, Avalonia, SkiaSharp). This is where active development happens. |
| [v9/](v9/) | Legacy ImageGlass 9 (WinForms). Archived; only critical fixes are considered. |

Use `macos-x64` as the base for Intel port pull requests. Upstream ImageGlass uses `develop` for contributions and `prod` for stable releases.


## Building and running

**Version 10** (in [source/](source/)):

- Visual Studio 2026 for the Windows build, or VS Code for the macOS and Linux builds
- .NET 10 SDK
- Open `source/ImageGlass.slnx`, or use the CLI:

```powershell
# build the Windows app (x64 is required; the default AnyCPU fails on native deps)
dotnet build ImageGlass.Win32 -p:Platform=x64

# run it with an image
dotnet run --project ImageGlass.Win32 -p:Platform=x64 -- "C:\path\to\photo.jpg"
```

Use `ImageGlass.Mac` or `ImageGlass.Linux` instead of `ImageGlass.Win32` on those platforms.

> If the app fails to start with a theme error, copy `source/__assets/__app/_themes` next to the built executable. A plain `dotnet build` output does not include the theme assets that the packaged builds ship with.

**Version 9** (in [v9/](v9/)): Visual Studio 2026 on Windows 11, plus VS Code for `WebUI`.

### Intel macOS

Build on macOS 15 or later with the Intel .NET 10 SDK and Xcode Command Line Tools. From `source/`, run:

```sh
publish_dir="$PWD/__artifacts/publish/osx-x64-aot"
dotnet publish ImageGlass.Mac -c Release -r osx-x64 -p:Platform=x64 \
  --self-contained true -p:PublishAot=true -p:PublishTrimmed=true \
  -p:PublishSingleFile=true -p:PublishReadyToRun=false -o "$publish_dir"
ditto __assets/__app "$publish_dir"
bash __assets/mac/script-pack-mac-app.sh --rid osx-x64 --publish-dir "$publish_dir" --sign adhoc
bash __assets/mac/script-pack-mac-dmg.sh --rid osx-x64 --sign adhoc
```

The VS Code `pack-mac-x64-dmg` task runs the same sequence. The app is under
`__artifacts/bundle/osx-x64/` and the disk image is under `__artifacts/bundle/`.
Both are for local testing and the app bundles its runtime. To test the CoreCLR build,
publish to a separate directory with the same RID and self-contained setting and set all
four `Publish*` options above to `false`. Pass that directory to the app packer.

The Intel build uses Magick.NET 15.1.0 and a locally packaged Q16-HDRI Magick.Native
without OpenMP. It embeds ImageMagick 7.1.2-36, which includes the fix for
[the upstream TIFF advisory](https://github.com/ImageMagick/ImageMagick/security/advisories/GHSA-gr4q-6v79-qj3g).
See the [pinned Intel native build and verification instructions](source/__assets/mac/codec/README.md).
The Intel app has its own bundle identifier and uses the regular ImageGlass per-user
configuration and single-instance lock.

Developer ID packaging requires an explicit `--sign developer-id`, your own
`SIGN_IDENTITY`, and a `NOTARY_PROFILE` for the disk-image step.

### Intel update channel and release verification

The `github-intel` channel uses the anonymous [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#list-releases) for [FarMC666/ImageGlass-macOS](https://github.com/FarMC666/ImageGlass-macOS/releases). It considers published releases and prereleases with a matching Intel tag and uploaded DMG. The comparison uses the four-part application version first and the Intel release sequence second.

- Set `IgIntelReleaseSequence` in [ImageGlass.Mac.csproj](source/ImageGlass.Mac/ImageGlass.Mac.csproj) before building. Use the same number in `<IgVersion>-mac-x64.<sequence>`. Start at `1` for a new application version, and increment the sequence for each new DMG with the same application version.
- From `source/`, run `dotnet run --project __assets/mac/update-check/UpdateCheck.csproj -c Release`. The fixtures cover sequence ordering, skipped releases, pagination and asset filtering. On an Intel test Mac, check **Help > Check for update** and launch with automatic checks enabled and a last-check timestamp older than eight days. Check that a launch shortly afterward keeps the timestamp and that opting out still allows manual checks.
- Verify the DMG with `hdiutil verify` and record its `shasum -a 256`. Mount it and run `codesign --verify --deep --strict` on the App. Check the main executable and bundled Magick dylib for the x86_64 slice and macOS deployment target.
- Draft the prerelease against the exact tested commit. Check the tag, release notes and the `ImageGlass_<IgVersion>_mac-x64.dmg` asset, including its SHA-256. State the ad-hoc signature, notarization and tested macOS versions. After publication, confirm that the anonymous release API exposes the asset and that the downloaded DMG still has the same hash.

### Syncing an upstream release

Use `origin` for this port. Keep upstream limited to fetching `prod`. From the repository root, add the remote if absent and configure the fetch and push URLs.

```sh
git remote add upstream https://github.com/d2phap/ImageGlass.git # first-time setup only
git remote set-url upstream https://github.com/d2phap/ImageGlass.git
git remote set-url --push upstream file:///dev/null
git config remote.upstream.fetch '+refs/heads/prod:refs/remotes/upstream/prod'
git fetch upstream
```

Select a stable tag from the [official upstream releases](https://github.com/d2phap/ImageGlass/releases) and fetch it explicitly. The commands below use `10.0.6.906`.

```sh
tag=10.0.6.906
git fetch --no-tags upstream "refs/tags/$tag:refs/tags/$tag"
git log --left-right --oneline "macos-x64...refs/tags/$tag"
```

If the selected tag is already an ancestor of `macos-x64`, the port includes it. Otherwise, merge the verified tag in a separate branch or worktree based on `macos-x64`. Before advancing the maintenance branch, verify the Intel native dependency and image codecs, Classic/Pro license behavior, both update checks, and the signed DMG.

## Submitting code

Submit port changes as pull requests against `macos-x64`. For changes to official ImageGlass, follow the [upstream contribution guide](https://github.com/d2phap/ImageGlass/blob/develop/CONTRIBUTING.md).

Before you open it:

- **Keep it focused.** One topic per pull request. Unrelated refactoring makes a change much harder to review.
- **Match the surrounding code.** The conventions for v10 (AOT and trim safety, thread safety, disposal of Skia objects, reusing the `Ph*` base controls and themed resources instead of hardcoded values, localizing user-facing strings through `LangId`) are documented in [source/CLAUDE.md](source/CLAUDE.md).
- **Localize new UI text.** Add a `LangId` key with its English default rather than a literal string.
- **Say how you verified it.** There is no automated test suite for v10, so the description should list the steps you ran, the image formats you tried, and the platforms you tested on. Screenshots or a short recording are very welcome for UI changes.
- **Mention the platforms you could not test.** That is useful information, not a problem.
- Confirm the [CLA](CLA.md) checkbox in the pull request template.

For performance or image-loading problems, the app has two opt-in tracers that produce logs worth attaching: `--ig-startup-trace` (startup timing) and `--ig-photo-trace` (photo load and render).


## Code review process

Reviews are done by one maintainer, so the bigger the pull request, the longer it will take to review and merge. Try to break down large pull requests into smaller chunks that are easier to review and merge.

It is also always helpful to have some context for your pull request. What was the purpose? Why does it matter to you?

Large or architectural changes are best discussed first in an [issue](https://github.com/d2phap/ImageGlass/issues) or a [discussion](https://github.com/d2phap/ImageGlass/discussions), before you write the code.


## Translations

Translations for both ImageGlass 10 and ImageGlass 9 are managed on [Crowdin](https://crowdin.com/project/imageglass). Join the project there, pick your language, and translate in the browser. No build tools needed, and no pull request to open.

ImageGlass 10 uses language packs (`*.iglang.json`), so you can also work on a pack locally and test it before it goes to Crowdin:

1. Open **Settings > Language** in ImageGlass 10 and export the built-in English pack.
2. Translate the values in the exported `.iglang.json` file.
3. Import it back through the same page to see it live in the app.

Published packs are listed at [imageglass.org/languages](https://imageglass.org/languages).


## Reporting bugs

Use [Intel port Issues](https://github.com/FarMC666/ImageGlass-macOS/issues) for this build and [upstream Issues](https://github.com/d2phap/ImageGlass/issues/new/choose) for official builds. Include:

- The full Intel release tag, Classic or Pro edition, and macOS version
- Exact steps to reproduce
- The image file that triggers it, when the problem is format-specific
- Screenshots, a recording, or the trace log described above

Please **do not** file security vulnerabilities as public issues. See [SECURITY.md](SECURITY.md).


## Questions

If you have a question, start a [discussion](https://github.com/d2phap/ImageGlass/discussions/new/choose) or ask on [Discord](https://discord.gg/tWjbynH2X8). Do a quick search first, in case someone already asked the same thing.

You can also write to phap@imageglass.org. Priority support is available with Pro Business subscriptions, see [imageglass.org/support](https://imageglass.org/support).

