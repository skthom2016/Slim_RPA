namespace Tracto.Designer.Unit.Tests.Credentials;

using FluentAssertions;
using Tracto.Designer.Core.Credentials;
using Tracto.Designer.Core.Scripting;

public class CredentialVaultTests
{
    [Fact]
    public void InMemory_Store_And_Resolve()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("api-key", "secret123");
        v.ResolveCredential("api-key").Should().Be("secret123");
    }

    [Fact]
    public void InMemory_Unknown_Credential_Returns_Null()
    {
        new InMemoryCredentialVault().ResolveCredential("no-such").Should().BeNull();
    }

    [Fact]
    public void InMemory_Remove_Removes()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("x", "y");
        v.RemoveCredential("x");
        v.ResolveCredential("x").Should().BeNull();
    }

    [Fact]
    public void InMemory_List_Returns_Hints()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("db-pass", "verylongsecret");
        var list = v.ListCredentials();
        list.Should().ContainSingle(e => e.Name == "db-pass");
        list.First().ObfuscatedHint.Should().NotContain("verylongsecret");
        list.First().ObfuscatedHint.Should().Contain("*");
    }

    [Fact]
    public void InMemory_Overwrite_Credential()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("k", "v1");
        v.StoreCredential("k", "v2");
        v.ResolveCredential("k").Should().Be("v2");
    }

    [Fact]
    public void InMemory_Case_Insensitive()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("MyKey", "val");
        v.ResolveCredential("mykey").Should().Be("val");
    }

    [Fact]
    public void Resolver_Replaces_Placeholder()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("api-key", "secret123");
        var resolver = new CredentialParameterResolver(v);
        var step = new ScriptStep { StepNumber = 1, Description = "x", Action = "y" };
        step.Parameters["token"] = "{{credential:api-key}}";
        var resolved = resolver.ResolveParameters(step);
        resolved["token"].Should().Be("secret123");
    }

    [Fact]
    public void Resolver_Leaves_Unknown_Placeholder()
    {
        var resolver = new CredentialParameterResolver(new InMemoryCredentialVault());
        var step = new ScriptStep { StepNumber = 1, Description = "x", Action = "y" };
        step.Parameters["token"] = "{{credential:missing}}";
        var resolved = resolver.ResolveParameters(step);
        resolved["token"].Should().Be("{{credential:missing}}");
    }

    [Fact]
    public void Resolver_DetectsPlaceholders()
    {
        var resolver = new CredentialParameterResolver(new InMemoryCredentialVault());
        var step = new ScriptStep { StepNumber = 1, Description = "x", Action = "y" };
        step.Parameters["p"] = "{{credential:foo}}";
        resolver.HasCredentialPlaceholders(step).Should().BeTrue();
    }

    [Fact]
    public void Obfuscate_Short_Value_All_Stars()
    {
        var v = new InMemoryCredentialVault();
        v.StoreCredential("k", "ab");
        v.ListCredentials().First().ObfuscatedHint.Should().Be("**");
    }
}
