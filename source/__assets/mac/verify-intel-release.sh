#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/pack-mac-common.sh"

run_checks() {
    local script
    for script in "$script_dir/"*.sh; do
        /bin/bash -n "$script"
    done
    dotnet run --project "$script_dir/update-check/UpdateCheck.csproj" -c Release
}

if [[ $# == 1 && "$1" == --checks-only ]]; then
    run_checks
    exit 0
fi

if [[ $# -lt 2 || $# -gt 3 ]]; then
    fail "Usage: $0 <intel-release-tag> <path-to-dmg> [expected-sha256] or $0 --checks-only"
fi

tag="$1"
dmg="$2"
expected_sha256="${3:-}"
[[ "$tag" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+-mac-x64\.[1-9][0-9]*$ ]] || fail "Invalid Intel release tag: $tag"
[[ -f "$dmg" ]] || fail "DMG not found: $dmg"
dmg="$(cd "$(dirname "$dmg")" && pwd)/$(basename "$dmg")"
app_version="${tag%-mac-x64.*}"
[[ "${dmg##*/}" == "ImageGlass_${app_version}_mac-x64.dmg" ]] || fail "The DMG filename does not match $tag"

[[ "$(uname -s)" == Darwin ]] || fail "DMG verification requires macOS"
if [[ -n "$expected_sha256" ]]; then
    [[ "$expected_sha256" =~ ^[[:xdigit:]]{64}$ ]] || fail "Invalid SHA-256: $expected_sha256"
    expected_sha256="$(printf '%s' "$expected_sha256" | tr '[:upper:]' '[:lower:]')"
fi
sha256="$(shasum -a 256 "$dmg")"
sha256="${sha256%% *}"
[[ -z "$expected_sha256" || "$sha256" == "$expected_sha256" ]] || fail "DMG SHA-256 does not match $expected_sha256"

run_checks
hdiutil verify "$dmg"

rid=osx-x64
mac_pack_settings
mount_dir="$(mktemp -d "${TMPDIR:-/tmp}/imageglass-verify.XXXXXXXX")"
mounted=false
cleanup() {
    local result=$?
    if [[ "$mounted" == true ]]; then
        hdiutil detach "$mount_dir" > /dev/null || result=1
    fi
    rmdir "$mount_dir" || result=1
    exit "$result"
}
trap cleanup EXIT
hdiutil attach -readonly -nobrowse -noautoopen -mountpoint "$mount_dir" "$dmg"
mounted=true

bundle="$mount_dir/$app_name.app"
plist="$bundle/Contents/Info.plist"
[[ -f "$plist" ]] || fail "Missing bundle: $app_name.app"
[[ "$(bundle_executable "$bundle")" == ImageGlass ]] || fail "Unexpected bundle executable"
[[ "$(plutil -extract LSMinimumSystemVersion raw "$plist")" == "$min_os" ]] || fail "Expected bundle deployment target $min_os"
codesign --verify --deep --strict --verbose=2 "$bundle"
codesign -dv "$bundle"

tag_file="$bundle/Contents/Resources/ImageGlass.IntelReleaseTag.txt"
[[ -f "$tag_file" ]] || fail "The signed bundle is missing its Intel release tag. Use a DMG built with the release metadata target."
embedded_tag="$(cat "$tag_file")"
[[ "$embedded_tag" == "$tag" ]] || fail "Expected $tag, but the signed bundle contains $embedded_tag"

for binary in "$bundle/Contents/MacOS/ImageGlass" "$bundle/Contents/MacOS/Magick.Native-Q16-HDRI-x64.dll.dylib"; do
    [[ -f "$binary" ]] || fail "Missing binary: ${binary##*/}"
    [[ "$(lipo -archs "$binary")" == "$arch" ]] || fail "Expected $arch: ${binary##*/}"
    load_commands="$(vtool -show-build "$binary")"
    printf '%s\n' "$load_commands"
    awk -v target="$min_os" '
        $1 == "platform" { macos = ($2 == "MACOS") }
        $1 == "minos" && macos {
            split($2, actual, ".")
            split(target, limit, ".")
            compatible = actual[1] < limit[1] ||
                (actual[1] == limit[1] && (actual[2] < limit[2] ||
                (actual[2] == limit[2] && actual[3] <= limit[3])))
        }
        END { exit !compatible }
    ' <<< "$load_commands" || fail "Missing or incompatible macOS deployment target: ${binary##*/}"
done

printf 'Verified Intel release %s\nSHA-256 %s\n' "$embedded_tag" "$sha256"
