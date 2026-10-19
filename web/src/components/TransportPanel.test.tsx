// @vitest-environment jsdom
// 连接设置字段集联动：插件参数表单按 spec 条件可见性（visibleWhenKey / visibleWhenValues）渲染——
// 选某连接类型只显示该类型相关字段（Zebra：TCP=主机+端口、USB=USB 名、驱动=打印机名），
// kind / printMode 恒显；无声明参数恒显示（兼容旧后端与无条件插件）。
// spec Label 直出来自后端，此处以 label 文本定位字段（required 星号后缀用子串匹配）；
// 隐藏字段的值保留在表单状态由 lib 层 pluginParamsFromConfig / 提交链路覆盖，不在组件断言范围。

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import i18next from '../i18n'
import { TransportPluginParamsEditor } from './TransportPanel'
import type { PluginParamValue, PluginParams, TransportPluginInfo } from '../lib/api/types'

/** 后端 Zebra 插件 spec 形态镜像（含条件可见性声明与展示端翻译键）。 */
const ZEBRA: TransportPluginInfo = {
  id: 'labelframe-transport-zebra',
  displayName: 'Zebra',
  parameters: [
    {
      key: 'kind', label: '连接类型', type: 'Select', required: true, defaultValue: 'Tcp',
      labelKey: 'plugin.labelframe-transport-zebra.params.kind.label',
      options: [
        { value: 'Tcp', label: 'TCP' },
        { value: 'Usb', label: 'USB（自动发现）', labelKey: 'plugin.labelframe-transport-zebra.params.kind.opt.Usb' },
        { value: 'Driver', label: 'Windows 驱动', labelKey: 'plugin.labelframe-transport-zebra.params.kind.opt.Driver' },
      ],
    },
    { key: 'host', label: '打印机地址 / IP', type: 'String', required: true, hint: '如 192.168.1.50', labelKey: 'plugin.labelframe-transport-zebra.params.host.label', hintKey: 'plugin.labelframe-transport-zebra.params.host.hint', visibleWhenKey: 'kind', visibleWhenValues: ['Tcp'] },
    { key: 'port', label: '端口', type: 'Int', defaultValue: '9100', labelKey: 'plugin.labelframe-transport-zebra.params.port.label', visibleWhenKey: 'kind', visibleWhenValues: ['Tcp'] },
    { key: 'printerName', label: 'Windows 打印机名', type: 'String', required: true, labelKey: 'plugin.labelframe-transport-zebra.params.printerName.label', visibleWhenKey: 'kind', visibleWhenValues: ['Driver'] },
    { key: 'usbName', label: 'Zebra USB 打印机名', type: 'String', labelKey: 'plugin.labelframe-transport-zebra.params.usbName.label', visibleWhenKey: 'kind', visibleWhenValues: ['Usb'] },
    { key: 'printMode', label: '打印方式', type: 'Select', required: true, defaultValue: 'image', labelKey: 'plugin.labelframe-transport-zebra.params.printMode.label', options: [{ value: 'image', label: '图片（默认）', labelKey: 'plugin.labelframe-transport-zebra.params.printMode.opt.image' }, { value: 'native', label: '原生指令', labelKey: 'plugin.labelframe-transport-zebra.params.printMode.opt.native' }] },
  ],
}

/** 状态宿主：setParam 落本地 state，驱动联动重渲染（贴近面板表单真实用法）。 */
function Host({ plugin, initial }: { plugin: TransportPluginInfo; initial: PluginParams }) {
  const [params, setParams] = useState<PluginParams>(initial)
  return (
    <TransportPluginParamsEditor
      plugin={plugin}
      params={params}
      setParam={(key: string, value: PluginParamValue) => setParams((p) => ({ ...p, [key]: value }))}
    />
  )
}

function expectFieldVisible(label: string, visible: boolean) {
  const fields = screen.queryAllByText(label, { exact: false })
  expect(fields.length, `字段「${label}」出现次数`).toBe(visible ? 1 : 0)
}

/** kind 下拉（parameters 顺序第一个 Select；printMode 第二个）。 */
function kindSelect(): HTMLSelectElement {
  return screen.getAllByRole('combobox')[0] as HTMLSelectElement
}

afterEach(() => {
  cleanup()
  if (i18next.language !== 'zh-CN') void i18next.changeLanguage('zh-CN')
})

