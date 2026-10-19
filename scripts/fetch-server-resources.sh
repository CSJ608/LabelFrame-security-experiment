#!/usr/bin/env bash
# LabelFrame Docker 整合部署——资源在线落位脚本
#
# 用法（在 compose 所在目录执行；纯用户态，无需 root）：
#   落位到默认目录 ./labelframe：                    bash fetch-server-resources.sh
#   指定目标目录：                                    bash fetch-server-resources.sh /srv/labelframe
#   指定版本（默认优先读同目录 .env 的 LABELFRAME_VERSION）：bash fetch-server-resources.sh --version 0.36.0
#   指定清单（本地文件 / URL，测试或镜像源）：          bash fetch-server-resources.sh --manifest install-manifest.json
#
# 行为：消费 install-manifest.json（sha256 逐源强制校验 + urls 多源回退，fail-closed）→
#   五类资源按 Docker 挂载布局落位到目标目录（默认 ./labelframe，即「整合进已有 compose」片段的单根挂载点）：
#     webui         → <目标>/server/plugins/web-ui/（解压展开；临时目录解压 + 原子换名）
#     client-msi    → <目标>/server/client-packages/
#     pda-apk       → <目标>/server/pda-packages/
#     plugin-zebra  → <目标>/server/plugin-packages/
#     plugin-tspl   → <目标>/server/plugin-packages/（1.1.0 起收录；旧版清单无此条目时跳过）
# 幂等：重跑无害——已就位且 sha256 相符的包文件跳过下载；webui 每次重新校验展开。
# 旧版本包文件保留（下载中心「最新上传」货架语义允许多版本共存）。
# 前置：curl 或 wget、sha256sum、unzip（Ubuntu / Debian：apt-get install -y curl unzip）。
# 适用：在线环境（可达 GitHub Releases）；离线环境改用离线部署包（docs/DEPLOY.md）。
set -euo pipefail

REPO_RELEASES_URL="https://github.com/marci-labs/LabelFrame/releases"
MANIFEST_LATEST_URL="$REPO_RELEASES_URL/latest/download/install-manifest.json"

TARGET_DIR="./labelframe"
VERSION_ARG=""
MANIFEST_ARG=""

die() {
  echo "[LabelFrame 资源落位] 错误：$*" >&2
  exit 1
}
info() {
  echo "[LabelFrame 资源落位] $*"
}
usage() {
  cat <<'USAGE'
LabelFrame Docker 整合部署——资源在线落位脚本（docs/DEPLOY.md「整合进已有 compose」）
用法：
  bash fetch-server-resources.sh [目标目录]                     落位到 ./labelframe（默认）
  bash fetch-server-resources.sh --version <版本>               指定版本（如 0.36.0）
  bash fetch-server-resources.sh --manifest <清单文件 / URL>    指定 install-manifest.json
  bash fetch-server-resources.sh -h | --help                    本说明
版本解析优先级：--version > 同目录 .env 的 LABELFRAME_VERSION > 官方 latest 通道。
USAGE
  exit 0
}

# ---- 参数 ----
while [ "$#" -gt 0 ]; do
  case "$1" in
    --version) [ "$#" -ge 2 ] || die "--version 需要一个参数（如 0.36.0）。"; VERSION_ARG="$2"; shift 2 ;;
    --manifest) [ "$#" -ge 2 ] || die "--manifest 需要一个参数（本地文件 / URL）。"; MANIFEST_ARG="$2"; shift 2 ;;
    -h|--help) usage ;;
    -*) die "未知参数：$1（--help 查看用法）。" ;;
    *) [ "$TARGET_DIR" = "./labelframe" ] || die "目标目录只接受一个：$TARGET_DIR 与 $1"; TARGET_DIR="$1"; shift ;;
  esac
done

# ---- 前置检查（纯用户态；不碰 systemd / 不需要 root）----
DOWNLOADER=""
if command -v curl >/dev/null 2>&1; then
  DOWNLOADER=curl
