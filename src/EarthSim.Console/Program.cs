using EarthSim.Core;

// 使い方: dotnet run --project src/EarthSim.Console -- --seed 12345 --population 20 --max-days 36500
//   --seed        アースの Seed（省略時: default-seed）
//   --population  初期人口（省略時: 20 = MVP。設計は 100）
//   --max-days    プロトタイプ用の安全上限（ゲーム仕様の打ち切りではない）
//   --all-log     B レベル以下のログも全部出す（省略時: S/A のみ）

string seed = GetArg("--seed") ?? "default-seed";
int population = int.Parse(GetArg("--population") ?? "20");
int maxDays = int.Parse(GetArg("--max-days") ?? (365 * 200).ToString());
bool allLog = args.Contains("--all-log");

var config = new SimulationConfig
{
    Seed = seed,
    InitialPopulation = population,
    MaxDays = maxDays,
};

Console.WriteLine($"=== アース観察シミュレーション (sim v{Simulation.SimulationVersion}) ===");
Console.WriteLine($"Seed={seed} / 初期人口={population} / 安全上限={maxDays} 日");
Console.WriteLine();

var result = new Simulation(config).Run();

foreach (var e in result.Log)
{
    if (!allLog && e.Level is not ("S" or "A")) continue;
    Console.WriteLine($"{e.Day,6} 日目 [{e.Level}] {e.Text}");
}

Console.WriteLine();
Console.WriteLine("--- 結果 ---");
Console.WriteLine($"存続日数 : {result.DaysSurvived} 日（{result.DaysSurvived / 365} 年）");
Console.WriteLine($"最大人口 : {result.MaxPopulation} 人 / 最終人口: {result.FinalPopulation} 人");
Console.WriteLine($"出生総数 : {result.TotalBirths} / 死亡総数: {result.TotalDeaths}");
Console.WriteLine($"発見     : {(result.DiscoveredItems.Count == 0 ? "なし" : string.Join("、", result.DiscoveredItems))}");
Console.WriteLine($"文明     : {Simulation.CivilizationName(result.CivilizationLevel)}（Lv.{result.CivilizationLevel}）");
Console.WriteLine($"建物     : {(result.BuildingsCompleted.Count == 0 ? "なし" : string.Join("、", result.BuildingsCompleted))}（住居の増築 {result.ExtraHouses} 棟）");
Console.WriteLine($"最終資源 : 木材 {result.FinalWood} / 食料 {result.FinalFood} / 鉱石 {result.FinalOre} / 道具 {result.FinalTools}");
Console.WriteLine($"知識保有 : {string.Join(" / ", result.KnowledgeHolders.Select(kv => $"{kv.Key} {kv.Value} 人"))}（薬草 {result.FinalHerbs} / 薬 {result.FinalMedicine}）");
Console.WriteLine($"結果ハッシュ: {result.ResultHash}");

// 決定論チェック：同じ Seed で回し直して、結果が完全一致するか
var rerun = new Simulation(config).Run();
Console.WriteLine();
Console.WriteLine(rerun.ResultHash == result.ResultHash
    ? "決定論チェック: OK（同じ Seed = 同じ結果）"
    : "決定論チェック: NG（同じ Seed で結果が変わった）");

string? GetArg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
