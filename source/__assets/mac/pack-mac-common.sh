# Shared settings for the macOS app and DMG packers.

fail() {
    echo "Error: $*" >&2
    exit 1
}

mac_pack_settings() {
    case "$rid" in
        osx-x64)
            arch=x86_64
            app_name="ImageGlass Intel"
            min_os=15.0
            default_bundle_id=local.imageglass.intel
            ;;
        osx-arm64)
            arch=arm64
            app_name=ImageGlass
            min_os=12.0
            default_bundle_id=com.duongdieuphap.imageglass
            ;;
        *) fail "Unsupported macOS RID: $rid" ;;
    esac

    workspace_dir="$(cd "$script_dir/../.." && pwd)"
    output_dir="$workspace_dir/__artifacts/bundle"
    bundle_dir="$output_dir/$rid"
    app_dir="$bundle_dir/$app_name.app"
    props_file="$workspace_dir/Directory.Build.props"
    ig_version="$(read_build_prop IgVersion)"
    [[ -n "$ig_version" ]] || fail "Could not read IgVersion from $props_file"
}

read_build_prop() {
    sed -n "s:.*<$1>\(.*\)</$1>.*:\1:p" "$props_file"
}

check_signing_identity() {
    [[ -n "${SIGN_IDENTITY:-}" ]] || fail "Set SIGN_IDENTITY to your Developer ID Application identity."
    local identities
    identities="$(security find-identity -v -p codesigning)"
    [[ "$identities" == *"\"$SIGN_IDENTITY\""* ]] || fail "Signing identity not found: $SIGN_IDENTITY"
}

bundle_executable() {
    /usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$1/Contents/Info.plist"
}

sign_app() {
    local app="$1" mode="$2" executable code
    local -a options
    case "$mode" in
        adhoc) options=(--sign - --timestamp=none) ;;
        developer-id)
            options=(--sign "$SIGN_IDENTITY" --timestamp --options runtime)
            ;;
        *) fail "Unsupported signing mode: $mode" ;;
    esac

    executable="$app/Contents/MacOS/$(bundle_executable "$app")"
    while IFS= read -r -d '' code; do
        [[ "$code" != "$executable" ]] || continue
        if [[ "$(file -b "$code")" == *Mach-O* ]]; then
            codesign --force "${options[@]}" "$code"
        fi
    done < <(find "$app/Contents" -type f -print0)

    if [[ "$mode" == developer-id ]]; then
        codesign --force "${options[@]}" --entitlements "$script_dir/ImageGlass.entitlements" "$app"
    else
        codesign --force "${options[@]}" "$app"
    fi
    codesign --verify --deep --strict --verbose=2 "$app"
}
