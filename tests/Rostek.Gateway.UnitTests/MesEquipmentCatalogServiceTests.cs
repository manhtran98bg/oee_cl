using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.MesSync;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class MesEquipmentCatalogServiceTests
{
    [Fact]
    public async Task FetchMoldingMachines_normalizes_items_and_marks_missing_code()
    {
        var service = new MesEquipmentCatalogService(
            new StubMesEquipmentCatalogClient(
            [
                new MesMoldingMachineDto(" 1-1 ", " Molding 1 ", " J350 ", " SN-1 ", " JSW ", " Line 1 "),
                new MesMoldingMachineDto("", "Missing Code", null, null, null, null)
            ]),
            NullLogger<MesEquipmentCatalogService>.Instance);

        var result = await service.FetchMoldingMachinesAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Collection(
            result.Value!,
            item =>
            {
                Assert.Equal("1-1", item.Code);
                Assert.Equal("Molding 1", item.Name);
                Assert.Equal("J350", item.Model);
                Assert.Equal("SN-1", item.Serial);
                Assert.Equal("JSW", item.Manufacturer);
                Assert.Equal("Line 1", item.Location);
                Assert.True(item.CanImport);
                Assert.Null(item.Message);
            },
            item =>
            {
                Assert.Equal(string.Empty, item.Code);
                Assert.False(item.CanImport);
                Assert.Equal("Missing code", item.Message);
            });
    }

    private sealed class StubMesEquipmentCatalogClient(IReadOnlyList<MesMoldingMachineDto> machines) : IMesEquipmentCatalogClient
    {
        public Task<IReadOnlyList<MesMoldingMachineDto>> FetchMoldingMachinesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(machines);
    }
}
