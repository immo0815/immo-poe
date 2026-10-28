#!/bin/bash
# PreToolUse on Bash. Never blocks; adds context for Claude.
#  git commit: CLAUDE.md asks to look at the repository first (fetch, main, branches, open PRs).
#  git push:   ToolVersion.Number must go up with every push - warn when it equals origin/main's.
cmd=$(jq -r '.tool_input.command // empty')
cd "${CLAUDE_PROJECT_DIR:-.}"
msg=""
if [[ "$cmd" =~ git[[:space:]]+commit ]]; then
  git fetch -q origin 2>/dev/null
  msg="Before committing (CLAUDE.md): behind origin/main by $(git rev-list --count HEAD..origin/main 2>/dev/null) commits. Other branches: $(git branch -r 2>/dev/null | grep -v HEAD | tr -d ' ' | head -15 | tr '\n' ' '). Check open pull requests via the GitHub tools."
elif [[ "$cmd" =~ git[[:space:]]+push ]]; then
  f=src/PoEformance.Features/ToolVersion.cs
  git fetch -q origin main 2>/dev/null
  num() { sed -n 's/.*Number = "\([^"]*\)".*/\1/p'; }
  mine=$(git show "HEAD:$f" 2>/dev/null | num)
  base=$(git show "origin/main:$f" 2>/dev/null | num)
  if [ -n "$mine" ] && [ "$mine" = "$base" ]; then
    msg="WARNING: ToolVersion.Number is still $mine, the same as origin/main. CLAUDE.md: raise it by one patch with every push."
  fi
fi
[ -n "$msg" ] && jq -n --arg m "$msg" '{hookSpecificOutput:{hookEventName:"PreToolUse",additionalContext:$m}}'
exit 0
