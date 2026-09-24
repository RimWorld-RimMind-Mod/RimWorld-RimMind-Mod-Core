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
		if [[ -d $path ]]; then
			echo "$path"
			return 0
		fi
	done
	return 1
}

# 解析 RimWorld 路径
if [[ -n ${RIMWORLD_PATH:-} ]]; then
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

if [[ ! -d $RIMWORLD_PATH ]]; then
	echo "Error: RimWorld not found at $RIMWORLD_PATH"
	exit 1
fi

# 确保 Mods 目录存在
if [[ ! -d $RIMWORLD_MODS ]]; then
	echo "Error: Mods directory not found at $RIMWORLD_MODS"
	exit 1
fi

# 构建
SOURCE_DIR="$MOD_DIR/Source"

if [[ $MOD_NAME == "RimMind-Core" ]]; then
	CONTRACTS_CSPROJ="$SOURCE_DIR/Contracts/RimMindCore.Contracts.csproj"
	KERNEL_CSPROJ="$SOURCE_DIR/Kernel/RimMindCore.Kernel.csproj"
	CORE_CSPROJ=$(find "$SOURCE_DIR" -maxdepth 1 -name "RimMindCore.csproj" 2>/dev/null | head -1)

	if [[ ! -f $CONTRACTS_CSPROJ ]]; then
		echo "Error: Contracts csproj not found: $CONTRACTS_CSPROJ"
		exit 1
	fi
	if [[ ! -f $KERNEL_CSPROJ ]]; then
		echo "Error: Kernel csproj not found: $KERNEL_CSPROJ"
		exit 1
	fi
	if [[ -z $CORE_CSPROJ ]]; then
		echo "Error: Core csproj not found in $SOURCE_DIR"
		exit 1
	fi

	echo "=== Building $MOD_NAME (Contracts) ==="
	if ! dotnet build "$CONTRACTS_CSPROJ" -c Release --nologo -v quiet; then
		echo "Error: Contracts build failed"
		exit 1
	fi
	echo "  Contracts build successful"

	echo "=== Building $MOD_NAME (Kernel) ==="
	if ! dotnet build "$KERNEL_CSPROJ" -c Release --nologo -v quiet; then
		echo "Error: Kernel build failed"
		exit 1
	fi
	echo "  Kernel build successful"

	echo "=== Building $MOD_NAME (Core) ==="
	if ! dotnet build "$CORE_CSPROJ" -c Release --nologo -v quiet; then
		echo "Error: Core build failed"
		exit 1
	fi
	echo "  Core build successful"
else
	CSPROJ=$(find "$SOURCE_DIR" -maxdepth 1 -name "*.csproj" 2>/dev/null | head -1)
	if [[ -n $CSPROJ ]]; then
		echo "=== Building $MOD_NAME ==="
		if ! dotnet build "$CSPROJ" -c Release --nologo -v quiet; then
			echo "Error: Build failed"
			exit 1
		fi
		echo "  Build successful"
	else
		echo "No .csproj found in Source/, skipping build"
	fi
fi

# 部署
echo "=== Deploying $MOD_NAME -> $RIMWORLD_MODS/$MOD_NAME ==="
if ! rsync -a --delete \
	--exclude='Source/' \
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
