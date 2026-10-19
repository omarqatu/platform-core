using Microsoft.Extensions.Configuration;

namespace Ops;

// The operations procedure (PLATFORM_CORE Amendment 01 §5.4). Usage:
//   Ops create-tenant --name <name> --owner-email <address>
// Its configuration is the environment alone: ConnectionStrings__provisioner (and, for the commands that list
// tenants, ConnectionStrings__job_runner — none yet), and Ops__PublicOrigin for the invitation link.
// Exit code 0 on success, 1 on a usage or configuration error; a failure in the database throws, loudly.
// A named class, not top-level statements: the white-box tests reference Api and Ops together, and two global
// Program types would collide.
internal static class Program
{
    private static Task<int> Main(string[] args) =>
        OpsCommand.RunAsync(args, new ConfigurationBuilder().AddEnvironmentVariables().Build(), Console.Out, Console.Error);
}
