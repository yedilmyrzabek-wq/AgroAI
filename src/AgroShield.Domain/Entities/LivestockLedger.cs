namespace AgroShield.Domain.Entities;

public class LivestockLedger
{
    public Guid Id { get; set; }
    public Guid FarmId { get; set; }
    public string LivestockType { get; set; } = null!;
    public int Count { get; set; }
    public string PrevHash { get; set; } = null!;
    public string EntryHash { get; set; } = null!;
    public string Source { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public Farm Farm { get; set; } = null!;
}
