using System.Text;

namespace EarthSim.Core;

public sealed record SimulationConfig
{
    /// <summary>アースを決める Seed（文字列）。同じ Seed + 同じバージョン = 同じ結果。</summary>
    public required string Seed { get; init; }

    /// <summary>初期人口。設計は 100 人、MVP は 20 人。設定値 1 つで切り替える。</summary>
    public int InitialPopulation { get; init; } = 20;

    /// <summary>
    /// プロトタイプ用の安全上限（日数）。ゲーム仕様としての打ち切りではない
    /// （仕様 v0: 打ち切りなし、全滅まで続ける）。長時間テストが終わらない場合の保険。
    /// </summary>
    public int MaxDays { get; init; } = 365 * 300;
}

public sealed record LogEntry(int Day, string Level, string Text);

public sealed record SimulationResult
{
    public required string Seed { get; init; }
    public required int DaysSurvived { get; init; }
    public required int MaxPopulation { get; init; }
    public required int FinalPopulation { get; init; }
    public required int TotalBirths { get; init; }
    public required int TotalDeaths { get; init; }
    public required IReadOnlyList<string> DiscoveredItems { get; init; }
    public required int CivilizationLevel { get; init; }
    public required long FinalWood { get; init; }
    public required long FinalFood { get; init; }
    public required long FinalOre { get; init; }
    public required long FinalTools { get; init; }
    public required IReadOnlyList<string> BuildingsCompleted { get; init; }
    public required int ExtraHouses { get; init; }
    public required IReadOnlyList<LogEntry> Log { get; init; }

    /// <summary>決定論チェック用：結果とログ全体のハッシュ。</summary>
    public required string ResultHash { get; init; }
}

/// <summary>
/// ヘッドレスなシミュレーション本体（UI に依存しない）。
/// 乱数は用途別ストリームだけを使い、時刻や Guid には一切依存しない。
/// </summary>
public sealed class Simulation
{
    public const string SimulationVersion = "0.3.0";

    private static readonly string[] DiscoveryPool =
    {
        "石のナイフ", "木の柵", "燻製肉の保存法", "薬草の知識",
        "銅鉱石", "罠", "鉄鉱石", "水車の原型",
    };

    private static readonly string[] CivilizationNames = { "木器", "石器", "銅器", "鉄器" };

    /// <summary>建物（この順に建つ）。効果は各処理側で HasBuilding を見て適用する。</summary>
    private sealed record BuildingDef(string Name, int WoodCost, int OreCost);
    private static readonly BuildingDef[] BuildingDefs =
    {
        new("木の柵", 15, 0),    // 街の守り：発病率が下がる
        new("住居", 25, 0),      // 出産率と休息の回復が上がる
        new("倉庫", 30, 0),      // 採取の食料が増える
        new("鍛冶場", 20, 10),   // 鍛冶の生産が 2 倍になる
        new("集会所", 35, 0),    // 交流で親密度が育ちやすくなる
        new("診療所", 30, 0),    // 看病と自然回復が強くなる
        new("図書館", 40, 0),    // 知識の蓄積：発見確率が上がる（本の仕組みの土台）
    };

    private static readonly string[] Syllables =
    {
        "ア", "ル", "カ", "セ", "ミ", "ラ", "ノ", "エ", "ト", "ユ",
        "キ", "サ", "レ", "オ", "フィ", "ド", "グ", "ハ", "メ", "ン",
    };

    private readonly SimulationConfig _config;
    private readonly DeterministicRandom _residentRng;
    private readonly DeterministicRandom _eventRng;

    private readonly List<Resident> _residents = new();
    private readonly List<LogEntry> _log = new();
    private readonly List<string> _discovered = new();
    private int _nextId = 1;

    private long _wood;
    private long _food;
    private long _ore;
    private long _tools;
    private long _development;

    private int _totalBirths;
    private int _totalDeaths;
    private int _maxPopulation;

    private readonly List<string> _buildingsCompleted = new();
    private int _projectProgress;
    private int _extraHouses;

    private bool HasBuilding(string name) => _buildingsCompleted.Contains(name);

    /// <summary>現在進行中の建築プロジェクト。全て建った後は「住居の増築」が繰り返し出る。</summary>
    private BuildingDef CurrentProject()
    {
        foreach (var def in BuildingDefs)
        {
            if (!HasBuilding(def.Name)) return def;
        }
        return new BuildingDef("住居の増築", 25, 0);
    }

