package com.labelframe.updates;

import com.android.apksig.ApkVerifier;
import java.io.File;
import java.security.MessageDigest;

/** 验证 APK 完整内容并返回单签名证书指纹。 */
public final class ApkVerification {
    public static String verify(String path, int sdk) throws Exception {
        ApkVerifier.Result result = new ApkVerifier.Builder(new File(path))
            .setMinCheckedPlatformVersion(24).setMaxCheckedPlatformVersion(sdk).build().verify();
        if (!result.isVerified() || !result.isVerifiedUsingV2Scheme()
                || result.getSignerCertificates().size() != 1 || result.getSigningCertificateLineage() != null)
            throw new SecurityException("APK signature, signer count or lineage rejected");
        byte[] digest = MessageDigest.getInstance("SHA-256").digest(result.getSignerCertificates().get(0).getEncoded());
        StringBuilder hex = new StringBuilder();
        for (byte b : digest) hex.append(String.format(java.util.Locale.ROOT, "%02X", b & 0xff));
        return hex.toString();
    }
}
