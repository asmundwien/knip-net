namespace CatH.FrameworkControllerEntry
{
    public abstract class GhostController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        [Microsoft.AspNetCore.Mvc.HttpGet]
        public Microsoft.AspNetCore.Mvc.IActionResult Index() => Ok();
    }

    public sealed class OrdersController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        private readonly string _state;

        public OrdersController()
        {
            _state = BuildState();
        }

        public Microsoft.AspNetCore.Mvc.IActionResult Index()
        {
            UseState();
            return Ok();
        }

        [Microsoft.AspNetCore.Mvc.NonAction]
        public void PublicHelper() { }

        protected void ProtectedHelper() { }

        public void GenericHelper<T>() { }

        public static void StaticHelper() { }

        private static string BuildState() => "ready";

        private void UseState() => System.Console.WriteLine(_state);
    }

    public sealed class PlainController
    {
        public void Index() => IndexCore();

        [Microsoft.AspNetCore.Mvc.NonAction]
        public void PublicHelper() { }

        protected void ProtectedHelper() { }

        private void IndexCore() { }
    }

    [Microsoft.AspNetCore.Mvc.Controller]
    public sealed class AttributedEndpoint
    {
        public void Index() => IndexCore();

        [Microsoft.AspNetCore.Mvc.NonAction]
        public void PublicHelper() { }

        private void IndexCore() { }
    }

    [Microsoft.AspNetCore.Mvc.NonController]
    public sealed class IgnoredController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public void Index() { }
    }

    internal sealed class InternalController : Microsoft.AspNetCore.Mvc.ControllerBase
    {
        public void Index() { }
    }
}

namespace CatH.FrameworkComponentEntry
{
    public sealed class DashboardComponent : Microsoft.AspNetCore.Components.ComponentBase
    {
        private readonly string _state;

        public DashboardComponent()
        {
            _state = BuildState();
        }

        protected override void OnInitialized() => RenderCore();

        public void PublicHelper() { }

        protected void ProtectedHelper() { }

        private static string BuildState() => "ready";

        private void RenderCore() => System.Console.WriteLine(_state);
    }
}

namespace CatH.FrameworkHubEntry
{
    public sealed class ChatHub : Microsoft.AspNetCore.SignalR.Hub
    {
        private readonly string _state;

        public ChatHub()
        {
            _state = BuildState();
        }

        public System.Threading.Tasks.Task SendAsync()
        {
            SendCore();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        protected void ProtectedHelper() { }

        private static string BuildState() => "ready";

        private void SendCore() => System.Console.WriteLine(_state);
    }
}

namespace CatH.FrameworkPageModelEntry
{
    public sealed class IndexModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
    {
        private readonly string _state;

        public IndexModel()
        {
            _state = BuildState();
        }

        public void OnGet() => RenderCore();

        [Microsoft.AspNetCore.Mvc.RazorPages.NonHandler]
        public void OnPostHelper() { }

        public void PublicHelper() { }

        public void Onboarding() { }

        protected void ProtectedHelper() { }

        private static string BuildState() => "ready";

        private void RenderCore() => System.Console.WriteLine(_state);
    }
}

namespace CatH.FrameworkHostedServiceEntry
{
    public sealed class Worker : Microsoft.Extensions.Hosting.BackgroundService
    {
        private readonly string _state;

        public Worker()
        {
            _state = BuildState();
        }

        protected override System.Threading.Tasks.Task ExecuteAsync(
            System.Threading.CancellationToken stoppingToken)
        {
            ExecuteCore();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void PublicHelper() { }

        protected void ProtectedHelper() { }

        private static string BuildState() => "ready";

        private void ExecuteCore() => System.Console.WriteLine(_state);
    }

    public sealed class DirectHostedService : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly string _state;

        public DirectHostedService()
        {
            _state = BuildState();
        }

        public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            StartCore();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) =>
            System.Threading.Tasks.Task.CompletedTask;

        public void PublicHelper() { }

        private static string BuildState() => "ready";

        private void StartCore() => System.Console.WriteLine(_state);
    }
}
