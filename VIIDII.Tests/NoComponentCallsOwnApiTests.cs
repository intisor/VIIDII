using Xunit;

namespace VIIDII.Tests;

/// <summary>
/// Guards a rule rather than a behaviour: a Blazor component must not call this app's
/// own HTTP API.
///
/// <para>
/// Two separate bugs came out of breaking that rule, and neither showed up in a test:
/// </para>
/// <list type="bullet">
/// <item>
/// RosterUpload POSTed to /api/roster/analyze and got a 401 on every attempt. The
/// upload screen simply reported "The list could not be read (401)". A lecturer could
/// not import a class list at all.
/// </item>
/// <item>
/// ClassDetail GET /api/roster/classes/{id} and blew up the circuit, but only on
/// classes that already had a list, so the page looked healthy until a real upload
/// had been done once.
/// </item>
/// </list>
///
/// <para>
/// The cause in both cases is the same: an <c>HttpClient</c> has no cookie container,
/// so the sign in cookie never left the process and every <c>/api</c> endpoint, which
/// sits behind <c>RequireAuthorization</c>, rejected the caller's own page. It is also
/// a needless round trip to the same process, over a socket, for data the component's
/// own scoped services can hand over directly.
/// </para>
///
/// <para>
/// The failure is invisible to route tests and to compilation, because the code is
/// valid and the route exists. Only clicking through the page finds it.
/// </para>
/// </summary>
public class NoComponentCallsOwnApiTests
{
    private static IEnumerable<string> RazorFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VIIDII.csproj")))
            {
                break;
            }

            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var components = Path.Combine(directory!.FullName, "Components");

        Assert.True(Directory.Exists(components), $"No Components directory under {directory.FullName}.");

        return Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories);
    }

    [Fact]
    public void NoRazorComponentInjectsAnHttpClient()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("@inject HttpClient", StringComparison.Ordinal))
                {
                    offenders.Add($"{Relative(file)}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A component injects an HttpClient, so it is calling this app's own API and will "
            + "get a 401: there is no cookie container, so the sign in cookie never leaves "
            + "the process.\nCall the scoped service directly instead.\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void NoRazorComponentCallsAnAbsoluteApiUrl()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Anchored on the member access so a bare word "Http" in prose or a
                // class named something-Http does not trip it.
                if (line.Contains("Http.GetFromJson", StringComparison.Ordinal)
                    || line.Contains("Http.PostAsync", StringComparison.Ordinal)
                    || line.Contains("Http.PostAsJson", StringComparison.Ordinal)
                    || line.Contains("Http.SendAsync", StringComparison.Ordinal))
                {
                    offenders.Add($"{Relative(file)}:{i + 1}  {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A component is issuing an HTTP call to this app's own API.\n"
            + string.Join("\n", offenders));
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(AppContext.BaseDirectory, path);
}