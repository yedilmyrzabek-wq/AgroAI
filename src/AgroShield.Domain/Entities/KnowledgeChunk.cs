namespace AgroShield.Domain.Entities;

public class KnowledgeChunk
{
    public Guid Id { get; set; }
    public string SourceDoc { get; set; } = null!;
    public string? SourceUrl { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = null!;
    public string? EmbeddingJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
