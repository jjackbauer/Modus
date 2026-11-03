#!/usr/bin/env bash
set -euo pipefail
export PATH=/usr/sbin:/usr/bin:/sbin:/bin

dotnet_path="$1"
worker_path="$2"
worker_dir="$(dirname "$worker_path")"
dotnet_dir="$(dirname "$dotnet_path")"
root="$(mktemp -d /tmp/wip-sandbox-root.XXXXXX)"
cleanup() {
    if command -v findmnt >/dev/null 2>&1; then
        while IFS= read -r mount; do umount "$mount" 2>/dev/null || true; done < <(findmnt -R -n -o TARGET "$root" 2>/dev/null | tac)
    fi
    rm -rf "$root" 2>/dev/null || true
}
trap cleanup EXIT

mkdir -p "$root/dotnet" "$root/app" "$root/tmp" "$root/dev" "$root/proc" "$root/usr/share" "$root/usr/lib" "$root/lib64" "$root/lib"
touch "$root/dev/null" "$root/dev/urandom" "$root/dev/random"
mount --bind "$dotnet_dir" "$root/dotnet"
mount --bind "$worker_dir" "$root/app"
mount -t tmpfs tmpfs "$root/tmp"
mount --bind /dev/null "$root/dev/null"
mount --bind /dev/urandom "$root/dev/urandom"
mount --bind /dev/random "$root/dev/random"
mount -t proc proc "$root/proc"
mount --rbind /usr/share "$root/usr/share"
mount --rbind /usr/lib "$root/usr/lib"
mount --rbind /usr/lib "$root/lib"
if [ -d /usr/lib64 ]; then mount --rbind /usr/lib64 "$root/lib64"; fi

export DOTNET_ROOT=/dotnet DOTNET_CLI_HOME=/tmp HOME=/tmp TMP=/tmp TEMP=/tmp TMPDIR=/tmp
chroot "$root" /dotnet/$(basename "$dotnet_path") /app/$(basename "$worker_path")
