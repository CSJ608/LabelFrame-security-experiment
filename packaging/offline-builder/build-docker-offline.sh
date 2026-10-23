#!/usr/bin/env bash
set -euo pipefail

BUILDER_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$BUILDER_ROOT/dist"
CACHE_DIR="$BUILDER_ROOT/cache"
SOURCE_MANIFEST="$BUILDER_ROOT/offline-source-manifest.json"
STAGING_ROOT=""

info() { printf '[Docker Builder] %s\n' "$*" >&2; }
die() { printf '[Docker Builder] 错误：%s\n' "$*" >&2; exit 1; }
need_value() { [ "$#" -ge 2 ] && [ -n "$2" ] || die "$1 需要参数值"; }

usage() {
  cat <<'USAGE'
用法：bash build-docker-offline.sh [--output-dir <目录>] [--cache-dir <目录>] [--source-manifest <文件>]

在有网且已安装 Docker 的准备机上，按 Builder 内固定版本与镜像 digest 生成 Linux/Docker 离线包。
USAGE
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --output-dir)      need_value "$@"; OUTPUT_DIR="$2"; shift 2 ;;
    --cache-dir)       need_value "$@"; CACHE_DIR="$2"; shift 2 ;;
    --source-manifest) need_value "$@"; SOURCE_MANIFEST="$2"; shift 2 ;;
    -h|--help)         usage; exit 0 ;;
    *)                 die "未知参数：$1" ;;
  esac
done

for command_name in docker curl sha256sum gzip; do
  command -v "$command_name" >/dev/null 2>&1 || die "缺少命令：$command_name"
done
PYTHON=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1 && "$candidate" -c 'import json' >/dev/null 2>&1; then
    PYTHON="$candidate"
    break
  fi
done
[ -n "$PYTHON" ] || die '缺少可用的 Python 3。'
[ -s "$SOURCE_MANIFEST" ] || die "来源清单不存在：$SOURCE_MANIFEST"
mkdir -p "$OUTPUT_DIR" "$CACHE_DIR"
STAGING_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/labelframe-docker-builder.XXXXXX")"
trap 'rm -rf "$STAGING_ROOT"' EXIT

read_json() {
  "$PYTHON" - "$SOURCE_MANIFEST" "$1" <<'PY'
import json, sys
value=json.load(open(sys.argv[1],encoding='utf-8'))
for part in sys.argv[2].split('.'):
    if part.isdigit(): value=value[int(part)]
    else: value=value[part]
if isinstance(value,(dict,list)): print(json.dumps(value,separators=(',',':')))
else: print(value)
PY
}

SCHEMA_VERSION="$(read_json schemaVersion)"
[ "$SCHEMA_VERSION" = "1" ] || die "不支持的 schemaVersion：$SCHEMA_VERSION"
VERSION="$(read_json labelframeVersion)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+-][0-9A-Za-z.-]+)?$ ]] || die "版本号非法：$VERSION"
INSTALL_MANIFEST_URL="$(read_json installManifest.url)"
INSTALL_MANIFEST_SHA="$(read_json installManifest.sha256)"
IMAGE_REFERENCE="$(read_json serverImage.reference)"
IMAGE_DIGEST="$(read_json serverImage.digest)"
[[ "$IMAGE_DIGEST" =~ ^sha256:[0-9a-f]{64}$ ]] || die "镜像 digest 非法：$IMAGE_DIGEST"

