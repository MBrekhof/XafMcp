using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using ModelContextProtocol;

namespace XafMcp.Blazor.Server.Mcp;

/// <summary>
/// Scoped per MCP request. Authenticates the fixed McpAgent service user on the scope's
/// security strategy, then hands out SECURED object spaces. Every data-touching tool goes
/// through here — no non-secured access anywhere in the MCP path (spec §4).
/// </summary>
public sealed class McpSecurityContext(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<McpSecurityContext> logger) {
    public const string UserName = "McpAgent";
    public const string RoleName = "McpReadOnly";

    bool authenticated;

    public IObjectSpace CreateObjectSpace(Type type) {
        EnsureAuthenticated();
        return serviceProvider.GetRequiredService<IObjectSpaceFactory>().CreateObjectSpace(type);
    }

    void EnsureAuthenticated() {
        if (authenticated) return;
        var securityStrategy = serviceProvider.GetRequiredService<ISecurityStrategyBase>();
        if (securityStrategy.IsAuthenticated) { authenticated = true; return; }
        if (securityStrategy is not SecurityStrategyBase strategyBase) {
            throw new McpException($"Unexpected security strategy '{securityStrategy.GetType().FullName}'.");
        }
        var nonSecuredFactory = serviceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var verifySpace = nonSecuredFactory.CreateNonSecuredObjectSpace<PermissionPolicyUser>();
        var userManager = serviceProvider.GetRequiredService<UserManager>();
        var user = userManager.FindUserByName<PermissionPolicyUser>(verifySpace, UserName);
        if (user is null) {
            throw new McpException($"Service user '{UserName}' not found — run the database updater (dotnet run -- --updateDatabase).");
        }
        var password = configuration["McpAgent:Password"] ?? string.Empty;
        var logonParams = new AuthenticationStandardLogonParameters(UserName, password);
        if (strategyBase is SecurityStrategy concreteStrategy) {
            concreteStrategy.Authentication.SetLogonParameters(logonParams);
        }
        using var logonSpace = nonSecuredFactory.CreateNonSecuredObjectSpace<PermissionPolicyUser>();
        strategyBase.Logon(logonSpace);
        logger.LogDebug("MCP scope authenticated as '{User}'", UserName);
        authenticated = true;
    }
}
