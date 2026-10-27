param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\artifacts\thunion-2.99.T.1-singlefile'),
    [string]$MSBuild = ''
)

$ErrorActionPreference = 'Stop'
$build = Join-Path $PSScriptRoot 'Build-PCL-Union.ps1'
& $build -OutputDirectory (Join-Path $OutputRoot 'all-disabled') -MSBuild $MSBuild
& $build -OutputDirectory (Join-Path $OutputRoot 'theme-disabled') -MSBuild $MSBuild -EnableDownload -EnableTHUnion
& $build -OutputDirectory (Join-Path $OutputRoot 'all-enabled') -MSBuild $MSBuild -EnableDownload -EnableTheme -EnableTHUnion
