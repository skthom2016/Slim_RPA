#nullable enable
using FluentAssertions;
using Tracto.Desktop.Protocol;
using Xunit;

namespace Tracto.Desktop.Protocol.Tests;

/// <summary>Which variable names a script reads, how references are replaced, and the mask.</summary>
[Collection("SecretMask")]
public sealed class RunSecretsTests : IDisposable
{
    private const string User = "fin.ops@acme.com";
    private const string Pass = "Zebra#Secret-4471";

    public void Dispose() => SecretMask.Clear();

    private static IReadOnlyDictionary<string, string?> P(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => (string?)i.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, object?> O(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => (object?)i.Value, StringComparer.OrdinalIgnoreCase);

    private static RunSecrets Loaded(string password = Pass)
    {
        var s = new RunSecrets();
        s.Load(new Dictionary<string, EnvValue>
        {
            ["SITE_A_USERNAME"] = new(User, false),
            ["SITE_A_PASSWORD"] = new(password, true),
            ["SITE_A_CODE"] = new("123456", true),
            ["PORTAL_URL"] = new("https://portal.example.com", false),
        });
        return s;
    }

    [Fact]
    public void Names_written_out_and_names_built_from_job_parameters_are_collected()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["param.site"] = "SITE1" };
        var names = RunSecrets.CollectNames(
        [
            ("step.1", P(("value", "${env:ACME_USERNAME}"))),
            ("step.2", P(("value", "${env:ACME_PASSWORD}"))),
            ("step.3", P(("value", "${env:${param.site}_PASSWORD}"), ("url", "${env:PORTAL_URL}/x"))),
            ("step.4", P(("value", "plain text"))),
        ], vars);

        names.Should().Equal("ACME_PASSWORD", "ACME_USERNAME", "PORTAL_URL", "SITE1_PASSWORD");
    }

    [Fact]
    public void A_name_built_from_an_earlier_steps_output_is_refused()
    {
        var act = () => RunSecrets.CollectNames(
            [("step.2", P(("value", "${env:${first.value}_PASSWORD}")))], new Dictionary<string, string>());
        act.Should().Throw<CredentialScriptException>().WithMessage("*step.2*job parameters*");
    }

    [Fact]
    public void References_become_standins_then_the_real_values()
    {
        var secrets = Loaded();
        var values = new List<string>();
        var p = secrets.Protect(O(
            ("a", "${env:SITE_A_USERNAME}"),
            ("b", "pw=${env:SITE_A_PASSWORD}!"),
            ("c", "${env:SITE_A_CODE}"),
            ("d", "${env:PORTAL_URL}/login")), new Dictionary<string, string>(), values);

        foreach (var v in p.Values.Cast<string>())
        {
            v.Should().NotContain(Pass).And.NotContain(User);
            v.Should().NotContain("${");
        }

        RunSecrets.Reveal(p, values);
        p["a"].Should().Be(User);
        p["b"].Should().Be("pw=" + Pass + "!");
        p["c"].Should().Be("123456");
        p["d"].Should().Be("https://portal.example.com/login");
    }

    [Fact]
    public void A_value_that_looks_like_a_variable_is_inserted_as_plain_text()
    {
        const string tricky = "P${now.date}x${secret:Y}!";
        var values = new List<string>();
        var p = Loaded(tricky).Protect(O(("v", "${env:SITE_A_PASSWORD}")), new Dictionary<string, string>(), values);
        p["v"]!.ToString().Should().NotContain("${");
        RunSecrets.Reveal(p, values);
        p["v"].Should().Be(tricky);
    }

    [Fact]
    public void Non_text_values_are_left_alone()
    {
        var values = new List<string>();
        var p = Loaded().Protect(new Dictionary<string, object?> { ["n"] = 42, ["x"] = null }, new Dictionary<string, string>(), values);
        p["n"].Should().Be(42);
        p["x"].Should().BeNull();
    }

    [Fact]
    public void A_variable_the_orchestrator_did_not_give_is_a_script_error()
    {
        var act = () => Loaded().Protect(O(("v", "${env:NOT_THERE}")), new Dictionary<string, string>(), []);
        act.Should().Throw<CredentialScriptException>().WithMessage("*NOT_THERE*not given*");
    }

    [Fact]
    public void The_mask_hides_every_value_but_leaves_short_ones_and_other_text_alone()
    {
        SecretMask.Set([User, Pass, "123456", "ab"]);
        SecretMask.Apply($"typed {User} and {Pass} then 123456 ab").Should().Be("typed ●●●● and ●●●● then ●●●● ab");
        SecretMask.Apply("nothing to hide").Should().Be("nothing to hide");
        SecretMask.Apply(null).Should().BeEmpty();
        SecretMask.Clear();
        SecretMask.Apply(Pass).Should().Be(Pass);
    }

    [Fact]
    public void A_longer_value_is_masked_whole_before_a_shorter_one_inside_it()
    {
        SecretMask.Set(["Zebra", "Zebra#Secret-4471"]);
        SecretMask.Apply("x Zebra#Secret-4471 y").Should().Be("x ●●●● y");
    }

    [Fact]
    public void Only_secret_values_are_masked_and_only_they_count_for_outputs()
    {
        var s = Loaded();
        s.HoldsSecret("the password is " + Pass).Should().BeTrue();
        s.HoldsSecret("code 123456 here").Should().BeTrue();
        s.HoldsSecret(User).Should().BeFalse("a plain value may be read from a page and kept");
        s.MaskValues().Should().BeEquivalentTo(Pass, "123456");
        s.HoldsSecret("nothing").Should().BeFalse();
        s.HoldsSecret(null).Should().BeFalse();
    }
}

/// <summary>The mask is one shared value, so the tests that set it do not run at the same time.</summary>
[CollectionDefinition("SecretMask", DisableParallelization = true)]
public sealed class SecretMaskCollection;
