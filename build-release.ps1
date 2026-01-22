# KoThumb2 デュアルリリースビルドスクリプト
# 2つのビルドを作成: フレームワーク依存版（軽量）と自己完結型版（単一EXE）

param(
    [string]$Version = "2.0.0"
)

Write-Host "--- KoThumb2 v$Version Dual Release Build ---" -ForegroundColor Cyan
Write-Host ""

# 変数定義
$ProjectFile = "KoThumb2.csproj"
$DistDir = "dist"
$TempFrameworkDir = "$DistDir\temp_framework"
$TempStandaloneDir = "$DistDir\temp_standalone"
$FrameworkZipFile = "$DistDir\KoThumb2-v$Version-framework-dependent-release.zip"
$StandaloneZipFile = "$DistDir\KoThumb2-v$Version-standalone-release.zip"

# ビルド開始時刻を記録
$BuildStartTime = Get-Date

# Create dist directory if it doesn't exist
if (!(Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}

# 既存のZIPファイルを削除
if (Test-Path $FrameworkZipFile) {
    Remove-Item -Path $FrameworkZipFile -Force
}
if (Test-Path $StandaloneZipFile) {
    Remove-Item -Path $StandaloneZipFile -Force
}

# 一時ディレクトリを削除
if (Test-Path $TempFrameworkDir) {
    Remove-Item -Path $TempFrameworkDir -Recurse -Force
}
if (Test-Path $TempStandaloneDir) {
    Remove-Item -Path $TempStandaloneDir -Recurse -Force
}

# ========================================
# フレームワーク依存ビルド（軽量版）
# ========================================
Write-Host "Building Framework-Dependent (Lightweight)..." -ForegroundColor Yellow
$frameworkBuildSuccess = $false
try {
    Write-Host "  Publishing..." -ForegroundColor Gray
    dotnet publish $ProjectFile `
        -c Release `
        --self-contained false `
        /p:PublishSingleFile=false `
        /p:DebugType=none `
        /p:DebugSymbols=false `
        --output $TempFrameworkDir

    if ($LASTEXITCODE -eq 0) {
        # Copy README
        if (Test-Path "README.md") {
            Copy-Item "README.md" -Destination $TempFrameworkDir
        }
        
        # Create ZIP
        if (Test-Path $FrameworkZipFile) {
            Remove-Item $FrameworkZipFile
        }
        Compress-Archive -Path "$TempFrameworkDir\*" -DestinationPath $FrameworkZipFile
        Write-Host "  ✓ Framework-dependent build completed" -ForegroundColor Green
        $frameworkBuildSuccess = $true
    } else {
        throw "Publish failed!"
    }
} catch {
    Write-Host "  ✗ Framework-dependent build failed: $($_.Exception.Message)" -ForegroundColor Red
}

# ========================================
# 自己完結型ビルド（単一EXE版）
# ========================================
Write-Host ""
Write-Host "Building Self-Contained (Single EXE)..." -ForegroundColor Yellow
$standaloneBuildSuccess = $false
try {
    Write-Host "  Publishing..." -ForegroundColor Gray
    dotnet publish $ProjectFile `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        /p:DebugType=none `
        /p:DebugSymbols=false `
        --output $TempStandaloneDir

    if ($LASTEXITCODE -eq 0) {
        # Copy README
        if (Test-Path "README.md") {
            Copy-Item "README.md" -Destination $TempStandaloneDir
        }
        
        # Create ZIP
        if (Test-Path $StandaloneZipFile) {
            Remove-Item $StandaloneZipFile
        }
        Compress-Archive -Path "$TempStandaloneDir\*" -DestinationPath $StandaloneZipFile
        Write-Host "  ✓ Self-contained build completed" -ForegroundColor Green
        $standaloneBuildSuccess = $true
    } else {
        throw "Publish failed!"
    }
} catch {
    Write-Host "  ✗ Self-contained build failed: $($_.Exception.Message)" -ForegroundColor Red
}

# 両方のビルドが失敗した場合はエラー終了
if (-not $frameworkBuildSuccess -and -not $standaloneBuildSuccess) {
    Write-Error "Both builds failed!"
    exit 1
}

# Cleanup temporary directories
Write-Host ""
Write-Host "Cleaning up temporary files..." -ForegroundColor Yellow
if (Test-Path $TempFrameworkDir) {
    Remove-Item -Path $TempFrameworkDir -Recurse -Force
}
if (Test-Path $TempStandaloneDir) {
    Remove-Item -Path $TempStandaloneDir -Recurse -Force
}
Write-Host "Cleanup completed" -ForegroundColor Green
Write-Host ""

# ビルド結果のサマリー表示
$BuildEndTime = Get-Date
$BuildDuration = $BuildEndTime - $BuildStartTime
$BuildTimeSeconds = [math]::Round($BuildDuration.TotalSeconds, 1)

Write-Host "--- Build Finished! ---" -ForegroundColor Green
Write-Host ""

# フレームワーク依存ビルドの情報
if ($frameworkBuildSuccess -and (Test-Path $FrameworkZipFile)) {
    $frameworkZipInfo = Get-Item $FrameworkZipFile
    $frameworkZipHash = Get-FileHash $FrameworkZipFile -Algorithm SHA256
    
    Write-Host "📦 Framework-Dependent Build (Lightweight):" -ForegroundColor Cyan
    Write-Host "   File: $($frameworkZipInfo.Name)" -ForegroundColor White
    Write-Host "   Size: $([math]::Round($frameworkZipInfo.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host "   SHA256: $($frameworkZipHash.Hash)" -ForegroundColor Gray
    Write-Host "   ⚠ Requires .NET 9.0 Desktop Runtime" -ForegroundColor Yellow
    Write-Host ""
}

# 自己完結型ビルドの情報
if ($standaloneBuildSuccess -and (Test-Path $StandaloneZipFile)) {
    $standaloneZipInfo = Get-Item $StandaloneZipFile
    $standaloneZipHash = Get-FileHash $StandaloneZipFile -Algorithm SHA256
    
    Write-Host "📦 Self-Contained Build (Single EXE):" -ForegroundColor Cyan
    Write-Host "   File: $($standaloneZipInfo.Name)" -ForegroundColor White
    Write-Host "   Size: $([math]::Round($standaloneZipInfo.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host "   SHA256: $($standaloneZipHash.Hash)" -ForegroundColor Gray
    Write-Host "   ✓ No .NET Runtime installation required" -ForegroundColor Green
    Write-Host ""
}

Write-Host "⏱ Total build time: $BuildTimeSeconds seconds" -ForegroundColor White
Write-Host "Package is located at: $DistDir\" -ForegroundColor White
Write-Host ""
