using System.IO;
using System.Text.Json;
using MindAttic.Media.Capture.Agent;
using MindAttic.Media.Capture.Sets;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Windows.Tests;

public class SetCommandsTests
{
    [Test]
    public void Set_new_json_names_the_set_folder()
    {
        var set = new PreviewSet { Id = "2026-11-24T10.00.00_X-T1_smoke", Title = "smoke", Directory = Path.Combine(Path.GetTempPath(), "smoke") };

        using var doc = JsonDocument.Parse(SetCommands.ManifestJson(set));

        Assert.That(doc.RootElement.GetProperty("dir").GetString(), Is.EqualTo(set.Directory));
        Assert.That(doc.RootElement.GetProperty("title").GetString(), Is.EqualTo("smoke"));
        Assert.That(PreviewJson.Serialize(set), Does.Not.Contain("\"dir\""), "preview.json keeps its schema");
    }
}
