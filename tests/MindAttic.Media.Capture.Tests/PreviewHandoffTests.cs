using MindAttic.Media.Capture.Sets;
using NUnit.Framework;

namespace MindAttic.Media.Capture.Tests;

public class PreviewHandoffTests
{
    [Test]
    public void PipeName_IsTheViewersPerUserPipe() =>
        Assert.That(PreviewHandoff.PipeName, Does.StartWith("mindattic-mediaviewer-cli-").And.Not.Contain(" "));

    [Test]
    public void Payload_IsOneArgumentPerLineThenAnEmptyLine()
    {
        Assert.That(PreviewHandoff.Payload(new PreviewRequest(@"R:\Preview\set one")), Is.EqualTo("R:\\Preview\\set one\n\n"));
        Assert.That(PreviewHandoff.Payload(new PreviewRequest(@"R:\Preview\s", "002_web.png")),
            Is.EqualTo("R:\\Preview\\s\n--select\n002_web.png\n\n"));
    }

    [Test]
    public void ToArgs_SkipsAnEmptySelect() =>
        Assert.That(new PreviewRequest(@"C:\s", "").ToArgs(), Is.EqualTo(new[] { @"C:\s" }));
}