describe('TransportPluginParamsEditor 字段集联动（按连接类型显隐）', () => {
  it('kind=Tcp：显示主机 / 端口 / 打印方式，不显示 Windows 打印机名与 USB 名', () => {
    render(<Host plugin={ZEBRA} initial={{ kind: 'Tcp', port: 9100 }} />)
    expectFieldVisible('连接类型', true)
    expectFieldVisible('打印机地址 / IP', true)
    expectFieldVisible('端口', true)
    expectFieldVisible('打印方式', true)
    expectFieldVisible('Windows 打印机名', false)
    expectFieldVisible('Zebra USB 打印机名', false)
  })

  it('切换 kind=Driver：只显示 Windows 打印机名（主机 / 端口 / USB 名隐藏）', () => {
    render(<Host plugin={ZEBRA} initial={{ kind: 'Tcp', port: 9100 }} />)
    fireEvent.change(kindSelect(), { target: { value: 'Driver' } })
    expectFieldVisible('Windows 打印机名', true)
    expectFieldVisible('打印机地址 / IP', false)
    expectFieldVisible('端口', false)
    expectFieldVisible('Zebra USB 打印机名', false)
    expectFieldVisible('打印方式', true)
  })

  it('切换 kind=Usb：只显示 USB 名（可空自动发现），主机 / 端口 / 打印机名隐藏', () => {
    render(<Host plugin={ZEBRA} initial={{ kind: 'Tcp', port: 9100 }} />)
    fireEvent.change(kindSelect(), { target: { value: 'Usb' } })
    expectFieldVisible('Zebra USB 打印机名', true)
    expectFieldVisible('打印机地址 / IP', false)
    expectFieldVisible('端口', false)
    expectFieldVisible('Windows 打印机名', false)
  })

  it('无 visibleWhen 声明的插件（旧后端 / 无条件插件）全部字段恒显示', () => {
    const plain: TransportPluginInfo = {
      id: 'tcp9100',
      displayName: '网口打印机（TCP 9100）',
      parameters: [
        { key: 'host', label: '打印机地址 / IP', type: 'String', required: true },
        { key: 'port', label: '端口', type: 'Int', defaultValue: '9100' },
      ],
    }
    render(<Host plugin={plain} initial={{}} />)
    expectFieldVisible('打印机地址 / IP', true)
    expectFieldVisible('端口', true)
  })
})

describe('TransportPluginParamsEditor 插件自描述键化渲染（决策 #192）', () => {
  it('EN 态：带键 spec 渲染英文词条（label / Select 选项 / hint），无中文混排', async () => {
    await i18next.changeLanguage('en')
    render(<Host plugin={ZEBRA} initial={{ kind: 'Tcp', port: 9100 }} />)

    expectFieldVisible('Connection type', true)
    expectFieldVisible('Printer address / IP', true)
    expectFieldVisible('Port', true)
    expectFieldVisible('Print mode', true)
    expectFieldVisible('连接类型', false)
    expectFieldVisible('打印机地址 / IP', false)

    const optionTexts = Array.from(kindSelect().options).map((o) => o.textContent)
    expect(optionTexts).toContain('USB (auto-discovery)')
    expect(optionTexts).toContain('Windows driver')
    expect(optionTexts).not.toContain('USB（自动发现）')

    expect(screen.getByPlaceholderText('e.g. 192.168.1.50')).toBeTruthy()
  })

  it('EN 态：无键插件（第三方 / 旧后端 spec）回退原文显示，不劣化', async () => {
    await i18next.changeLanguage('en')
    const third: TransportPluginInfo = {
      id: 'third-party-demo',
      displayName: '第三方演示传输',
      parameters: [
        { key: 'host', label: '设备地址', type: 'String', required: true, hint: '厂商自定义提示' },
      ],
    }
    render(<Host plugin={third} initial={{}} />)

    expectFieldVisible('设备地址', true)
    expect(screen.getByPlaceholderText('厂商自定义提示')).toBeTruthy()
  })
})

describe('TransportPluginParamsEditor 参数行分组（Row 同行）', () => {
  it('同 Row 值的字段渲染于同一行（行序 = 首个成员声明序）；无 Row 声明各自成行', () => {
    const plugin: TransportPluginInfo = {
      id: 'demo',
      displayName: '演示插件',
      parameters: [
        { key: 'kind', label: '连接类型', type: 'Select', required: true, defaultValue: 'Tcp', row: 1, options: [{ value: 'Tcp', label: 'TCP' }] },
        { key: 'host', label: '打印机地址 / IP', type: 'String', required: true, row: 2 },
        { key: 'port', label: '端口', type: 'Int', defaultValue: '9100', row: 2 },
        { key: 'paper', label: '纸宽（毫米）', type: 'Int' },
      ],
    }
    render(<Host plugin={plugin} initial={{ kind: 'Tcp' }} />)
    const rows = screen.queryAllByTestId('param-row')
    expect(rows.length).toBe(3)
    expect(rows[0]?.textContent).toContain('连接类型')
    expect(rows[1]?.textContent).toContain('打印机地址 / IP')
    expect(rows[1]?.textContent).toContain('端口')
    expect(rows[2]?.textContent).toContain('纸宽（毫米）')
  })
})
