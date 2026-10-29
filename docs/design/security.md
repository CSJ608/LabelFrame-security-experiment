# Server 安全边界（实验设计）

## 1. 职责与边界

对应实验 [#3](https://github.com/CSJ608/LabelFrame-security-experiment/issues/3)，盘点基线 e85667c。本文是先行契约，不是完成声明；实施、测试与欠账记录在 Issue。来源历史中的全匿名和专用更新口令叙述不作为实验权限依据；当前来源升级管理只有同源检查，见 [更新发布](update-publishing.md)。不新增注册、guest 或多角色。

只在 Server 装配安全层，LabelFrame.Api 共享模板/日志/导入/渲染实现与 WinHost 本地接口不增加强制登录。机器与浏览器认证分离；不启动真实 WinHost/PDA，不改变打印机最后一段链路。仅实验回环运行，不能据此宣称可发行部署。

## 2. 内部结构

当前已接线原型：`Security/ServerEndpointAccess`、`AdminSecurity`、`AdminTicketStore`、`AdminLoginLimiter`、`ServerPublicInfo` 经 Program 装配；`AdminSecurityTests` 是无存储 TestServer，`AdminBoundaryEndpointsTests` 是全部路径隔离的真实 Kestrel 回环动态端口。前端登录/退出及旧 fixture 迁移尚未交付，因此原型不可作为完整产品使用。

权限分类使用 ASP.NET 路由匹配后的 RouteEndpoint 原始模板及 HTTP 方法，而不是请求字符串前缀；新端点未登记时落到 admin。方法不符、大小写、尾斜杠与编码路径不能获得不同于实际选中端点的权限。保留框架已有上传大小限制、文件名安全检查、签名校验、快照和安装保护。

首增量使用 ASP.NET Core CookieAuthentication、IAntiforgery、PasswordHasher 与服务端 ITicketStore，不自创加密。Cookie HttpOnly、SameSite=Strict、无 Domain、HTTPS 时 Secure；固定 8 小时不滑动，浏览器不持久保存密码或令牌。服务端会话有容量上限与过期清理；注销删除票据，旧 Cookie 重放失效。单进程内存会话与临时 DataProtection 密钥使重启强制浏览器重新登录；这不是设备重新配对。

admin 密码只通过部署侧提供框架哈希（Server:AdminPasswordHash）引导，原型不提供匿名 setup/reset，也没有默认密码。未配置时仍可下载/运行但管理锁定。离线生成哈希、替换配置并重启为恢复路径，撤销所有浏览器会话。不能把哈希/密码输出到日志、Issue、提交或命令历史。后续易用的初始化界面另做有本机证明的设计，不临时开放远程首访抢注。

## 3. 关键流程

1. `GET /api/auth/session` 返回 authenticated、csrfToken（框架令牌）与配置状态；匿名态也发令牌供登录防 CSRF。响应 no-store。登录前后分别获取令牌，身份改变后旧匿名令牌不复用。
2. `POST /api/auth/login` 接受 password，限制长度；必须通过传输、同源、防伪检查。所有登录尝试有有界全局固定窗口额度（首原型 10 次/分钟），失败同一错误，成功不重置额度，避免分布式绕过；受攻击可造成临时登录拒绝，原型明确不宣称高可用。后续可加受信来源分区但不信任任意 X-Forwarded-For。
3. 管理请求先验证传输与来源，再验证认证；写方法必须携带 `X-LabelFrame-CSRF`。不能因共享端点 DisableAntiforgery 元数据绕过 Server 的显式验证。登录和注销同样检查。错误为 JSON（401 未认证、403 来源/传输/防伪拒绝、429 频率超限），不重定向 HTML 登录页。
4. `POST /api/auth/logout` 撤销当前票据并清 Cookie。浏览器先关闭敏感树、增加请求代数、取消请求/停止轮询并清空 Provider/草稿/日志，再发注销；失败显示未确认撤销，不可声称安全退出。并发中已经授权且已执行的写操作不能靠注销回滚。
5. 页面公开区使用独立组件，不挂载会查询模板、设备、历史的管理 Provider。深链未登录仅显示登录入口；匿名仅下载与静态帮助。登录后挂载新的管理状态树；401/会话失效同退出本地清理，多标签页同步退出。下载中心公共列表/下载与上传删除发布状态组件拆分。

### 传输与代理

默认远程 admin 需要 HTTPS；HTTP 例外仅 TCP 直连回环，不能靠 Host=localhost 或转发头构造回环身份。原型不根据 X-Forwarded-Proto 放行：现有代理配置仅恢复设备来源 IP，不等于安全传输证明。进入安全层前保存真实 TCP 对端及是否存在转发头；带转发头的 HTTP 不享受回环例外，即使代理在本机。反代 TLS 终止支持待独立受信代理配置、单跳边界和伪造负例完成后启用，不盲信 `*`。便利内网 HTTP 为可选产品方向，本增量不实现静默降级；证书部署重大选择留待用户决定。

管理会话与令牌禁止跨源读取，带 Origin 时必须严格匹配当前 scheme/host/port 且为纯 origin（不接受 userinfo/path/query/fragment），Sec-Fetch-Site 的 same-site/cross-site 拒绝。无 Origin 非浏览器仍须 Cookie + 写防伪令牌，不等价免认证。Server 运行接口的跨源业务兼容不能把管理路径一并开放；WinHost CORS 不改。

## 4. 实际端点权限矩阵

下表穷举 Program.cs、Server/Api 五个模块及共享 Endpoints 四个模块在本基线的映射；花括号使用源码参数名，更新平台 p = client-updates / android-updates。

| 分类 | 方法与端点 | 说明 |
|---|---|---|
| 公开 | GET /healthz；GET /api/server/info | info 只公开版本、uiEnabled 与经去 userinfo/query/fragment 校验的 publicBaseUrl；删除 listenUrl/trustedProxies |
| 公开下载 | GET /api/client-packages；GET /api/client-packages/{fileName}；GET /api/pda-packages；GET /api/pda-packages/{fileName} | 列表只文件分发元数据，不拼入发布状态/设备列表 |
| 登录生命周期 | GET /api/auth/session；POST /api/auth/login；POST /api/auth/logout | session/login 无登录要求但有传输/来源限制；logout 需登录与防伪 |
| 管理 | GET /api/server/ipv4-candidates；GET /api/devices；GET /api/jobs；GET /api/logs | 网卡、在线设备、历史及诊断不得公开 |
| 管理 | GET/POST /api/templates；GET/DELETE /api/templates/{name}；GET /api/templates/{name}/export；POST /api/templates/import；POST /api/templates/{name}/preview | 模板全部受保护；匿名运行作业不能写回模板库 |
| 管理 | POST /api/print/render-image；POST /api/print/render-images；POST /api/import/excel-template；POST /api/import/excel | 设计/调试与数据导入 |
| 管理 | POST /api/client-packages；DELETE /api/client-packages/{fileName}；POST /api/pda-packages；DELETE /api/pda-packages/{fileName}；POST /api/plugin-packages；DELETE /api/plugin-packages/{fileName} | 所有包写入始终需 admin |
| 管理 | GET /api/{p}；GET /api/{p}/drafts；POST /api/{p}/preview；POST /api/{p}/publish；PUT /api/{p}/{id}/pause；GET /api/{p}/status | 两平台全部发布管理；旧 PDA Bearer 人工恢复仍拒绝 |
| 设备运行 | POST /api/devices；GET /api/devices/{deviceId}/jobs/notify；GET /api/devices/{deviceId}/jobs/pending；POST /api/devices/{deviceId}/jobs/{jobId}/result；POST /api/devices/{deviceId}/jobs/{jobId}/progress；POST /api/logs | 注册/心跳、领取和回报；GET 领取也写状态，不按“GET 安全”推断权限 |
| 设备运行 | GET /api/{p}/candidate；GET /api/{p}/{id}/package；POST /api/{p}/status；GET /api/plugin-packages；GET /api/plugin-packages/{fileName} | 更新选择/下载/设备状态与插件安装读取；插件页面仍需 admin，不向匿名 UI 展示插件管理 |
| 集成运行 | POST /api/jobs；GET /api/jobs/{jobId}；GET /api/devices/by-ip/{ip} | 保留信任局域网业务接入；可读作业/定位设备不是完整业务数据保护 |
| 默认管理 | 所有新增或未分类 API | 显式登记后才能放宽 |

静态 UI/帮助资源无秘密，可匿名托管；SPA fallback 不是数据授权。所有管理写入由后端保护，不能只隐藏菜单。信任模式运行接口会接受自带模板的作业并使设备出纸，但不保存模板/上传插件/发布更新；这是已授权运行能力，不是管理等价写入。

### 机器凭据后续增量（本轮原型不伪造已完成）

当前保留信任模式运行接口，明确允许设备冒充/作业数据暴露/伪日志与伪更新状态。启用验证后设备凭据只访问自身领取、回报、日志与更新；集成密钥访问授权设备的提交/查询，不能上传模板/包或获得 admin。每台独立随机长期凭据，服务端存哈希及撤销记录、客户端安全持久保存，正常升级/断网/重启不重配；ID/IP 均非凭据。配对码期限、六位码与批量 30 分钟尚未定值；须限制尝试、单台原子消费，批量仍逐台发凭据。

开关关闭不删凭据，再启用有效；启用前以设备/集成最近活动列出影响，明确确认后生效，不伪称旧匿名设备已经配对。开关与凭据落独立持久存储，备份/恢复不能无意恢复已撤销秘密；无确认不能热切断现有打印。当前不暴露一个没有这些保障的 UI 开关。

callbackUrl 当前只限 http/https、禁重定向、10 秒超时与有限重试，信任模式仍可请求内部地址。未来按集成身份绑定允许目标，不无差别封杀内网；匿名回调风险在最小原型中保留并单列，不宣称已消除 SSRF。

## 5. 设计取舍与回退

EXP-S01/02 见 [DESIGN](../DESIGN.md)。框架依据：[Cookie 与撤销](https://learn.microsoft.com/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0)、[ITicketStore](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.cookies.iticketstore?view=aspnetcore-10.0)、[防伪令牌](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)。

先设计与分类/会话组件测试，再装配 Program 与完整隔离 HTTP 测试，再前端路由/退出；这些是同一 Issue 的阶段，未接线的模块不算已保护。回退到来源程序会恢复匿名管理，必须同时限制网络，不能无提示作为安全回退。首原型不迁移业务库，重启撤销浏览器会话；设备持久化不是本步交付。

## 6. 不变量与验证

- 新 API 默认 admin；回归断言所有实际路由的分类，管理写端点匿名必须拒绝。
- 测试仅模拟设备/造数据，不运行继承发行/E2E，不启动 WinHost/PDA。启动完整 Program 前覆盖三库、文本日志、三种包、两种更新、UI 全部路径；不得只改三库后落到 ProgramData。
- 正负例覆盖匿名管理、公开最小信息、登录失败限流、Cookie 属性、CSRF、跨源、退出后重放、过期/重启、HTTP/TLS、Host/X-Forwarded-* 伪造与编码/尾斜杠路由。
- 前端测试覆盖深链不挂敏感树、401 清状态、多标签页注销、停止轮询、取消及过期代数防回填；先解决隔离版本匹配工具链，不改全局环境。
- 实际检查通过前不关闭 Issue、不冒称 AC 完成；公开证据不含密码、哈希、Cookie、生产数据或环境秘密。