    public Simulation(SimulationConfig config)
    {
        _config = config;
        // 世界生成用ストリームは将来の地形生成で使う。今は住人／イベントの 2 本。
        _residentRng = DeterministicRandom.FromSeed(config.Seed, "residents");
        _eventRng = DeterministicRandom.FromSeed(config.Seed, "events");
    }

    public SimulationResult Run()
    {
        GenerateInitialResidents();
        _wood = 50;
        _food = 100;
        _maxPopulation = _residents.Count;
        AddLog(0, "A", $"アース開始（Seed={_config.Seed}, 初期人口 {_residents.Count} 人）");

        int day = 0;
        while (day < _config.MaxDays)
        {
            day++;
            StepDay(day);

            int alive = AliveCount();
            if (alive > _maxPopulation) _maxPopulation = alive;
            if (alive == 0)
            {
                AddLog(day, "S", $"{day} 日目（{day / 365} 年目）に全滅した");
                break;
            }
        }

        var result = new SimulationResult
        {
            Seed = _config.Seed,
            DaysSurvived = day,
            MaxPopulation = _maxPopulation,
            FinalPopulation = AliveCount(),
            TotalBirths = _totalBirths,
            TotalDeaths = _totalDeaths,
            DiscoveredItems = _discovered.ToList(),
            CivilizationLevel = CivilizationLevel(),
            FinalWood = _wood,
            FinalFood = _food,
            FinalOre = _ore,
            FinalTools = _tools,
            BuildingsCompleted = _buildingsCompleted.ToList(),
            ExtraHouses = _extraHouses,
            Log = _log.ToList(),
            ResultHash = "",
        };
        return result with { ResultHash = ComputeHash(result) };
    }

    // ---------- 初期生成 ----------

    private void GenerateInitialResidents()
    {
        for (int i = 0; i < _config.InitialPopulation; i++)
        {
            _residents.Add(CreateResident(ageDays: GenerateInitialAgeDays()));
        }
    }

    /// <summary>MVP の年齢分布案：18〜45 歳中心、少数だけ子供・老人（下書きの提案）。</summary>
    private int GenerateInitialAgeDays()
    {
        int roll = _residentRng.NextInt(100);
        int years = roll switch
        {
            < 80 => _residentRng.NextInt(18, 46),
            < 90 => _residentRng.NextInt(6, 18),
            _ => _residentRng.NextInt(46, 66),
        };
        return years * 365 + _residentRng.NextInt(365);
    }

    private Resident CreateResident(int ageDays, Resident? father = null, Resident? mother = null)
    {
        var rng = _residentRng;
        var sex = rng.NextInt(2) == 0 ? Sex.Male : Sex.Female;

        Personality personality;
        double height;
        if (father is not null && mother is not null)
        {
            personality = InheritPersonality(father.Personality, mother.Personality);
            height = (father.HeightCm + mother.HeightCm) / 2 + rng.NextInt(-8, 9);
        }
        else
        {
            personality = new Personality
            {
                Obsession = rng.NextPercent(),
                Boredom = rng.NextPercent(),
                Empathy = rng.NextPercent(),
                Emotionality = rng.NextPercent(),
                Intuition = rng.NextPercent(),
                Sociability = rng.NextPercent(),
                Aggression = rng.NextPercent(),
                Conservatism = rng.NextPercent(),
                Optimism = rng.NextPercent(),
                Curiosity = rng.NextPercent(),
                Cooperativeness = rng.NextPercent(),
                Greed = rng.NextPercent(),
                RiskTolerance = rng.NextPercent(),
                Planning = rng.NextPercent(),
            };
            height = sex == Sex.Male ? 155 + rng.NextDouble() * 30 : 145 + rng.NextDouble() * 25;
        }

        return new Resident
        {
            Id = _nextId++,
            Name = GenerateName(),
            Sex = sex,
            AgeDays = ageDays,
            HeightCm = height,
            WeightKg = Math.Max(12, height - 105 + rng.NextInt(-8, 9)),
            Health = 60 + rng.NextDouble() * 35,
            Stamina = 60 + rng.NextDouble() * 35,
            Immunity = rng.NextPercent(),
            Fertility = rng.NextPercent(),
            Personality = personality,
        };
    }

