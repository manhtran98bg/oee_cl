using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.Common;

namespace Rostek.Gateway.Application.MesSync;

public sealed record MesMoldingMachineDto(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("serial")] string? Serial,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("location")] string? Location);

public sealed record MesMoldingMachineListItem(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("serial")] string? Serial,
    [property: JsonPropertyName("manufacturer")] string? Manufacturer,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("can_import")] bool CanImport,
    [property: JsonPropertyName("message")] string? Message);

public interface IMesEquipmentCatalogClient
{
    Task<IReadOnlyList<MesMoldingMachineDto>> FetchMoldingMachinesAsync(CancellationToken cancellationToken);
}

public interface IMesEquipmentCatalogService
{
    Task<GatewayResult<IReadOnlyList<MesMoldingMachineListItem>>> FetchMoldingMachinesAsync(CancellationToken cancellationToken);
}

public sealed class MesEquipmentCatalogService(
    IMesEquipmentCatalogClient client,
    ILogger<MesEquipmentCatalogService> logger) : IMesEquipmentCatalogService
{
    public async Task<GatewayResult<IReadOnlyList<MesMoldingMachineListItem>>> FetchMoldingMachinesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var machines = await client.FetchMoldingMachinesAsync(cancellationToken);
            var items = machines
                .Select(ToListItem)
                .OrderByDescending(machine => machine.CanImport)
                .ThenBy(machine => machine.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return GatewayResult<IReadOnlyList<MesMoldingMachineListItem>>.Ok(items);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Failed to fetch MES molding machine catalog");
            return GatewayResult<IReadOnlyList<MesMoldingMachineListItem>>.Fail($"Failed to fetch MES machines: {exception.Message}");
        }
    }

    private static MesMoldingMachineListItem ToListItem(MesMoldingMachineDto machine)
    {
        var code = Normalize(machine.Code) ?? string.Empty;
        return new MesMoldingMachineListItem(
            code,
            Normalize(machine.Name) ?? string.Empty,
            Normalize(machine.Model),
            Normalize(machine.Serial),
            Normalize(machine.Manufacturer),
            Normalize(machine.Location),
            !string.IsNullOrWhiteSpace(code),
            string.IsNullOrWhiteSpace(code) ? "Missing code" : null);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
