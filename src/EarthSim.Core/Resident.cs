namespace EarthSim.Core;

/// <summary>性格パラメータ（各 0〜100）。仕様 v0 で決めた 14 項目。</summary>
public sealed class Personality
{
    public int Obsession { get; set; }        // 執着・こだわり傾向
    public int Boredom { get; set; }          // 飽きやすさ／興味の切替
    public int Empathy { get; set; }          // 共感性
    public int Emotionality { get; set; }     // 情動感受性
    public int Intuition { get; set; }        // 直感志向
    public int Sociability { get; set; }      // 社交性
    public int Aggression { get; set; }       // 攻撃性
    public int Conservatism { get; set; }     // 保守性
    public int Optimism { get; set; }         // 楽観性
    public int Curiosity { get; set; }        // 探求心
    public int Cooperativeness { get; set; }  // 協調性
    public int Greed { get; set; }            // 利益志向
    public int RiskTolerance { get; set; }    // 危険許容度
    public int Planning { get; set; }         // 計画性

    public Personality Clone() => (Personality)MemberwiseClone();
}

public enum Sex { Male, Female }

public enum JobType { None, Woodcutter, Blacksmith, Explorer, Carpenter, Pharmacist }

public enum LocationType { Town, Forest, Mine, Cave, Dungeon }

/// <summary>
/// 図書館の本から得る知識。住人は最初は全て持っていない（知識の基本原則）。
/// 前半 5 つが基本書、後半 3 つが建築シリーズ（対応する建物を建てるための知識）。
/// </summary>
public enum KnowledgeType
{
    Blacksmithing, Mining, Logging, Carpentry, Pharmacy,
    PharmacyBuilding, WorkshopBuilding, HouseBuilding,
}

/// <summary>住人 1 人。HP/MP ではなく身体・性格・状態で管理する（仕様 v0）。</summary>
public sealed class Resident
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Sex Sex { get; init; }

    /// <summary>年齢（日数）。1 Tick = 1 日で加算される。</summary>
    public int AgeDays { get; set; }
    public int AgeYears => AgeDays / 365;

    // 身体
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public double Health { get; set; } = 80;   // 健康度 0〜100
    public double Stamina { get; set; } = 80;  // 体力 0〜100
    public int Immunity { get; set; }          // 免疫 0〜100
    public int Fertility { get; set; }         // 妊孕性 0〜100

    // 性格
    public required Personality Personality { get; set; }

    // 状態（日々変わる）
    public double Fatigue { get; set; }
    public double Hunger { get; set; }
    public double Stress { get; set; }
    public JobType Job { get; set; } = JobType.None;
    public LocationType Location { get; set; } = LocationType.Town;
    public int? PartnerId { get; set; }
    public int ChildrenCount { get; set; }
    public bool IsSick { get; set; }
    public bool IsInjured { get; set; }

    /// <summary>飽きやすさ表現用：直前と同じ行動が何日続いたか。</summary>
    public string? LastAction { get; set; }
    public int SameActionDays { get; set; }

    /// <summary>交流で育つ関係値。相手 Id → 親密度（決定的に Id 順で処理する）。</summary>
    public Dictionary<int, int> Affinity { get; } = new();

    /// <summary>習得済みの知識（図書館の本で学ぶ）。生まれた時は空。</summary>
    public HashSet<KnowledgeType> Knowledge { get; } = new();

    /// <summary>学習中の知識 → 進捗（100 で習得）。</summary>
    public Dictionary<KnowledgeType, int> StudyProgress { get; } = new();

    public bool IsAlive { get; set; } = true;
    public int? DeathDay { get; set; }
    public string? DeathCause { get; set; }

    public bool IsAdult => AgeYears >= 18;
}
