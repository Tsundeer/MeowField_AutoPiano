namespace MeowField.Infrastructure.Network;

/// <summary>
/// 在线曲库的一个镜像源。Gitee 与 GitHub 的 raw 地址结构一致
/// （<c>{base}{相对路径}</c>），因此同一个类型可以承载两者。
/// </summary>
public sealed record LibrarySource(string Name, string BaseUrl)
{
    public Uri CatalogUri => new(BaseUrl + "catalog.json", UriKind.Absolute);

    public Uri CompressedCatalogUri => new(BaseUrl + "catalog.json.gz", UriKind.Absolute);

    public Uri TrackUri(string relativePath) => new(BaseUrl + EscapePath(relativePath), UriKind.Absolute);

    /// <summary>
    /// 曲目路径含中文、全角括号等字符，必须逐段编码；
    /// 目录分隔符要还原，否则 raw 端点会把整段当成一个文件名。
    /// </summary>
    public static string EscapePath(string relativePath) =>
        Uri.EscapeDataString(relativePath.Replace('\\', '/')).Replace("%2F", "/", StringComparison.Ordinal);
}

/// <summary>
/// 内置镜像顺序。<c>raw.githubusercontent.com</c> 在国内基本不可达，
/// 因此 Gitee 放首位，GitHub 仅作为境外用户与兜底。
/// </summary>
public static class LibrarySources
{
    // Gitee 与 GitHub 是两个账号：镜像托管在 Gitee 的个人账号下，
    // 上游仓库仍是 GitHub 上的 Tsundeer，两者不能共用一个 Owner。
    public const string GiteeOwner = "qinqinding";
    public const string GitHubOwner = "Tsundeer";
    public const string Repository = "MeowField_MidiLibrary";
    public const string PrimaryBranch = "main";

    public static LibrarySource Gitee { get; } =
        new("Gitee", $"https://gitee.com/{GiteeOwner}/{Repository}/raw/{PrimaryBranch}/");

    // Gitee 新建仓库默认分支是 master，镜像脚本未改默认分支时仍可用。
    public static LibrarySource GiteeMaster { get; } =
        new("Gitee", $"https://gitee.com/{GiteeOwner}/{Repository}/raw/master/");

    public static LibrarySource GitHub { get; } =
        new("GitHub", $"https://raw.githubusercontent.com/{GitHubOwner}/{Repository}/{PrimaryBranch}/");

    public static IReadOnlyList<LibrarySource> Default { get; } = [Gitee, GiteeMaster, GitHub];
}
