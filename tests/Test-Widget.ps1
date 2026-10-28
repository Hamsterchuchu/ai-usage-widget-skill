[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskExe=& (Join-Path $taskRoot 'skills\ai-usage-widget\scripts\Build-Widget.ps1') -OutputDirectory (Join-Path $taskRoot 'artifacts')
$taskScratch=Join-Path $taskRoot ('artifacts\tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskScratch -Force | Out-Null
$taskArgs=@('--self-test','--data-dir',('"' + $taskScratch + '"'))
$taskProcess=Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WindowStyle Hidden -Wait -PassThru
if ($taskProcess.ExitCode -ne 0) { throw 'Widget verification failed.' }
foreach ($taskName in @('verification.json','customization-verification.json')) {
  $taskResult=Get-Content -LiteralPath (Join-Path $taskScratch $taskName) -Raw | ConvertFrom-Json
  if (!$taskResult.success) { throw "$taskName failed." }
  Write-Output ("{0}: {1} checks passed" -f $taskName,$taskResult.count)
}
$taskPreview=Start-Process -FilePath $taskExe -ArgumentList '--render-preview','--demo','--data-dir',('"' + $taskScratch + '"') -WindowStyle Hidden -Wait -PassThru
if ($taskPreview.ExitCode -ne 0 -or !(Test-Path -LiteralPath (Join-Path $taskScratch 'preview.png'))) { throw 'Preview render failed.' }
if (Test-Path -LiteralPath (Join-Path $taskScratch 'usage.json')) { throw 'Demo wrote over usage data.' }
Write-Output 'Preview renders without a backend or persisted demo usage.'
