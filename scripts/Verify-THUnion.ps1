param(
    [string]$ArtifactsRoot = (Join-Path $PSScriptRoot '..\artifacts\thunion-2.99.T.1-singlefile'),
    [string]$WikiRoot = (Join-Path $PSScriptRoot '..\..\Wiki'),
    [string]$MSBuild = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$ArtifactsRoot = (Resolve-Path -LiteralPath $ArtifactsRoot).Path
$WikiRoot = (Resolve-Path -LiteralPath $WikiRoot).Path
if (-not $MSBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $MSBuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
& $MSBuild (Join-Path $root 'tests\THUnionChecks\THUnionChecks.csproj') /restore /p:Configuration=Release /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'THUnion 检查程序构建失败。' }
foreach ($scenario in @('all-disabled', 'theme-disabled', 'all-enabled')) {
    $variant = $scenario
    $release = Join-Path $ArtifactsRoot $variant
    $evidence = Join-Path $ArtifactsRoot "checks-$scenario"
    $hostDirectory = Join-Path $evidence ('host-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
    # Copy only the EXE into a portable directory; run the test bootstrap elsewhere without any config.
    $manifest = Get-Content -LiteralPath (Join-Path $release 'build-manifest.json') -Raw | ConvertFrom-Json
    foreach ($file in $manifest.files) {
        $source = Join-Path $release $file.path
        if ((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "交付文件校验失败：$source" }
        $destination = Join-Path $hostDirectory $file.path
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination
    }
    $exe = Join-Path $hostDirectory 'PCL-Union-2.99.T.1.exe'
    $runnerDirectory = Join-Path $evidence ('runner-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runnerDirectory -Force | Out-Null
    $runner = Join-Path $runnerDirectory 'THUnionChecks.exe'
    Copy-Item -LiteralPath (Join-Path $root 'tests\THUnionChecks\bin\Release\net48\THUnionChecks.exe') -Destination $runner
    & $runner $exe $variant $evidence $WikiRoot $scenario 2>&1 |
        Tee-Object -FilePath (Join-Path $ArtifactsRoot "checks-$scenario.log")
    if ($LASTEXITCODE -ne 0) { throw "$scenario 检查失败，参见 checks-$scenario.log。" }
}