elif command -v wget >/dev/null 2>&1; then
  DOWNLOADER=wget
else
  die "未找到 curl 或 wget——请先安装（Ubuntu / Debian：apt-get install -y curl）。"
fi
command -v sha256sum >/dev/null 2>&1 || die "未找到 sha256sum——请先安装 coreutils。"
command -v unzip >/dev/null 2>&1 || die "未找到 unzip（展开管理界面需要）——请先安装（Ubuntu / Debian：apt-get install -y unzip）。"

download() {
  # download <URL> <目标文件>：curl / wget 双支持（-f：HTTP 错误码视为失败；-L：跟随 Release 重定向）
  local url="$1" dest="$2"
  if [ "$DOWNLOADER" = curl ]; then
    curl -fsSL --connect-timeout 10 --retry 2 -o "$dest" "$url"
  else
    wget -q -t 2 -T 60 -O "$dest" "$url"
  fi
}

file_sha256() {
  sha256sum "$1" | awk '{print $1}'
}

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ---- 版本解析：--version > .env 的 LABELFRAME_VERSION > latest ----
normalize_version() {
  # 去掉前导 v 与可能的引号 / 回车（.env 为 CRLF 行尾时）
  local v="${1#v}"
  v="${v//\"/}"
  v="${v//\'/}"
  printf '%s' "$v" | tr -d '\r'
}

CHANNEL="latest"
VERSION_PINNED=""
if [ -n "$VERSION_ARG" ]; then
  VERSION_PINNED="$(normalize_version "$VERSION_ARG")"
  info "使用指定版本：$VERSION_PINNED（--version）。"
elif [ -f ./.env ]; then
  ENV_VERSION="$(grep -E '^\s*LABELFRAME_VERSION\s*=' ./.env | tail -1 | cut -d= -f2- || true)"
  ENV_VERSION="$(normalize_version "$ENV_VERSION")"
  if [ -n "$ENV_VERSION" ] && [ "$ENV_VERSION" != "latest" ]; then
    VERSION_PINNED="$ENV_VERSION"
    info "使用 .env 钉定版本：$VERSION_PINNED（LABELFRAME_VERSION）——与 compose 镜像版本保持一致。"
  elif [ -n "$ENV_VERSION" ]; then
    info ".env 的 LABELFRAME_VERSION=latest——走官方 latest 通道（建议生产钉定具体版本）。"
  fi
fi

# ---- 获取清单 ----
if [ -n "$MANIFEST_ARG" ]; then
  case "$MANIFEST_ARG" in
    http://*|https://*)
      info "获取清单（指定 URL）：$MANIFEST_ARG"
      download "$MANIFEST_ARG" "$WORK/install-manifest.json" || die "清单下载失败：$MANIFEST_ARG"
      ;;
    *)
      [ -f "$MANIFEST_ARG" ] || die "--manifest 指定的文件不存在：$MANIFEST_ARG"
      cp "$MANIFEST_ARG" "$WORK/install-manifest.json"
      info "使用本地清单：$MANIFEST_ARG"
      ;;
  esac
else
  if [ -n "$VERSION_PINNED" ]; then
    CHANNEL="v$VERSION_PINNED"
    MANIFEST_URL="$REPO_RELEASES_URL/download/v$VERSION_PINNED/install-manifest.json"
  else
    MANIFEST_URL="$MANIFEST_LATEST_URL"
  fi
  info "获取清单（$CHANNEL 通道）：$MANIFEST_URL"
  download "$MANIFEST_URL" "$WORK/install-manifest.json" || die "清单下载失败——检查网络 / 代理，或用 --manifest <本地文件> 指定清单。"
fi
MANIFEST="$WORK/install-manifest.json"
[ -s "$MANIFEST" ] || die "清单文件为空或不可读：$MANIFEST"

