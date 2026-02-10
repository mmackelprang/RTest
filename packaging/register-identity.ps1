#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Registers MSIX sparse package identity for Radio Console.
  Required for AudioPlaybackConnection (A2DP Sink) on Windows 10 2004+.

.DESCRIPTION
  Enables developer sideloading, creates a self-signed certificate, trusts it,
  and registers the sparse package manifest. This enables AudioPlaybackConnection
  to maintain persistent A2DP sink connections (without identity, connections
  drop after ~5s).

  Run this script ONCE as Administrator after building the project.

.EXAMPLE
  .\register-identity.ps1
  .\register-identity.ps1 -ExternalLocation "D:\prj\RTest\RTest\src\Radio.API\bin\Release\net8.0-windows10.0.19041.0"
#>
param(
  [string]$ExternalLocation = (Join-Path $PSScriptRoot "..\src\Radio.API\bin\Release\net8.0-windows10.0.19041.0")
)

$ErrorActionPreference = "Stop"
$PackageName = "RadioConsole.GrandpasRadio"
$Publisher = "CN=RadioConsole"
$ManifestPath = Join-Path $PSScriptRoot "AppxManifest.xml"

Write-Host "=== Radio Console MSIX Sparse Package Registration ===" -ForegroundColor Cyan

# Step 0: Check if already registered
$existing = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if ($existing) {
  Write-Host "Package '$PackageName' is already registered." -ForegroundColor Yellow
  Write-Host "  Version: $($existing.Version)"
  Write-Host "  Location: $($existing.InstallLocation)"
  Write-Host "To re-register, run unregister-identity.ps1 first." -ForegroundColor Yellow
  exit 0
}

# Step 1: Enable developer sideloading (required for unsigned sparse packages)
Write-Host "`n[1/5] Ensuring developer sideloading is enabled..." -ForegroundColor White
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
if (-not (Test-Path $regPath)) {
  New-Item -Path $regPath -Force | Out-Null
}

$devMode = (Get-ItemProperty -Path $regPath -Name "AllowDevelopmentWithoutDevLicense" -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense

if ($devMode -eq 1) {
  Write-Host "  Developer Mode already enabled" -ForegroundColor Green
} else {
  # Developer Mode required for unsigned sparse packages (sideloading alone isn't enough)
  Set-ItemProperty -Path $regPath -Name "AllowDevelopmentWithoutDevLicense" -Value 1 -Type DWord
  Set-ItemProperty -Path $regPath -Name "AllowAllTrustedApps" -Value 1 -Type DWord
  Write-Host "  Enabled Developer Mode (AllowDevelopmentWithoutDevLicense=1)" -ForegroundColor Green
}

# Step 2: Create self-signed certificate
Write-Host "`n[2/5] Creating self-signed certificate for '$Publisher'..." -ForegroundColor White
$cert = Get-ChildItem Cert:\LocalMachine\My |
  Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date) } |
  Select-Object -First 1

if (-not $cert) {
  $cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Publisher `
    -KeyUsage DigitalSignature `
    -FriendlyName "Radio Console Sparse Package" `
    -CertStoreLocation "Cert:\LocalMachine\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

  Write-Host "  Created certificate: $($cert.Thumbprint)" -ForegroundColor Green
} else {
  Write-Host "  Using existing certificate: $($cert.Thumbprint)" -ForegroundColor Green
}

# Step 3: Trust the certificate
Write-Host "`n[3/5] Adding certificate to TrustedPeople store..." -ForegroundColor White
$trustedStore = Get-ChildItem Cert:\LocalMachine\TrustedPeople |
  Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

if (-not $trustedStore) {
  $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
  $store.Open("ReadWrite")
  $store.Add($cert)
  $store.Close()
  Write-Host "  Certificate added to TrustedPeople store" -ForegroundColor Green
} else {
  Write-Host "  Certificate already in TrustedPeople store" -ForegroundColor Green
}

# Step 4: Resolve external location
Write-Host "`n[4/5] Resolving external location..." -ForegroundColor White
$resolvedExternal = Resolve-Path $ExternalLocation -ErrorAction SilentlyContinue
if (-not $resolvedExternal) {
  Write-Host "  External location not found: $ExternalLocation" -ForegroundColor Yellow
  Write-Host "  Build the project first: dotnet build --configuration Release" -ForegroundColor Yellow
  Write-Host "  Using manifest directory as fallback..." -ForegroundColor Yellow
  $resolvedExternal = $PSScriptRoot
}
Write-Host "  External location: $resolvedExternal" -ForegroundColor Green

# Step 5: Register the sparse package
Write-Host "`n[5/5] Registering sparse package..." -ForegroundColor White
Add-AppxPackage -Path $ManifestPath -ExternalLocation $resolvedExternal -Register

Write-Host "`n=== Registration Complete ===" -ForegroundColor Green
Write-Host "Verify with: Get-AppxPackage -Name '$PackageName'"
Write-Host "AudioPlaybackConnection (A2DP Sink) should now maintain persistent connections."
