<#
.SYNOPSIS
  Convenience wrapper — deploys to Raspberry Pi (linux-arm64).
  See Deploy-ToLinux.ps1 for full documentation and parameters.

.EXAMPLE
  .\deploy\Deploy-ToPi.ps1
  .\deploy\Deploy-ToPi.ps1 -Logs
  .\deploy\Deploy-ToPi.ps1 -Quick -NoRestart
#>
# -TargetHost is passed explicitly. Deploy-ToLinux.ps1 defaults to the x64 appliance
# (`radio`); without naming `piradio` here this wrapper would ship ARM64 binaries to it.
& "$PSScriptRoot\Deploy-ToLinux.ps1" -TargetHost piradio -Runtime linux-arm64 @args
