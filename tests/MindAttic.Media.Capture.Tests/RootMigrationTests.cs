using MindAttic.Media.Capture.Output;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Tests;

public class RootMigrationTests
{
    private string _dir = null!;
    private string _old = null!;
    private string _new = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "capture-migrate-" + Guid.NewGuid().ToString("N"));
        _old = Path.Combine(_dir, "Capture");
        _new = Path.Combine(_dir, "Media", "Capture");
        Directory.CreateDirectory(_old);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private string Write(string root, string relative, string text = "x")
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Test]
    public void A_dry_run_plans_the_move_and_changes_nothing()
    {
        Write(_old, @"App\s1\shot.png");

        var report = RootMigration.Run(_old, _new, apply: false);

        Assert.That(report.Moved.Select(m => m.To), Is.EqualTo(new[] { Path.Combine(_new, "App") }));
        Assert.That(File.Exists(Path.Combine(_old, @"App\s1\shot.png")), Is.True);
        Assert.That(Directory.Exists(_new), Is.False);
        Assert.That(File.Exists(Path.Combine(_old, RootMigration.MarkerName)), Is.False);
    }

    [Test]
    public void An_app_folder_missing_from_the_new_root_moves_whole()
    {
        Write(_old, @"App\s1\shot.png", "png");

        var report = RootMigration.Run(_old, _new, apply: true);

        Assert.That(report.Moved, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(Path.Combine(_new, @"App\s1\shot.png")), Is.EqualTo("png"));
        Assert.That(Directory.Exists(Path.Combine(_old, "App")), Is.False);
    }

    [Test]
    public void An_existing_app_folder_is_merged_and_conflicts_stay_put()
    {
        Write(_old, @"App\s1\shot.png", "old");
        Write(_old, @"App\s2\shot.png", "old");
        Write(_new, @"App\s2\shot.png", "new");

        var report = RootMigration.Run(_old, _new, apply: true);

        Assert.That(File.ReadAllText(Path.Combine(_new, @"App\s1\shot.png")), Is.EqualTo("old"));
        Assert.That(File.ReadAllText(Path.Combine(_new, @"App\s2\shot.png")), Is.EqualTo("new"), "never overwritten");
        Assert.That(File.ReadAllText(Path.Combine(_old, @"App\s2\shot.png")), Is.EqualTo("old"), "never deleted");
        Assert.That(report.Skipped.Select(s => s.From), Is.EqualTo(new[] { Path.Combine(_old, @"App\s2") }));
    }

    [Test]
    public void The_marker_names_the_new_root_and_what_was_left()
    {
        Write(_old, @"App\s2\shot.png");
        Write(_new, @"App\s2\shot.png");

        var report = RootMigration.Run(_old, _new, apply: true, now: () => new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

        var marker = File.ReadAllText(Path.Combine(_old, RootMigration.MarkerName));
        Assert.That(report.Marker, Is.EqualTo(Path.Combine(_old, RootMigration.MarkerName)));
        Assert.That(marker, Does.Contain(_new).And.Contain("2026-11-24").And.Contain(@"App\s2"));
    }

    [Test]
    public void A_second_run_moves_nothing_and_ignores_the_marker()
    {
        Write(_old, @"App\s1\shot.png");
        Write(_old, @"Other\s1\shot.png");
        Write(_new, @"App\s0\shot.png");
        RootMigration.Run(_old, _new, apply: true);

        var again = RootMigration.Run(_old, _new, apply: true);

        Assert.That(again.Moved, Is.Empty);
        Assert.That(again.Skipped, Is.Empty);
        Assert.That(File.Exists(Path.Combine(_new, RootMigration.MarkerName)), Is.False);
    }

    [Test]
    public void Old_root_paths_in_moved_json_point_at_the_new_root()
    {
        var oldJson = _old.Replace("\\", "\\\\");
        Write(_old, @"App\s1\shot.png.json", $"{{\"files\":[\"{oldJson}\\\\App\\\\s1\\\\shot.png\"],\"outputRoot\":\"{oldJson}\",\"other\":\"{oldJson}Two\"}}");

        RootMigration.Run(_old, _new, apply: true);

        var newJson = _new.Replace("\\", "\\\\");
        Assert.That(File.ReadAllText(Path.Combine(_new, @"App\s1\shot.png.json")),
            Is.EqualTo($"{{\"files\":[\"{newJson}\\\\App\\\\s1\\\\shot.png\"],\"outputRoot\":\"{newJson}\",\"other\":\"{oldJson}Two\"}}"));
    }

    [Test]
    public void A_locked_file_is_skipped_and_reported()
    {
        Write(_old, @"App\s1\shot.png");
        Write(_new, @"App\keep.txt");
        using var held = new FileStream(Path.Combine(_old, @"App\s1\shot.png"), FileMode.Open, FileAccess.Read, FileShare.Read);

        var report = RootMigration.Run(_old, _new, apply: true);

        Assert.That(report.Skipped.Select(s => s.From), Is.EqualTo(new[] { Path.Combine(_old, @"App\s1") }));
        Assert.That(File.Exists(Path.Combine(_old, @"App\s1\shot.png")), Is.True);
    }

    [Test]
    public void A_missing_old_root_is_a_no_op()
    {
        var report = RootMigration.Run(Path.Combine(_dir, "nowhere"), _new, apply: true);

        Assert.That(report.Moved, Is.Empty);
        Assert.That(report.Marker, Is.Null);
    }
}
