#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec bash "$script_dir/script-pack-mac-app.sh" --rid osx-arm64 --sign none "$@"
