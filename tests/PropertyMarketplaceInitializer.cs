using System.Runtime.CompilerServices;

internal static class PropertyMarketplaceInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        PropertyMarketplaceTests.Run();
    }
}