    private Personality InheritPersonality(Personality a, Personality b)
    {
        int Mix(int x, int y) => Math.Clamp((x + y) / 2 + _residentRng.NextInt(-15, 16), 0, 100);
        return new Personality
        {
            Obsession = Mix(a.Obsession, b.Obsession),
            Boredom = Mix(a.Boredom, b.Boredom),
            Empathy = Mix(a.Empathy, b.Empathy),
            Emotionality = Mix(a.Emotionality, b.Emotionality),
            Intuition = Mix(a.Intuition, b.Intuition),
            Sociability = Mix(a.Sociability, b.Sociability),
            Aggression = Mix(a.Aggression, b.Aggression),
            Conservatism = Mix(a.Conservatism, b.Conservatism),
            Optimism = Mix(a.Optimism, b.Optimism),
            Curiosity = Mix(a.Curiosity, b.Curiosity),
            Cooperativeness = Mix(a.Cooperativeness, b.Cooperativeness),
            Greed = Mix(a.Greed, b.Greed),
            RiskTolerance = Mix(a.RiskTolerance, b.RiskTolerance),
            Planning = Mix(a.Planning, b.Planning),
        };
    }

    private string GenerateName()
    {
        int parts = _residentRng.NextInt(2, 4);
        var sb = new StringBuilder();
        for (int i = 0; i < parts; i++) sb.Append(Syllables[_residentRng.NextInt(Syllables.Length)]);
        return sb.ToString();
    }

    // ---------- 1 Tick（1 日） ----------

    private void StepDay(int day)
    {
        // 1. 健康・老化
        foreach (var r in Alive())
        {
            r.AgeDays++;
            r.Hunger += 15;
            if (r.Hunger >= 100) r.Health -= 2;
            if (r.IsSick) r.Health -= 1.0;
            if (r.IsInjured) r.Health -= 0.5;
            if (r.AgeYears >= 70) r.Health -= 0.05 * (r.AgeYears - 69);

            // 自然回復：病気・怪我は日数で治ることもある。健康なら少しずつ回復
            // 診療所があると病気の自然回復が速い
            if (r.IsSick && _eventRng.Chance(HasBuilding("診療所") ? 0.14 : 0.08)) r.IsSick = false;
            if (r.IsInjured && _eventRng.Chance(0.15)) r.IsInjured = false;
            if (!r.IsSick && !r.IsInjured && r.Hunger < 80)
                r.Health = Math.Min(100, r.Health + 1.0);

            // 発病：免疫が低いほどかかりやすい。木の柵があると街が守られて下がる
            double sickChance = (0.003 * (100 - r.Immunity) / 100.0 + 0.0002) * (HasBuilding("木の柵") ? 0.7 : 1.0);
            if (!r.IsSick && _eventRng.Chance(sickChance))
            {
                r.IsSick = true;
                AddLog(day, "B", $"{r.Name} が病気になった");
            }
        }

        // 食事：1 人 1 日 1 食（在庫があれば自動で消費）
        foreach (var r in Alive())
        {
            if (_food > 0)
            {
                _food--;
                r.Hunger = Math.Max(0, r.Hunger - 90);
            }
        }

        // 燃料：囲炉裏や鍛冶の燃料として、木材が毎日少しずつ消費される
        _wood = Math.Max(0, _wood - (AliveCount() + 9) / 10);

        // 2〜3. 行動選択と解決（Utility AI）
        var newborns = new List<Resident>();
        foreach (var r in Alive())
        {
            string action = ChooseAction(r);
            if (action == r.LastAction) r.SameActionDays++; else { r.LastAction = action; r.SameActionDays = 1; }
            ExecuteAction(day, r, action, newborns);
        }
        _residents.AddRange(newborns);

        // 出産判定（結婚している女性、1 日単位の確率）
        foreach (var mother in Alive().Where(r => r.Sex == Sex.Female && r.PartnerId is not null).ToList())
        {
            if (mother.AgeYears is < 18 or > 42) continue;
            var father = _residents.FirstOrDefault(x => x.Id == mother.PartnerId && x.IsAlive);
            if (father is null) continue;
            double p = 0.0035 * (mother.Fertility / 70.0) * (father.Fertility / 70.0)
                       * (HasBuilding("住居") ? 1.3 : 1.0);
            if (!_eventRng.Chance(p)) continue;

            var child = CreateResident(ageDays: 0, father: father, mother: mother);
            newborns.Add(child);
            mother.ChildrenCount++;
            father.ChildrenCount++;
            _totalBirths++;
            AddLog(day, "A", $"{mother.Name} と {father.Name} の間に {child.Name} が生まれた");
        }
        _residents.AddRange(newborns);

        // 4. 死亡判定
        foreach (var r in Alive().ToList())
        {
            string? cause = null;
            if (r.Health <= 0) cause = r.IsSick ? "病気" : "衰弱";
            else if (r.AgeYears > 60 && _eventRng.Chance((r.AgeYears - 60) * 0.00035 * ((100 - r.Health) / 50.0 + 0.4)))
                cause = "老衰";
            if (cause is null) continue;

            r.IsAlive = false;
            r.DeathDay = day;
            r.DeathCause = cause;
            _totalDeaths++;
            if (r.PartnerId is int pid)
            {
                var partner = _residents.FirstOrDefault(x => x.Id == pid);
                if (partner is not null) partner.PartnerId = null;
                r.PartnerId = null;
            }
            AddLog(day, "A", $"{r.Name} が{cause}で亡くなった（{r.AgeYears} 歳）");
        }

        // 5. 文明レベルは発見数と発展度から都度算出する（ComputeHash 参照）
    }

