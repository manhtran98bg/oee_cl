using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Configurations;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Host.Pages.Configuration;

public sealed class HistoryModel(IConfigurationVersionService service) : PageModel
{
    public IReadOnlyList<ConfigurationVersion> Versions { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Versions = await service.ListAsync(cancellationToken);
    }
}
