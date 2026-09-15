namespace OpenCampus.IntegrationTests;

/// <summary>
/// All integration test classes share one host and one isolated database (TST-01),
/// which is created before the first class runs and dropped after the last (TST-02).
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
