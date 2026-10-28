[CmdletBinding()]
param(
  [string]$OutputDirectory = (Join-Path (Get-Location).Path 'artifacts\claude-usage-bridge'),
  [string]$UsageFile = (Join-Path $env:LOCALAPPDATA 'AIUsageWidget\claude\usage.json')
)
$ErrorActionPreference='Stop'
$taskOutput=[IO.Path]::GetFullPath($OutputDirectory)
$taskUsage=[IO.Path]::GetFullPath($UsageFile)
if (Test-Path -LiteralPath $taskOutput) { throw 'Choose a new output directory; existing bridge files are preserved.' }
$taskHooks=Join-Path $taskOutput 'hooks'
New-Item -ItemType Directory -Path $taskHooks -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $taskOutput '.claude-plugin') -Force | Out-Null
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskUsage)) -Force | Out-Null
$taskTemplate=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\assets\claude-bridge\register.js.template') -Raw -Encoding UTF8
$taskScript=$taskTemplate.Replace('__USAGE_OUTPUT_JSON__', ($taskUsage | ConvertTo-Json -Compress))
[IO.File]::WriteAllText((Join-Path $taskHooks 'register.js'),$taskScript,(New-Object Text.UTF8Encoding($false)))
$taskUtf8=New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $taskHooks 'hooks.json'),(@{modules=@('./register.js')} | ConvertTo-Json),$taskUtf8)
[IO.File]::WriteAllText((Join-Path $taskOutput '.claude-plugin\plugin.json'),(@{name='ai-usage-bridge';version='0.2.0';license='MIT';description='Experimental desktop Mods SDK bridge: export sanitized account usage with engine-held authentication.'} | ConvertTo-Json),$taskUtf8)
Write-Output $taskOutput
