using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace XafMcp.E2E;

[TestFixture]
public class LoginSmokeTests : PageTest {
    const string BaseUrl = "http://localhost:5210";

    static async Task<bool> AppIsUp() {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try { return (await http.GetAsync($"{BaseUrl}/LoginPage")).IsSuccessStatusCode; }
        catch { return false; }
    }

    [Test]
    public async Task Admin_can_log_in_and_sees_navigation() {
        if (!await AppIsUp()) Assert.Ignore("App not running — start it with scripts/run-app.ps1 first.");
        await Page.GotoAsync($"{BaseUrl}/LoginPage", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        // XAF Blazor login: role-based locators (CSS alternation can hit the page's hidden Enter-key submit).
        var userName = Page.GetByRole(AriaRole.Textbox, new() { Name = "User Name" });
        await userName.WaitForAsync(new() { Timeout = 15000 });
        await userName.FillAsync("Admin"); // password stays empty for the dev Admin
        await Page.GetByRole(AriaRole.Button, new() { Name = "Log In" }).ClickAsync();
        // Landed in the app: navigation shows a domain item (XAF captions nav items singular: "Customer").
        await Expect(Page.GetByRole(AriaRole.Treeitem, new() { Name = "Customer", Exact = true })).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Page.ScreenshotAsync(new() { Path = "login-smoke.png", FullPage = true });
    }
}
