# Deploy this RimMind mod to RimWorld Mods folder (Windows PowerShell).
# Place at: RimMind-*/script/deploy-single.ps1
#
# Usage:
#   .\script\deploy-single.ps1
#   .\script\deploy-single.ps1 "D:\Games\RimWorld"
#   $env:RIMWORLD_DIR="D:\Games\RimWorld"; .\script\deploy-single.ps1

param(
    [string]$RimWorldDir = "",
    [string[]]$GameVersions = @("1.7", "1.6")
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ModDir    = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$ModName   = Split-Path -Leaf $ModDir

function Detect-RimWorldPath {
    $paths = @(
        "C:\Program Files (x86)\Steam\steamapps\common\RimWorld",
        "C:\Program Files\Steam\steamapps\common\RimWorld",
        "$env:USERPROFILE\scoop\apps\steam\current\steamapps\common\RimWorld"
    )
    foreach ($path in $paths) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

if ($RimWorldDir -eq "" -and $env:RIMWORLD_DIR) {
    $RimWorldDir = $env:RIMWORLD_DIR
} elseif ($RimWorldDir -eq "" -and $env:RIMWORLD_PATH) {
    $RimWorldDir = $env:RIMWORLD_PATH
} elseif ($RimWorldDir -eq "") {
    $RimWorldDir = Detect-RimWorldPath
    if (-not $RimWorldDir) {
        Write-Error "Cannot find RimWorld installation"
        Write-Host "  Usage: .\script\deploy-single.ps1 [RimWorldPath]" -ForegroundColor Yellow
        Write-Host "  Or:    `$env:RIMWORLD_DIR='D:\Games\RimWorld'; .\script\deploy-single.ps1" -ForegroundColor Yellow
        exit 1
    }
}

if (-not (Test-Path $RimWorldDir)) {
    Write-Error "RimWorld directory not found: $RimWorldDir"
    exit 1
}

$RimWorldMods = Join-Path $RimWorldDir "Mods"
if (-not (Test-Path $RimWorldMods)) {
    Write-Error "Mods directory not found at $RimWorldMods"
    exit 1
}

# Build for all target game versions (1.7 & 1.6)
$SourceDir = Join-Path $ModDir "Source"

foreach ($gameVersion in $GameVersions) {
    Write-Host "=== Building $ModName (RimWorld $gameVersion) ===" -ForegroundColor Cyan

    if ($ModName -eq "RimMind-Core") {
        $domainCsproj = Join-Path $SourceDir "Domain\RimMindCore.Domain.csproj"
        $applicationCsproj = Join-Path $SourceDir "Application\RimMindCore.Application.csproj"
        $coreCsproj = Get-ChildItem -Path $SourceDir -Filter "RimMindCore.csproj" -File -ErrorAction SilentlyContinue | Select-Object -First 1

        if (-not (Test-Path $domainCsproj)) {
            Write-Error "Domain csproj not found: $domainCsproj"
            exit 1
        }
        if (-not (Test-Path $applicationCsproj)) {
            Write-Error "Application csproj not found: $applicationCsproj"
            exit 1
        }
        if (-not $coreCsproj) {
            Write-Error "Core csproj not found in $SourceDir"
            exit 1
        }

        dotnet build $domainCsproj -c Release -p:GameVersion=$gameVersion -p:SkipDeployToModsFolder=true --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "Domain build failed ($gameVersion)"; exit $LASTEXITCODE }

        dotnet build $applicationCsproj -c Release -p:GameVersion=$gameVersion -p:SkipDeployToModsFolder=true --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "Application build failed ($gameVersion)"; exit $LASTEXITCODE }

        dotnet build $coreCsproj.FullName -c Release -p:GameVersion=$gameVersion -p:SkipDeployToModsFolder=true --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { Write-Error "Core build failed ($gameVersion)"; exit $LASTEXITCODE }
    } else {
        $csproj = Get-ChildItem -Path $SourceDir -Filter "*.csproj" -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($csproj) {
            dotnet build $csproj.FullName -c Release -p:GameVersion=$gameVersion -p:SkipDeployToModsFolder=true --nologo -v quiet
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Build failed ($gameVersion)"
                exit $LASTEXITCODE
            }
        } else {
            Write-Host "No .csproj found in Source\, skipping build" -ForegroundColor Yellow
        }
    }
}

# Deploy with clean runtime exclusions (never copy Core/Newtonsoft DLLs into non-Core mods)
$DestDir = Join-Path $RimWorldMods $ModName
Write-Host "=== Deploying $ModName -> $DestDir ===" -ForegroundColor Cyan

$excludeFiles = @(
    "*.csproj", "*.user", ".gitignore", ".git", ".gitattributes",
    ".coderabbit.yaml", ".pre-commit-config.yaml",
    "AGENTS.md", "CLAUDE.md", "Directory.Build.props", "Directory.Build.targets",
    "INTRODUCTION.txt", "*.deps.json"
)
if ($ModName -ne "RimMind-Core") {
    $excludeFiles += @(
        "0_RimMindDomain.*", "1_RimMindApplication.*", "2_RimMindCore.*", "Newtonsoft.Json.*"
    )
}

robocopy $ModDir $DestDir /MIR /XD Source Tests ArchTests IntegrationTests TestSupport obj bin .git .github .claude docs script /XF $excludeFiles /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) {
    Write-Error "Deployment failed (robocopy exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

if ($ModName -ne "RimMind-Core") {
    foreach ($ver in $GameVersions) {
        $deployedAssemblies = Join-Path $DestDir "$ver\Assemblies"
        if (Test-Path $deployedAssemblies) {
            foreach ($pattern in @("0_RimMindDomain.*", "1_RimMindApplication.*", "2_RimMindCore.*", "Newtonsoft.Json.*")) {
                Get-ChildItem -Path $deployedAssemblies -Filter $pattern -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Write-Host "  Done: $DestDir" -ForegroundColor Green