    private string ChooseAction(Resident r)
    {
        var p = r.Personality;
        bool adult = r.IsAdult;
        bool anyoneSick = _residents.Any(x => x.IsAlive && (x.IsSick || x.IsInjured) && x.Id != r.Id);

        var scores = new List<(string Action, double Score)>
        {
            ("休息", r.Fatigue * 0.8 + (100 - r.Stamina) * 0.5 + r.Stress * 0.2 + 5),
            ("様子見", p.Conservatism * 0.5 + p.Emotionality * 0.2 + 5),
            ("訓練", adult ? p.Obsession * 0.5 + p.Curiosity * 0.2 : 15),
        };
        if (adult)
        {
            scores.Add(("採取", p.Planning * 0.4 + p.Greed * 0.4 + (_food < AliveCount() * 3 ? 25 : 0) + 10));
            // 採掘：鉱石が十分ある時は掘らない（掘りすぎ防止）
            if (_ore < AliveCount() * 3 + 20)
                scores.Add(("採掘", p.Curiosity * 0.3 + p.RiskTolerance * 0.4 + p.Greed * 0.3 + 5));
            scores.Add(("探索", p.Curiosity * 0.8 + p.RiskTolerance * 0.5 - (100 - r.Health) * 0.3));
            scores.Add(("交流", p.Sociability * 0.6 + p.Empathy * 0.3 + (r.PartnerId is null ? 20 : 0) + 5));
            // 鍛冶：道具が人口分より少ない時だけ（作りすぎ防止）
            if (_ore > 0 && _tools < AliveCount()) scores.Add(("鍛冶", p.Planning * 0.5 + p.Cooperativeness * 0.3 + p.Greed * 0.2 + 5));
            // 建築：木材があり、プロジェクトが鉱石待ちで止まっていない時だけ。
            // 増築は「住む人が増えた時」だけ必要になる
            var project = CurrentProject();
            bool waitingForOre = _projectProgress >= project.WoodCost && _ore < project.OreCost;
            bool extraHouseNeeded = project.Name != "住居の増築" || AliveCount() > 20 + _extraHouses * 5;
            if (_wood > 0 && !waitingForOre && extraHouseNeeded)
                scores.Add(("建築", p.Planning * 0.5 + p.Cooperativeness * 0.3 + p.Obsession * 0.2 + 8));
            if (anyoneSick) scores.Add(("看病", p.Empathy * 0.6 + p.Planning * 0.2 + 10));
        }

        string best = "休息";
        double bestScore = double.MinValue;
        foreach (var (action, score) in scores)
        {
            double s = score;
            // 飽きやすさ：同じ行動が続くとスコアが下がる
            if (action == r.LastAction) s -= p.Boredom * 0.06 * r.SameActionDays;
            // 小さな決定論的ゆらぎ（イベント乱数、Id 順に消費するので決定的）
            s += _eventRng.NextDouble() * 4;
            if (s > bestScore) { bestScore = s; best = action; }
        }
        return best;
    }

