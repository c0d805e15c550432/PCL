param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot '..'),
    [string]$Executable = '',
    [string]$OutputDirectory = '',
    [string]$MSBuild = '',
    [ValidateSet('standard', 'enabled')][string]$Variant = 'standard'
)

$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
if (-not $Executable) { $Executable = Join-Path $SourceRoot 'artifacts\standard\PCL-Union-2.99.T.0.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $SourceRoot ('artifacts\checks-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
if (-not $MSBuild) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $MSBuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}

# Guard the removed attack paths without starting the launcher or contacting its services.
$appRoot = Join-Path $SourceRoot 'Plain Craft Launcher 2'
$launch = Get-Content -LiteralPath (Join-Path $appRoot 'Modules\Minecraft\ModLaunch.vb') -Raw
$settings = Get-Content -LiteralPath (Join-Path $appRoot 'Pages\PageSetup\Settings.vb') -Raw
$events = Get-Content -LiteralPath (Join-Path $appRoot 'Modules\ModEvent.vb') -Raw
$secret = Get-Content -LiteralPath (Join-Path $appRoot 'Modules\ModSecret.vb') -Raw
$startup = Get-Content -LiteralPath (Join-Path $appRoot 'Application.xaml.vb') -Raw
$packs = Get-Content -LiteralPath (Join-Path $appRoot 'Modules\Minecraft\ModModpack.vb') -Raw
$network = Get-Content -LiteralPath (Join-Path $appRoot 'Modules\Base\ModNet.vb') -Raw
if ($launch -match 'McLaunchCustom|AdvanceRun|cmd\.exe' -or $settings -match 'AdvanceRun' -or
    $events -match 'StartProcess\(|Settings\.SetSafe\(' -or $startup -match 'UpdateReplace|--update' -or
    $packs -match 'PreLaunchCommand|VersionAdvanceRun' -or $secret -match 'Sub ServerSub|Sub UpdateReplace' -or
    $network -match 'ShouldValidateSslCertificateOnLogin|ServerCertificateValidationCallback' -or $settings -match 'ToolDownloadCert') {
    throw '安全回归检查失败：已移除的执行路径重新出现。'
}
'PASS source security guards' | Set-Content -LiteralPath (Join-Path $OutputDirectory 'security-checks.log') -Encoding utf8

foreach ($project in @('SchematicChecks', 'LauncherChecks')) {
    & $MSBuild (Join-Path $SourceRoot "tests\$project\$project.csproj") /restore /p:Configuration=Release /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "$project 构建失败" }
}

# A fresh directory makes checks repeatable without deleting previous evidence.
$run = Join-Path $OutputDirectory ([guid]::NewGuid().ToString('N'))
& (Join-Path $SourceRoot 'tests\SchematicChecks\bin\Release\net48\SchematicChecks.exe') (Join-Path $run 'files') 2>&1 |
    Tee-Object -FilePath (Join-Path $OutputDirectory 'schematic-tests.log')
if ($LASTEXITCODE -ne 0) { throw '投影测试失败' }

# Create only a junction inside this run's disposable fixtures. The scanner must skip it.
[Reflection.Assembly]::LoadFrom((Join-Path $SourceRoot 'tests\SchematicChecks\bin\Release\net48\SchematicChecks.exe')) | Out-Null
$fixtureGame = Join-Path $run 'files\instance-a'
$junction = Join-Path $fixtureGame 'schematics\linked-fixtures'
New-Item -ItemType Junction -Path $junction -Target (Join-Path $run 'files') | Out-Null
$library = [PCLCS.Schematics.SchematicLibrary]::new($fixtureGame)
$warnings = [Collections.Generic.List[string]]::new()
$inventory = $library.Scan([Threading.CancellationToken]::None, $warnings)
if ($inventory.Count -ne 2 -or $warnings.Count -ne 1) { throw '扫描未正确跳过目录联接' }
$rejected = $false
try { $library.ValidateFile((Join-Path $junction '测试.litematic')) | Out-Null } catch { $rejected = $true }
if (-not $rejected) { throw '文件操作未拒绝目录联接' }
'PASS junction scan skipped and linked file operation rejected' | Add-Content -LiteralPath (Join-Path $OutputDirectory 'security-checks.log') -Encoding utf8
# Remove only the known link, not its target; both resolved lexical paths are under this run.
if (-not ([IO.Path]::GetFullPath($junction)).StartsWith([IO.Path]::GetFullPath($run) + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw '测试目录联接不在本次检查目录内'
}
[IO.Directory]::Delete($junction, $false)

& (Join-Path $SourceRoot 'tests\LauncherChecks\bin\Release\net48\LauncherChecks.exe') $Executable (Join-Path $run 'ui') $Variant 2>&1 |
    Tee-Object -FilePath (Join-Path $OutputDirectory 'launcher-ui-tests.log')
if ($LASTEXITCODE -ne 0) { throw '启动器 WPF 测试失败' }
Get-FileHash -LiteralPath $Executable -Algorithm SHA256
