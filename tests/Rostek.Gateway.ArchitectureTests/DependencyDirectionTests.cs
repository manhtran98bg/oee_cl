using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Runtime.Machines;
using Xunit;

namespace Rostek.Gateway.ArchitectureTests;

public sealed class DependencyDirectionTests
{
    [Fact]
    public void Domain_does_not_reference_outer_projects()
    {
        var references = typeof(Machine).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToList();

        Assert.DoesNotContain("Rostek.Gateway.Application", references);
        Assert.DoesNotContain("Rostek.Gateway.Infrastructure", references);
        Assert.DoesNotContain("Rostek.Gateway.Runtime", references);
        Assert.DoesNotContain("Rostek.Gateway.Host", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
    }

    [Fact]
    public void Runtime_does_not_reference_application_or_host()
    {
        var references = typeof(MachineRuntimeManager).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToList();

        Assert.DoesNotContain("Rostek.Gateway.Application", references);
        Assert.DoesNotContain("Rostek.Gateway.Host", references);
    }

    [Fact]
    public void Application_does_not_reference_host()
    {
        var references = typeof(IMachineService).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToList();

        Assert.DoesNotContain("Rostek.Gateway.Host", references);
    }
}
