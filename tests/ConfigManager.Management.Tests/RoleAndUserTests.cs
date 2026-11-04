using System.Security.Claims;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Domain;

namespace ConfigManager.Management.Tests;

public class RoleResolverTests
{
    private static readonly RoleBindingInfo[] Bindings =
    [
        new(SubjectTypes.Group, "editors", NamespaceRole.Editor),
        new(SubjectTypes.Group, "approvers", NamespaceRole.Approver),
        new(SubjectTypes.User, "alice", NamespaceRole.Viewer),
    ];

    [Fact]
    public void Takes_max_over_user_and_group_bindings() =>
        Assert.Equal(NamespaceRole.Editor, RoleResolver.Resolve(Bindings, "alice", ["editors"], false));

    [Fact]
    public void Takes_highest_of_multiple_groups() =>
        Assert.Equal(NamespaceRole.Approver, RoleResolver.Resolve(Bindings, "bob", ["editors", "approvers"], false));

    [Fact]
    public void No_matching_binding_returns_null() =>
        Assert.Null(RoleResolver.Resolve(Bindings, "eve", ["other"], false));

    [Fact]
    public void User_binding_does_not_match_group_with_same_name() =>
        Assert.Null(RoleResolver.Resolve(Bindings, "bob", ["alice"], false));

    [Fact]
    public void Admin_is_owner_without_bindings() =>
        Assert.Equal(NamespaceRole.Owner, RoleResolver.Resolve([], "root", [], true));

    [Fact]
    public void Hierarchy_is_ordered() =>
        Assert.True(NamespaceRole.Viewer < NamespaceRole.Editor && NamespaceRole.Editor < NamespaceRole.Approver && NamespaceRole.Approver < NamespaceRole.Owner);

    [Theory]
    [InlineData("viewer", true, NamespaceRole.Viewer)]
    [InlineData("OWNER", true, NamespaceRole.Owner)]
    [InlineData("root", false, default(NamespaceRole))]
    public void Parses_roles(string input, bool ok, NamespaceRole expected)
    {
        Assert.Equal(ok, RoleResolver.TryParse(input, out var role));
        if (ok) Assert.Equal(expected, role);
    }
}

public class CurrentUserTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void Reads_basic_claims()
    {
        var u = new CurrentUser(Principal(new Claim("preferred_username", "alice"), new("name", "Alice A"), new("email", "a@x.io")));
        Assert.True(u.IsAuthenticated);
        Assert.Equal("alice", u.Username);
        Assert.Equal("Alice A", u.Name);
        Assert.Equal("a@x.io", u.Email);
        Assert.False(u.IsAdmin);
        Assert.Empty(u.Groups);
    }

    [Fact]
    public void Groups_as_multiple_claims_strip_leading_slash()
    {
        var u = new CurrentUser(Principal(new Claim("groups", "/orders-editors"), new("groups", "viewers")));
        Assert.Equal(["orders-editors", "viewers"], u.Groups);
    }

    [Fact]
    public void Groups_as_json_array_string()
    {
        var u = new CurrentUser(Principal(new Claim("groups", "[\"/orders-editors\",\"viewers\"]")));
        Assert.Equal(["orders-editors", "viewers"], u.Groups);
    }

    [Fact]
    public void Invalid_groups_json_is_ignored()
    {
        var u = new CurrentUser(Principal(new Claim("groups", "[oops")));
        Assert.Empty(u.Groups);
    }

    [Fact]
    public void Realm_access_with_cm_admin_makes_admin()
    {
        var u = new CurrentUser(Principal(new Claim("realm_access", "{\"roles\":[\"offline_access\",\"cm-admin\"]}")));
        Assert.True(u.IsAdmin);
    }

    [Fact]
    public void Realm_access_without_cm_admin_is_not_admin()
    {
        var u = new CurrentUser(Principal(new Claim("realm_access", "{\"roles\":[\"offline_access\"]}")));
        Assert.False(u.IsAdmin);
    }

    [Fact]
    public void Falls_back_to_sub_when_no_username()
    {
        Assert.Equal("abc", new CurrentUser(Principal(new Claim("sub", "abc"))).Username);
    }
}
