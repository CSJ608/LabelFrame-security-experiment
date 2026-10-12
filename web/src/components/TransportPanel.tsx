// 连接方式：设置页完整面板。
// 传输插件化——后端 availablePlugins（spec 驱动）存在时按插件目录动态渲染参数表单；
// 旧后端（无 availablePlugins）回退内置 4 模式（Log / Tcp / WindowsDriver / Zebra）。
// 交互：先测试后生效（非 testOnly 后端先测试再切换持久化）；失败返回当前连接、前端全局状态不动。
// 全部走 localApi（本机 Client）；连接徽标优先后端 displayText。
// 面板文案 key 化（settings 域 transportForm.* + common.transport.mode/zebraKind）。

import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import i18next from '../i18n'
import { localApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { PluginParams, PluginParamValue, TransportApplyRequest, TransportMode, TransportParams, TransportPluginInfo, ZebraKind } from '../lib/api/types'
import {
  ALL_TRANSPORT_MODES,
  ZEBRA_KINDS,
  defaultParams,
  defaultPluginParams,
  effectivePluginId,
  formatTransport,
  isSpecVisible,
  modeLabel,
  pluginParamsFromConfig,
  specDefaultValue,
  specOptions,
  zebraKindLabel,
} from '../lib/transport'
import { useApp } from '../state/AppContext'
import { Icon } from './Icon'

/* * 原生指令模式提示：原生指令无指令级预览，界面以文案说明效果口径。
 * 随数据与打印页迁移 key 化（common.transport.*——该徽标同时用于本面板表单）。 */
export function NativePrintModeHint() {
  const { t } = useTranslation()
  return (
    <span className="badge warn" data-testid="native-print-mode-hint" title={t('transport.nativeModeTitle')}>
      {t('transport.nativeModeHint')}
    </span>
  )
}

/** 表单当前选中插件是否处于原生指令模式（选择即时反馈用；连接态判断用 isNativePrintMode）。 */
function formNativeMode(plugin: TransportPluginInfo | undefined, params: PluginParams): boolean {
  if (plugin?.supportsDocumentCompile !== true) return false
  const v = params.printMode
  return typeof v === 'string' && v.trim().toLowerCase() === 'native'
}

/** 表单状态：插件模式（新后端）维护 pluginId + 参数字典；旧模式维护 mode + 平铺参数。
 *  只维护当前生效方式的参数，切换插件 / 模式丢弃其它输入。 */
function useTransportForm() {
  const { transportConfig } = useApp()
  const plugins = transportConfig?.availablePlugins ?? []
  const pluginMode = plugins.length > 0

  const [mode, setMode] = useState<TransportMode>(transportConfig?.mode ?? 'Log')
  const [params, setParams] = useState<TransportParams>(() => defaultParams(transportConfig?.mode ?? 'Log', transportConfig))
  const [pluginId, setPluginId] = useState<string>(() => {
    if (plugins.length > 0) {
      const cur = effectivePluginId(transportConfig)
      return plugins.some((p) => p.id === cur) ? cur : plugins[0].id
    }
    return ''
  })
  const [pluginParams, setPluginParams] = useState<PluginParams>(() => {
    if (plugins.length === 0) return {}
    const cur = effectivePluginId(transportConfig)
    const p = plugins.find((x) => x.id === cur) ?? plugins[0]
    return pluginParamsFromConfig(p, transportConfig)
  })
  const [touched, setTouched] = useState(false)

  // 后端探测 / 连接测试刷新全局配置后，表单与当前生效连接同步（未手动编辑过时）；用户编辑后不再覆盖
  useEffect(() => {
    if (touched) return
    const list = transportConfig?.availablePlugins ?? []
    if (list.length > 0) {
      const cur = effectivePluginId(transportConfig)
      const p = list.find((x) => x.id === cur) ?? list[0]
      setPluginId(p.id)
      setPluginParams(pluginParamsFromConfig(p, transportConfig))
    } else {
      const cur = transportConfig?.mode ?? 'Log'
      if (mode !== cur) {
        setMode(cur)
        setParams(defaultParams(cur, transportConfig))
      }
    }
  }, [transportConfig]) // eslint-disable-line react-hooks/exhaustive-deps

  const switchMode = (m: TransportMode) => {
    setTouched(true)
    setMode(m)
    setParams(defaultParams(m, transportConfig))
  }

  const setParam = useCallback((key: keyof TransportParams, value: string | number | undefined) => {
    setTouched(true)
    setParams((p) => ({ ...p, [key]: value }))
  }, [])

  const switchPlugin = (p: TransportPluginInfo) => {
    setTouched(true)
    setPluginId(p.id)
    setPluginParams(defaultPluginParams(p))
  }

  const setPluginParam = useCallback((key: string, value: PluginParamValue) => {
    setTouched(true)
    setPluginParams((p) => ({ ...p, [key]: value }))
  }, [])

  /** 拼 POST /api/transport 请求体：插件模式 = pluginId + params 字典；旧模式 = mode + 平铺参数（空值省略）。 */
  const buildRequest = useCallback((): TransportApplyRequest => {
    if (pluginMode) {
      // 后端 params 字典为 string→string（TransportApplyRequest.Params 类型），Int / Bool 必须序列化为字符串（联调修复）
      const serialized: PluginParams = {}
      for (const [k, v] of Object.entries(pluginParams)) {
        if (v === undefined || v === null) continue
        serialized[k] = typeof v === 'boolean' ? (v ? 'true' : 'false') : String(v)
      }
      return { pluginId, params: serialized }
    }
    const req: TransportApplyRequest = { mode }
    if (mode === 'Tcp') {
      req.tcpHost = params.tcpHost?.trim() || undefined
      req.tcpPort = params.tcpPort
    } else if (mode === 'WindowsDriver') {
      req.printerName = params.printerName?.trim() || undefined
    } else if (mode === 'Zebra') {
      req.zebraKind = params.zebraKind ?? 'Tcp'
      if (req.zebraKind === 'Usb') {
        req.zebraUsbName = params.zebraUsbName?.trim() || undefined
      } else if (req.zebraKind === 'Driver') {
        req.printerName = params.printerName?.trim() || undefined
      } else {
        req.tcpHost = params.tcpHost?.trim() || undefined
        req.tcpPort = params.tcpPort
      }
    }
    return req
  }, [pluginMode, pluginId, pluginParams, mode, params])

  return { pluginMode, plugins, pluginId, pluginParams, mode, params, switchMode, setParam, switchPlugin, setPluginParam, buildRequest }
}

/** 旧模式参数输入区（Log 无参数）。 */
export function TransportParamsEditor({
  mode,
  params,
  setParam,
}: {
  mode: TransportMode
  params: TransportParams
  setParam: (key: keyof TransportParams, value: string | number | undefined) => void
}) {
  const { t } = useTranslation('settings')
  if (mode === 'Log') {
    return <div className="hint">{t('transportForm.logHint')}</div>
  }
  if (mode === 'Tcp') {
    return (
      <>
        <label className="field" style={{ maxWidth: 240 }}>
          {t('transportForm.host')}
          <input
            className="input mono"
            value={params.tcpHost ?? ''}
            onChange={(ev) => setParam('tcpHost', ev.target.value)}
            placeholder="192.168.1.50"
            spellCheck={false}
          />
        </label>
        <label className="field" style={{ maxWidth: 120 }}>
          {t('transportForm.port')}
          <input
            className="input mono"
            type="number"
            min={1}
            max={65535}
            value={params.tcpPort ?? 9100}
            onChange={(ev) => setParam('tcpPort', ev.target.value === '' ? undefined : Number(ev.target.value))}
          />
        </label>
      </>
    )
  }
  if (mode === 'WindowsDriver') {
    return (
      <label className="field" style={{ maxWidth: 340 }}>
        {t('transportForm.printerName')}
        <input
          className="input mono"
          value={params.printerName ?? ''}
          onChange={(ev) => setParam('printerName', ev.target.value)}
          placeholder="ZDesigner ZD421-203dpi ZPL"
          spellCheck={false}
        />
      </label>
    )
  }
  // Zebra
  const kind = params.zebraKind ?? 'Tcp'
  return (
    <>
      <label className="field" style={{ maxWidth: 220 }}>
        {t('transportForm.zebraKind')}
        <select className="input" value={kind} onChange={(ev) => setParam('zebraKind', ev.target.value as ZebraKind)}>
          {ZEBRA_KINDS.map((k) => (
            <option key={k} value={k}>
              {zebraKindLabel(k)}
            </option>
          ))}
        </select>
      </label>
      {kind === 'Tcp' && (
        <>
          <label className="field" style={{ maxWidth: 240 }}>
            {t('transportForm.host')}
            <input
              className="input mono"
              value={params.tcpHost ?? ''}
              onChange={(ev) => setParam('tcpHost', ev.target.value)}
              placeholder="192.168.1.50"
              spellCheck={false}
            />
          </label>
          <label className="field" style={{ maxWidth: 120 }}>
            {t('transportForm.port')}
            <input
              className="input mono"
              type="number"
              min={1}
              max={65535}
              value={params.tcpPort ?? 9100}
              onChange={(ev) => setParam('tcpPort', ev.target.value === '' ? undefined : Number(ev.target.value))}
            />
          </label>
        </>
      )}
      {kind === 'Driver' && (
        <label className="field" style={{ maxWidth: 340 }}>
          {t('transportForm.printerName')}
          <input
            className="input mono"
            value={params.printerName ?? ''}
            onChange={(ev) => setParam('printerName', ev.target.value)}
            placeholder="ZDesigner ZD421-203dpi ZPL"
            spellCheck={false}
          />
        </label>
      )}
      {kind === 'Usb' && (
        <label className="field" style={{ maxWidth: 260 }}>
          {t('transportForm.usbName')}
          <input
            className="input mono"
            value={params.zebraUsbName ?? ''}
            onChange={(ev) => setParam('zebraUsbName', ev.target.value)}
            placeholder={t('transportForm.usbPlaceholder')}
            spellCheck={false}
          />
        </label>
      )}
    </>
  )
}

/* * 插件参数输入区：按 TransportParameterSpec 动态渲染——String 文本 / Int 数字 / Bool 开关 / Select 下拉。 */
export function TransportPluginParamsEditor({
  plugin,
  params,
  setParam,
}: {
  plugin: TransportPluginInfo
  params: PluginParams
  setParam: (key: string, value: PluginParamValue) => void
}) {
  const { t } = useTranslation('settings')
  if (plugin.parameters.length === 0) {
    // 无参数时说明文直出插件 Description（后端句读已完整），不再追加「（无参数）。」拼接残句
    return <div className="hint">{plugin.description || t('transportForm.noParams')}</div>
  }
  return (
    <>
      {plugin.parameters.map((spec) => {
        // 字段集按连接类型切换：不可见字段不渲染（值仍保留在表单状态，切换类型再切回不丢输入）
        if (!isSpecVisible(spec, params)) return null
        const value = params[spec.key]
        if (spec.type === 'Bool') {
          return (
            <label key={spec.key} className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }} title={spec.hint}>
              <input type="checkbox" checked={value === true || value === 'true'} onChange={(ev) => setParam(spec.key, ev.target.checked)} />
              {spec.label}
              {spec.required && <span style={{ color: 'var(--danger)' }}>*</span>}
            </label>
          )
        }
        if (spec.type === 'Select') {
          const options = specOptions(spec)
          return (
            <label key={spec.key} className="field" style={{ maxWidth: 220 }} title={spec.hint}>
              <span>
                {spec.label}
                {spec.required && <span style={{ color: 'var(--danger)' }}> *</span>}
              </span>
              <select className="input" value={String(value ?? specDefaultValue(spec))} onChange={(ev) => setParam(spec.key, ev.target.value)}>
                {options.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.label}
                  </option>
                ))}
              </select>
            </label>
          )
        }
        if (spec.type === 'Int') {
          return (
            <label key={spec.key} className="field" style={{ maxWidth: 140 }} title={spec.hint}>
              <span>
                {spec.label}
                {spec.required && <span style={{ color: 'var(--danger)' }}> *</span>}
              </span>
              <input
                className="input mono"
                type="number"
                min={0}
                max={65535}
                value={value === undefined || value === '' ? '' : String(value)}
                onChange={(ev) => setParam(spec.key, ev.target.value === '' ? '' : Number(ev.target.value))}
              />
            </label>
          )
        }
        return (
          <label key={spec.key} className="field" style={{ maxWidth: 260 }} title={spec.hint}>
            <span>
              {spec.label}
              {spec.required && <span style={{ color: 'var(--danger)' }}> *</span>}
            </span>
            <input
              className="input mono"
              value={String(value ?? '')}
              onChange={(ev) => setParam(spec.key, ev.target.value)}
              placeholder={spec.hint}
              spellCheck={false}
            />
          </label>
        )
      })}
    </>
  )
}

