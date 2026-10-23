#!/usr/bin/env bash
# 组装 LabelFrame Docker 离线部署包；契约见 docs/DESIGN.md §6.13。
#
# Offline Builder 在准备机按 release-time digest 拉取并校验镜像、下载原子资源后调用本脚本，生成
# labelframe-offline-<版本>-linux-x64.tar.gz；目标机继续运行包内既有 install.sh 完成零外网部署。
# 包结构与口径：
#   - images/  docker save 导出的镜像 tar.gz（ghcr 全名 tag 原样保留：load 后本地命中 tag，compose 默认
#     pull=missing 不再联网拉取，compose.yml 因此可与在线版逐字节同源，防漂移）；
#   - compose.yml 与 packaging/ubuntu/docker-compose.yml 逐字节同源复制（对齐 release job compose 分发
#     附件口径）；.env 按当版版本号生成（LABELFRAME_VERSION 钉定）；
#   - packages/  客户端 MSI / PDA APK / 官方 .lfplugin（拷入挂载目录即经下载中心分发——离线环境闭环）；
#   - plugins/web-ui/  管理界面 zip 预解压（compose 默认挂载 ./plugins/web-ui 即服务端默认 WebUiPath，
#     管理界面开箱可用，免用户手工解压 zip）；
#   - SHA256SUMS  除账本自身外全部部署文件哈希；归档旁车 `.tar.gz.sha256` 供执行 install.sh 前验证整包；
#   - README.md / install.sh 来自 packaging/offline/（README 版本占位 __LABELFRAME_VERSION__ 随组包替换）。
#   - 整包不进 install-manifest（与 compose 分发附件同口径：自足分发单元而非可安装产物，决策 #116 / #134）。
#
# 用法（bash，仓库根或任意目录均可；--help 看全参数）：
#   bash scripts/make-offline-bundle.sh -v 0.30.0 \
#     --image-tar <labelframe-server 镜像 tar.gz> \
#     --client-package <LabelFrame-Client-*.msi> \
#     --pda-package <LabelFrame-AndroidHost-*.apk> \
#     --plugin-package <labelframe-transport-zebra-*.lfplugin> \
#     --webui-zip <labelframe-server-webui-*.zip> \
#     -o bundle
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE_REPO_DEFAULT="ghcr.io/marci-labs/labelframe-server"

VERSION=""
IMAGE_TAR=""
CLIENT_PKG=""
PDA_PKG=""
PLUGIN_PKGS=()
WEBUI_ZIP=""
OUTPUT_DIR=""
COMPOSE_FILE=""
TEMPLATE_ROOT=""
BUILD_INFO=""

die() { echo "[离线包] 错误：$*" >&2; exit 1; }
info() { echo "[离线包] $*"; }

usage() {
  cat <<'USAGE'
LabelFrame Linux 离线部署包组装脚本（docs/DEPLOY.md §4.2 / DESIGN 决策 #160）
用法：
  bash scripts/make-offline-bundle.sh -v <版本> --image-tar <镜像tar.gz> \
    --client-package <Client.msi> --pda-package <APK> \
    --plugin-package <lfplugin> --webui-zip <webui.zip> [-o 输出目录] [--compose-file <compose.yml>] \
    [--template-root <模板目录>] [--build-info <offline-build-info.json>]
参数：
  -v, --version         当版版本号（包名与 .env 钉定值）
  --image-tar           docker save | gzip 导出的 server 镜像 tar.gz（已构建并验收的镜像）
  --client-package      客户端 MSI（packages/client/）
  --pda-package         PDA 宿主 APK（packages/pda/）
  --plugin-package      官方传输插件 .lfplugin（packages/plugin/；可多次传入逐个收录）
  --webui-zip           服务端管理界面 zip（预解压到 plugins/web-ui/）
  -o, --output-dir      输出目录（默认 <仓库>/artifacts/offline-bundle）
  --compose-file        compose 源文件（默认 packaging/ubuntu/docker-compose.yml，逐字节同源复制）
  --template-root       README.md 与 install.sh 所在目录（默认 packaging/offline）
  --build-info          Builder 审计记录；提供时收录到包根
USAGE
  exit 0
}

