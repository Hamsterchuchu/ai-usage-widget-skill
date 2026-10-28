[CmdletBinding()]
param(
  [ValidateSet('codex','claude')][string]$Provider = 'codex',
  [string]$Executable = (Join-Path (Get-Location).Path 'artifacts\AIUsageWidget.exe'),
  [string]$DataDirectory = '',
  [switch]$Preview,
  [switch]$Demo
)
$ErrorActionPreference = 'Stop'
$taskExe = [IO.Path]::GetFullPath($Executable)
if (!(Test-Path -LiteralPath $taskExe)) { throw 'Build the widget with Build-Widget.ps1 first.' }
if ($Demo -and !$Preview) { throw '-Demo requires -Preview.' }
$taskArgs = @('--provider', $Provider)
if ($DataDirectory) {
  $taskData = [IO.Path]::GetFullPath($DataDirectory)
  if ($taskData.Contains('"')) { throw 'Invalid data directory.' }
  $taskArgs += @('--data-dir', ('"' + [regex]::Replace($taskData,'(\\+)$','$1$1') + '"'))
}
if ($Preview) { $taskArgs += '--preview' }
if ($Demo) { $taskArgs += '--demo' }
Start-Process -FilePath $taskExe -ArgumentList $taskArgs -WindowStyle Hidden | Out-Null
