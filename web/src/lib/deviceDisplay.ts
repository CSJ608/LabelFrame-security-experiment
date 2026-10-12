// 设备显示名（· ，评审 B-6）：作业的「目标设备」显示口径与在线设备页 / 目标设备下拉
// 一致——设备名（GET /api/devices 的 name）优先；无可解析名称（空 / 空白 / 设备不在列表）回退设备 ID。

/** 设备显示名：name 非空（去首尾空白）用 name，否则回退 deviceId。 */
export function deviceDisplayName(name: string | null | undefined, deviceId: string): string {
  const trimmed = name?.trim()
  return trimmed ? trimmed : deviceId
}
