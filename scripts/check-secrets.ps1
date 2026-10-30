<#
.SYNOPSIS
  Scans the repository for secrets and personal data before they are committed or published.

.DESCRIPTION
  Checks every tracked and untracked-but-not-ignored text file for: private keys, common API key and token formats,
  bearer tokens, real user profile paths, e-mail addresses (other than GitHub noreply and example.com), and links to
  private AI sessions. Also fails if certificate or key files are tracked.

  Run it before every commit (the .githooks/pre-commit hook does this) and in CI. Use -History to also scan every
  commit that has ever been made, which is what to run before switching the repository to public.

.EXAMPLE
  powershell -File scripts\check-secrets.ps1
  powershell -File scripts\check-secrets.ps1 -History
#>
param([switch]$History)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$rules = @(
    @{ Name = 'Private key block'; Pattern = '-----BEGIN [A-Z ]*PRIVATE KEY-----' },
    @{ Name = 'OpenAI-style API key'; Pattern = '\bsk-[A-Za-z0-9_-]{20,}' },
    @{ Name = 'Gateway (kn-) API key'; Pattern = '\bkn-[A-Za-z0-9]{16,}' },
    @{ Name = 'GitHub token'; Pattern = '\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}' },
    @{ Name = 'Slack token'; Pattern = '\bxox[baprs]-[A-Za-z0-9-]{10,}' },
    @{ Name = 'AWS access key'; Pattern = '\bAKIA[0-9A-Z]{16}\b' },
    @{ Name = 'Bearer token'; Pattern = 'Bearer\s+[A-Za-z0-9._~+/=-]{24,}' },
    @{ Name = 'Real user profile path'; Pattern = '(?i)C:\\Users\\(?!someone\b|user\b|username\b|you\b|yourname\b|public\b|default\b|<)[A-Za-z0-9._-]+' },
    @{ Name = 'E-mail address'; Pattern = '(?i)\b[A-Z0-9._%+-]+@(?!users\.noreply\.github\.com\b|example\.(com|org)\b|anthropic\.com\b)[A-Z0-9.-]+\.[A-Z]{2,}\b' },
    @{ Name = 'Private AI session link'; Pattern = 'claude\.ai/(code/)?session_' },
    @{ Name = 'Password assignment'; Pattern = '(?i)\b(password|passwd|secret|api[_-]?key)\s*[:=]\s*["''][^"'']{8,}["'']' }
)

# Strings that look like findings but are deliberate test fixtures or documentation.
$allow = @(
    'sk-test-key', 'sk-super-secret', 'password = "password"', "-CertPassword = 'password'", 'sk-\.\.\.', 'kn-\.\.\.'
)

$blockedNames = '\.(pfx|p12|pem|key|snk|cer)$|(^|/)\.env(\..+)?$|FIRST-LOGIN\.txt$'
$skipExt = '\.(png|ico|jpg|jpeg|gif|zip|msix|dll|exe|pdb|pri|bin|snk|webp)$'

$files = @(git ls-files) + @(git ls-files --others --exclude-standard) | Sort-Object -Unique
$problems = New-Object System.Collections.Generic.List[string]

foreach ($f in $files) {
    if (-not (Test-Path -LiteralPath $f)) { continue }
    if ($f -match $blockedNames) { $problems.Add("Blocked file type is tracked: $f"); continue }
    if ($f -match $skipExt) { continue }
    if ($f -eq 'scripts/check-secrets.ps1') { continue }

    $lineNo = 0
    foreach ($line in [IO.File]::ReadLines((Resolve-Path -LiteralPath $f))) {
        $lineNo++
        foreach ($rule in $rules) {
            if ($line -match $rule.Pattern) {
                $hit = $Matches[0]
                if ($allow | Where-Object { $line -match $_ }) { continue }
                $problems.Add("${f}:${lineNo}: $($rule.Name): $hit")
            }
        }
    }
}

if ($History) {
    Write-Host 'Scanning full git history...'
    $patch = git log --all -p --no-color
    foreach ($rule in $rules) {
        $found = $patch | Select-String -Pattern $rule.Pattern | Where-Object { $line = $_.Line; -not ($allow | Where-Object { $line -match $_ }) } | Select-Object -First 3
        foreach ($m in $found) { $problems.Add("history: $($rule.Name): $($m.Matches[0].Value)") }
    }
    $names = git log --all --name-only --pretty=format: | Where-Object { $_ -match $blockedNames } | Sort-Object -Unique
    foreach ($n in $names) { $problems.Add("history: blocked file was committed at some point: $n") }
}

if ($problems.Count -eq 0) {
    Write-Host "No secrets or personal data found in $($files.Count) files." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($problems.Count) problem(s):" -ForegroundColor Red
$problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
exit 1
