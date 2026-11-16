using Rostek.Gateway.Application.Common;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class NaturalCodeComparerTests
{
    [Fact]
    public void Sorts_machine_codes_by_numeric_segments()
    {
        string[] codes = ["2-11", "1-12", "1-2", "2-2", "1-1", "1-11", "2-1", "1-3"];

        var sorted = codes.OrderBy(code => code, NaturalCodeComparer.Instance).ToList();

        Assert.Equal(["1-1", "1-2", "1-3", "1-11", "1-12", "2-1", "2-2", "2-11"], sorted);
    }

    [Fact]
    public void Sorts_case_insensitively_and_handles_large_numeric_segments()
    {
        string[] codes = ["M-100000000000000000000", "m-9", "M-10", "M-2"];

        var sorted = codes.OrderBy(code => code, NaturalCodeComparer.Instance).ToList();

        Assert.Equal(["M-2", "m-9", "M-10", "M-100000000000000000000"], sorted);
    }
}
