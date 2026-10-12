namespace Mise.Application.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Application_assembly_is_loadable()
    {
        var assembly = typeof(Mise.Application.DependencyInjection).Assembly;

        Assert.Equal("Mise.Application", assembly.GetName().Name);
    }
}
