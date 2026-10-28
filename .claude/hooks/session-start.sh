#!/bin/bash
# Cloud sessions start without a .NET SDK; the repo needs 10.0 (see ci.yml). Local machines are left alone.
set -euo pipefail
[ "${CLAUDE_CODE_REMOTE:-}" = "true" ] || exit 0

export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o "${TMPDIR:-/tmp}/dotnet-install.sh"
  bash "${TMPDIR:-/tmp}/dotnet-install.sh" --channel 10.0 --install-dir "$DOTNET_ROOT" >&2
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
    echo "export PATH=\"$DOTNET_ROOT:\$PATH\""
    echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  } >> "$CLAUDE_ENV_FILE"
fi

cd "${CLAUDE_PROJECT_DIR:-.}"
dotnet restore PoEformance.slnx >&2