# ---- 解析清单（行级解析 CI 生成的 schemaVersion=1 形态；与 install-server-linux.sh 同模式，无 jq 依赖）----
SCHEMA_VERSION="$(awk 'match($0, /"schemaVersion": *[0-9]+/) { v = $0; sub(/.*"schemaVersion": */, "", v); sub(/[^0-9].*/, "", v); print v; exit }' "$MANIFEST")"
[ "$SCHEMA_VERSION" = "1" ] || die "清单 schemaVersion=${SCHEMA_VERSION:-<缺失>} 不受本脚本支持（当前支持 1）——请从仓库更新本脚本。"

MANIFEST_VERSION="$(awk 'match($0, /"labelframeVersion": *"/) { v = $0; sub(/.*"labelframeVersion": *"/, "", v); sub(/".*/, "", v); print v; exit }' "$MANIFEST")"
[ -n "$MANIFEST_VERSION" ] || die "清单缺少 labelframeVersion——清单非法。"
if [ -n "$VERSION_PINNED" ] && [ "$MANIFEST_VERSION" != "$VERSION_PINNED" ]; then
  die "清单版本（$MANIFEST_VERSION）与要求版本（$VERSION_PINNED）不符——检查 --version / .env 或清单来源。"
fi
info "资源版本：$MANIFEST_VERSION"

parse_component() {
  awk -v want="$1" '
    /"id": *"/ {
      id = $0; sub(/.*"id": *"/, "", id); sub(/".*/, "", id)
      inb = (id == want)
    }
    inb && /"version": *"/ { v = $0; sub(/.*"version": *"/, "", v); sub(/".*/, "", v); print "version\t" v }
    inb && /"urls": *\[/ {
      u = $0; sub(/.*"urls": *\[/, "", u); sub(/\].*/, "", u)
      n = split(u, arr, /", */)
      for (i = 1; i <= n; i++) { gsub(/^[ \t"]+|[ \t"]+$/, "", arr[i]); if (arr[i] != "") print "url\t" arr[i] }
    }
    inb && /"sha256": *"/ { s = $0; sub(/.*"sha256": *"/, "", s); sub(/".*/, "", s); print "sha256\t" s }
    inb && /"sizeBytes": *[0-9]/ { b = $0; sub(/.*"sizeBytes": */, "", b); sub(/[^0-9].*/, "", b); print "sizeBytes\t" b }
  ' "$MANIFEST"
}

load_component() {
  # load_component <组件 id>：解析进 COMP_VERSION / COMP_SHA256 / COMP_SIZE / COMP_URLS / COMP_NAME
  local id="$1" key value
  COMP_VERSION=""; COMP_SHA256=""; COMP_SIZE=""; COMP_URLS=()
  while IFS=$'\t' read -r key value; do
    case "$key" in
      version) COMP_VERSION="$value" ;;
      sha256) COMP_SHA256="$value" ;;
      sizeBytes) COMP_SIZE="$value" ;;
      url) COMP_URLS+=("$value") ;;
    esac
  done < <(parse_component "$id")
  [ -n "$COMP_SHA256" ] || die "清单缺少组件条目：$id（manifest 应收录当版全部产物，见 DESIGN.md）。"
  [ "${#COMP_URLS[@]}" -ge 1 ] || die "组件 $id 的 urls 为空——清单非法。"
  echo "$COMP_SHA256" | grep -Eq '^[0-9a-f]{64}$' || die "组件 $id 的 sha256 非小写 64 位 hex：$COMP_SHA256"
  COMP_NAME="$(basename "${COMP_URLS[0]}")"
  [ -n "$COMP_NAME" ] || die "组件 $id 的 urls[0] 缺文件名：${COMP_URLS[0]}"
}

