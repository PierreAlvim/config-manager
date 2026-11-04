using ConfigManager.OpenFeature;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddConfigManagerOpenFeature(builder.Configuration.GetSection("ConfigManager"));

var app = builder.Build();

app.MapGet("/checkout", async (IFeatureClient flags, string? tenant, string? userId) =>
{
    var context = BuildContext(userId, tenant);
    var newCheckout = await flags.GetBooleanDetailsAsync("new-checkout", false, context);
    var maxItems = await flags.GetIntegerDetailsAsync("max-items-per-order", 5, context);
    var banner = await flags.GetStringDetailsAsync("checkout-banner", "", context);

    return new
    {
        newCheckout = newCheckout.Value,
        maxItemsPerOrder = maxItems.Value,
        banner = banner.Value,
        details = new[]
        {
            Describe("new-checkout", newCheckout.Value, newCheckout.Variant, newCheckout.Reason, newCheckout.FlagMetadata),
            Describe("max-items-per-order", maxItems.Value, maxItems.Variant, maxItems.Reason, maxItems.FlagMetadata),
            Describe("checkout-banner", banner.Value, banner.Variant, banner.Reason, banner.FlagMetadata),
        },
    };
});

app.MapGet("/flags", async (IFeatureClient flags, string? tenant, string? userId) =>
{
    var context = BuildContext(userId, tenant);
    var newCheckout = await flags.GetBooleanDetailsAsync("new-checkout", false, context);
    var maxItems = await flags.GetIntegerDetailsAsync("max-items-per-order", 5, context);
    var banner = await flags.GetStringDetailsAsync("checkout-banner", "", context);

    return new
    {
        provider = new { name = ConfigManagerProvider.ProviderName, status = flags.ProviderStatus.ToString() },
        flags = new[]
        {
            Describe("new-checkout", newCheckout.Value, newCheckout.Variant, newCheckout.Reason, newCheckout.FlagMetadata),
            Describe("max-items-per-order", maxItems.Value, maxItems.Variant, maxItems.Reason, maxItems.FlagMetadata),
            Describe("checkout-banner", banner.Value, banner.Variant, banner.Reason, banner.FlagMetadata),
        },
    };
});

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", (IFeatureClient flags) =>
    flags.ProviderStatus == ProviderStatus.Ready
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = flags.ProviderStatus.ToString() }, statusCode: StatusCodes.Status503ServiceUnavailable));

app.Run();

static EvaluationContext BuildContext(string? userId, string? tenant)
{
    var builder = EvaluationContext.Builder();
    if (!string.IsNullOrWhiteSpace(userId)) builder.SetTargetingKey(userId);
    if (!string.IsNullOrWhiteSpace(tenant)) builder.Set("tenant", tenant);
    return builder.Build();
}

static object Describe(string flag, object? value, string? variant, string? reason, ImmutableMetadata? metadata) =>
    new { flag, value, variant, reason, version = metadata?.GetInt("version") };
