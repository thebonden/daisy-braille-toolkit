namespace DAISY_Braille_Toolkit.Services.SharePoint
{
    /// <summary>
    /// Lightweight config container used by the SharePoint login/test dialog.
    /// Note: TenantId/ClientId are not secrets. Tokens are cached securely via DPAPI.
    /// </summary>
    public sealed record SharePointAuthConfig(
        string TenantId,
        string ClientId,
        string SiteUrl,
        string CountersListName,
        string ProductionsListName,
        string[]? Scopes = null)
    {
        /// <summary>
        /// Default delegated scopes that are typically sufficient for working with SharePoint lists via Microsoft Graph.
        /// IT may prefer Sites.Selected instead of Sites.ReadWrite.All for least privilege.
        /// </summary>
        public static readonly string[] DefaultScopes =
        [
            "User.Read",
            "Sites.ReadWrite.All"
        ];

        public string[] EffectiveScopes => Scopes is { Length: > 0 } ? Scopes : DefaultScopes;
    }
}