/** 设置页完整面板：当前生效连接 + 插件 / 模式选择 + 参数 + 测试连接 / 保存并应用。 */
export function TransportPanel() {
  const app = useApp()
  const { t } = useTranslation('settings')
  const form = useTransportForm()
  const [busy, setBusy] = useState<'test' | 'save' | null>(null)
  const [result, setResult] = useState<{ ok: boolean; msg: string } | null>(null)

  const run = async (testOnly: boolean) => {
    setBusy(testOnly ? 'test' : 'save')
    setResult(null)
    try {
      const r = testOnly ? await localApi.testTransport(form.buildRequest()) : await localApi.setTransport(form.buildRequest())
      if (r.ok) {
        // 切换成功后立即用响应 config 更新全局状态（不依赖 healthz 10s 轮询）
        app.applyTransportConfig(r.config)
        // 异步闭包文案读 i18next 单例（111 惯例：错误构造期即当时界面语言）
        if (!testOnly) app.setStatus(i18next.t('settings:transportForm.switched', { message: r.message }))
        setResult({ ok: true, msg: r.message })
      } else {
        // 200 + ok:false：测试失败不切换，config 仍是当前生效连接，全局状态不动
        setResult({ ok: false, msg: r.message })
      }
    } catch (err) {
      setResult({ ok: false, msg: err instanceof ApiError ? err.message : t('transportForm.saveFailed') })
    } finally {
      setBusy(null)
    }
  }

  const currentPlugin = form.plugins.find((p) => p.id === form.pluginId) ?? form.plugins[0]

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span className="hint">{t('transportForm.currentConnection')}</span>
        {/* 本机连接徽标随「本机打印服务」运行状态着色（本机事实），不随服务端地址连通性 */}
        <span className={'badge ' + (app.localServiceUp ? 'ok' : '')}>
          {formatTransport(app.transportConfig) || app.transport || t('value.unknown')}
        </span>
      </div>

      {form.pluginMode ? (
        <>
          <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap' }}>
            {form.plugins.map((p) => (
              <label key={p.id} className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
                <input type="radio" name="transport-plugin" checked={form.pluginId === p.id} onChange={() => form.switchPlugin(p)} />
                {p.displayName}
              </label>
            ))}
          </div>
          {currentPlugin?.description && <div className="hint">{currentPlugin.description}</div>}
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
            {currentPlugin && <TransportPluginParamsEditor plugin={currentPlugin} params={form.pluginParams} setParam={form.setPluginParam} />}
          </div>
          {formNativeMode(currentPlugin, form.pluginParams) && <NativePrintModeHint />}
        </>
      ) : (
        <>
          <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap' }}>
            {ALL_TRANSPORT_MODES.map((m) => (
              <label key={m} className="field" style={{ flexDirection: 'row', alignItems: 'center', gap: 6, margin: 0 }}>
                <input type="radio" name="transport-mode" checked={form.mode === m} onChange={() => form.switchMode(m)} />
                {modeLabel(m)}
              </label>
            ))}
          </div>
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
            <TransportParamsEditor mode={form.mode} params={form.params} setParam={form.setParam} />
          </div>
        </>
      )}

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
        <button className="btn" onClick={() => void run(true)} disabled={busy !== null}>
          <Icon name="link" size={13} />
          {busy === 'test' ? t('transportForm.testing') : t('transportForm.test')}
        </button>
        <button className="btn primary" onClick={() => void run(false)} disabled={busy !== null}>
          <Icon name="save" size={13} />
          {busy === 'save' ? t('transportForm.applying') : t('transportForm.save')}
        </button>
        {result && <span className={result.ok ? 'badge ok' : 'badge err'}>{result.msg}</span>}
      </div>
    </div>
  )
}
