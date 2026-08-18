using Rostek.Gateway.Application.Common;

namespace Rostek.Gateway.Application.MachineTemplates;

public interface IMachineTemplateService
{
    Task<IReadOnlyList<MachineTemplateListItem>> ListAsync(CancellationToken cancellationToken);
    Task<MachineTemplateInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TemplateSignalListItem>> ListSignalsAsync(Guid templateId, CancellationToken cancellationToken);
    Task<TemplateSignalInput?> GetSignalInputAsync(Guid templateId, Guid signalId, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(MachineTemplateInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveSignalAsync(TemplateSignalInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> DeleteSignalAsync(Guid id, string? userName, CancellationToken cancellationToken);
}
