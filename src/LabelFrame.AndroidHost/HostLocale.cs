namespace LabelFrame.AndroidHost;

/// <summary>
/// 当前资源语言判定：values/ 缺省 = 中文，values-en/ = 英文
/// 其余系统语言一律回退中文。经语言哨兵资源（<c>current_language</c>：values = zh / values-en = en）
/// 读出实际命中的资源表，而不是直接解析 <c>Configuration</c>——回退规则交给 Android 资源机制本身，
/// 哨兵值即权威。错误码翻译（<see cref="Errors.LfErrorCatalog"/>）据此选道。
/// </summary>
internal static class HostLocale
{
    /// <summary>当前生效资源语言是否为英文。</summary>
    public static bool IsEnglish(Android.Content.Context context)
        => context.GetString(Resource.String.current_language) == "en";
}
