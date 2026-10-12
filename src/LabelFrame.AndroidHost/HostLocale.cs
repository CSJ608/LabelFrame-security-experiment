namespace LabelFrame.AndroidHost;

/// <summary>当前资源语言判定（values/ 缺省 = 中文，values-en/ = 英文，其余回退中文）。</summary>
internal static class HostLocale
{
    /// <summary>当前生效资源语言是否为英文（经语言哨兵资源 current_language 读实际命中的资源表，
    /// 不解析 Configuration——回退规则交给 Android 资源机制本身，哨兵值即权威）。</summary>
    public static bool IsEnglish(Android.Content.Context context)
        => context.GetString(Resource.String.current_language) == "en";
}
