using System.IO;
using MindAttic.Media.Capture.Agent;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Windows.Tests;

// LEGACY-ROOT: remove with the legacy-root read fallback after one release.
public class RecoverRootsTests
{
    [Test]
    public void Recover_scans_the_legacy_root_while_the_output_root_is_the_default()
    {
        var legacy = Path.Combine(Path.GetTempPath(), "capture-legacy-" + System.Guid.NewGuid().ToString("N"));
        var defaults = new CaptureOptions();

        var roots = AgentProgram.RecoverRoots(null, defaults, legacy);

        Assert.That(roots, Is.EqualTo(new[] { CaptureOptions.DefaultOutputRoot, defaults.FallbackRoot, legacy }));
        Assert.That(AgentProgram.RecoverRoots(null, defaults), Does.Contain(CaptureOptions.LegacyOutputRoot));
    }

    [Test]
    public void Recover_with_a_custom_root_does_not_scan_the_legacy_root()
    {
        var custom = new CaptureOptions { OutputRoot = @"D:\Captures" };

        Assert.That(AgentProgram.RecoverRoots(null, custom), Does.Not.Contain(CaptureOptions.LegacyOutputRoot));
        Assert.That(AgentProgram.RecoverRoots(@"D:\Elsewhere", new CaptureOptions()), Is.EqualTo(new[] { @"D:\Elsewhere" }));
    }
}
