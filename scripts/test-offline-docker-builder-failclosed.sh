#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEMP="$(mktemp -d "${TMPDIR:-/tmp}/labelframe-docker-failclosed.XXXXXX")"
trap 'rm -rf "$TEMP"' EXIT
VERSION="9.8.7"
BUILDER="$TEMP/builder"
CACHE="$BUILDER/cache"
OUTPUT="$BUILDER/dist"
FAKE_BIN="$TEMP/bin"
mkdir -p "$BUILDER/helpers" "$BUILDER/templates/docker" "$CACHE" "$OUTPUT" "$FAKE_BIN"
cp "$ROOT/packaging/offline-builder/build-docker-offline.sh" "$BUILDER/"
cp "$ROOT/scripts/make-offline-bundle.sh" "$BUILDER/helpers/"
cp "$ROOT/packaging/offline/README.md" "$BUILDER/templates/docker/"
cp "$ROOT/packaging/offline/install.sh" "$BUILDER/templates/docker/"

make_file() { printf 'fixture:%s' "$1" > "$CACHE/$2"; }
make_file client "LabelFrame-Client-$VERSION.msi"
make_file pda "LabelFrame-AndroidHost-$VERSION.apk"
make_file webui "labelframe-server-webui-$VERSION.zip"
make_file zebra "labelframe-transport-zebra-$VERSION.lfplugin"
make_file tspl "labelframe-transport-tspl-$VERSION.lfplugin"
printf 'services: { labelframe-server: { image: fixture } }\n' > "$CACHE/compose.yml"

python - "$CACHE" "$VERSION" <<'PY'
import hashlib,json,os,sys
cache,version=sys.argv[1:]
def sha(name): return hashlib.sha256(open(os.path.join(cache,name),'rb').read()).hexdigest()
def size(name): return os.path.getsize(os.path.join(cache,name))
files={
 'client-msi':f'LabelFrame-Client-{version}.msi',
 'pda-apk':f'LabelFrame-AndroidHost-{version}.apk',
 'webui':f'labelframe-server-webui-{version}.zip',
 'plugin-zebra':f'labelframe-transport-zebra-{version}.lfplugin',
 'plugin-tspl':f'labelframe-transport-tspl-{version}.lfplugin'}
manifest={'schemaVersion':1,'labelframeVersion':version,'generatedAt':'2026-01-01T00:00:00Z','components':[
 {'id':i,'type':'asset','version':version,'dependsOn':[],'urls':['https://127.0.0.1:1/'+n],'sha256':sha(n),'sizeBytes':size(n),'silentArgs':'','topologies':['offline'],'notes':'fixture'} for i,n in files.items()]}
with open(os.path.join(cache,'install-manifest.json'),'w',encoding='utf-8') as f: json.dump(manifest,f); f.write('\n')
source={'schemaVersion':1,'labelframeVersion':version,
 'installManifest':{'url':'https://127.0.0.1:1/install-manifest.json','sha256':sha('install-manifest.json')},
 'serverImage':{'reference':f'example.invalid/labelframe-server:{version}','digest':'sha256:'+'a'*64},
 'artifacts':[{'id':'compose','fileName':'compose.yml','url':'https://127.0.0.1:1/compose.yml','sha256':sha('compose.yml')}],
 'webView2Standalone':{},'builder':{'contractVersion':'1','generator':'fixture'}}
with open(os.path.join(os.path.dirname(cache),'offline-source-manifest.json'),'w',encoding='utf-8') as f: json.dump(source,f); f.write('\n')
PY

cat > "$FAKE_BIN/docker" <<'SH'
#!/usr/bin/env bash
set -e
case "$1" in
  pull) exit 0 ;;
  image)
    if [ "$2" = inspect ]; then
      printf 'example.invalid/labelframe-server@sha256:%064d\n' 0
      exit 0
    fi ;;
  tag) exit 0 ;;
  save) printf 'SHOULD_NOT_SAVE\n' > "${FAKE_DOCKER_SAVE_MARKER:?}"; exit 0 ;;
esac
exit 1
SH
chmod +x "$FAKE_BIN/docker"

export PATH="$FAKE_BIN:$PATH"
export FAKE_DOCKER_SAVE_MARKER="$TEMP/docker-save-called"
set +e
bash "$BUILDER/build-docker-offline.sh" --output-dir "$OUTPUT" --cache-dir "$CACHE" > "$TEMP/output.log" 2>&1
RC=$?
set -e
[ "$RC" -ne 0 ] || { cat "$TEMP/output.log"; echo '期望 digest 不符时非零退出' >&2; exit 1; }
[ ! -e "$FAKE_DOCKER_SAVE_MARKER" ] || { cat "$TEMP/output.log"; echo 'digest 不符时不得执行 docker save' >&2; exit 1; }
if find "$OUTPUT" -maxdepth 1 -name 'labelframe-offline-*.tar.gz' | grep -q .; then
  cat "$TEMP/output.log"
  echo 'digest 不符时不得产最终归档' >&2
  exit 1
fi
grep -q 'RepoDigest' "$TEMP/output.log" || { cat "$TEMP/output.log"; echo '缺 digest 可行动错误' >&2; exit 1; }
echo '通过：Docker Builder digest fail-closed fixture。'
