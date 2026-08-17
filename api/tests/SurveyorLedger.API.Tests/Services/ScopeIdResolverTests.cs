using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using SurveyorLedger.API.Services;

namespace SurveyorLedger.API.Tests.Services;

/// <summary>ScopeIdResolver is a pure dispatcher - these tests exercise the routing logic itself, independent of any concrete provider (EF/DB).</summary>
public class ScopeIdResolverTests
{
    private static Mock<IScopeLinkProvider> MakeProvider(string childType, string parentType, Guid? parentId, List<Guid>? childIds = null)
    {
        var mock = new Mock<IScopeLinkProvider>();
        mock.SetupGet(p => p.ChildScopeType).Returns(childType);
        mock.SetupGet(p => p.ParentScopeType).Returns(parentType);
        mock.Setup(p => p.GetParentIdAsync(It.IsAny<Guid>())).ReturnsAsync(parentId);
        mock.Setup(p => p.GetChildIdsAsync(It.IsAny<Guid>())).ReturnsAsync(childIds ?? []);
        return mock;
    }

    [Fact]
    public async Task GetParentIdAsync_RoutesToMatchingProvider()
    {
        var workspaceId = Guid.NewGuid();
        var jobProvider = MakeProvider("Job", "Workspace", workspaceId);
        var resolver = new ScopeIdResolver([jobProvider.Object], Mock.Of<ILogger<ScopeIdResolver>>());

        var result = await resolver.GetParentIdAsync("Job", Guid.NewGuid());

        Assert.Equal(workspaceId, result);
    }

    [Fact]
    public async Task GetParentIdAsync_NoProviderRegistered_ReturnsNullWithoutThrowing()
    {
        var resolver = new ScopeIdResolver([], Mock.Of<ILogger<ScopeIdResolver>>());

        var result = await resolver.GetParentIdAsync("Workspace", Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetChildIdsAsync_RoutesToProviderMatchingBothScopeTypes()
    {
        var jobId = Guid.NewGuid();
        var jobProvider = MakeProvider("Job", "Workspace", null, [jobId]);
        var resolver = new ScopeIdResolver([jobProvider.Object], Mock.Of<ILogger<ScopeIdResolver>>());

        var result = await resolver.GetChildIdsAsync("Workspace", "Job", Guid.NewGuid());

        Assert.Single(result);
        Assert.Equal(jobId, result[0]);
    }

    [Fact]
    public async Task GetChildIdsAsync_UnregisteredPair_ReturnsEmptyWithoutThrowing()
    {
        var jobProvider = MakeProvider("Job", "Workspace", null, [Guid.NewGuid()]);
        var resolver = new ScopeIdResolver([jobProvider.Object], Mock.Of<ILogger<ScopeIdResolver>>());

        // Right parent type, wrong child type - no provider matches this exact pair.
        var result = await resolver.GetChildIdsAsync("Workspace", "Land", Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddingNewProvider_RequiresNoResolverChange()
    {
        // Simulates adding an Organization level: a second provider for a different pair,
        // registered alongside the existing one. ScopeIdResolver itself is untouched.
        var workspaceId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var jobProvider = MakeProvider("Job", "Workspace", workspaceId);
        var workspaceProvider = MakeProvider("Workspace", "Organization", orgId);
        var resolver = new ScopeIdResolver([jobProvider.Object, workspaceProvider.Object], Mock.Of<ILogger<ScopeIdResolver>>());

        var jobParent = await resolver.GetParentIdAsync("Job", Guid.NewGuid());
        var workspaceParent = await resolver.GetParentIdAsync("Workspace", Guid.NewGuid());

        Assert.Equal(workspaceId, jobParent);
        Assert.Equal(orgId, workspaceParent);
    }
}
