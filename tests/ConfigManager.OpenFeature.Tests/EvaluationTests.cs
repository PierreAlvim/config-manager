using System.Globalization;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace ConfigManager.OpenFeature.Tests;

public class EvaluationTests : IAsyncLifetime
{
    private readonly ConfigManagerProvider _provider;

    public EvaluationTests()
    {
        var handler = new FakeHandler(r => r.RequestUri!.ToString() == T.TokenUrl ? T.Token() : T.Json(T.SnapshotJson(7), etag: "\"7\""));
        _provider = ConfigManagerProvider.Create(T.Options(o => o.PollInterval = TimeSpan.FromHours(1)), new HttpClient(handler));
    }

    public Task InitializeAsync() => _provider.InitializeAsync(EvaluationContext.Empty);
    public Task DisposeAsync() => _provider.ShutdownAsync();

    [Fact]
    public async Task Metadata_name() => Assert.Equal("ConfigManager", _provider.GetMetadata().Name);

    [Fact]
    public async Task Boolean_static()
    {
        var r = await _provider.ResolveBooleanValueAsync("bool-static", false);
        Assert.True(r.Value);
        Assert.Equal(Reason.Static, r.Reason);
        Assert.Equal("on", r.Variant);
        Assert.Equal(ErrorType.None, r.ErrorType);
        Assert.Equal(7, r.FlagMetadata!.GetInt("version"));
        Assert.Equal("orders", r.FlagMetadata.GetString("namespace"));
        Assert.Equal("dev", r.FlagMetadata.GetString("environment"));
    }

    [Fact]
    public async Task Boolean_targeting_match_and_default()
    {
        var hit = await _provider.ResolveBooleanValueAsync("bool-rule", false, T.Ctx("u1", ("tenant", new Value("acme"))));
        Assert.True(hit.Value);
        Assert.Equal(Reason.TargetingMatch, hit.Reason);

        var miss = await _provider.ResolveBooleanValueAsync("bool-rule", true, T.Ctx("u1", ("tenant", new Value("other"))));
        Assert.False(miss.Value);
        Assert.Equal(Reason.Default, miss.Reason);

        var none = await _provider.ResolveBooleanValueAsync("bool-rule", true);
        Assert.False(none.Value);
        Assert.Equal(Reason.Default, none.Reason);
    }

    [Fact]
    public async Task Split_requires_targeting_key()
    {
        var split = await _provider.ResolveBooleanValueAsync("bool-split", false, T.Ctx("user-1"));
        Assert.True(split.Value);
        Assert.Equal(Reason.Split, split.Reason);

        var noKey = await _provider.ResolveBooleanValueAsync("bool-split", true);
        Assert.False(noKey.Value);
        Assert.Equal(Reason.Default, noKey.Reason);
    }

    [Fact]
    public async Task String_integer_double()
    {
        var s = await _provider.ResolveStringValueAsync("str", "d");
        Assert.Equal("none", s.Value);

        var i = await _provider.ResolveIntegerValueAsync("int", 5);
        Assert.Equal(10, i.Value);

        var d = await _provider.ResolveDoubleValueAsync("dbl", 0);
        Assert.Equal(2.5, d.Value);

        var intAsDouble = await _provider.ResolveDoubleValueAsync("int", 0);
        Assert.Equal(10.0, intAsDouble.Value);
    }

