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

    public ChecklistTask? ChecklistTask { get; set; }
}
