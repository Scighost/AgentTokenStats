#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

detect_rid() {
  local os arch cpu
  os=$(uname -s | tr '[:upper:]' '[:lower:]')
  arch=$(uname -m)

  case "$os" in
    linux*) os=linux ;;
    darwin*) os=osx ;;
    mingw*|msys*|cygwin*) os=win ;;
    *)
      echo "Unsupported OS: $os" >&2
      exit 1
      ;;
  esac

  case "$arch" in
    x86_64|amd64) cpu=x64 ;;
    aarch64|arm64) cpu=arm64 ;;
    i386|i686) cpu=x86 ;;
    armv7l|armv6l) cpu=arm ;;
    *)
      echo "Unsupported CPU architecture: $arch" >&2
      exit 1
      ;;
  esac

  if [[ "$os" == linux ]] && { [[ -f /etc/alpine-release ]] || ldd --version 2>&1 | grep -qi musl; }; then
    os=linux-musl
  fi

  printf '%s-%s\n' "$os" "$cpu"
}

rid=$(detect_rid)
if [[ "$rid" == win-* ]]; then
  exe=ats.exe
else
  exe=ats
fi
out="artifacts/${rid}"

(
  cd src/AgentTokenStats.Web
  if [[ -f package-lock.json ]]; then
    npm ci
  else
    npm install
  fi
  npm run build
)

dotnet publish src/AgentTokenStats/AgentTokenStats.csproj \
  -c Release \
  -r "$rid" \
  --self-contained true \
  -o "$out"

if [[ "$rid" != win-* ]]; then
  chmod +x "${out}/${exe}"
fi

echo "Published to ${out}/${exe}"
