$json = [Console]::In.ReadToEnd() | ConvertFrom-Json
$path = $json.tool_input.file_path
if ($path -match '[\\/]Migrations[\\/].*\.cs$') {
    Write-Error "Migration files are generated via 'dotnet ef migrations add', not hand-edited. Regenerate instead."
    exit 2
}
exit 0
