using EfCoreTesting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddTransient<AuditableEntityInterceptor>();

services.AddDbContext<AuditableEntityInterceptorContext>((serviceProvider, options) =>
{
    var interceptor = serviceProvider.GetService<AuditableEntityInterceptor>();

    options.UseNpgsql("Host=localhost;Database=testdb;Username=postgres;Password=admin")
        .AddInterceptors(interceptor);
});

var serviceProvider = services.BuildServiceProvider();

var ctx = serviceProvider.GetRequiredService<AuditableEntityInterceptorContext>();

await ctx.Database.EnsureDeletedAsync();
await ctx.Database.EnsureCreatedAsync();

var testEntity = new TestEntity
{
    Name = "test",
};

ctx.TestEntities.Add(testEntity);

await ctx.SaveChangesAsync();

var trans = await ctx.Database.BeginTransactionAsync();

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

await trans.CommitAsync();

var auditEntries = ctx.AuditEntries.ToList();