using MindAttic.Media.Capture.Sets;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Tests;

// LEGACY-ROOT: remove with the legacy-root read fallback after one release.
public class PreviewSetLegacyRootTests
{
    private string _root = null!;
    private string _legacy = null!;

    [SetUp]
    public void SetUp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "capture-legacy-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(dir, "Media", "Preview");
        _legacy = Path.Combine(dir, "Preview");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_legacy);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(Path.GetDirectoryName(_legacy)!, recursive: true);

    private PreviewSetStore Store(DateTimeOffset? at = null) =>
        new(_root, now: () => at ?? DateTimeOffset.Now) { LegacyReadRoot = _legacy };

    [Test]
    public void A_set_only_in_the_legacy_root_is_listed_and_resolved()
    {
        var old = new PreviewSetStore(_legacy, now: () => new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)).Create("old one", "X-T1", "Repo");
        var fresh = Store(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)).Create("new one", "X-T2", "Repo");

        var listed = Store().List();

        Assert.That(listed.Select(s => s.Directory), Is.EqualTo(new[] { fresh.Directory, old.Directory }), "newest first across both roots");
        Assert.That(Store().List("Repo").Count, Is.EqualTo(2));
        Assert.That(Store().Resolve(old.Id), Is.EqualTo(old.Directory));
        Assert.That(PreviewSetStore.Open(Store().Resolve(old.Id)).Title, Is.EqualTo("old one"));
    }

    [Test]
    public void A_set_in_both_roots_is_listed_once_from_the_new_root()
    {
        var created = Store(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)).Create("twice", null, "Repo");
        var copy = Path.Combine(_legacy, "Repo", created.Id);
        Directory.CreateDirectory(copy);
        File.Copy(Path.Combine(created.Directory, PreviewSet.ManifestName), Path.Combine(copy, PreviewSet.ManifestName));

        var listed = Store().List();

        Assert.That(listed.Select(s => s.Directory), Is.EqualTo(new[] { created.Directory }));
        Assert.That(Store().Resolve(created.Id), Is.EqualTo(created.Directory));
    }

    [Test]
    public void New_sets_go_to_the_new_root_only()
    {
        var set = Store().Create("fresh", null, "Repo");

        Assert.That(set.Directory, Does.StartWith(_root));
        Assert.That(Directory.Exists(Path.Combine(_legacy, "Repo")), Is.False);
    }

    [Test]
    public void Only_the_default_root_reads_the_legacy_root()
    {
        Assert.That(PreviewSetStore.LegacyRootFor(null), Is.EqualTo(PreviewSetStore.LegacyRoot));
        Assert.That(PreviewSetStore.LegacyRootFor(PreviewSetStore.DefaultRoot), Is.EqualTo(PreviewSetStore.LegacyRoot));
        Assert.That(PreviewSetStore.LegacyRootFor(_root), Is.Null);
    }

    [Test]
    public void A_custom_root_does_not_list_legacy_sets()
    {
        new PreviewSetStore(_legacy).Create("old one", null, "Repo");

        Assert.That(new PreviewSetStore(_root).List(), Is.Empty);
    }
}
