<#
.SYNOPSIS
  Convenience wrapper — deploys to Raspberry Pi (linux-arm64).
  See Deploy-ToLinux.ps1 for full documentation and parameters.

.EXAMPLE
  .\deploy\Deploy-ToPi.ps1
  .\deploy\Deploy-ToPi.ps1 -Logs
  .\deploy\Deploy-ToPi.ps1 -Quick -NoRestart
#>
# Pin the Pi's host and runtime — but only when the caller has not named them.
#
# This wrapper has no param() block, so everything the caller passes lands in $args and is
# forwarded verbatim. Hardcoding `-TargetHost piradio` ahead of @args is NOT safe: PiHost is an
# alias of TargetHost, so the documented form `Deploy-ToPi.ps1 -PiHost piradio` would bind the
# same parameter twice and PowerShell rejects it with ParameterAlreadyBound before any work
# starts. That form appears in CLAUDE.md, README.md and deploy/DEPLOYMENT.md.
#
# The host still has to be pinned, because Deploy-ToLinux.ps1 now defaults to the x64 appliance
# (`radio`): without this, a bare `Deploy-ToPi.ps1` would ship ARM64 binaries to that box.
function Test-CallerNamed {
  param([string[]]$Arguments, [string[]]$ParameterNames)
  foreach ($a in $Arguments) {
    if ($a -isnot [string] -or -not $a.StartsWith('-')) { continue }
    # Strip the leading dash and any `-Name:value` suffix, then allow PowerShell's
    # prefix-abbreviation form (-Target, -PiH, ...) to count as naming the parameter.
    $given = $a.TrimStart('-').Split(':')[0]
    if ([string]::IsNullOrEmpty($given)) { continue }
    foreach ($n in $ParameterNames) {
      if ($n.StartsWith($given, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
  }
  return $false
}

# NOTE: the defaults go in a HASHTABLE, not an array. Splatting an array passes its elements
# POSITIONALLY — `@('-TargetHost','piradio')` would bind the literal string "-TargetHost" as the
# host. Hashtable splatting is what binds by name. Verified both ways before landing this.
$defaults = @{}
if (-not (Test-CallerNamed -Arguments $args -ParameterNames @('TargetHost', 'PiHost'))) {
  $defaults['TargetHost'] = 'piradio'
}
if (-not (Test-CallerNamed -Arguments $args -ParameterNames @('Runtime'))) {
  $defaults['Runtime'] = 'linux-arm64'
}

& "$PSScriptRoot\Deploy-ToLinux.ps1" @defaults @args
