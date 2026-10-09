using System.Text;
using Microsoft.Extensions.Configuration;
using VIIDII.Services;
using Xunit;

namespace VIIDII.Tests.Services;

/// <summary>
/// Tests for TURN credential issuance.
///
/// Getting this wrong is a silent failure: cotturn simply refuses the connection
/// and video "randomly fails" for students behind NAT.
/// </summary>
public class TurnCredentialGeneratorTests
{
    [Fact]
    public void Generate_ProducesUsernameThatIsAFutureUnixTimestamp()
    {
        const string secret = "test-shared-secret";

        var (username, _) = TurnCredentialGenerator.Generate(secret, TimeSpan.FromHours(12));

        var expiry = long.Parse(username);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.True(expiry > now, "Credential must not be already expired.");
        Assert.InRange(expiry - now, 11 * 3600, 12 * 3600 + 60);
    }

    [Fact]
    public void Generate_ProducesBase64HmacMatchingTheTurnRestScheme()
    {
        const string secret = "test-shared-secret";

        var (username, credential) = TurnCredentialGenerator.Generate(secret, TimeSpan.FromHours(1));

        using var hmac = new System.Security.Cryptography.HMACSHA1(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        // cotturn recomputes this server-side: base64(HMAC-SHA1(secret, username))
        Assert.Equal(expected, credential);
    }

    [Fact]
    public void Generate_IsDeterministicForTheSameUsername()
    {
        const string secret = "test-shared-secret";

        var (_, first) = TurnCredentialGenerator.Generate(secret, TimeSpan.FromHours(1));
        var (_, second) = TurnCredentialGenerator.Generate(secret, TimeSpan.FromHours(1));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_ProducesDifferentCredentialsForDifferentSecrets()
    {
        var (_, credentialA) = TurnCredentialGenerator.Generate("secret-a", TimeSpan.FromHours(1));
        var (_, credentialB) = TurnCredentialGenerator.Generate("secret-b", TimeSpan.FromHours(1));

        Assert.NotEqual(credentialA, credentialB);
    }

    [Fact]
    public void TurnServerOptions_BindsFromServersSection()
    {
        // Mirrors the exact binding performed by the /api/config/ice endpoint.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TurnServers:SharedSecret"] = "super-secret",
                ["TurnServers:Servers:0:Url"] = "turn:turn.example.com:443",
                ["TurnServers:Servers:0:Urls:0"] = "turn:turn.example.com:443",
                ["TurnServers:Servers:0:Urls:1"] = "turns:turn.example.com:443",
                ["TurnServers:Servers:0:CredentialTtlHours"] = "8",
            })
            .Build();

        var servers = configuration
            .GetSection("TurnServers:Servers")
            .Get<List<TurnServerOptions>>();

        Assert.NotNull(servers);
        Assert.Single(servers!);
        Assert.Equal("turn:turn.example.com:443", servers![0].Url);
        Assert.Equal(2, servers[0].Urls.Count);
        Assert.Equal("super-secret", configuration["TurnServers:SharedSecret"]);
        Assert.Equal(8, servers[0].CredentialTtlHours);
    }

    [Fact]
    public void TurnServerOptions_EmptyServersSectionBindsToEmptyList()
    {
        // The default appsettings ships an empty Servers array; the endpoint must
        // fall back to public relays rather than throwing.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TurnServers:SharedSecret"] = "",
            })
            .Build();

        var servers = configuration
            .GetSection("TurnServers:Servers")
            .Get<List<TurnServerOptions>>() ?? new List<TurnServerOptions>();

        Assert.Empty(servers);
    }

    [Fact]
    public void TurnServerOptions_DefaultsTtlToTwelveHours()
    {
        var options = new TurnServerOptions();

        Assert.Equal(12, options.CredentialTtlHours);
    }
}