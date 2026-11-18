using EarthSim.Core;
using Xunit;

namespace EarthSim.Core.Tests;

public class SimulationTests
{
    private static SimulationConfig Config(string seed) => new()
    {
        Seed = seed,
        InitialPopulation = 20,
        MaxDays = 365 * 60,
    };

    [Fact]
    public void SameSeed_ProducesSameResult()
    {
        var a = new Simulation(Config("test-seed-1")).Run();
        var b = new Simulation(Config("test-seed-1")).Run();
        Assert.Equal(a.ResultHash, b.ResultHash);
        Assert.Equal(a.DaysSurvived, b.DaysSurvived);
        Assert.Equal(a.TotalBirths, b.TotalBirths);
    }

    [Fact]
    public void DifferentSeed_UsuallyProducesDifferentResult()
    {
        var a = new Simulation(Config("test-seed-1")).Run();
        var b = new Simulation(Config("test-seed-2")).Run();
        Assert.NotEqual(a.ResultHash, b.ResultHash);
    }

    [Fact]
    public void Buildings_GetBuiltOverTime()
    {
        var r = new Simulation(Config("test-seed-1")).Run();
        Assert.NotEmpty(r.BuildingsCompleted);
    }

    [Fact]
    public void Result_IsInternallyConsistent()
    {
        var r = new Simulation(Config("test-seed-3")).Run();
        Assert.True(r.DaysSurvived > 0);
        Assert.True(r.MaxPopulation >= r.FinalPopulation);
        // 全滅で終わった場合、出生 - 死亡 の収支と最終人口が一致する
        if (r.FinalPopulation == 0)
        {
            Assert.Equal(20 + r.TotalBirths, r.TotalDeaths);
        }
    }
}
