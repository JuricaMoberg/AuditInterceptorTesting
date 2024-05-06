namespace EfCoreTesting;

using Microsoft.EntityFrameworkCore;

public class AuditableEntityInterceptorContext : DbContext
{
    public AuditableEntityInterceptorContext(DbContextOptions<AuditableEntityInterceptorContext> options)
        : base(options)
    {

    }

    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    public DbSet<TestEntityLong> TestEntityLongs => Set<TestEntityLong>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<OptionalChildTestEntity> OptionalChildTestEntities => Set<OptionalChildTestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}

public class AuditEntry
{
    public int Id { get; set; }

    /// <summary>
    /// Entity type
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Entity id
    /// </summary>
    public long EntityId { get; set; }

    /// <summary>
    /// Old serialized object properties.
    /// Navigation properties are not serialized.
    /// </summary>
    public string OldSerializedProperties { get; set; } = string.Empty;

    /// <summary>
    /// Old serialized object properties.
    /// Navigation properties are not serialized.
    /// </summary>
    public string NewSerializedProperties { get; set; } = string.Empty;

    /// <summary>
    /// Entities with same SaveChangesKey were save in same SaveChanges action
    /// </summary>
    public string SaveChangesKey { get; set; } = string.Empty;
}


public class TestEntity : IAuditableEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime? UpdateTime { get; set; }

    public int? OptionalChildTestEntityId { get; set; }

    public OptionalChildTestEntity? OptionalChildTestEntity { get; set; }
}

public class TestEntityLong : IAuditableEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime? UpdateTime { get; set; }
}

public class OptionalChildTestEntity
{
    public int Id { get; set; }

    public List<TestEntity> TestEntities { get; set; } = new();
}