﻿using System.Text.Json;
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
                AddAuditEntriesToContext(auditData, dbContext);
            }

            _transactionTriggeredInInterceptor = false;
            _auditAlreadySaved = false;

            return await base.SavingChangesAsync(eventData, result, cancellationToken); ;
        }

        using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        _transactionTriggeredInInterceptor = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        AddAuditEntriesToContext(auditData, dbContext);
        var response = await base.SavingChangesAsync(eventData, result, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return response;
    }

    private void AddAuditEntriesToContext(List<AuditData> auditData, DbContext dbContext)
    {
        if (!auditData.Any())
        {
            return;
        }

        var dbContextId = dbContext.ContextId.ToString();
        var auditEntries = GetAuditEntries(auditData, dbContextId);

        dbContext.Set<AuditEntry>().AddRange(auditEntries);
    }

    private IEnumerable<AuditData> GetAuditData(List<EntityEntry<IAuditableEntity>> entiries)
    {
        foreach (var entry in entiries)
        {
            var isAdded = entry.State == EntityState.Added;

            var modifiedProperties = entry.Properties
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
                Entry = entry,
                OldSerializedProperties = oldSerializedProperties,
                ModifiedProperties = modifiedProperties
                    .Select(x => x.Metadata.Name)
                    .ToList()
            };
        }
    }

    private IEnumerable<AuditEntry> GetAuditEntries(List<AuditData> auditsData, string saveChangesKey)
    {
        foreach (var auditData in auditsData)
        {
            var modifiedProperties = auditData.Entry.Properties
                .Where(x => !x.Metadata.ClrType.IsClass || x.Metadata.ClrType == typeof(string))
                .Where(x => auditData.ModifiedProperties.Contains(x.Metadata.Name));

            var newProperties = modifiedProperties.ToDictionary(x => x.Metadata.Name, x => x.CurrentValue)!;
            var newSerializedProperties = JsonSerializer.Serialize(newProperties);

            var idObject = auditData.Entry.Properties
                .Where(x => x.Metadata.Name == nameof(TestEntity.Id))
                .Select(x => x.OriginalValue)
                .FirstOrDefault()!;

            // just casting to long/int won't work since it can be either one of those
            var entityId = Convert.ToInt64(idObject);

            yield return new AuditEntry
            {
                EntityType = auditData.Entry.Metadata.Name,
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

    public EntityEntry<IAuditableEntity> Entry { get; set; } = null!;

    public string OldSerializedProperties { get; set; } = string.Empty;
}
