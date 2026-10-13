# 快速部署脚本（无参数）
# 编译并部署 Mod 到 RimWorld

Write-Host "正在部署 RimTalk-ExpandMemory..." -ForegroundColor Cyan

# 编译
# 1.7 目标需要 .NET 10 SDK；PATH 上的 dotnet 可能只有旧版本，优先使用用户目录下的 SDK
$Dotnet = if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT "dotnet.exe" }
          elseif (Test-Path (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")) { Join-Path $env:USERPROFILE ".dotnet\dotnet.exe" }
          else { "dotnet" }

& $Dotnet build RimTalk-ExpandMemory.csproj --configuration Release -p:GameVersion=1.6 --verbosity minimal
$b16 = $LASTEXITCODE
& $Dotnet build RimTalk-ExpandMemory.csproj --configuration Release -p:GameVersion=1.7 -p:RimWorldDir="$env:RIMWORLD_DIR" --verbosity minimal
$b17 = $LASTEXITCODE

if ($b16 -eq 0 -and $b17 -eq 0) {
    Write-Host "? 编译成功" -ForegroundColor Green
    
    # 部署
    $TargetPath = "D:\steam\steamapps\common\RimWorld\Mods\RimTalk-ExpandMemory"
    
    # 创建目录
    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
    
    # 复制文件
    Copy-Item -Path "About" -Destination "$TargetPath\About" -Recurse -Force
    Copy-Item -Path "1.6" -Destination "$TargetPath\1.6" -Recurse -Force
    Copy-Item -Path "1.7" -Destination "$TargetPath\1.7" -Recurse -Force
    Copy-Item -Path "Languages" -Destination "$TargetPath\Languages" -Recurse -Force
    Copy-Item -Path "LoadFolders.xml" -Destination "$TargetPath\LoadFolders.xml" -Force
    
    if (Test-Path "Textures") {
        Copy-Item -Path "Textures" -Destination "$TargetPath\Textures" -Recurse -Force
    }
    
    Write-Host "? 部署完成: $TargetPath" -ForegroundColor Green
    Write-Host ""
    Write-Host "请在 RimWorld Mod 管理器中启用 Mod" -ForegroundColor Yellow
} else {
    Write-Host "? 编译失败" -ForegroundColor Red
}
