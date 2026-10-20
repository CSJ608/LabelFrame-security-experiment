# LabelFrame.AndroidHost

Android / PDA 打印宿主。

> **真机验收已通过（2026-09-08，UROVO DT50 / Android 11）**：注册 / 心跳 / 模板下发 / 作业打印（路由 + 直连）/ 离线恢复 / 断网重连（挂起 → 续打 → 失败项重打）/ 开机自启 / 前台服务保活 / JS 桥 CORS 全部通过。仍不在 `LabelFrame.slnx` 解决方案中（CI 单独构建本工程，见下）；遗留验收项（16KB 运行时验证，待 Android 15+ 16KB 内核设备）见 DESIGN「风险与未决问题」。

## 定位

**后台打印执行服务**：打印入口在业务系统侧（Server 路由或第三方程序经 JS 桥），宿主自身不提供业务打印界面；唯一 UI 是原生配置页（点 App 图标打开），组织为「主页 + 三子页」（每屏一个任务，文案面向不懂技术的仓库用户）：

- **主页**：当前状态卡（语义色摘要——绿「一切正常」/ 红「连不上」/ 灰「服务未运行」+ 服务器 / 打印机明细）与三个设置入口（每行带当前值摘要）。
- **① 连接服务器**子页：地址 + 「测试连接」（用的是输入框地址，不用先保存）。
- **② 连接打印机**子页（三选一连接方式）：**网线**（IP + 端口默认 9100，默认且一级路径）/ **蓝牙**（MAC 地址手输，Android 12+ 选蓝牙保存时请求「附近的设备」权限）/ **USB 数据线**（自动识别第一台 Zebra 打印机，首次连接弹系统授权）+ 「打印测试标签」——本地提交内置测试标签走完整链路（校验 → 渲染 → `^GF` → SDK 发送 → 终态）；输入未保存时提示「先保存再测试」（测试打印走已保存配置）。品牌 / 连接类型是传输插件的路由键。
- **③ 本机信息**子页：设备号取系统唯一码（ANDROID_ID）自动生成只读展示（等宽字体 + 复制）；设备名称可选编辑（默认 `PDA-<码后 4 位>`，注册 Server 随 name 上报）。
- 每个子页自带「**保存并重启服务**」：写配置后自动重启宿主服务生效，无需 force-stop。
- 常驻通知点击打开配置页，一句话文案（服务器状态 + 打印机地址）；浏览器打开 `http://127.0.0.1:53970` 为轻量状态页（本机 / 服务器 / 打印机三卡片 + 测试打印）。

## 职责

- 前台服务（`PrintHostService`）常驻 + 开机自启（`BootReceiver`，BOOT_COMPLETED / MY_PACKAGE_REPLACED）。
- 本地 HTTP 服务（仅 127.0.0.1:53970，TcpListener 极简实现）：健康检查、提交 / 列表 / 查询 / 挂起 / 恢复 / 取消作业、失败项重打、打印机状态与测试、宿主配置、测试打印、状态页；宽松 CORS + OPTIONS 预检（JS 桥）。
- 打印机传输：Zebra 官方 Link-OS SDK `5.0.3685`（`ZebraSdkTransport`）——tcp（`TcpConnection`）/ 蓝牙 SPP（`BluetoothConnection` 按 MAC）/ USB（`UsbDiscoverer` 自动发现锁定第一台）；状态与连接测试走 SDK `GetCurrentStatus()` 官方语义（不保留 `~HS` 双轨）。Core 的 `Tcp9100PrintTransport` 保留为跨平台兜底（Linux 客户端使用）。
- 向 Server 注册设备并经 notify 长轮询（20s）领取定向作业 + 独立 1s 回报循环（与 WinHost `ServerRoutingWorker` 同构）。
- 中文栅格化：Android.Graphics 渲染为 1bpp 位图（^GF），与 WinHost 同契约。
- 可观测性：`HostLog` 轻量静态门面（logcat + 本地滚动文件）+ `CrashGuard` 全局崩溃捕获，见下节。

## 可观测性

现场排障三条通道（轻量实现，零新依赖）：

1. **adb logcat**：tag 前缀 `LabelFrame.` + 区域名——`Host`（前台服务生命周期 / 开机自启）、`Http`（本地 HTTP 请求失败，含 method / path / 异常消息）、`Print`（打印循环发送 / 领取失败）、`Server`（轮询 / 回报失败，含目标地址）、`Ui`（配置页操作失败）、`Crash`（未捕获异常完整堆栈）。

   ```bash
   adb logcat -s LabelFrame.*:V
   ```