verified_download() {
  local id="$1" file_name="$2" url="$3" expected_sha="$4" expected_size="${5:-0}"
  [[ "$file_name" != */* && "$file_name" != *\\* ]] || die "文件名非法：$file_name"
  [[ "$url" == https://* ]] || die "拒绝非 HTTPS 来源：$url"
  local target="$CACHE_DIR/$file_name" temporary="$CACHE_DIR/.${file_name}.download.$$"
  if [ -s "$target" ]; then
    local actual
    actual="$(sha256sum "$target" | cut -d' ' -f1)"
    if [ "$actual" = "$expected_sha" ] && { [ "$expected_size" = "0" ] || [ "$(wc -c < "$target")" = "$expected_size" ]; }; then
      info "缓存命中：[$id] $file_name"
      printf '%s\n' "$target"
      return
    fi
    info "缓存不符，删除重取：[$id] $file_name"
    rm -f "$target"
  fi
  rm -f "$temporary"
  if ! curl -fL --retry 3 --connect-timeout 30 --max-time 900 "$url" -o "$temporary"; then
    rm -f "$temporary"
    die "下载失败：[$id] $url"
  fi
  [ "$(sha256sum "$temporary" | cut -d' ' -f1)" = "$expected_sha" ] || { rm -f "$temporary"; die "SHA256 不符：[$id]"; }
  if [ "$expected_size" != "0" ] && [ "$(wc -c < "$temporary")" != "$expected_size" ]; then
    rm -f "$temporary"
    die "大小不符：[$id]"
  fi
  mv -f "$temporary" "$target"
  printf '%s\n' "$target"
}

INSTALL_MANIFEST="$(verified_download install-manifest install-manifest.json "$INSTALL_MANIFEST_URL" "$INSTALL_MANIFEST_SHA")"
[ "$("$PYTHON" - "$INSTALL_MANIFEST" <<'PY'
import json,sys
print(json.load(open(sys.argv[1],encoding='utf-8'))['labelframeVersion'])
PY
)" = "$VERSION" ] || die "install manifest 与 Builder 版本不一致"

component_field() {
  "$PYTHON" - "$INSTALL_MANIFEST" "$1" "$2" <<'PY'
import json,sys
m=json.load(open(sys.argv[1],encoding='utf-8'))
rows=[x for x in m['components'] if x['id']==sys.argv[2]]
if len(rows)!=1: raise SystemExit(f"component {sys.argv[2]} count={len(rows)}")
v=rows[0][sys.argv[3]]
if isinstance(v,list): print(v[0])
else: print(v)
PY
}

component_download() {
  local id="$1" url file_name sha size
  url="$(component_field "$id" urls)"
  file_name="$("$PYTHON" - "$url" <<'PY'
from urllib.parse import urlparse,unquote
import os,sys
print(unquote(os.path.basename(urlparse(sys.argv[1]).path)))
PY
)"
  [ -n "$file_name" ] || die "组件 [$id] URL 无文件名"
  sha="$(component_field "$id" sha256)"
  size="$(component_field "$id" sizeBytes)"
  verified_download "$id" "$file_name" "$url" "$sha" "$size"
}

CLIENT_PACKAGE="$(component_download client-msi)"
PDA_PACKAGE="$(component_download pda-apk)"
WEBUI_ZIP="$(component_download webui)"
ZEBRA_PACKAGE="$(component_download plugin-zebra)"
TSPL_PACKAGE="$(component_download plugin-tspl)"

COMPOSE_URL="$(read_json artifacts.0.url)"
COMPOSE_SHA="$(read_json artifacts.0.sha256)"
COMPOSE_FILE="$(verified_download compose compose.yml "$COMPOSE_URL" "$COMPOSE_SHA")"

IMAGE_REPOSITORY="${IMAGE_REFERENCE%:*}"
IMAGE_BY_DIGEST="$IMAGE_REPOSITORY@$IMAGE_DIGEST"
info "按 digest 拉取镜像：$IMAGE_BY_DIGEST"
docker pull "$IMAGE_BY_DIGEST"
if ! docker image inspect "$IMAGE_BY_DIGEST" --format '{{range .RepoDigests}}{{println .}}{{end}}' | grep -Fx "$IMAGE_BY_DIGEST" >/dev/null; then
  die "RepoDigest 与来源清单不一致：$IMAGE_BY_DIGEST"
fi
docker tag "$IMAGE_BY_DIGEST" "$IMAGE_REFERENCE"
IMAGE_TAR="$STAGING_ROOT/labelframe-server-$VERSION.image.tar.gz"
docker save "$IMAGE_REFERENCE" | gzip > "$IMAGE_TAR"
[ -s "$IMAGE_TAR" ] || die "docker save 未生成镜像归档"

BUILD_INFO="$STAGING_ROOT/offline-build-info.json"
"$PYTHON" - "$BUILD_INFO" "$SOURCE_MANIFEST" "$INSTALL_MANIFEST" "$IMAGE_BY_DIGEST" \
  "$CLIENT_PACKAGE" "$PDA_PACKAGE" "$WEBUI_ZIP" "$ZEBRA_PACKAGE" "$TSPL_PACKAGE" <<'PY'
import datetime,hashlib,json,os,sys
def sha(path):
    h=hashlib.sha256()
    with open(path,'rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''): h.update(block)
    return h.hexdigest()
out,source_path,manifest_path,image,*files=sys.argv[1:]
source=json.load(open(source_path,encoding='utf-8'))
ids=['client-msi','pda-apk','webui','plugin-zebra','plugin-tspl']
value={
 'schemaVersion':1,
 'builderContractVersion':str(source['builder']['contractVersion']),
 'labelframeVersion':source['labelframeVersion'],
 'packageKind':'docker-full',
 'generatedAtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'serverImageRepoDigest':image,
 'files':[{'id':i,'fileName':os.path.basename(p),'source':'cache-or-download','sha256':sha(p),'sizeBytes':os.path.getsize(p)} for i,p in zip(ids,files)]
}
with open(out,'w',encoding='utf-8',newline='\n') as f: json.dump(value,f,ensure_ascii=False,indent=2); f.write('\n')
PY

BUNDLE_OUTPUT="$STAGING_ROOT/output"
bash "$BUILDER_ROOT/helpers/make-offline-bundle.sh" \
  -v "$VERSION" --image-tar "$IMAGE_TAR" \
  --client-package "$CLIENT_PACKAGE" --pda-package "$PDA_PACKAGE" \
  --plugin-package "$ZEBRA_PACKAGE" --plugin-package "$TSPL_PACKAGE" \
  --webui-zip "$WEBUI_ZIP" --compose-file "$COMPOSE_FILE" \
  --template-root "$BUILDER_ROOT/templates/docker" --build-info "$BUILD_INFO" -o "$BUNDLE_OUTPUT"

ARCHIVE="$BUNDLE_OUTPUT/labelframe-offline-$VERSION-linux-x64.tar.gz"
SIDECAR="$ARCHIVE.sha256"
[ -s "$ARCHIVE" ] || die "底层组包未生成目标归档"
[ -s "$SIDECAR" ] || die "底层组包未生成归档 SHA256 旁车"
TARGET="$OUTPUT_DIR/$(basename "$ARCHIVE")"
PARTIAL="$TARGET.partial.$$"
SIDECAR_PARTIAL="$TARGET.sha256.partial.$$"
cp "$ARCHIVE" "$PARTIAL"
cp "$SIDECAR" "$SIDECAR_PARTIAL"
mv -f "$PARTIAL" "$TARGET"
mv -f "$SIDECAR_PARTIAL" "$TARGET.sha256"
info "已生成：$TARGET"
info "外部校验：$TARGET.sha256"
