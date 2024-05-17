using System.Text.Json;
using EfCoreTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private bool _transactionTriggeredInInterceptor = false;
    private bool _auditAlreadySaved = false;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;

        if (dbContext is null)
        {
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var entries = dbContext.ChangeTracker
            .Entries<IAuditableEntity>()
            .Where(x => x.State == EntityState.Modified || x.State == EntityState.Added)
            .Select(x =>
                new EntityEntryWrapper
                {
                    Entry = x
                })
            .ToList();

        var auditData = GetAuditData(entries)
            .ToList();

        var transactionExists = dbContext.Database.CurrentTransaction is not null;

        if (transactionExists)
        {
            if (!_transactionTriggeredInInterceptor && !_auditAlreadySaved)
            {

                _auditAlreadySaved = true;
                await dbContext.SaveChangesAsync(cancellationToken);
                AddAuditEntriesToContext(auditData, entries, dbContext);
            }

            _transactionTriggeredInInterceptor = false;
            _auditAlreadySaved = false;

            return await base.SavingChangesAsync(eventData, result, cancellationToken); ;
        }

        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _transactionTriggeredInInterceptor = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAuditEntriesToContext(auditData, entries, dbContext);
        var response = await base.SavingChangesAsync(eventData, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }

    private void AddAuditEntriesToContext(List<AuditData> auditData, List<EntityEntryWrapper> entryWrappers, DbContext dbContext)
    {
        if (!auditData.Any())
        {
            return;
        }

        var dbContextId = dbContext.ContextId.ToString();
        var auditEntries = GetAuditEntries(entryWrappers, auditData, dbContextId);

        dbContext.Set<AuditEntry>().AddRange(auditEntries);
    }

    private IEnumerable<AuditData> GetAuditData(List<EntityEntryWrapper> entryWrappers)
    {
        foreach (var entryWrapper in entryWrappers)
        {
            var isAdded = entryWrapper.Entry.State == EntityState.Added;

            var modifiedProperties = entryWrapper.Entry.Properties
                .Where(x => !x.Metadata.ClrType.IsClass || x.Metadata.ClrType == typeof(string))
                .ToList();

            // use WhereIf instead
            if (!isAdded)
            {
                modifiedProperties = modifiedProperties
                    .Where(x => x.IsModified)
                    .ToList();
            }

            var oldProperties = modifiedProperties.ToDictionary(x => x.Metadata.Name, x => x.OriginalValue)!;
            var oldSerializedProperties = JsonSerializer.Serialize(oldProperties);

            yield return new AuditData
            {
                EntityType = entryWrapper.Entry.Metadata.Name,
                EntityKey = entryWrapper.Key,
                OldSerializedProperties = oldSerializedProperties,
                ModifiedProperties = modifiedProperties
                    .Select(x => x.Metadata.Name)
                    .ToList()
            };
        }
    }

    private IEnumerable<AuditEntry> GetAuditEntries(List<EntityEntryWrapper> entryWrappers, List<AuditData> auditsData, string saveChangesKey)
    {
        foreach (var auditData in auditsData)
        {
            var entryWrapper = entryWrappers
                .Where(x => x.Entry.Metadata.Name == auditData.EntityType)
                .Where(x => x.Key == auditData.EntityKey)
                .First();

            var modifiedProperties = entryWrapper.Entry.Properties
                .Where(x => !x.Metadata.ClrType.IsClass || x.Metadata.ClrType == typeof(string))
                .Where(x => auditData.ModifiedProperties.Contains(x.Metadata.Name));

            var newProperties = modifiedProperties.ToDictionary(x => x.Metadata.Name, x => x.CurrentValue)!;
            var newSerializedProperties = JsonSerializer.Serialize(newProperties);

            var idObject = entryWrapper.Entry.Properties
                .Where(x => x.Metadata.Name == nameof(TestEntity.Id))
                .Select(x => x.OriginalValue)
                .FirstOrDefault()!;

            // just casting to long/int won't work since it can be either one of those
            var entityId = Convert.ToInt64(idObject);

            yield return new AuditEntry
            {
                EntityType = auditData.EntityType,
                EntityId = entityId,
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

    public Guid EntityKey { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string OldSerializedProperties { get; set; } = string.Empty;
}

public class EntityEntryWrapper
{
    public EntityEntry<IAuditableEntity> Entry { get; set; } = null!;

    public Guid Key { get; set; } = Guid.NewGuid();
}