    private void ExecuteAction(int day, Resident r, string action, List<Resident> newborns)
    {
        var rng = _eventRng;
        r.Fatigue += 8;
        r.Stamina = Math.Max(0, r.Stamina - 6);

        switch (action)
        {
            case "採取":
                r.Location = LocationType.Forest;
                if (r.Job == JobType.None) r.Job = JobType.Woodcutter;
                // 食料が足りない時は食料中心、足りている時は木材中心に採る
                if (_food < AliveCount() * 5)
                {
                    _food += rng.NextInt(2, 5) + (_tools > 0 ? 1 : 0) + (HasBuilding("倉庫") ? 1 : 0);
                    _wood += rng.NextInt(0, 2);
                }
                else
                {
                    _wood += rng.NextInt(1, 3) + (_tools > 0 ? 1 : 0);
                    _food += rng.NextInt(0, 2);
                }
                WearTool(rng);
                break;

            case "採掘":
                r.Location = LocationType.Cave;
                _ore += rng.NextInt(1, 3) + (_tools > 0 ? 1 : 0);
                if (rng.Chance(0.03 * InjuryMultiplier()))
                {
                    r.IsInjured = true;
                    r.Health -= rng.NextInt(8, 22);
                    AddLog(day, "B", $"{r.Name} が洞窟で怪我をした");
                }
                WearTool(rng);
                break;

            case "探索":
                r.Location = rng.NextInt(2) == 0 ? LocationType.Forest : LocationType.Cave;
                if (r.Job == JobType.None) r.Job = JobType.Explorer;
                if (rng.Chance(0.05 * InjuryMultiplier()))
                {
                    r.IsInjured = true;
                    r.Health -= _tools > 0 ? rng.NextInt(8, 20) : rng.NextInt(10, 26);
                    AddLog(day, "B", $"{r.Name} が探索中に怪我をした");
                }
                WearTool(rng);
                var remaining = DiscoveryPool.Where(x => !_discovered.Contains(x)).ToList();
                // 図書館があると知識が蓄積し、発見が生まれやすくなる
                if (remaining.Count > 0 && rng.Chance(0.008 * (HasBuilding("図書館") ? 1.5 : 1.0)))
                {
                    string found = remaining[rng.NextInt(remaining.Count)];
                    _discovered.Add(found);
                    _development += 10;
                    AddLog(day, "S", $"{r.Name} が「{found}」を発見した");
                }
                break;

            case "鍛冶":
                r.Location = LocationType.Town;
                r.Job = JobType.Blacksmith;
                if (_ore > 0 && _wood > 0)
                {
                    _ore--; _wood--;
                    // 鍛冶場があると 1 回で 2 個作れて発展度も大きい
                    _tools += HasBuilding("鍛冶場") ? 2 : 1;
                    _development += HasBuilding("鍛冶場") ? 4 : 2;
                }
                break;

            case "建築":
            {
                r.Location = LocationType.Town;
                r.Job = JobType.Carpenter;
                var project = CurrentProject();
                int invest = Math.Min(5, Math.Min(project.WoodCost - _projectProgress, (int)Math.Min(5, _wood)));
                if (invest > 0)
                {
                    _wood -= invest;
                    _projectProgress += invest;
                }
                if (_projectProgress >= project.WoodCost && _ore >= project.OreCost)
                {
                    _ore -= project.OreCost;
                    _projectProgress = 0;
                    _development += 5;
                    if (project.Name == "住居の増築")
                    {
                        _extraHouses++;
                        AddLog(day, "A", $"住居が増築された（{r.Name} ら大工の仕事、増築 {_extraHouses} 棟目）");
                    }
                    else
                    {
                        _buildingsCompleted.Add(project.Name);
                        AddLog(day, "A", $"「{project.Name}」が完成した（{r.Name} ら大工の仕事）");
                    }
                }
                break;
            }

            case "交流":
            {
                r.Location = LocationType.Town;
                var others = Alive().Where(x => x.Id != r.Id).ToList();
                if (others.Count == 0) break;
                // 既に親しい相手がいれば、その人と過ごすことが多い（関係が育つ）
                var closest = others.OrderByDescending(x => r.Affinity.GetValueOrDefault(x.Id))
                                    .ThenBy(x => x.Id).First();
                var other = r.Affinity.GetValueOrDefault(closest.Id) >= 20 && rng.Chance(0.6)
                    ? closest
                    : others[rng.NextInt(others.Count)];
                int gain = rng.NextInt(6, 15);
                if (HasBuilding("集会所")) gain = (int)(gain * 1.5);
                r.Affinity[other.Id] = r.Affinity.GetValueOrDefault(other.Id) + gain;
                other.Affinity[r.Id] = other.Affinity.GetValueOrDefault(r.Id) + gain;

                if (r.IsAdult && other.IsAdult && r.Sex != other.Sex
                    && r.PartnerId is null && other.PartnerId is null
                    && r.Affinity.GetValueOrDefault(other.Id) >= 50)
                {
                    r.PartnerId = other.Id;
                    other.PartnerId = r.Id;
                    AddLog(day, "A", $"{r.Name} と {other.Name} が結婚した");
                }
                break;
            }

            case "看病":
            {
                var target = Alive().Where(x => (x.IsSick || x.IsInjured) && x.Id != r.Id)
                                      .OrderBy(x => x.Id).FirstOrDefault();
                if (target is not null)
                {
                    bool clinic = HasBuilding("診療所");
                    target.Health = Math.Min(100, target.Health + (clinic ? 14 : 8));
                    if (target.IsSick && rng.Chance(clinic ? 0.5 : 0.3)) target.IsSick = false;
                    if (target.IsInjured && rng.Chance(clinic ? 0.6 : 0.4)) target.IsInjured = false;
                }
                break;
            }

            case "訓練":
                r.Stress = Math.Max(0, r.Stress - 3);
                break;

            case "休息":
                r.Location = LocationType.Town;
                r.Stamina = Math.Min(100, r.Stamina + 30 + Math.Min(10, _extraHouses * 2));
                r.Fatigue = Math.Max(0, r.Fatigue - 40);
                r.Health = Math.Min(100, r.Health + 1.5 + (HasBuilding("住居") ? 1.0 : 0));
                break;

            default: // 様子見
                r.Stress = Math.Max(0, r.Stress - 5);
                break;
        }
    }