    [Fact]
    public async Task Structure_object_is_converted_recursively()
    {
        var r = await _provider.ResolveStructureValueAsync("obj", new Value());
        var s = r.Value.AsStructure!;
        Assert.Equal("stripe", s["primary"].AsString);
        Assert.Equal(2, s["n"].AsInteger);
        Assert.Equal(0.5, s["ratio"].AsDouble);
        Assert.True(s["ok"].AsBoolean);
        Assert.True(s["none"].IsNull);
        var list = s["list"].AsList!;
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0].AsInteger);
        Assert.Equal("a", list[1].AsString);
        Assert.Equal(1, list[2].AsStructure!["x"].AsInteger);
    }

    [Fact]
    public async Task Structure_array_is_list()
    {
        var r = await _provider.ResolveStructureValueAsync("arr", new Value());
        Assert.Equal(2, r.Value.AsList!.Count);
    }

    [Theory]
    [InlineData("str")]
    [InlineData("obj")]
    public async Task Type_mismatch_boolean(string flag)
    {
        var r = await _provider.ResolveBooleanValueAsync(flag, true);
        Assert.Equal(ErrorType.TypeMismatch, r.ErrorType);
        Assert.True(r.Value);
        Assert.Equal(Reason.Error, r.Reason);
    }

    [Fact]
    public async Task Type_mismatch_other_types()
    {
        Assert.Equal(ErrorType.TypeMismatch, (await _provider.ResolveStringValueAsync("int", "d")).ErrorType);
        Assert.Equal(ErrorType.TypeMismatch, (await _provider.ResolveIntegerValueAsync("str", 3)).ErrorType);
        Assert.Equal(ErrorType.TypeMismatch, (await _provider.ResolveIntegerValueAsync("dbl", 3)).ErrorType); // 2.5 is not an integer
        Assert.Equal(ErrorType.TypeMismatch, (await _provider.ResolveDoubleValueAsync("bool-static", 3)).ErrorType);
        var s = await _provider.ResolveStructureValueAsync("int", new Value("dflt"));
        Assert.Equal(ErrorType.TypeMismatch, s.ErrorType);
        Assert.Equal("dflt", s.Value.AsString);
    }

    [Fact]
    public async Task Fractional_double_variant_is_not_an_integer()
    {
        var r = await _provider.ResolveIntegerValueAsync("dbl", 0, T.Ctx("u"));
        Assert.Equal(ErrorType.TypeMismatch, r.ErrorType); // default variant 2.5
    }

    [Fact]
    public async Task Not_found_returns_default()
    {
        var r = await _provider.ResolveStringValueAsync("nope", "fallback");
        Assert.Equal(ErrorType.FlagNotFound, r.ErrorType);
        Assert.Equal("fallback", r.Value);
        Assert.Equal(Reason.Error, r.Reason);
    }

    [Fact]
    public async Task Disabled_returns_default_with_reason_disabled()
    {
        var r = await _provider.ResolveBooleanValueAsync("off", false);
        Assert.False(r.Value);
        Assert.Equal(Reason.Disabled, r.Reason);
        Assert.Equal(ErrorType.None, r.ErrorType);
    }

    [Fact]
    public async Task Not_ready_provider_reports_provider_not_ready()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        var p = ConfigManagerProvider.Create(T.Options(), new HttpClient(handler));
        var r = await p.ResolveBooleanValueAsync("x", true);
        Assert.Equal(ErrorType.ProviderNotReady, r.ErrorType);
        Assert.True(r.Value);
    }

    [Fact]
    public void Context_normalization()
    {
        var ctx = EvaluationContext.Builder()
            .SetTargetingKey("u1")
            .Set("s", "text")
            .Set("b", true)
            .Set("nb", false)
            .Set("i", 42)
            .Set("l", 9_000_000_000L)
            .Set("d", 1.5)
            .Set("dt", new DateTime(2026, 11, 4, 10, 30, 0, DateTimeKind.Utc))
            .Set("st", Structure.Builder().Set("a", "b").Build())
            .Set("list", new Value(new List<Value> { new("x") }))
            .Set("null", new Value())
            .Build();

        var input = EvaluationMapper.ToInput(ctx);

        Assert.Equal("u1", input.TargetingKey);
        Assert.Equal("text", input.Attributes["s"]);
        Assert.Equal("true", input.Attributes["b"]);
        Assert.Equal("false", input.Attributes["nb"]);
        Assert.Equal("42", input.Attributes["i"]);
        Assert.Equal("9000000000", input.Attributes["l"]);
        Assert.Equal("1.5", input.Attributes["d"]);
        Assert.Equal("2026-11-04T10:30:00.0000000Z", input.Attributes["dt"]);
        Assert.False(input.Attributes.ContainsKey("st"));
        Assert.False(input.Attributes.ContainsKey("list"));
        Assert.False(input.Attributes.ContainsKey("null"));
        Assert.False(input.Attributes.ContainsKey("targetingKey"));
    }

    [Fact]
    public void Context_numbers_use_invariant_culture()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var input = EvaluationMapper.ToInput(EvaluationContext.Builder().Set("d", 1.5).Build());
            Assert.Equal("1.5", input.Attributes["d"]);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public async Task Numeric_attribute_flows_into_rule_evaluation()
    {
        var adult = await _provider.ResolveBooleanValueAsync("num-attr", false, EvaluationContext.Builder().Set("age", 21).Build());
        Assert.True(adult.Value);
        var minor = await _provider.ResolveBooleanValueAsync("num-attr", true, EvaluationContext.Builder().Set("age", 12).Build());
        Assert.False(minor.Value);
    }
}
