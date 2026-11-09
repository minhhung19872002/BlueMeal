namespace BlueMeal.Infrastructure.Persistence;

/// <summary>
/// Seeds clearly-labelled SAMPLE data (ASSUMPTIONS A-14): ~80 foods, 40 ingredients, 30 dishes, 3 recommendations.
/// Filled in Phase P2/P3.
/// </summary>
public sealed class SampleDataSeeder(AppDbContext db)
{
    public Task SeedAsync(CancellationToken ct = default) => Task.CompletedTask;
}
