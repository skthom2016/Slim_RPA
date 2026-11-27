#nullable enable
using FluentAssertions;
using Tracto.Desktop.Protocol;
using Xunit;

namespace Tracto.Desktop.Protocol.Tests;

/// <summary>Which credential and setting names a script reads, how references are replaced, and the mask.</summary>
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
        s.Load(new Dictionary<string, CredentialValues> { ["site-a"] = new(User, password, "123456") },
               new Dictionary<string, string> { ["portal-url"] = "https://portal.example.com" });
        return s;
    }

    [Fact]
    public void Names_written_out_and_names_built_from_job_parameters_are_collected()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["param.site"] = "bank.hdfc" };
        var (creds, settings) = RunSecrets.CollectNames(
        [
            ("step.1", P(("value", "${credential:givewell-portal.username}"))),
            ("step.2", P(("value", "${credential:givewell-portal.password}"))),
            ("step.3", P(("value", "${credential:${param.site}.password}"), ("url", "${setting:portal-url}/x"))),
            ("step.4", P(("value", "plain text"))),
        ], vars);

        creds.Should().Equal("bank.hdfc", "givewell-portal");
        settings.Should().Equal("portal-url");
    }

    [Fact]
    public void A_name_built_from_an_earlier_steps_output_is_refused()
    {
        var act = () => RunSecrets.CollectNames(
            [("step.2", P(("value", "${credential:${first.value}.password}")))], new Dictionary<string, string>());
        act.Should().Throw<CredentialScriptException>().WithMessage("*step.2*job parameters*");
    }

    [Fact]
    public void References_become_standins_then_the_real_values()
    {
        var secrets = Loaded();
        var values = new List<string>();
        var p = secrets.Protect(O(
            ("a", "${credential:site-a.username}"),
            ("b", "pw=${credential:site-a.password}!"),
            ("c", "${credential:site-a.totp}"),
            ("d", "${setting:portal-url}/login")), new Dictionary<string, string>(), values);

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
        var p = Loaded(tricky).Protect(O(("v", "${credential:site-a.password}")), new Dictionary<string, string>(), values);
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
    public void A_one_time_code_is_refused_when_the_credential_has_no_key()
    {
        var s = new RunSecrets();
        s.Load(new Dictionary<string, CredentialValues> { ["site-a"] = new(User, Pass, null) }, new Dictionary<string, string>());
        var act = () => s.Protect(O(("v", "${credential:site-a.totp}")), new Dictionary<string, string>(), []);
        act.Should().Throw<CredentialScriptException>().WithMessage("*no one-time-code key*");
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
    public void Only_passwords_and_codes_count_as_secrets_for_outputs()
    {
        var s = Loaded();
        s.HoldsSecret("the password is " + Pass).Should().BeTrue();
        s.HoldsSecret("code 123456 here").Should().BeTrue();
        s.HoldsSecret(User).Should().BeFalse("a username may be read from a page and kept");
        s.HoldsSecret("nothing").Should().BeFalse();
        s.HoldsSecret(null).Should().BeFalse();
    }
}

/// <summary>The mask is one shared value, so the tests that set it do not run at the same time.</summary>
[CollectionDefinition("SecretMask", DisableParallelization = true)]
public sealed class SecretMaskCollection;
