#!/bin/bash
# PostToolUse on Edit|Write: format the one .cs file that changed, per .editorconfig.
f=$(jq -r '.tool_input.file_path // empty')
[[ "$f" == *.cs ]] || exit 0
command -v dotnet >/dev/null || exit 0
cd "${CLAUDE_PROJECT_DIR:-.}"
dotnet format PoEformance.slnx --no-restore --include "$f" >&2 || true
exit 0
