using Microsoft.EntityFrameworkCore;

namespace PersistenceService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DocumentResult> DocumentResults => Set<DocumentResult>();
    public DbSet<FailedDocumentResult> FailedDocumentResults => Set<FailedDocumentResult>();
}