need_value() { [ "$#" -ge 2 ] && [ -n "$2" ] || die "$1 需要一个参数值"; }

while [ "$#" -gt 0 ]; do
  case "$1" in
    -v|--version)        need_value "$@"; VERSION="$2"; shift 2 ;;
    --image-tar)         need_value "$@"; IMAGE_TAR="$2"; shift 2 ;;
    --client-package)    need_value "$@"; CLIENT_PKG="$2"; shift 2 ;;
    --pda-package)       need_value "$@"; PDA_PKG="$2"; shift 2 ;;
    --plugin-package)    need_value "$@"; PLUGIN_PKGS+=("$2"); shift 2 ;;
    --webui-zip)         need_value "$@"; WEBUI_ZIP="$2"; shift 2 ;;
    -o|--output-dir)     need_value "$@"; OUTPUT_DIR="$2"; shift 2 ;;
    --compose-file)      need_value "$@"; COMPOSE_FILE="$2"; shift 2 ;;
    --template-root)     need_value "$@"; TEMPLATE_ROOT="$2"; shift 2 ;;
    --build-info)        need_value "$@"; BUILD_INFO="$2"; shift 2 ;;
    -h|--help)           usage ;;
    *) die "未知参数：$1（--help 查看用法）" ;;
  esac
done

[ -n "$VERSION" ] || die "缺少 -v <版本号>（--help 查看用法）"
[ -n "$IMAGE_TAR" ] || die "缺少 --image-tar（先在已构建镜像的机器执行 docker save | gzip）"
[ -n "$CLIENT_PKG" ] || die "缺少 --client-package"
[ -n "$PDA_PKG" ] || die "缺少 --pda-package"
[ "${#PLUGIN_PKGS[@]}" -ge 1 ] || die "缺少 --plugin-package（可多次传入，逐个收录）"
[ -n "$WEBUI_ZIP" ] || die "缺少 --webui-zip"

for f in "$IMAGE_TAR" "$CLIENT_PKG" "$PDA_PKG" "${PLUGIN_PKGS[@]}" "$WEBUI_ZIP"; do
  [ -s "$f" ] || die "输入文件不存在或为空：$f"
done

command -v sha256sum >/dev/null 2>&1 || die "未找到 sha256sum（Ubuntu / Debian：apt-get install -y coreutils）。"
command -v unzip >/dev/null 2>&1 || die "未找到 unzip（预解压管理界面需要；Ubuntu / Debian：apt-get install -y unzip）。"
command -v tar >/dev/null 2>&1 || die "未找到 tar。"

COMPOSE_FILE="${COMPOSE_FILE:-$REPO_ROOT/packaging/ubuntu/docker-compose.yml}"
[ -s "$COMPOSE_FILE" ] || die "compose 源文件不存在：$COMPOSE_FILE"
grep -q 'labelframe-server' "$COMPOSE_FILE" || die "compose 源文件缺镜像引用（labelframe-server）——文件异常"

OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/artifacts/offline-bundle}"
BUNDLE_NAME="labelframe-offline-$VERSION-linux-x64"
BUNDLE_DIR="$OUTPUT_DIR/$BUNDLE_NAME"
TEMPLATE_ROOT="${TEMPLATE_ROOT:-$REPO_ROOT/packaging/offline}"
README_TEMPLATE="$TEMPLATE_ROOT/README.md"
INSTALL_TEMPLATE="$TEMPLATE_ROOT/install.sh"
[ -s "$README_TEMPLATE" ] || die "缺 README.md 模板：$README_TEMPLATE"
[ -s "$INSTALL_TEMPLATE" ] || die "缺 install.sh 模板：$INSTALL_TEMPLATE"
if [ -n "$BUILD_INFO" ]; then
  [ -s "$BUILD_INFO" ] || die "offline-build-info 不存在或为空：$BUILD_INFO"