acquire_component() {
  # acquire_component <组件 id>：校验后的产物下载到 $WORK/$COMP_NAME（urls 逐源回退，sha256 / sizeBytes 双校验）
  local id="$1" dest="$WORK/$COMP_NAME" src i=1 total="${#COMP_URLS[@]}"
  [ -n "$COMP_SIZE" ] || die "组件 $id 缺 sizeBytes——清单非法。"
  for src in "${COMP_URLS[@]}"; do
    info "[$id] 下载（源 $i/$total）：$src"
    if download "$src" "$dest" && [ "$(file_sha256 "$dest")" = "$COMP_SHA256" ]; then
      if [ "$(stat -c %s "$dest")" != "$COMP_SIZE" ]; then
        info "[$id] 体积不符（期望 $COMP_SIZE 字节），换下一源。"
        rm -f "$dest"
      else
        info "[$id] 下载校验通过（sha256 / sizeBytes 一致）。"
        return 0
      fi
    else
      rm -f "$dest"
      info "[$id] 获取失败或哈希不符，尝试下一源。"
    fi
    i=$((i + 1))
  done
  die "[$id] 全部 $total 个源均失败——请检查网络，或改用离线部署包（docs/DEPLOY.md）。"
}

# ---- 落位 ----
mkdir -p "$TARGET_DIR/server/client-packages" "$TARGET_DIR/server/pda-packages" "$TARGET_DIR/server/plugin-packages" \
  "$TARGET_DIR/server/plugins" "$TARGET_DIR/logs"

# 包文件类组件：目标已就位且 sha256 相符则跳过（幂等），否则下载校验后落位
place_package() {
  # place_package <组件 id> <目标目录>
  local id="$1" dir="$2"
  load_component "$id"
  # dest 引用本行之前已赋值的 dir（同一 local 语句内先展开后赋值，交叉引用会触发 unbound）
  local dest="$dir/$COMP_NAME"
  if [ -f "$dest" ] && [ "$(file_sha256 "$dest")" = "$COMP_SHA256" ]; then
    info "[$id] 已就位且校验相符，跳过：$dest"
    return 0
  fi
  acquire_component "$id"
  mv -f "$WORK/$COMP_NAME" "$dest"
  info "[$id] 落位：$dest"
}

# 管理界面：每次下载校验后解压展开，临时目录 + 原子换名（避免「移除即无头」检测窗口）
place_webui() {
  local id="webui" webui_dir="$TARGET_DIR/server/plugins/web-ui" staging="$WORK/webui-staging"
  load_component "$id"
  acquire_component "$id"
  mkdir -p "$staging"
  unzip -q "$WORK/$COMP_NAME" -d "$staging"
  [ -f "$staging/index.html" ] || die "管理界面 zip 解压后缺少 index.html——插件包不完整。"
  if [ -d "$webui_dir" ]; then
    local old="$WORK/webui-old"
    mv "$webui_dir" "$old"
    mv "$staging" "$webui_dir"
    rm -rf "$old"
  else
    mv "$staging" "$webui_dir"
  fi
  info "[webui] 落位（展开）：$webui_dir"
}

place_package client-msi "$TARGET_DIR/server/client-packages"
place_package pda-apk "$TARGET_DIR/server/pda-packages"
place_package plugin-zebra "$TARGET_DIR/server/plugin-packages"
# TSPL 插件自 1.1.0 起随发版收录；钉定旧版（manifest 无 plugin-tspl 条目）时跳过而非失败
if parse_component plugin-tspl | grep -q .; then
  place_package plugin-tspl "$TARGET_DIR/server/plugin-packages"
else
  info "[plugin-tspl] 清单版本 $MANIFEST_VERSION 未收录 TSPL 插件，跳过。"
fi
place_webui

info "完成（版本 $MANIFEST_VERSION）。布局："
cat <<LAYOUT
  $TARGET_DIR/
  └── server/
      ├── plugins/web-ui/          管理界面（浏览器访问 http://<宿主机>:53961）
      ├── client-packages/         客户端 MSI（客户端设置页「更新与安装包」可下载）
      ├── pda-packages/            PDA APK（管理界面「下载中心」扫码下载）
      ├── plugin-packages/         传输插件 .lfplugin（客户端「插件管理」可安装）
      └──（server.db 等数据文件由服务端启动时自动生成）
LAYOUT
info "下一步：docker compose up -d 后访问 /healthz 自检；整合片段与用法见 docs/DEPLOY.md「整合进已有 compose」。"
