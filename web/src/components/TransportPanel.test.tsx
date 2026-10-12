// @vitest-environment jsdom
// 连接设置字段集联动：插件参数表单按 spec 条件可见性（visibleWhenKey / visibleWhenValues）渲染——
// 选某连接类型只显示该类型相关字段（Zebra：TCP=主机+端口、USB=USB 名、驱动=打印机名），
// kind / printMode 恒显；无声明参数恒显示（兼容旧后端与无条件插件）。
// spec Label 直出来自后端，此处以 label 文本定位字段（required 星号后缀用子串匹配）；
// 隐藏字段的值保留在表单状态由 lib 层 pluginParamsFromConfig / 提交链路覆盖，不在组件断言范围。

import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import '../i18n'
import { TransportPluginParamsEditor } from './TransportPanel'
import type { PluginParamValue, PluginParams, TransportPluginInfo } from '../lib/api/types'

/** 后端 Zebra 插件 spec 形态镜像（含条件可见性声明）。 */
const ZEBRA: TransportPluginInfo = {
  id: 'labelframe-transport-zebra',
  displayName: 'Zebra',
  parameters: [
    {
      key: 'kind', label: '连接类型', type: 'Select', required: true, defaultValue: 'Tcp',
      options: [
        { value: 'Tcp', label: 'TCP' },
        { value: 'Usb', label: 'USB（自动发现）' },
        { value: 'Driver', label: 'Windows 驱动' },
      ],
    },
    { key: 'host', label: '打印机地址 / IP', type: 'String', required: true, hint: '如 192.168.1.50', visibleWhenKey: 'kind', visibleWhenValues: ['Tcp'] },
    { key: 'port', label: '端口', type: 'Int', defaultValue: '9100', visibleWhenKey: 'kind', visibleWhenValues: ['Tcp'] },
    { key: 'printerName', label: 'Windows 打印机名', type: 'String', required: true, visibleWhenKey: 'kind', visibleWhenValues: ['Driver'] },
    { key: 'usbName', label: 'Zebra USB 打印机名', type: 'String', visibleWhenKey: 'kind', visibleWhenValues: ['Usb'] },
    { key: 'printMode', label: '打印方式', type: 'Select', required: true, defaultValue: 'image', options: [{ value: 'image', label: '图片（默认）' }, { value: 'native', label: '原生指令' }] },
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

afterEach(cleanup)

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
