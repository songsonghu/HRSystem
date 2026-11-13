namespace HRSystem.Domain.Entities;

/// <summary>One AE code of the leaver, recorded on the departure's AE Code task. An employee may hold several.</summary>
public class ChecklistTaskAeCode
{
    public const int MaxCodeLength = 50;
    public const int MaxNameLength = 200;

    public int Id { get; set; }
    public int ChecklistTaskId { get; set; }

    /// <summary>Row number shown on the page, from 1.</summary>
    public int LineNo { get; set; }
    public string AeCode { get; set; } = string.Empty;
    public string AeName { get; set; } = string.Empty;

    /// <summary>
    /// Set on a linked (second-level) account: the AE code of the first-level line it belongs to, e.g. the
    /// account "LSQ CAPITAL LIMITED (2205)" under AE code 2205. Empty on a first-level line.
    /// </summary>
    public string? ParentAeCode { get; set; }

    public ChecklistTask? ChecklistTask { get; set; }
}
