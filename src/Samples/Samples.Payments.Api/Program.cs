using ConfigManager.OpenFeature;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddConfigManagerOpenFeature(builder.Configuration.GetSection("ConfigManager"));

var app = builder.Build();

app.MapGet("/payment-options", async (IFeatureClient flags, string? country, string? userId) =>
{
    var e = await EvaluateAsync(flags, userId, country);
    return new
    {
        pixEnabled = e.Pix.Value,
        feePercentage = e.Fee.Value,
        providers = e.Providers.Value.ToPlainObject(),
        details = e.Describe(),
    };
});

app.MapGet("/flags", async (IFeatureClient flags, string? country, string? userId) =>
{
    var e = await EvaluateAsync(flags, userId, country);
    return new
    {
        provider = new { name = ConfigManagerProvider.ProviderName, status = flags.ProviderStatus.ToString() },
        flags = e.Describe(),
    };
});

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (IFeatureClient flags) =>
    flags.ProviderStatus == ProviderStatus.Ready
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = flags.ProviderStatus.ToString() }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

static async Task<Evaluations> EvaluateAsync(IFeatureClient flags, string? userId, string? country)
{
    var builder = EvaluationContext.Builder();
    if (!string.IsNullOrWhiteSpace(userId)) builder.SetTargetingKey(userId);
    if (!string.IsNullOrWhiteSpace(country)) builder.Set("country", country);
    var context = builder.Build();

    return new Evaluations(
        await flags.GetBooleanDetailsAsync("pix-enabled", false, context),
        await flags.GetDoubleDetailsAsync("fee-percentage", 0d, context),
        await flags.GetObjectDetailsAsync("payment-providers", new Value(), context));
}

internal sealed record Evaluations(
    FlagEvaluationDetails<bool> Pix,
    FlagEvaluationDetails<double> Fee,
    FlagEvaluationDetails<Value> Providers)
{
    public object[] Describe() =>
    [
        Entry(Pix.FlagKey, Pix.Value, Pix.Variant, Pix.Reason, Pix.FlagMetadata),
        Entry(Fee.FlagKey, Fee.Value, Fee.Variant, Fee.Reason, Fee.FlagMetadata),
        Entry(Providers.FlagKey, Providers.Value.ToPlainObject(), Providers.Variant, Providers.Reason, Providers.FlagMetadata),
    ];

    private static object Entry(string flag, object? value, string? variant, string? reason, ImmutableMetadata? metadata) =>
        new { flag, value, variant, reason, version = metadata?.GetInt("version") };
}
