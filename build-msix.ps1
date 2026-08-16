# build-msix.ps1 — builds a signed MSIX for sideload testing.
#
# TARGET MACHINE SETUP (run once, as admin, before installing the .msix):
#   $pw = ConvertTo-SecureString "Dev@1234" -Force -AsPlainText
#   Import-PfxCertificate -FilePath dev-cert.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $pw
#
# Then double-click the generated .msix to install.

param(
    [string]$CertPassword = "Dev@1234",
    [string]$OutDir       = "$PSScriptRoot\publish\msix"
)

$ErrorActionPreference = "Stop"
$certFile = "$PSScriptRoot\dev-cert.pfx"
$pw = ConvertTo-SecureString $CertPassword -Force -AsPlainText

# ── 1. Ensure cert exists ─────────────────────────────────────────────────────
if (-not (Test-Path $certFile)) {
    Write-Host "Creating self-signed cert (CN=ONEVO)..."
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=ONEVO" `
        -KeyUsage DigitalSignature `
        -FriendlyName "ONEVO TrayApp Dev" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    Export-PfxCertificate -Cert $cert -FilePath $certFile -Password $pw | Out-Null
    Write-Host "Cert saved: $certFile"
}

# ── 2. Import into CurrentUser\My so MSBuild can access the private key ───────
Write-Host "Importing cert into store..."
$imported = Import-PfxCertificate -FilePath $certFile -CertStoreLocation "Cert:\CurrentUser\My" -Password $pw
$thumbprint = $imported.Thumbprint
Write-Host "Thumbprint: $thumbprint"

# ── 3. Build MSIX using thumbprint (no PFX password needed at sign time) ──────
Write-Host "Building MSIX..."
dotnet publish "$PSScriptRoot\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj" `
    -f net10.0-windows10.0.19041.0 `
    -r win-x64 `
    -c Release `
    -p:WindowsPackageType=MSIX `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateThumbprint="$thumbprint" `
    -p:AppxPackageDir="$OutDir\"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$msix = Get-ChildItem "$OutDir" -Recurse -Filter "*.msix" | Select-Object -First 1
Write-Host ""
Write-Host "Done: $($msix.FullName)"
Write-Host ""
Write-Host "To install on another machine:"
Write-Host "  1. Copy dev-cert.pfx + the .msix to the target"
Write-Host "  2. On target (admin PowerShell):"
Write-Host "       `$pw = ConvertTo-SecureString 'Dev@1234' -Force -AsPlainText"
Write-Host "       Import-PfxCertificate -FilePath dev-cert.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password `$pw"
Write-Host "  3. Double-click the .msix"