2. **本地滚动日志**（现场无法 adb 时取证）：`{FilesDir}/logs/host-<yyyyMMdd>-<NNN>.log`，与 logcat 同一封装同步写入；单文件 512KB 上限滚动到下一序号，目录保留最近 6 个。

   ```bash
   adb shell run-as com.labelframe.androidhost ls files/logs/
   adb shell run-as com.labelframe.androidhost cat files/logs/host-$(date +%Y%m%d)-001.log
   ```

3. **崩溃摘要**：未捕获异常（Java 层默认处理器 + .NET `AppDomain.UnhandledException` 双通道，进程创建首行即注册于 `HostApplication`）→ logcat Error 完整堆栈 + `{FilesDir}/crash/crash-<时间戳>.txt`（时间 / 来源 / 版本 / 系统 / 设备 / 堆栈；保留最近 3 份）；下次服务启动检测到上次崩溃摘要会记录 Warn 提示。摘要与本地日志**不回传服务端**（回传管道见 DESIGN「风险与未决问题」）。

语义约束：`EmbeddedHttpServer` 每请求 catch 仍吞掉异常（单请求失败不影响服务），只加日志；循环失败的重试节奏不变；进度上报失败保持静默（「不告警刷屏」口径，Server 轮询主循环与终态回报失败记周期 Warn）。

## 第三方 PDA 程序集成

本宿主是 PDA 上唯一打印执行宿主；第三方程序不嵌入 LabelFrame 代码，统一走 HTTP 公共契约：

- **路由模式（主）**：任意程序 → Server `POST /api/jobs`（`targetDeviceId` 指向本 PDA 的 device_id）→ 宿主自动领取打印 → Server 查询终态。
- **直连模式（就近单张）**：与 PDA 同机的 App / WebView / 浏览器页面 → `http://127.0.0.1:53970`（即「JS 桥」，fetch 直接调用，跨源已放开）。

不做 Android SDK / Intent / 广播等原生契约形态（有真实需求先更新 DESIGN 再实施）。

## 配置（SharedPreferences：labelframe）

| 键 | 默认 | 说明 |
|---|---|---|
| `server_url` | 空 | Server 地址，为空不启用路由 |
| `printer_brand` | zebra | 打印机品牌（未来插件路由键） |
| `connection_type` | tcp | 连接类型（tcp / bluetooth / usb；未识别值按 tcp） |
| `tcp_host` | 192.168.1.50 | 打印机 IP（connection_type=tcp） |
| `tcp_port` | 9100 | 打印机端口（connection_type=tcp） |
| `bluetooth_mac` | 空 | 蓝牙打印机 MAC 地址（connection_type=bluetooth） |
| `device_name` | PDA-xxxx | 设备名称（注册 Server 展示；xxxx 为设备码后 4 位） |
| `device_uuid` | — | 设备号兜底（仅 ANDROID_ID 取不到时生成一次） |

设备号自动取 `Settings.Secure.ANDROID_ID` 原值（不加 `pda-` 前缀），不接受配置（多台设备天然不撞号；恢复出厂后变化，视为新设备）。读写入口：配置页 UI 或 `GET/POST /api/host/config`（POST 持久化，重启宿主生效——配置页「保存并重启服务」自动完成重启）。

## 构建（CI 自动化 + 随发版出包）

- **日常 CI**：任一 PR / push master 触发 `ci.yml` 的「Android 构建（PDA 宿主）」job（第三项必需检查），Release 配置 + `-p:EmbedAssembliesIntoApk=true`，产物可下载。
- **发版**：推 `v*` tag 后 `release.yml` 构建 `LabelFrame-AndroidHost-<版本>.apk` 上传 GitHub Release 附件（版本号 `ApplicationDisplayVersion` / versionCode 由 tag 注入；配置页「本机信息」子页与 `/healthz`、`/api/host/config` 可见）。
- **本地构建**（需要 .NET 10 SDK + Android workload、Android SDK（platforms;android-36、build-tools 36.0.0）、JDK 17）：

```powershell
.\scripts\build-androidhost.ps1                        # Debug（默认，联调）
.\scripts\build-androidhost.ps1 -Configuration Release # 交付真机（单 arm64，约 22MB）
.\scripts\build-androidhost.ps1 -Configuration Release -Version 0.26.0   # 带版本号
```

