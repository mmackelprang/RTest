<#
.SYNOPSIS
  Convenience wrapper — deploys to Raspberry Pi (linux-arm64).
  See Deploy-ToLinux.ps1 for full documentation and parameters.

.EXAMPLE
  .\deploy\Deploy-ToPi.ps1
  .\deploy\Deploy-ToPi.ps1 -Logs
  .\deploy\Deploy-ToPi.ps1 -Quick -NoRestart
#>
& "$PSScriptRoot\Deploy-ToLinux.ps1" -Runtime linux-arm64 @args
