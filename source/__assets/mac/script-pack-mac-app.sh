#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/pack-mac-common.sh"

rid=
publish_dir=
sign_mode=adhoc
while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid) rid="${2:?Missing RID}"; shift 2 ;;
        --publish-dir) publish_dir="${2:?Missing publish directory}"; shift 2 ;;
        --sign) sign_mode="${2:?Missing signing mode}"; shift 2 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

mac_pack_settings
publish_dir="${publish_dir:-$workspace_dir/__artifacts/publish/$rid}"
main="$publish_dir/ImageGlass"
[[ -f "$main" ]] || fail "Missing $main. Publish ImageGlass for $rid first."
[[ "$(lipo -archs "$main")" == "$arch" ]] || fail "Expected a $arch executable: $main"

case "$sign_mode" in
    none|adhoc) ;;
    developer-id) check_signing_identity ;;
    *) fail "Unsupported signing mode: $sign_mode" ;;
esac

short_version="$(read_build_prop IgBundleShortVersion)"
build_number="$(read_build_prop IgBundleBuild)"
short_version="${short_version:-${ig_version%.*}}"
build_number="${build_number:-${ig_version##*.}}"
bundle_id="${IG_BUNDLE_ID:-$default_bundle_id}"
[[ "$bundle_id" =~ ^[A-Za-z0-9.-]+$ ]] || fail "Invalid bundle identifier: $bundle_id"

mkdir -p "$bundle_dir"
staging="$(mktemp -d "$bundle_dir/.app.XXXXXXXX")"
trap 'rm -rf "$staging"' EXIT
staged_app="$staging/$app_name.app"
contents="$staged_app/Contents"
mkdir -p "$contents/MacOS" "$contents/Resources"
ditto "$publish_dir" "$contents/MacOS"
ditto "$workspace_dir/__assets/__app" "$contents/MacOS"
cp "$script_dir/logo.icns" "$contents/Resources/"
cp "$workspace_dir/../LICENSE" "$contents/Resources/"
rm -rf "$contents/MacOS/_ext_icons"
find "$contents/MacOS" -depth \( -name '*.dSYM' -o -name '*.pdb' -o -name '.DS_Store' -o -name '._*' \) -exec rm -rf {} +

sed -e "s/\${IG_SHORT_VERSION}/$short_version/g" \
    -e "s/\${IG_BUILD}/$build_number/g" \
    -e "s/\${IG_MINIMUM_SYSTEM_VERSION}/$min_os/g" \
    -e "s/\${IG_BUNDLE_ID}/$bundle_id/g" \
    -e "s/\${IG_BUNDLE_NAME}/$app_name/g" \
    "$script_dir/Info.plist" > "$contents/Info.plist"

plutil -lint "$contents/Info.plist"
chmod +x "$contents/MacOS/ImageGlass"

# Keep native binaries beside the executable. The .NET apphost also resolves managed files
# relative to that directory, so preserve their paths with links to the resources.
shopt -s dotglob nullglob
code_names=()
for entry in "$contents/MacOS/"*; do
    name="${entry##*/}"
    if [[ -f "$entry" && "$(file -b "$entry")" == *Mach-O* ]]; then
        lipo "$entry" -verify_arch "$arch" || fail "Missing $arch slice: $name"
        code_names+=("$name")
        continue
    fi
    mv "$entry" "$contents/Resources/$name"
    ln -s "../Resources/$name" "$entry"
done

# The apphost resolves ImageGlass.dll to Resources in a multi-file CoreCLR publish.
# It then looks for hostfxr and other native dependencies next to that assembly.
if [[ -f "$contents/Resources/ImageGlass.dll" ]]; then
    for name in "${code_names[@]}"; do
        [[ "$name" != ImageGlass ]] || continue
        ln -s "../MacOS/$name" "$contents/Resources/$name"
    done
fi

if [[ "$sign_mode" != none ]]; then
    sign_app "$staged_app" "$sign_mode"
fi
# Keep the previous verified app until staging and signing have succeeded.
if [[ -e "$app_dir" ]]; then
    mv "$app_dir" "$staging/previous.app"
fi
if ! mv "$staged_app" "$app_dir"; then
    if [[ -e "$staging/previous.app" ]]; then
        mv "$staging/previous.app" "$app_dir"
    fi
    fail "Could not install the staged bundle at $app_dir"
fi
echo "Created bundle: $app_dir"
