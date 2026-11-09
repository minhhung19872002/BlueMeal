using System.Reflection;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Controllers;
using BlueMeal.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BlueMeal.UnitTests.Authorization;

/// <summary>SPEC §8: every endpoint carries [HasPermission] (or explicit [AllowAnonymous]); deny by default.</summary>
public class EndpointPermissionTests
{
    private static IEnumerable<(Type Controller, MethodInfo Action)> Endpoints()
    {
        var controllers = typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));
        foreach (var c in controllers)
            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(true).Any()))
                yield return (c, m);
    }

    [Fact]
    public void Controllers_exist()
        => Endpoints().Should().NotBeEmpty();

    [Fact]
    public void Every_endpoint_declares_permission_or_anonymous()
    {
        var missing = Endpoints()
            .Where(e => e.Action.GetCustomAttribute<AllowAnonymousAttribute>(true) is null
                        && e.Controller.GetCustomAttribute<AllowAnonymousAttribute>(true) is null
                        && !e.Action.GetCustomAttributes<HasPermissionAttribute>(true).Any()
                        && !e.Controller.GetCustomAttributes<HasPermissionAttribute>(true).Any())
            .Select(e => $"{e.Controller.Name}.{e.Action.Name}")
            .ToList();
        missing.Should().BeEmpty("all endpoints must be protected by [HasPermission] or explicitly [AllowAnonymous]");
    }

    [Fact]
    public void Every_permission_code_used_by_endpoints_is_registered()
    {
        var known = Permissions.All.Select(p => p.Code).Append(Permissions.Self).ToHashSet();
        var unknown = new List<string>();
        foreach (var (c, m) in Endpoints())
        {
            var attrs = m.GetCustomAttributes<HasPermissionAttribute>(true).Concat(c.GetCustomAttributes<HasPermissionAttribute>(true));
            foreach (var a in attrs)
            {
                var code = PermissionResolver.Resolve(a, c);
                if (!known.Contains(code)) unknown.Add($"{c.Name}.{m.Name} → {code}");
            }
        }
        unknown.Should().BeEmpty();
    }

    [Fact]
    public void Permission_codes_are_unique_and_follow_module_action_format()
    {
        var codes = Permissions.All.Select(p => p.Code).ToList();
        codes.Should().OnlyHaveUniqueItems();
        codes.Should().AllSatisfy(c => c.Should().MatchRegex(@"^[a-z_]+(\.[a-z_]+)+$"));
    }

    [Fact]
    public void Default_roles_grant_expected_permissions()
    {
        var all = Permissions.All.Select(p => p.Code).ToList();
        DefaultRoles.All.Single(r => r.Code == DefaultRoles.SysAdmin).Grants("system.backup").Should().BeTrue();
        var storekeeper = DefaultRoles.All.Single(r => r.Code == DefaultRoles.Storekeeper);
        storekeeper.Grants(Permissions.StockReceipt.Create).Should().BeTrue();
        storekeeper.Grants(Permissions.Menu.Approve).Should().BeFalse();
        storekeeper.Grants(Permissions.User.Create).Should().BeFalse();
        var approver = DefaultRoles.All.Single(r => r.Code == DefaultRoles.MenuApprover);
        approver.Grants(Permissions.Menu.Approve).Should().BeTrue();
        approver.Grants(Permissions.Menu.Create).Should().BeFalse();
        var rep = DefaultRoles.All.Single(r => r.Code == DefaultRoles.CustomerRep);
        all.Where(rep.Grants).Should().BeSubsetOf(new[] { Permissions.Menu.View, Permissions.MealOrder.View, Permissions.MealOrder.Create, Permissions.MealOrder.Edit, Permissions.SystemInfo.Version, Permissions.Dashboard.View });
    }

    [Fact]
    public void Mfa_is_mandatory_for_privileged_roles()
        => DefaultRoles.MfaMandatory.Should().BeEquivalentTo([DefaultRoles.SysAdmin, DefaultRoles.Manager, DefaultRoles.MenuApprover, DefaultRoles.Accountant]);
}
