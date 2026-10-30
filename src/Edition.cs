namespace ModHub;

/// <summary>
/// Which build this is. The normal build (Nexus mod 194 and GitHub) has everything. The Thunderstore build
/// (dotnet build -p:Edition=Thunderstore) leaves out Nexus and the self-update: there the mod manager
/// (r2modman, Gale) updates the Hub. Static readonly rather than const, so the other build's code doesn't warn as unreachable.
/// </summary>
internal static class Edition
{
#if THUNDERSTORE
    public static readonly bool Thunderstore = true;
#else
    public static readonly bool Thunderstore = false;
#endif
    public static bool Nexus => !Thunderstore;
    public static bool SelfUpdate => !Thunderstore;
}
