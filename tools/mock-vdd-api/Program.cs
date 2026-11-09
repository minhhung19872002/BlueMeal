namespace MockVddApi;

/// <summary>Entry point (no top-level statements so test projects can reference this assembly next to BlueMeal.Api).</summary>
public static class Entry
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        MockServer.Configure(builder.Services, builder.Configuration);
        var app = builder.Build();
        MockServer.Map(app);
        app.Run();
    }
}