fi

info "组装离线包 $BUNDLE_NAME -> $OUTPUT_DIR"
rm -rf "$BUNDLE_DIR"
mkdir -p "$BUNDLE_DIR/images" "$BUNDLE_DIR/packages/client" "$BUNDLE_DIR/packages/pda" \
         "$BUNDLE_DIR/packages/plugin" "$BUNDLE_DIR/plugins/web-ui"

# 1) 镜像 tar（重命名为版本钉定名；tag 为 ghcr 全名，load 后 compose 本地命中、不再拉取）
cp "$IMAGE_TAR" "$BUNDLE_DIR/images/labelframe-server-$VERSION.image.tar.gz"
info "[1/7] 镜像 tar 就位：images/labelframe-server-$VERSION.image.tar.gz"

# 2) compose.yml 同源复制（逐字节一致——cmp 复核，防漂移）
cp "$COMPOSE_FILE" "$BUNDLE_DIR/compose.yml"
cmp -s "$COMPOSE_FILE" "$BUNDLE_DIR/compose.yml" || die "compose.yml 同源复制不一致（cmp）——中止"
info "[2/7] compose.yml 同源复制完成（与 $(realpath "$COMPOSE_FILE" 2>/dev/null || echo "$COMPOSE_FILE") 逐字节一致）"

# 3) .env（版本钉定；LABELFRAME_IMAGE 注释模板与 release job compose 分发附件同语义）
cat > "$BUNDLE_DIR/.env" <<EOF
# LabelFrame 服务端离线部署 compose 环境变量（组包自动生成，LABELFRAME_VERSION 已钉定为本包版本）
# 用法：先 docker load 镜像（见 README 或直接运行 install.sh），再 docker compose up -d
LABELFRAME_VERSION=$VERSION
#LABELFRAME_IMAGE=$IMAGE_REPO_DEFAULT
EOF
grep -q "^LABELFRAME_VERSION=$VERSION$" "$BUNDLE_DIR/.env" || die ".env 生成校验失败"
info "[3/7] .env 生成（LABELFRAME_VERSION=$VERSION 钉定）"

# 4) README（版本占位替换）+ install.sh
sed "s/__LABELFRAME_VERSION__/$VERSION/g" "$README_TEMPLATE" > "$BUNDLE_DIR/README.md"
if grep -q '__LABELFRAME_VERSION__' "$BUNDLE_DIR/README.md"; then die "README 版本占位替换不完全"; fi
cp "$INSTALL_TEMPLATE" "$BUNDLE_DIR/install.sh"
chmod +x "$BUNDLE_DIR/install.sh"
if [ -n "$BUILD_INFO" ]; then
  cp "$BUILD_INFO" "$BUNDLE_DIR/offline-build-info.json"
fi
info "[4/7] README / install.sh 就位"

# 5) packages 三件（拷入挂载目录即经下载中心分发；官方插件包可多个，逐个收录）
cp "$CLIENT_PKG" "$BUNDLE_DIR/packages/client/"
cp "$PDA_PKG" "$BUNDLE_DIR/packages/pda/"
for pkg in "${PLUGIN_PKGS[@]}"; do
  cp "$pkg" "$BUNDLE_DIR/packages/plugin/"
done
info "[5/7] packages 就位：client（$(basename "$CLIENT_PKG")）/ pda（$(basename "$PDA_PKG")）/ plugin（共 ${#PLUGIN_PKGS[@]} 个：${PLUGIN_PKGS[*]##*/}）"

