using System.Text.Json;
using EfCoreTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;

        if (dbContext is null)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var entries = dbContext.ChangeTracker
            .Entries<IAuditableEntity>()
            .Where(x => x.State == EntityState.Modified)
            .ToList();

        var auditData = GetAuditData(entries)
            .ToList();

        var transactionExists = dbContext.Database.CurrentTransaction is not null;

        if (transactionExists)
        {
            // not sure how to know when we created transaction in interceptor
            // or we reuse transaction from the code
            //if(!interceptorTransaction)
            //{
            // await dbContext.SaveChangesAsync(cancellationToken);
            // AddAuditEntriesToContext(auditData, dbContext);
            //}

            return await base.SavingChangesAsync(eventData, result, cancellationToken); ;
        }

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAuditEntriesToContext(auditData, dbContext);
        var response = await base.SavingChangesAsync(eventData, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var dbContext = eventData.Context;

        if (dbContext is null)
        {
            return base.SavingChanges(eventData, result);
        }

        var entries = dbContext.ChangeTracker
            .Entries<IAuditableEntity>()
            .Where(x => x.State == EntityState.Modified)
            .ToList();

        var auditData = GetAuditData(entries)
            .ToList();

        if (true)
        {

            dbContext.SaveChanges();
            AddAuditEntriesToContext(auditData, dbContext);

            return base.SavingChanges(eventData, result);
        }

        if (true)
        {

            AddAuditEntriesToContext(auditData, dbContext);
        }

        return base.SavingChanges(eventData, result);
    }



    private void AddAuditEntriesToContext(List<AuditData> auditData, DbContext dbContext)
    {
        if (!auditData.Any())
        {
            return;
        }

        var dbContextId = dbContext.ContextId.ToString();
        var entries = dbContext.ChangeTracker
            .Entries<IAuditableEntity>()
            .ToList();

        var auditEntries = GetAuditEntries(entries, auditData, dbContextId);

        dbContext.Set<AuditEntry>().AddRange(auditEntries);
    }

    private IEnumerable<AuditData> GetAuditData(IEnumerable<EntityEntry<IAuditableEntity>> entries)
    {

        foreach (var entry in entries)
        {
            var modifiedProperties = entry.Properties
                .Where(x => !x.Metadata.ClrType.IsClass || x.Metadata.ClrType == typeof(string))
                .Where(x => x.IsModified)
                .ToList();

            var tempPropertyChanged = modifiedProperties
                .Where(x => x.IsTemporary)
                .Any();

            var oldProperties = modifiedProperties.ToDictionary(x => x.Metadata.Name, x => x.OriginalValue)!;

            var oldSerializedProperties = JsonSerializer.Serialize(oldProperties);

            var idObject = entry.Properties
                 .Where(x => x.Metadata.Name == nameof(TestEntity.Id))
                 .Select(x => x.OriginalValue)
                 .FirstOrDefault()!;

            // just casting to long/int won't work since it can be either one of those
            var entityId = Convert.ToInt64(idObject);

            yield return new AuditData
            {
                EntityType = entry.Metadata.Name,
                EntityId = entityId,
                OldSerializedProperties = oldSerializedProperties,
                ModifiedProperties = modifiedProperties
                    .Select(x => x.Metadata.Name)
                    .ToList()
            };
        }
    }

    private IEnumerable<AuditEntry> GetAuditEntries(List<EntityEntry<IAuditableEntity>> entries, List<AuditData> auditsData, string saveChangesKey)
    {
        foreach (var auditData in auditsData)
        {
            var entry = entries
                .Where(x => x.Metadata.Name == auditData.EntityType)
                .Where(x => Convert.ToInt64(x.Properties
                    .Where(x => x.Metadata.Name == nameof(TestEntity.Id))
                    .Select(x => x.OriginalValue)
                    .First()) == auditData.EntityId)
                .First();

            var modifiedProperties = entry.Properties
                .Where(x => !x.Metadata.ClrType.IsClass || x.Metadata.ClrType == typeof(string))
                .Where(x => auditData.ModifiedProperties.Contains(x.Metadata.Name));

            var newProperties = modifiedProperties.ToDictionary(x => x.Metadata.Name, x => x.CurrentValue)!;
            var newSerializedProperties = JsonSerializer.Serialize(newProperties);

            yield return new AuditEntry
            {
                EntityType = auditData.EntityType,
                EntityId = auditData.EntityId,
                OldSerializedProperties = auditData.OldSerializedProperties,
                NewSerializedProperties = newSerializedProperties,
                SaveChangesKey = saveChangesKey
            };
        }
    }
}

public class AuditData
{
    public List<string> ModifiedProperties { get; set; } = new();

    public long EntityId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string OldSerializedProperties { get; set; } = string.Empty;
}