param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..'),
    [string]$OutputDirectory = '',
    [string]$MSBuild = '',
    [switch]$EnableDownload,
    [switch]$EnableTheme,
    [switch]$EnableTHUnion
)

$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$clientFile = Join-Path $SourceRoot '.local\client-id.txt'
if (-not (Test-Path -LiteralPath $clientFile)) { throw '请先按 BUILD.md 配置 .local/client-id.txt。' }
$clientId = [guid]::Empty
if (-not [guid]::TryParse([IO.File]::ReadAllText($clientFile).Trim(), [ref]$clientId) -or $clientId -eq [guid]::Empty) {
    throw '本地 Microsoft Client ID 格式不正确。'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $SourceRoot 'artifacts\standard' }
if (-not $MSBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $MSBuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $MSBuild -or -not (Test-Path -LiteralPath $MSBuild)) { throw '需要支持 VB/WPF、C# 13、.NET Framework 4.8 的 Visual Studio MSBuild。' }
if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot 'MeloongCore\Shared\MeloongCore.csproj'))) { throw '请先初始化 MeloongCore 子模块。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$restoreLog = Join-Path $OutputDirectory 'restore.log'
$buildLog = Join-Path $OutputDirectory 'build-release.log'
& $MSBuild (Join-Path $SourceRoot 'Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj') /t:Restore /p:Configuration=Release /nologo /v:minimal /fl "/flp:logfile=$restoreLog;verbosity=normal;encoding=UTF-8"
if ($LASTEXITCODE -ne 0) { throw "NuGet 还原失败：$restoreLog" }
$downloadValue = $EnableDownload.IsPresent.ToString().ToLowerInvariant()
$themeValue = $EnableTheme.IsPresent.ToString().ToLowerInvariant()
$unionValue = $EnableTHUnion.IsPresent.ToString().ToLowerInvariant()
& $MSBuild (Join-Path $SourceRoot 'Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj') /p:Configuration=Release "/p:EnableDownload=$downloadValue" "/p:EnableTheme=$themeValue" "/p:EnableTHUnion=$unionValue" /nologo /v:minimal /fl "/flp:logfile=$buildLog;verbosity=normal;encoding=UTF-8"
if ($LASTEXITCODE -ne 0) { throw "构建失败：$buildLog" }
$executable = Join-Path $OutputDirectory 'PCL-Union-2.99.T.1.exe'
Copy-Item -LiteralPath (Join-Path $SourceRoot 'Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe') -Destination $executable -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot 'LICENCE') -Destination (Join-Path $OutputDirectory 'LICENCE') -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot 'THUNION.md') -Destination (Join-Path $OutputDirectory 'THUNION.md') -Force
# THUnion dependencies, native loaders and license texts are embedded in the EXE.
# Keep optional readable notices beside the release; no DLL or .config is required.
if ($EnableTHUnion) {
    $licenseOutput = Join-Path $OutputDirectory 'third-party-licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'licenses\THUnion') -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenseOutput $_.Name) -Force
    }
}
$files = @(@{ path = [IO.Path]::GetFileName($executable); sha256 = (Get-FileHash -LiteralPath $executable).Hash })
@{ version = '2.99.T.1'; singleFile = $true; downloads = $EnableDownload.IsPresent; themes = $EnableTheme.IsPresent; thunion = $EnableTHUnion.IsPresent; sha256 = (Get-FileHash -LiteralPath $executable).Hash; files = $files } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-manifest.json') -Encoding utf8
Get-FileHash -LiteralPath $executable -Algorithm SHA256
