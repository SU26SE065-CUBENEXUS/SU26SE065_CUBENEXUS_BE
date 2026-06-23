param(
    [string]$Email = "cubenexus065@gmail.com"
)

$projectPath = Join-Path $PSScriptRoot ".." "CubeNexus.API"
Push-Location $projectPath

Write-Host "Cau hinh Gmail SMTP cho CubeNexus API..."
Write-Host "Email gui: $Email"
Write-Host ""

$appPassword = Read-Host "Nhap App Password Gmail (16 ky tu, bo dau cach)" -AsSecureString
$plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($appPassword))
$plainPassword = $plainPassword -replace '\s', ''

dotnet user-secrets set "EmailSettings:Username" $Email
dotnet user-secrets set "EmailSettings:Password" $plainPassword
dotnet user-secrets set "EmailSettings:FromEmail" $Email
dotnet user-secrets set "EmailSettings:SmtpHost" "smtp.gmail.com"
dotnet user-secrets set "EmailSettings:SmtpPort" "587"
dotnet user-secrets set "EmailSettings:UseStartTls" "true"

Write-Host ""
Write-Host "Da luu user-secrets. Kiem tra:"
dotnet user-secrets list | Select-String "EmailSettings"

Pop-Location
