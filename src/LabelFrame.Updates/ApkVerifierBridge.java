import com.android.apksig.ApkVerifier;
import java.io.File;
import java.security.MessageDigest;
import java.util.HexFormat;

/** 使用官方验签器验证 APK 内容与单一签名身份。 */
class ApkVerifierBridge {
    public static void main(String[] args) throws Exception {
        if (args.length != 1) throw new IllegalArgumentException("APK path required");
        ApkVerifier.Result result = new ApkVerifier.Builder(new File(args[0])).build().verify();
        if (!result.isVerified() || !result.isVerifiedUsingV2Scheme()
                || result.getSignerCertificates().size() != 1 || result.getSigningCertificateLineage() != null)
            throw new SecurityException("APK signature, signer count or lineage rejected");
        byte[] certificate = result.getSignerCertificates().get(0).getEncoded();
        System.out.println(HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(certificate)));
    }
}
