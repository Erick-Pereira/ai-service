using Simcag.AIService.Application.Configuration;
using Simcag.Shared.Events;
using Xunit;

namespace Simcag.AIService.Tests;

public sealed class DataIngestedEventIdempotencyKeysTests
{
    [Fact]
    public void Build_prefers_document_id_over_file_hash()
    {
        var docId = Guid.NewGuid();
        var e = new DataIngestedEvent
        {
            DocumentId = docId,
            FileHash = "same-hash-for-both",
            TenantId = Guid.NewGuid(),
        };

        var key = DataIngestedEventIdempotencyKeys.Build(e);

        Assert.Equal($"ai-service:ingested-doc:{docId}", key);
    }

    [Fact]
    public void Build_same_hash_different_documents_produce_distinct_keys()
    {
        var hash = "abc123";
        var tenant = Guid.NewGuid();
        var a = new DataIngestedEvent { DocumentId = Guid.NewGuid(), FileHash = hash, TenantId = tenant };
        var b = new DataIngestedEvent { DocumentId = Guid.NewGuid(), FileHash = hash, TenantId = tenant };

        Assert.NotEqual(DataIngestedEventIdempotencyKeys.Build(a), DataIngestedEventIdempotencyKeys.Build(b));
    }
}
