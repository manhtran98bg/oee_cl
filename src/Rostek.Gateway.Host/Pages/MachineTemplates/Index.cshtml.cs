using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.MachineTemplates;

namespace Rostek.Gateway.Host.Pages.MachineTemplates;

public sealed class IndexModel(IMachineTemplateService service) : PageModel
{
    public IReadOnlyList<MachineTemplateListItem> Templates { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Templates = await service.ListAsync(cancellationToken);
    }
}
