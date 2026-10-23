# MSI 验签测试样本

`unsigned-client.msi` 仅包含本目录 `payload.txt`，没有自定义动作，不执行安装。来源是同目录 `fixture.wxs`；使用 WiX 7 在本目录执行 `wix build fixture.wxs -arch x64 -o unsigned-client.msi` 可重新生成。

测试复制样本后在内存创建临时 RSA 测试证书，经 Windows MSI SIP 签名，再调用生产验证器校验产品、版本、架构、兼容范围、发布者和完整内容摘要；还会修改 Property 表验证篡改拒绝。证书每次创建，不导入系统证书存储、不包含正式发布私钥。此样本不得分发或安装。
