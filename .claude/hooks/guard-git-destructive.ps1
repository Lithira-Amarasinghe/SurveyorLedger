$json = [Console]::In.ReadToEnd() | ConvertFrom-Json
$cmd = $json.tool_input.command
if ($cmd -match 'git\s+push\s+.*(--force|-f\b)' -or
    $cmd -match 'git\s+reset\s+--hard' -or
    $cmd -match 'git\s+branch\s+-D' -or
    $cmd -match 'git\s+clean\s+-.*f' -or
    $cmd -match 'git\s+commit\s+.*--amend' -or
    $cmd -match 'git\s+(checkout|restore)\s+\.\s*$' -or
    $cmd -match '--no-verify|--no-gpg-sign') {
    Write-Error "Blocked: destructive/irreversible git op requires explicit user request in chat, not just tool approval. Ask the user first."
    exit 2
}
exit 0