# 6) 管理界面预解压（compose 默认挂载 ./plugins/web-ui = 服务端默认 WebUiPath，开箱可用）
#    unzip 退出码 0=正常 / 1=警告（含 PowerShell 5.1 Compress-Archive 的反斜杠条目名——Info-ZIP 会
#    转换为目录正确解出），其余码一律失败；解压后再断言无反斜杠残留文件名（解压被搞坏即 fail-closed）。
WEBUI_ABS="$(cd "$(dirname "$WEBUI_ZIP")" && pwd)/$(basename "$WEBUI_ZIP")"
set +e
(cd "$BUNDLE_DIR/plugins/web-ui" && unzip -q "$WEBUI_ABS")
UNZIP_RC=$?
set -e
if [ "$UNZIP_RC" -ne 0 ] && [ "$UNZIP_RC" -ne 1 ]; then
  die "管理界面 zip 解压失败（unzip 退出码 $UNZIP_RC）——插件包不完整或损坏。"
fi
[ -s "$BUNDLE_DIR/plugins/web-ui/index.html" ] || die "管理界面 zip 解压后缺少 index.html——插件包不完整"
if find "$BUNDLE_DIR/plugins/web-ui" -name '*\\*' | grep -q .; then
  die "管理界面解压出现含反斜杠的文件名（zip 条目名异常，解压结果损坏）——fail-closed 拒绝出包"
fi
info "[6/7] 管理界面预解压完成（plugins/web-ui/）"

# 7) SHA256SUMS（除账本自身外全部部署文件；相对包根路径，install.sh 在包根强制校验）
(
  cd "$BUNDLE_DIR"
  : > SHA256SUMS
  while IFS= read -r -d '' relative; do
    hash="$(sha256sum "$relative" | cut -d' ' -f1)"
    printf '%s  %s\n' "$hash" "$relative" >> SHA256SUMS
  done < <(find . -type f ! -name SHA256SUMS -printf '%P\0' | LC_ALL=C sort -z)
)
[ -s "$BUNDLE_DIR/SHA256SUMS" ] || die "SHA256SUMS 生成失败"

# fail-closed 结构断言（AC-01）：任一关键件缺失 / 空文件即失败，不出残缺包
assert_nonempty() { [ -s "$1" ] || die "结构断言失败：缺 $1"; }
assert_nonempty "$BUNDLE_DIR/README.md"
assert_nonempty "$BUNDLE_DIR/install.sh"
assert_nonempty "$BUNDLE_DIR/compose.yml"
assert_nonempty "$BUNDLE_DIR/.env"
assert_nonempty "$BUNDLE_DIR/SHA256SUMS"
if [ -n "$BUILD_INFO" ]; then assert_nonempty "$BUNDLE_DIR/offline-build-info.json"; fi
assert_nonempty "$BUNDLE_DIR/images/labelframe-server-$VERSION.image.tar.gz"
assert_nonempty "$BUNDLE_DIR/plugins/web-ui/index.html"
ls "$BUNDLE_DIR"/packages/client/*.msi >/dev/null 2>&1    || die "结构断言失败：packages/client 缺 MSI"
ls "$BUNDLE_DIR"/packages/pda/*.apk >/dev/null 2>&1       || die "结构断言失败：packages/pda 缺 APK"
ls "$BUNDLE_DIR"/packages/plugin/*.lfplugin >/dev/null 2>&1 || die "结构断言失败：packages/plugin 缺 .lfplugin"
info "[7/7] SHA256SUMS 生成（$(wc -l < "$BUNDLE_DIR/SHA256SUMS") 条）+ 结构断言通过"

# 归档（tar.gz，解压得同名目录）
ARCHIVE_PATH="$OUTPUT_DIR/$BUNDLE_NAME.tar.gz"
tar --force-local -C "$OUTPUT_DIR" -czf "$ARCHIVE_PATH" "$BUNDLE_NAME"
[ -s "$ARCHIVE_PATH" ] || die "归档生成失败"
(cd "$OUTPUT_DIR" && sha256sum "$BUNDLE_NAME.tar.gz" > "$BUNDLE_NAME.tar.gz.sha256")
[ -s "$ARCHIVE_PATH.sha256" ] || die "归档 SHA256 旁车生成失败"
info "离线包完成：$ARCHIVE_PATH（$(du -h "$ARCHIVE_PATH" | cut -f1)）"
info "部署：拷到目标机解压后按 README 三步（或直接 bash install.sh）——全程零外网。"
