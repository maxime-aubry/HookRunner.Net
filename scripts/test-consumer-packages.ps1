# Build a real consumer from the packages produced by this checkout.
. "$PSScriptRoot/ci-common.ps1"
Push-Location $RepositoryRoot
try {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $smoke = Join-Path $RepositoryRoot 'artifacts/consumer'
    $feed = Join-Path $smoke 'feed'
    $consumer = Join-Path $smoke ('host-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $feed, $consumer | Out-Null
    foreach ($project in @('Domain', 'Application', 'Infrastructure', 'Cli')) {
        Invoke-Checked $dotnet @('pack', "src/HookRunner.$project/HookRunner.$project.csproj", '-c', 'Release', '--no-build', '-o', $feed)
    }
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HookRunner.Cli" VersionOverride="1.0.0" />
    <PackageReference Include="Scrutor" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <None Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $consumer 'Consumer.csproj')
    foreach ($file in @('Program.cs', 'InspectHook.cs', 'appsettings.json')) {
        Copy-Item -LiteralPath "fixtures/HookRunner.TestHost/$file" -Destination $consumer -Force
    }
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    @"
<configuration>
  <packageSources><clear /><add key="checkout" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="checkout"><package pattern="HookRunner.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $consumer 'NuGet.Config')
    $packageCache = Join-Path $smoke ('cache-' + [Guid]::NewGuid().ToString('N'))
    Invoke-Checked $dotnet @('restore', "$consumer/Consumer.csproj", '--packages', $packageCache, '--force')
    Invoke-Checked $dotnet @('build', "$consumer/Consumer.csproj", '-c', 'Release', '--no-restore')
    '<Solution />' | Set-Content -Encoding UTF8 -LiteralPath "$smoke/Consumer.slnx"
    $observation = Join-Path $smoke 'observation.json'
    if (Test-Path -LiteralPath $observation) { Remove-Item -LiteralPath $observation }
    $inputJson = @{ session_id = 'package-session'; hook_event_name = 'Stop'; cwd = $smoke; observation_path = $observation } | ConvertTo-Json -Compress
    $inputJson | & $dotnet "$consumer/bin/Release/net11.0/Consumer.dll" execute INSPECT --agent codex
    if ($LASTEXITCODE -ne 0) { throw 'The NuGet consumer failed.' }
    $actual = Get-Content -Raw -LiteralPath $observation | ConvertFrom-Json
    if ($actual.Dependency -cne 'consumer-service' -or $actual.Options.TimeoutSeconds -ne 37 -or $actual.Options.Projects.Count -ne 0) {
        throw 'The NuGet consumer did not receive its dependency and prepared options.'
    }
    'NuGet consumer and child execution passed.'
} finally { Pop-Location }
