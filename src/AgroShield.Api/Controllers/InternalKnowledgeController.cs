using AgroShield.Api.Filters;
using AgroShield.Domain.Entities;
using AgroShield.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AgroShield.Api.Controllers;

[ApiController]
[Route("api/internal/knowledge")]
[InternalApiKey]
public class InternalKnowledgeController(
    AppDbContext db,
    IHttpClientFactory factory,
    ILogger<InternalKnowledgeController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SnakeOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] KnowledgeSearchRequest request, CancellationToken ct)
    {
        var topK = Math.Clamp(request.TopK, 1, 20);

        // Try to get embedding from ai-assistant service
        float[]? queryEmbedding = null;
        try
        {
            var aiClient = factory.CreateClient("AiAssistant");
            var embedResp = await aiClient.PostAsJsonAsync("/embed", new { text = request.Query }, SnakeOpts, ct);
            if (embedResp.IsSuccessStatusCode)
            {
                var embedResult = await embedResp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
                if (embedResult.TryGetProperty("embedding", out var emb))
                    queryEmbedding = emb.EnumerateArray().Select(v => v.GetSingle()).ToArray();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding service unavailable, falling back to text search");
        }

        List<KnowledgeChunk> results;

        if (queryEmbedding is not null)
        {
            // Cosine similarity in application layer (pgvector fallback)
            var chunks = await db.KnowledgeChunks.ToListAsync(ct);
            results = chunks
                .Where(c => c.EmbeddingJson != null)
                .Select(c => new
                {
                    Chunk = c,
                    Score = CosineSimilarity(queryEmbedding, ParseEmbedding(c.EmbeddingJson!))
                })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .Select(x => x.Chunk)
                .ToList();
        }
        else
        {
            // pg_trgm fallback: text search
            var queryLower = request.Query.ToLowerInvariant();
            results = await db.KnowledgeChunks
                .Where(c => EF.Functions.ILike(c.Content, $"%{queryLower}%"))
                .Take(topK)
                .ToListAsync(ct);
        }

        return Ok(results.Select(c => new
        {
            c.Id,
            c.SourceDoc,
            c.SourceUrl,
            c.ChunkIndex,
            c.Content,
            c.CreatedAt,
        }));
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request, CancellationToken ct)
    {
        // try to get embeddings for all chunks
        foreach (var chunk in request.Chunks)
        {
            string? embeddingJson = null;
            try
            {
                var aiClient = factory.CreateClient("AiAssistant");
                var embedResp = await aiClient.PostAsJsonAsync("/embed", new { text = chunk }, SnakeOpts, ct);
                if (embedResp.IsSuccessStatusCode)
                {
                    var embedResult = await embedResp.Content.ReadFromJsonAsync<JsonElement>(SnakeOpts, ct);
                    if (embedResult.TryGetProperty("embedding", out var emb))
                        embeddingJson = emb.GetRawText();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embedding service unavailable for chunk ingestion");
            }

            db.KnowledgeChunks.Add(new KnowledgeChunk
            {
                Id = Guid.NewGuid(),
                SourceDoc = request.SourceDoc,
                SourceUrl = request.SourceUrl,
                ChunkIndex = request.Chunks.ToList().IndexOf(chunk),
                Content = chunk,
                EmbeddingJson = embeddingJson,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { ingested = request.Chunks.Length });
    }

    private static float[] ParseEmbedding(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<float[]>(json) ?? [];
        }
        catch { return []; }
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        var dot = a.Zip(b, (x, y) => x * y).Sum();
        var normA = MathF.Sqrt(a.Sum(x => x * x));
        var normB = MathF.Sqrt(b.Sum(x => x * x));
        return normA == 0 || normB == 0 ? 0 : dot / (normA * normB);
    }
}

public class KnowledgeSearchRequest
{
    public string Query { get; set; } = null!;
    public int TopK { get; set; } = 5;
}

public class IngestRequest
{
    public string SourceDoc { get; set; } = null!;
    public string? SourceUrl { get; set; }
    public string[] Chunks { get; set; } = [];
}
