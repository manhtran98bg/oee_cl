using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Rostek.Gateway.Host.Pages;

public sealed class ErrorModel : PageModel
{
    public string Message { get; private set; } = "An error occurred.";
    public void OnGet()
    {
    }
}
