using System.Text.Json;
using HRSystem.Application.DTOs;
using HRSystem.Application.Interfaces;
using HRSystem.Infrastructure.Services;

namespace HRSystem.Tests.Unit;

public class AeCodeDirectoryTests
{
    [Fact]
    public void Accounts_are_read_from_the_raw_response()
    {
        const string reply = """
            {
              "success": true,
              "message": "AeCode List Search completed",
              "aeCode": "",
              "aeName": "MICHELLE",
              "rawResponse": {
                "Result": [
                  { "UserID": "2205", "Name": "MICHELLE LI / NIKITA LAI", "Password": "", "Active": "Yes", "AEPool": "Yes" },
                  { "UserID": "2163", "Name": "SIMON SAPUTRA / MICHELLE LI XUE YING", "Active": "No " },
                  { "UserID": "", "Name": "no code" }
                ],
                "Id": 6,
                "Exception": null,
                "Status": 5,
                "IsCompletedSuccessfully": true
              },
              "timestamp": "2026-11-12T11:41:50.6309116+08:00"
            }
            """;
        using var json = JsonDocument.Parse(reply);

        var result = AeCodeDirectory.Parse(json.RootElement);

        Assert.True(result.Succeeded);
        Assert.Equal(new[]
        {
            new AeAccountDto("2205", "MICHELLE LI / NIKITA LAI", true),
            new AeAccountDto("2163", "SIMON SAPUTRA / MICHELLE LI XUE YING", false)
        }, result.Value);
    }

    [Fact]
    public void A_failed_search_reports_the_api_message()
    {
        using var json = JsonDocument.Parse("""{ "success": false, "message": "AeName too short", "rawResponse": null }""");

        var result = AeCodeDirectory.Parse(json.RootElement);

        Assert.False(result.Succeeded);
        Assert.Contains("AeName too short", result.Error);
    }

    [Fact]
    public void No_result_is_an_empty_list()
    {
        using var json = JsonDocument.Parse("""{ "success": true, "rawResponse": { "Result": [] } }""");

        Assert.Empty(AeCodeDirectory.Parse(json.RootElement).Value!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_api_is_called_through_the_proxy_only_when_configured(bool useProxy)
    {
        using var handler = AeCodeDirectory.CreateHandler(new AeCodeApiOptions { UseProxy = useProxy });

        Assert.Equal(useProxy, Assert.IsType<SocketsHttpHandler>(handler).UseProxy);
    }

    [Fact]
    public void The_proxy_is_used_unless_turned_off()
    {
        Assert.True(new AeCodeApiOptions().UseProxy);
    }

    [Fact]
    public void Linked_accounts_are_numbered_under_their_ae_code()
    {
        var lines = ChecklistAeCodeDto.Numbered(new[]
        {
            new ChecklistAeCodeDto { AeCode = "2205" },
            new ChecklistAeCodeDto { AeCode = "8001", ParentAeCode = "2205" },
            new ChecklistAeCodeDto { AeCode = "8002", ParentAeCode = "2205" },
            new ChecklistAeCodeDto { AeCode = "2163" },
            new ChecklistAeCodeDto { AeCode = "8003", ParentAeCode = "2163" }
        });

        Assert.Equal(new[] { "1", "1.1", "1.2", "2", "2.1" }, lines.Select(l => l.DisplayNo));
    }
}
