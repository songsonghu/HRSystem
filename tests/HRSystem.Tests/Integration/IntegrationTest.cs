using System.Net;
using System.Text.RegularExpressions;
using HRSystem.Tests.Infrastructure;

namespace HRSystem.Tests.Integration;

/// <summary>
/// Tests against a real SQL Server database. They are skipped unless
/// <see cref="TestApp.ConnectionVariable"/> points at a server where the login may create databases.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTest
{
    protected IntegrationTest(TestApp app)
    {
        App = app;
        Data = new TestData(app);
    }

    protected TestApp App { get; }
    protected TestData Data { get; }

    protected static void RequireDatabase()
        => Skip.IfNot(TestApp.IsConfigured, $"Set {TestApp.ConnectionVariable} to a SQL Server connection string to run integration tests.");

    protected async Task<HttpClient> SignedInClientAsync(string email, string password)
    {
        var client = App.CreateClient(new() { AllowAutoRedirect = false });
        var token = AntiForgeryToken(await client.GetStringAsync("/Identity/Account/Login"));
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static string AntiForgeryToken(string html)
        => Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
}
