# BanglaHost Automated Release Script for SourceForge

$ProjectName = "banglahost-local-server"
$FilePath = "I:\BanglaHost\installer\dist\BanglaHost-Setup-1.5.1.0.exe"
$FileName = "BanglaHost-Setup-1.5.1.0.exe"

$Username = "shahinurrahman"
$ApiKey = "4aa97d11-a979-4f8b-856b-43b53efd6a6e"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " BanglaHost SourceForge Auto-Releaser " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# Check if file exists
if (-Not (Test-Path $FilePath)) {
    Write-Host "Error: Cannot find file $FilePath" -ForegroundColor Red
    exit 1
}

$FileSize = [math]::Round((Get-Item $FilePath).Length / 1MB, 2)
Write-Host "File found: $FileName ($FileSize MB)" -ForegroundColor Green

Write-Host "`nStep 1: Uploading $FileName to SourceForge via SFTP..." -ForegroundColor Yellow

# Create SFTP batch commands
$SftpBatch = "$env:TEMP\sftp_upload.txt"
@"
cd /home/frs/project/$ProjectName
put $FilePath $FileName
exit
"@ | Set-Content $SftpBatch

sftp -b $SftpBatch "$Username@frs.sourceforge.net"

# Check if the upload was successful
if ($?) {
    Write-Host "`nStep 2: Upload successful! Setting as the default download for Windows..." -ForegroundColor Yellow
    
    $ApiUrl = "https://sourceforge.net/projects/$ProjectName/files/$FileName"
    $Body = "default=windows&api_key=$ApiKey"
    
    try {
        $Response = Invoke-WebRequest -Uri $ApiUrl -Method PUT -Body $Body -Headers @{"Accept"="application/json"}
        
        if ($Response.StatusCode -eq 200) {
            Write-Host "`nSUCCESS! $FileName is now live and set as the default download for Windows." -ForegroundColor Green
            Write-Host "Download Link: https://sourceforge.net/projects/$ProjectName/files/$FileName/download" -ForegroundColor Cyan
        } else {
            Write-Host "`nWarning: Upload succeeded, but failed to set as default. Status Code: $($Response.StatusCode)" -ForegroundColor Red
        }
    } catch {
        Write-Host "`nError calling the Release API: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "`nUpload failed. Please check your username, internet connection, and file path." -ForegroundColor Red
}

# Cleanup
Remove-Item $SftpBatch -ErrorAction SilentlyContinue
