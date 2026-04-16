#!/bin/bash
# Deploy this RimMind mod to RimWorld Mods folder.
# Place at: RimMind-*/script/deploy-single.sh
#
# Usage:
#   ./script/deploy-single.sh                                    # use default path
#   ./script/deploy-single.sh /path/to/RimWorld                  # custom path
#   RIMWORLD_PATH=/path/to/RimWorld ./script/deploy-single.sh    # env var override

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MOD_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
MOD_NAME="$(basename "$MOD_DIR")"

# 自动检测常见 RimWorld 安装路径
detect_rimworld_path() {
    local paths=(
        "/mnt/c/Program Files (x86)/Steam/steamapps/common/RimWorld"
        "/mnt/c/Program Files/Steam/steamapps/common/RimWorld"
        "$HOME/.steam/steam/steamapps/common/RimWorld"
        "$HOME/.local/share/Steam/steamapps/common/RimWorld"
    )
    for path in "${paths[@]}"; do
        if [[ -d "$path" ]]; then
            echo "$path"
            return 0
        fi
    done
    return 1
}

# 解析 RimWorld 路径
if [[ -n "${RIMWORLD_PATH:-}" ]]; then
    RIMWORLD_PATH="$RIMWORLD_PATH"
elif [[ $# -gt 0 ]]; then
    RIMWORLD_PATH="$1"
else
    RIMWORLD_PATH=$(detect_rimworld_path) || {
        echo "Error: Cannot find RimWorld installation"
        echo "  Usage: $0 [/path/to/RimWorld]"
        echo "  Or:    export RIMWORLD_PATH=/path/to/RimWorld && $0"
        exit 1
    }
fi

RIMWORLD_MODS="$RIMWORLD_PATH/Mods"

if [[ ! -d "$RIMWORLD_PATH" ]]; then
    echo "Error: RimWorld not found at $RIMWORLD_PATH"
    exit 1
fi

# 确保 Mods 目录存在
if [[ ! -d "$RIMWORLD_MODS" ]]; then
    echo "Error: Mods directory not found at $RIMWORLD_MODS"
    exit 1
fi

# 构建
CSPROJ=$(find "$MOD_DIR/Source" -maxdepth 1 -name "*.csproj" 2>/dev/null | head -1)
if [[ -n "$CSPROJ" ]]; then
    echo "=== Building $MOD_NAME ==="
    if ! dotnet build "$CSPROJ" -c Release --nologo -v quiet; then
        echo "Error: Build failed"
        exit 1
    fi
    echo "  Build successful"
else
    echo "No .csproj found in Source/, skipping build"
fi

# 部署
echo "=== Deploying $MOD_NAME -> $RIMWORLD_MODS/$MOD_NAME ==="
if ! rsync -a --delete \
    --exclude='Sources/' \
    --exclude='Tests/' \
    --exclude='*.csproj' \
    --exclude='*.user' \
    --exclude='obj/' \
    --exclude='.git/' \
    --exclude='.gitignore' \
    --exclude='script/' \
    "$MOD_DIR/" "$RIMWORLD_MODS/$MOD_NAME/"; then
    echo "Error: Deployment failed"
    exit 1
fi

# 验证部署
if [[ -d "$RIMWORLD_MODS/$MOD_NAME" ]]; then
    echo "  Done: $RIMWORLD_MODS/$MOD_NAME"
else
    echo "Error: Deployment verification failed"
    exit 1
fi
