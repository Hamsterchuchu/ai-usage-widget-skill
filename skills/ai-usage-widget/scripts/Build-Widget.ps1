[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts'))
$ErrorActionPreference = 'Stop'
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler = Join-Path $taskFramework 'csc.exe'
if (!(Test-Path -LiteralPath $taskCompiler)) { throw 'Windows x64 with .NET Framework 4.x is required.' }
$taskAssets = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets'))
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskExecutable = Join-Path $taskOutput 'AIUsageWidget.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 "/out:$taskExecutable" "/reference:$taskFramework\WPF\PresentationFramework.dll" "/reference:$taskFramework\WPF\PresentationCore.dll" "/reference:$taskFramework\WPF\WindowsBase.dll" "/reference:$taskFramework\System.Xaml.dll" "/reference:$taskFramework\System.Web.Extensions.dll" (Join-Path $taskAssets 'UsageCompanion.cs') (Join-Path $taskAssets 'Customization.cs')
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
Write-Output $taskExecutable
