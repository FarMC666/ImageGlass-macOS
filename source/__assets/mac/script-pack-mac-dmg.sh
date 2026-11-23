#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$script_dir/pack-mac-common.sh"

rid=
sign_mode=adhoc
while [[ $# -gt 0 ]]; do
    case "$1" in
        --rid) rid="${2:?Missing RID}"; shift 2 ;;
        --sign) sign_mode="${2:?Missing signing mode}"; shift 2 ;;
        *) fail "Unknown option: $1" ;;
    esac
done

mac_pack_settings
[[ -d "$app_dir" ]] || fail "Missing $app_dir. Run the app packer first."
case "$sign_mode" in
    adhoc) ;;
    developer-id)
        check_signing_identity
        [[ -n "${NOTARY_PROFILE:-}" ]] || fail "Set NOTARY_PROFILE to your notarytool keychain profile."
        ;;
    *) fail "Unsupported signing mode: $sign_mode" ;;
esac

staging="$(mktemp -d "$bundle_dir/.dmg.XXXXXXXX")"
trap 'rm -rf "$staging"' EXIT
payload="$staging/payload"
staged_app="$payload/$app_name.app"
mkdir -p "$payload"
ditto "$app_dir" "$staged_app"
ln -s /Applications "$payload/Applications"

executable="$(bundle_executable "$staged_app")"
[[ "$(lipo -archs "$staged_app/Contents/MacOS/$executable")" == "$arch" ]] || fail "Wrong application architecture."
sign_app "$staged_app" "$sign_mode"

release_type="$(read_build_prop IgReleaseType)"
label="$ig_version${release_type:+-$release_type}"
dmg_name="ImageGlass_${label}_mac-${rid#osx-}.dmg"
dmg="$staging/$dmg_name"
hdiutil create -volname "$app_name $ig_version" -srcfolder "$payload" -fs HFS+ -format UDZO "$dmg"

if [[ "$sign_mode" == developer-id ]]; then
    codesign --force --timestamp --sign "$SIGN_IDENTITY" "$dmg"
    notary_result="$(xcrun notarytool submit "$dmg" --keychain-profile "$NOTARY_PROFILE" --wait --output-format json)"
    printf '%s\n' "$notary_result"
    notary_status="$(printf '%s' "$notary_result" | plutil -extract status raw -)"
    [[ "$notary_status" == Accepted ]] || fail "Notarization status: $notary_status"
    xcrun stapler staple "$dmg"
    xcrun stapler validate "$dmg"
    spctl --assess --type open --context context:primary-signature --verbose=2 "$dmg"
fi
hdiutil verify "$dmg"
mv -f "$dmg" "$output_dir/$dmg_name"
echo "Created disk image: $output_dir/$dmg_name"
