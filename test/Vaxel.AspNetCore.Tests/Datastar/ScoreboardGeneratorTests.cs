using System.Text;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Datastar;

public sealed class ScoreboardGeneratorTests
{
    [Fact]
    public void Scoreboard_ReflectsConformanceAndParityClosure()
    {
        var scoreboardPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "parity", "SCOREBOARD.md"));
        Assert.True(File.Exists(scoreboardPath), "parity/SCOREBOARD.md must exist.");

        var content = File.ReadAllText(scoreboardPath, Encoding.UTF8);

        // Assert headlines match required v0.5 targets
        Assert.Contains("16 / 20", content); // SDK cases passing
        Assert.Contains("4", content); // Declined executeScript
        Assert.Contains("13 / 17", content); // Attribute plugins Full
        Assert.Contains("4 / 17", content); // Attribute plugins Outcome
        Assert.Contains("9 / 10", content); // Pro attributes matched
        Assert.Contains("4 / 4", content); // Actions matched
        Assert.Contains("100 %", content); // Example corpus expressible
    }
}
