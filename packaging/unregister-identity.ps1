#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Unregisters the MSIX sparse package identity for Radio Console.

.DESCRIPTION
  Removes the sparse package registration and optionally removes the
  self-signed certificate from the certificate stores.

.EXAMPLE
  .\unregister-identity.ps1
  .\unregister-identity.ps1 -RemoveCertificate
#>
param(
  [switch]$RemoveCertificate
)

$ErrorActionPreference = "Stop"
$PackageName = "RadioConsole.GrandpasRadio"
$Publisher = "CN=RadioConsole"

Write-Host "=== Radio Console MSIX Sparse Package Removal ===" -ForegroundColor Cyan

# Step 1: Remove package
Write-Host "`n[1/2] Removing package '$PackageName'..." -ForegroundColor White
$existing = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if ($existing) {
  Remove-AppxPackage -Package $existing.PackageFullName
  Write-Host "  Package removed successfully" -ForegroundColor Green
} else {
  Write-Host "  Package not found (already removed)" -ForegroundColor Yellow
}

# Step 2: Optionally remove certificate
if ($RemoveCertificate) {
  Write-Host "`n[2/2] Removing certificates for '$Publisher'..." -ForegroundColor White

  # Remove from Personal store
  $certs = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $Publisher }
  foreach ($cert in $certs) {
    Remove-Item $cert.PSPath -Force
    Write-Host "  Removed from Personal store: $($cert.Thumbprint)" -ForegroundColor Green
  }

  # Remove from TrustedPeople store
  $trustedCerts = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq $Publisher }
  foreach ($cert in $trustedCerts) {
    Remove-Item $cert.PSPath -Force
    Write-Host "  Removed from TrustedPeople store: $($cert.Thumbprint)" -ForegroundColor Green
  }

  if (-not $certs -and -not $trustedCerts) {
    Write-Host "  No certificates found for '$Publisher'" -ForegroundColor Yellow
  }
} else {
  Write-Host "`n[2/2] Skipping certificate removal (use -RemoveCertificate to remove)" -ForegroundColor Yellow
}

Write-Host "`n=== Removal Complete ===" -ForegroundColor Green
