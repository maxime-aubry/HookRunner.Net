param([string[]] $Tools = @('gitleaks', 'actionlint'))
. "$PSScriptRoot/ci-common.ps1"
$os = if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { 'win' }
elseif ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { 'osx' } else { 'linux' }
$architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
$rid = "$os-$architecture"
$manifest = Get-Content -Raw -LiteralPath "$RepositoryRoot/.config/ci-tools.json" | ConvertFrom-Json
foreach ($property in $manifest.PSObject.Properties) {
    $name = $property.Name
    if ($name -notin $Tools) { continue }
    $tool = $property.Value
    $assetProperty = $tool.assets.PSObject.Properties[$rid]
    if (-not $assetProperty) { throw "No pinned $name download for $rid" }
    $asset = $assetProperty.Value
    $destination = Join-Path $RepositoryRoot "artifacts/tooling/$name"
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $binary = Join-Path $destination $(if ($os -eq 'win') { "$name.exe" } else { $name })
    $archive = Join-Path $destination $asset.file
    $uri = "https://github.com/$($tool.repository)/releases/download/v$($tool.version)/$($asset.file)"
    # Verify the cached archive again before every extraction; never trust an existing executable.
    if (-not (Test-Path -LiteralPath $archive)) { Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $archive }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash -ine $asset.sha256) { throw "$name archive checksum mismatch" }
    if ($asset.file.EndsWith('.zip')) { Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force }
    else { Invoke-Checked 'tar' @('-xzf', $archive, '-C', $destination) }
    $versionArgument = if ($name -eq 'actionlint') { '-version' } else { 'version' }
    Invoke-Checked $binary @($versionArgument)
}