    // ---------- 補助 ----------

    /// <summary>道具（装備）の在庫が多いほど、採掘・探索の怪我率が下がる。</summary>
    private double InjuryMultiplier() => _tools >= 10 ? 0.5 : _tools > 0 ? 0.75 : 1.0;

    /// <summary>道具は作業でたまに壊れる（鍛冶の需要が続くように）。</summary>
    private void WearTool(DeterministicRandom rng)
    {
        if (_tools > 0 && rng.Chance(0.03)) _tools--;
    }

    private List<Resident> Alive() => _residents.Where(r => r.IsAlive).OrderBy(r => r.Id).ToList();
    private int AliveCount() => _residents.Count(r => r.IsAlive);

    private int CivilizationLevel()
    {
        int score = _discovered.Count * 10 + (int)(_development / 30);
        return score switch { >= 90 => 3, >= 50 => 2, >= 20 => 1, _ => 0 };
    }

    public static string CivilizationName(int level) => CivilizationNames[Math.Clamp(level, 0, 3)];

    private void AddLog(int day, string level, string text) => _log.Add(new LogEntry(day, level, text));

    private static string ComputeHash(SimulationResult result)
    {
        var sb = new StringBuilder();
        sb.Append(result.Seed).Append('|')
          .Append(result.DaysSurvived).Append('|')
          .Append(result.MaxPopulation).Append('|')
          .Append(result.TotalBirths).Append('|')
          .Append(result.TotalDeaths).Append('|')
          .Append(string.Join(",", result.DiscoveredItems)).Append('|')
          .Append(result.CivilizationLevel).Append('|')
          .Append(string.Join(",", result.BuildingsCompleted)).Append('|')
          .Append(result.ExtraHouses).Append('|')
          .Append(result.FinalTools);
        foreach (var e in result.Log) sb.Append('\n').Append(e.Day).Append(':').Append(e.Level).Append(':').Append(e.Text);

        ulong hash = 14695981039346656037UL;
        foreach (char c in sb.ToString())
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }
}
