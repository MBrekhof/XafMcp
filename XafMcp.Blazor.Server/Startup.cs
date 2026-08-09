using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;
using XafMcp.Blazor.Server.Services;
using XafMcp.Module.BusinessObjects;

namespace XafMcp.Blazor.Server;

public class Startup {
    public Startup(IConfiguration configuration) {
        Configuration = configuration;
    }
    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.HubConnectionHandler<>), typeof(ProxyHubConnectionHandler<>));
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddScoped<CircuitHandler, CircuitHandlerProxy>();
        services.AddXaf(Configuration, builder => {
            builder.UseApplication<XafMcpBlazorApplication>();
            builder.Modules
                .AddValidation(options => {
                    options.AllowValidationDetailsAccess = false;
                })
                .Add<XafMcp.Module.XafMcpModule>()
                .Add<XafMcpBlazorModule>();
            builder.ObjectSpaceProviders
                .AddSecuredEFCore(options => {
                    options.PreFetchReferenceProperties();
                })
                .WithDbContext<XafMcpEFCoreDbContext>((serviceProvider, options) => {
                    string? connectionString = Configuration.GetConnectionString("ConnectionString");
                    ArgumentNullException.ThrowIfNull(connectionString);
                    options.UseConnectionString(connectionString);
                })
                .AddNonPersistent();
            builder.Security
                .UseIntegratedMode(options => {
                    // ponytail: Lockout.Enabled requires ISecurityUserLockout on the user type (dxdocs:
                    // ISecurityUserLockout Interface). PermissionPolicyUser doesn't implement it and this
                    // task only wires the base security types — enable lockout if/when a custom
                    // ApplicationUser : PermissionPolicyUser, ISecurityUserLockout is introduced.
                    options.Lockout.Enabled = false;
                    options.RoleType = typeof(PermissionPolicyRole);
                    options.UserType = typeof(PermissionPolicyUser);
                    options.Events.OnSecurityStrategyCreated += securityStrategy => {
                        ((SecurityStrategy)securityStrategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                    };
                })
                .AddPasswordAuthentication(options => {
                    options.IsSupportChangePassword = true;
                });
        });
        var authentication = services.AddAuthentication(options => {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });
        authentication.AddCookie(options => {
            options.LoginPath = "/LoginPage";
        });
        services.AddScoped<Mcp.McpSecurityContext>();
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<Mcp.ServerInfoTools>()
            .WithTools<Mcp.DataTools>()
            .WithTools<Mcp.SecurityTools>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
        if (env.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        }
        else {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }
        // ponytail: no UseHttpsRedirection - MCP clients POST to http://localhost:5210/mcp and won't follow a 307
        app.UseRequestLocalization();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseXaf();
        app.UseEndpoints(endpoints => {
            endpoints.MapXafEndpoints();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
            endpoints.MapMcp("/mcp");
        });
    }
}

