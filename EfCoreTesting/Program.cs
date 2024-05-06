using EfCoreTesting;

var ctx = new AuditableEntityInterceptorContext();

await ctx.Database.EnsureDeletedAsync();
await ctx.Database.EnsureCreatedAsync();

var testEntity = new TestEntity
{
    Name = "test",
};

ctx.TestEntities.Add(testEntity);

await ctx.SaveChangesAsync();

//var trans = await ctx.Database.BeginTransactionAsync();

var optionalEntity = new OptionalChildTestEntity
{
    TestEntities = new List<TestEntity>
    {
        testEntity
    }
};

ctx.OptionalChildTestEntities.Add(optionalEntity);

await ctx.SaveChangesAsync();

testEntity.Name = "novo ime";

await ctx.SaveChangesAsync();


//await trans.CommitAsync();

var auditEntries = ctx.AuditEntries.ToList();