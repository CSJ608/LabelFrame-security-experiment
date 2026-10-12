// 防复发断言（「单码单参数键集」）——en 渲染变体矩阵：
// 对每个多调用点错误码的**各后端变体**（码 + params 键集）逐一做 en 渲染单测：
// 全部渲染英文词条、插值正确、无 `{{` 残留、无回退中文——同码任何变体参数键与词条占位符不对齐都会在此红。
// 最低覆盖锚定验收点名项：LF_SRV_006 两个变体（模板库端点 / 作业提交链）与 LF_SRV_001 各变体（deviceId / 按 IP 拆码 LF_SRV_012）。
// 变体清单与后端调用点一一对应（后端侧另有源扫描断言 ErrorParamKeyConsistencyTests 锚定「同码跨调用点参数键集一致」）。

import { describe, expect, it, afterEach } from 'vitest'
import { changeLocale, currentLocale } from './index'
import { resolveApiErrorMessage } from './errorMessages'

/** 后端携带 params 的错误变体清单：来源注释 = 后端调用点（新变体请同步登记）。 */
const VARIANTS: {
  provenance: string
  code: string
  params: Record<string, string>
  backend: string
  expected: string
}[] = [
  {
    provenance: 'LF_JOB_001：Core LabelJobQueue 挂起/恢复/取消/重打 ×4、WinHost GET /api/jobs、AndroidHost GET /api/jobs',
    code: 'LF_JOB_001',
    params: { jobId: 'job-7' },
    backend: '作业不存在：job-7。',
    expected: 'Job not found: job-7.',
  },
  {
    provenance: 'LF_JOB_002：Core LabelJobQueue 状态转移拒绝 ×4（键集 {status}）',
    code: 'LF_JOB_002',
    params: { status: 'Suspended' },
    backend: '作业当前状态 Suspended 不允许挂起。',
    expected: 'The job status Suspended does not allow this operation.',
  },
  {
    provenance: 'LF_JOB_003：Core RetryItemAsync 条目越界（#242 拆码，键集 {itemIndex}）',
    code: 'LF_JOB_003',
    params: { itemIndex: '7' },
    backend: '作业没有第 7 张标签。',
    expected: 'The job has no label #7.',
  },
  {
    provenance: 'LF_JOB_004：Core RetryItemAsync 条目非 Failed（#242 拆码，键集 {itemIndex,itemStatus}）',
    code: 'LF_JOB_004',
    params: { itemIndex: '1', itemStatus: 'Completed' },
    backend: '第 1 张状态为 Completed，仅 Failed 可重打。',
    expected: 'Label #1 has status Completed; only Failed labels can be reprinted.',
  },
  {
    provenance: 'LF_IO_001：AndroidHost 测试页发送失败（键集 {reason}）',
    code: 'LF_IO_001',
    params: { reason: '连接超时' },
    backend: '发送失败：连接超时。',
    expected: 'Send failed: 连接超时.',
  },
  {
    provenance: 'LF_API_001：Core SqliteLabelJobStore 请求重复且作业不存在（键集 {requestId}；词条无占位符=通用桶码，params 面向调用方透传）',
    code: 'LF_API_001',
    params: { requestId: 'req-9' },
    backend: '请求重复且作业不存在：req-9。',
    expected: 'Invalid request (missing required fields or invalid parameters).',
  },
  {
    provenance: 'LF_TPL_001：TemplateEndpoints GET/导出/预览 ×3（WinHost 接线；#242 返修统一键 templateName）',
    code: 'LF_TPL_001',
    params: { templateName: '标签A' },
    backend: '模板不存在:标签A。',
    expected: 'Template not found: 标签A.',
  },
  {
    provenance: 'LF_TRANSPORT_INVALID：WinHost TransportApi 连接方式不支持（键集 {mode}；词条无占位符=通用桶码）',
    code: 'LF_TRANSPORT_INVALID',
    params: { mode: 'carrier-pigeon' },
    backend: '不支持的连接方式：carrier-pigeon。',
    expected: 'Invalid connection configuration (missing or unsupported parameters).',
  },
  {
    provenance: 'LF_TRANSPORT_TEST_FAILED：WinHost PrinterApi 测试页发送失败（键集 {target,reason}）',
    code: 'LF_TRANSPORT_TEST_FAILED',
    params: { target: '10.0.0.5:9100', reason: 'timeout' },
    backend: '测试页发送失败：无法连接打印机「10.0.0.5:9100」——timeout。',
    expected:
      'Test page failed to send: cannot connect to printer "10.0.0.5:9100" — timeout. Check the printer address / network / driver and retry.',
  },
  {
    provenance: 'LF_PLUGIN_INVALID：AndroidHost 插件安装/卸载包校验失败 ×2（键集 {detail}）',
    code: 'LF_PLUGIN_INVALID',
    params: { detail: '插件包缺少根 manifest.json。' },
    backend: '插件包无效：插件包缺少根 manifest.json。',
    expected: 'Invalid plugin package: 插件包缺少根 manifest.json。',
  },
  {
    provenance: 'LF_SRV_001：ServerService 心跳/提交目标设备/领取 ×3（键集 {deviceId}；按 IP 变体已拆 LF_SRV_012）',
    code: 'LF_SRV_001',
    params: { deviceId: 'dev-1' },
    backend: '设备未注册：dev-1。',
    expected: 'Device not registered: dev-1.',
  },
  {
    provenance: 'LF_SRV_002：Server PackagesApi 插件包无效 / ImportEndpoints Excel 解析失败（键集 {detail}；词条无占位符=通用桶码）',
    code: 'LF_SRV_002',
    params: { detail: '插件包缺少根 manifest.json。' },
    backend: '插件包无效：插件包缺少根 manifest.json。',
    expected: 'Invalid request: missing required fields or invalid parameters.',
  },
  {
    provenance: 'LF_SRV_003：ServerService 作业查询/回报/进度 ×4（键集 {jobId}）',
    code: 'LF_SRV_003',
    params: { jobId: 'j-1' },
    backend: '作业不存在：j-1。',
    expected: 'Job not found: j-1.',
  },
  {
    provenance: 'LF_SRV_004：ServerService 非归属回报/进度 ×2（键集 {deviceId,jobId}）',
    code: 'LF_SRV_004',
    params: { deviceId: 'dev-1', jobId: 'j-1' },
    backend: '设备 dev-1 不是作业 j-1 的领取者。',
    expected: 'Device dev-1 is not the owner of job j-1.',
  },
  {
    provenance: 'LF_SRV_005：ServerService 回报/进度状态拒绝 ×2（键集 {jobId,status}）',
    code: 'LF_SRV_005',
    params: { jobId: 'j-1', status: 'Pending' },
    backend: '作业 j-1 当前状态 Pending 不允许回报结果。',
    expected: 'Job j-1 in status Pending does not allow this operation.',
  },
  {
    provenance: 'LF_SRV_006 变体①（验收失败项 1a）：TemplateEndpoints GET/导出/预览 ×3（Server 接线；#242 返修统一键 templateName）',
    code: 'LF_SRV_006',
    params: { templateName: 'AC04-模板α' },
    backend: '模板不存在:AC04-模板α。',
    expected: 'Template not found: AC04-模板α.',
  },
  {
    provenance: 'LF_SRV_006 变体②（验收通过项 1b）：ServerService 作业提交链（键集 {templateName}）',
    code: 'LF_SRV_006',
    params: { templateName: 'AC04-模板β' },
    backend: '模板不存在：AC04-模板β。',
    expected: 'Template not found: AC04-模板β.',
  },
  {
    provenance: 'LF_SRV_012 变体①：DevicesApi GET /api/devices/by-ip（#242 拆码，键集 {ip}）',
    code: 'LF_SRV_012',
    params: { ip: '10.0.0.9' },
    backend: '按 IP 未找到设备：10.0.0.9。',
    expected: 'No device found for IP: 10.0.0.9.',
  },
  {
    provenance: 'LF_SRV_012 变体②：ServerService 提交 targetIp 解析（键集 {ip}）',
    code: 'LF_SRV_012',
    params: { ip: '10.88.88.88' },
    backend: '按 IP 未找到设备：10.88.88.88。',
    expected: 'No device found for IP: 10.88.88.88.',
  },
  {
    provenance: 'LF_SRV_013：ServerService 提交 callbackUrl 校验（#242 拆码，键集 {callbackUrl}）',
    code: 'LF_SRV_013',
    params: { callbackUrl: 'file://x' },
    backend: 'callbackUrl 无效（仅支持 http/https 地址）：file://x。',
    expected: 'Invalid callbackUrl (only http/https URLs are allowed): file://x.',
  },
]

describe('en 渲染变体矩阵：同码各变体参数键与词条占位符对齐（#242 返修防复发）', () => {
  const originalLocale = currentLocale()

  afterEach(() => {
    changeLocale(originalLocale)
  })

  it.each(VARIANTS)('$code 渲染英文词条且插值正确（$provenance）', ({ code, params, backend, expected }) => {
    changeLocale('en')
    const resolved = resolveApiErrorMessage(code, params, backend)
    expect(resolved, `${code} 应渲染 en 词条而非回退中文`).toBe(expected)
    expect(resolved).not.toContain('{{')
    expect(resolved).not.toBe(backend)
  })
})