产出：`src\LabelFrame.AndroidHost\bin\<Configuration>\net10.0-android\com.labelframe.androidhost-Signed.apk`。`.NET Android 36.1.x` 起 `AndroidFastDeployment` 属性已失效，关闭 Fast Deployment 必须用 `EmbedAssembliesIntoApk`。

## 签名与升级

- **正式签名**：CI 发版要求四个 Secrets（`ANDROID_KEYSTORE_BASE64` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASSWORD`）**全部齐备**，用专用自签 keystore 签名；**任一缺失即 Release 构建失败，不回退 debug 签名**（debug 回退路径已移除——保证发版签名输出稳定可预期，杜绝两次构建签名不一致导致无法覆盖升级 / 设备号漂移）。keystore 一次性生成：

  ```powershell
  .\scripts\create-android-keystore.ps1 -Password '<强密码>' -SetGithubSecrets
  ```

- **证书管理**：keystore 与密码只存两处——GitHub 仓库 Secrets + 生成方离线备份（密码管理器 / 加密盘），**不得提交进仓库**；生成后立即备份别名与两个密码（store / key）。**keystore 或密码丢失 = 无法再发同签名升级包**（只能换签名，见下）；GitHub Secrets 可随时重写，keystore 文件还在即可恢复。
- **装机与升级路径**：
  - **装机**：推荐「服务端下载中心扫码下载」——把 Release 的 APK 放入服务端 `pda-packages` 目录（或经管理界面「下载中心」上传），PDA 与服务器同网扫条目旁二维码即可下载安装（详见 [docs/DEPLOY.md](../../docs/DEPLOY.md) §7）；也可 `adb install` GitHub Release 下载的 APK。
  - **同签名版本之间**（正式 → 正式）：直接 `adb install -r` 或 PDA 端覆盖安装，配置与设备号保留。
  - **换签名**：**需先卸载旧版再安装**——卸载会清空配置（服务器地址 / 打印机 IP / 设备名称需重填）；且 Android 8+ 的设备号（ANDROID_ID）绑定签名密钥，**换签名后设备号会变**，Server 设备目录会出现新条目（旧条目停留显示离线，可忽略）。
- 品牌化（品牌 v02）：启动器 / 自适应图标 = 品牌 v02 标识（深青＋珊瑚，与 MSI / 桌面同体系；各密度位图为 `Resources/` 静态资产、母版库在仓库 `assets/brand/`，旧生成脚本已退役；API 33+ 含主题单色层）；常驻通知小图标为白色单色矢量——**通知栏图标只保留 alpha 通道由系统着色，不能带品牌彩色底**（Android 平台约束，改图标时必须保持单色）。

## 原生库注意

- `SQLitePCLRaw.lib.e_sqlite3.android` 定版 **2.1.11**：2.1.12/2.1.13 误装 glibc 构建的 so（装载即 LinkageError）；升级前必须核对 android 包 so 为 NDK 构建（DT_NEEDED 应为 liblog/libc/libm 等，而非 libc.so.6）。
- 桌面版 `SQLitePCLRaw.lib.e_sqlite3` 不得回到 Core 引用——经 RID 回退图（android-arm64 → linux-arm64）会把 glibc so 打进 APK 压过 android 包。
- 服务启动已先 `JavaSystem.LoadLibrary("e_sqlite3")`（Android 链接器命名空间要求）。
- **Zebra SDK 毒丸引用清单**（csproj 内注释同步维护）：`Zebra.Printer.SDK 5.0.3685` 的传递依赖链含 MAUI / `System.Private.Windows.Core` 毒化物，须逐包 `ExcludeAssets="all"` 排除后才能 Android AOT 打包（官方支持矩阵偏离声明见 DESIGN）；SDK 升级时复查清单。许可证 §3.3.3：SDK 只随应用目标码分发，本体不单独进 Release 附件。

## 16KB 页

构建级验证通过（全部 arm64 so ELF 段对齐 ≥ 16KB——含 SDK 引入的 `libSkiaSharp.so`（实测 p_align=16384）、`zipalign -c -P 16`、无 XA0141）；运行时验证需 Android 15+ 16KB 内核设备（待设备可得）。

## 说明

- Android 12+ 从后台启动前台服务受限，开机自启需用户在系统设置允许；UROVO DT50（Android 11）实测默认策略下前台服务常驻。
- 宿主重启后本地↔Server 作业映射（内存态）丢失，Server 侧已 Claimed 作业停留 Claimed——与 WinHost / Linux Client 同构的既有语义，见 DESIGN「未决问题」。
