using System.Text.Json;
using HRSystem.Application.Common;
using HRSystem.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRSystem.Infrastructure.Services;

/// <summary>Settings of the AeCodeListSearch API (section "AeCodeApi").</summary>
public class AeCodeApiOptions
{
    /// <summary>The API host, e.g. "http://uhks0134:8066/".</summary>
    public string BaseUrl { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Calls <c>POST api/Internal/AeCodeListSearch?AeCode=..&amp;AeName=..</c> and reads the accounts from
/// <c>rawResponse.Result</c>: UserID is the AE code, Name the AE name, and Active "Yes" or "No".
/// </summary>
public class AeCodeDirectory : IAeCodeDirectory
{
    private const string SearchPath = "api/Internal/AeCodeListSearch";
    private const string Unavailable = "The AE code search is not available right now. Try again later, or enter the AE codes by hand.";

    private readonly HttpClient _http;
    private readonly ILogger<AeCodeDirectory> _logger;

    public AeCodeDirectory(HttpClient http, ILogger<AeCodeDirectory> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<AeAccountDto>>> SearchAsync(string? aeCode, string? aeName, CancellationToken ct = default)
    {
        if (_http.BaseAddress is null)
            return Result<IReadOnlyList<AeAccountDto>>.Fail("The AE code search is not set up (AeCodeApi:BaseUrl). Enter the AE codes by hand.");

        var url = $"{SearchPath}?AeCode={Uri.EscapeDataString(aeCode?.Trim() ?? string.Empty)}&AeName={Uri.EscapeDataString(aeName?.Trim() ?? string.Empty)}";
        try
        {
            // The API takes POST, with the search in the query string and no body.
            using var response = await _http.PostAsync(url, content: null, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AeCodeListSearch returned {Status}", (int)response.StatusCode);
                return Result<IReadOnlyList<AeAccountDto>>.Fail(Unavailable);
            }
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return Parse(json.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "AeCodeListSearch failed");
            return Result<IReadOnlyList<AeAccountDto>>.Fail(Unavailable);
        }
    }

    /// <summary>Reads the API's reply; public so the parsing can be tested without the API.</summary>
    public static Result<IReadOnlyList<AeAccountDto>> Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Result<IReadOnlyList<AeAccountDto>>.Fail(Unavailable);
        if (Property(root, "success") is { ValueKind: JsonValueKind.False })
        {
            var message = Property(root, "message") is { ValueKind: JsonValueKind.String } m ? m.GetString() : null;
            return Result<IReadOnlyList<AeAccountDto>>.Fail(string.IsNullOrWhiteSpace(message) ? Unavailable : $"AE code search failed: {message}");
        }

        var accounts = new List<AeAccountDto>();
        if (Property(root, "rawResponse") is { ValueKind: JsonValueKind.Object } raw
            && Property(raw, "Result") is { ValueKind: JsonValueKind.Array } result)
        {
            foreach (var row in result.EnumerateArray())
            {
                var code = Text(row, "UserID");
                if (string.IsNullOrEmpty(code)) continue;
                var active = Text(row, "Active");
                accounts.Add(new AeAccountDto(code, Text(row, "Name"), string.Equals(active, "Yes", StringComparison.OrdinalIgnoreCase)));
            }
        }
        return Result<IReadOnlyList<AeAccountDto>>.Success(accounts);
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return null;
    }

    private static string Text(JsonElement element, string name) => Property(element, name) switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString()?.Trim() ?? string.Empty,
        { ValueKind: JsonValueKind.Number } n => n.GetRawText(),
        _ => string.Empty
    };
}
