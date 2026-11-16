# PreToolUse guard: this repo is a personal fork. Claude must NEVER open pull
# requests (anywhere) and must NEVER write to / interact with the upstream
# repositories (reiichi001, goaaats, goatcorp). Exit code 2 blocks the tool call
# and feeds the message on stderr back to Claude.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    $payload = $raw | ConvertFrom-Json
} catch {
    exit 0
}

$cmd = $null
if ($payload.tool_input -and $payload.tool_input.command) {
    $cmd = [string]$payload.tool_input.command
}
if (-not $cmd) { exit 0 }

$reasons = @()

# 1. No pull requests, ever, on any repository.
if ($cmd -match '(?i)\bgh\s+pr\s+(create|new|merge|comment|review|edit|reopen|ready|close)\b') {
    $reasons += 'Opening or modifying pull requests is forbidden in this project (see CLAUDE.md). Merge locally into master and push to origin instead.'
}
if ($cmd -match '(?i)\bgh\s+api\b.*\bpulls\b') {
    $reasons += 'The GitHub pulls API is off-limits in this project (no PRs, see CLAUDE.md).'
}

# 2. Never write to or interact with upstream owners.
$upstream = '(?i)\b(reiichi001|goaaats|goatcorp)\b'
$writeish = '(?i)\b(gh\s+(issue|pr|release|discussion|workflow|run|label|gist|api|repo\s+(edit|delete|fork|sync|set-default|rename|archive))|git\s+push)\b'
if ($cmd -match $upstream -and $cmd -match $writeish) {
    $reasons += 'This command targets an upstream repository (reiichi001 / goaaats / goatcorp). Never create issues, comments, PRs, releases, or pushes upstream. Only `git fetch upstream` is allowed.'
}

# 3. The upstream remote is fetch-only.
if ($cmd -match '(?i)\bgit\s+push\b[^;|&]*\bupstream\b') {
    $reasons += 'Pushing to the upstream remote is forbidden. Push only to origin (pearstopher/Dalamud.DiscordBridge).'
}
if ($cmd -match '(?i)\bgit\s+remote\s+set-url\b[^;|&]*\bupstream\b') {
    $reasons += 'Do not change the upstream remote URLs; its push URL is intentionally disabled.'
}

if ($reasons.Count -gt 0) {
    [Console]::Error.WriteLine("BLOCKED by .claude/hooks/guard-upstream.ps1:")
    foreach ($r in $reasons) { [Console]::Error.WriteLine(" - $r") }
    exit 2
}

exit 0
