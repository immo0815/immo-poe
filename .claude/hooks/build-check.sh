#!/bin/bash
# Stop hook: CLAUDE.md wants the build at zero warnings. Only builds when C# changed, and only once
# per stop (stop_hook_active) so a failing build cannot loop.
in=$(cat)
[ "$(jq -r '.stop_hook_active // false' <<<"$in")" = "true" ] && exit 0
command -v dotnet >/dev/null || exit 0
cd "${CLAUDE_PROJECT_DIR:-.}"
git status --porcelain | grep -qE '\.(cs|csproj|props)$' || exit 0
out=$(dotnet build PoEformance.slnx --no-restore -warnaserror -nologo -v q 2>&1) && exit 0
echo "Build with -warnaserror failed:" >&2
echo "$out" | grep -E 'error|warning' | sort -u | head -30 >&2
exit 2
