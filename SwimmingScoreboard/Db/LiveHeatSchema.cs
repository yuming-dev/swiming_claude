namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 当前组库 表结构（SQLite，单独文件 current_heat.db）      2026-08-24
    //
    // 只装正在比赛的这一组。整个文件几十 KB，而竞赛管理库是 MB 量级 ——
    // 比赛中每次触板只动这个小库，不再碰大库。这是内存暴涨那个问题的正解：
    //     改前：每次触板 BuildCurrentPackage + SerializeObject(1.2MB) + JObject.Parse
    //           ≈ 20 MB；400 米一组上百次事件 ≈ 2 GB，且 1.2 MB 字符串进大对象堆
    //           （LOH 不压缩，所以清内存也降不下来）
    //     改后：每次触板只 UPDATE 本库一行
    //
    // 生命周期：
    //     选组 / 准备就绪 → Open() 建库，从 meet.db 灌入本组名单
    //     比赛中          → 只写本库（触板 / 盲表 / 反应时 / 分段 / 状态）
    //     确认本组成绩    → CommitToMeetDb() 按 heat_entry_id 写回 meet.db
    //     下一组          → Reset() 清空重建，内存和文件都不累积
    //
    // 所以「比赛中锁定」锁的只是这一组；其它组、其它项目在 meet.db 里照常
    // 读写，编排端可以正常改别的组 —— 这是用户明确要求的，不能比赛时全锁死。
    // ══════════════════════════════════════════════════════════════════════
    public static class LiveHeatSchema
    {
        public const int Version = 2;

        public const string Sql = @"
-- ══ 本组标识 + 比赛状态（只有一行）══════════════════════════════════
CREATE TABLE IF NOT EXISTS live_heat (
    id               INTEGER PRIMARY KEY CHECK (id = 1),
    meet_round_id    INTEGER,               -- 指向 meet.db 的 rounds.id
    meet_event_id    INTEGER,               -- 指向 meet.db 的 events.id
    ev_num           INTEGER,
    session_name     TEXT,
    age_group        TEXT,
    gender           TEXT,
    event_name       TEXT,
    distance         INTEGER,               -- 每棒距离
    stroke           TEXT,
    relay_legs       INTEGER DEFAULT 1,
    stage            TEXT,
    heat             INTEGER,
    total_heats      INTEGER,
    lane_count       INTEGER,
    pool_length      INTEGER,
    total_distance   INTEGER,               -- distance × max(relay_legs,1)，分段数由它算
    race_state       TEXT,                  -- Waiting / Ready / Racing / Finished
    gun_time         TEXT,
    opened_at        TEXT,
    result_confirmed INTEGER DEFAULT 0,
    schema_version   INTEGER NOT NULL
);

-- ══ 本组各泳道 ══════════════════════════════════════════════════════
-- heat_entry_id 指回 meet.db 的 heat_entries.id，确认成绩时按它写回，
-- 不用再靠（项目+性别+组别+赛次+组次+泳道）去反查 —— 那正是出过错的地方。
CREATE TABLE IF NOT EXISTS live_lanes (
    lane              INTEGER PRIMARY KEY,  -- 0 是合法泳道
    heat_entry_id     INTEGER,
    bib_number        TEXT,
    name              TEXT,                 -- 个人是姓名；接力是队名
    leg_names         TEXT,                 -- 接力各棒姓名，逗号分隔（大屏姓名列用）
    country           TEXT,
    age_category      TEXT,
    gender            TEXT,
    seed_time         TEXT,
    is_relay          INTEGER DEFAULT 0,

    final_time        REAL DEFAULT 0,
    rank              INTEGER DEFAULT 0,
    status            TEXT,                 -- DSQ / DNS / DNF / TRI
    record_note       TEXT,
    timing_source     TEXT,
    reaction_time     REAL DEFAULT 0,
    touchpad_time     REAL DEFAULT 0,
    start_block_time  REAL DEFAULT 0,
    pb1_time          REAL DEFAULT 0,
    pb2_time          REAL DEFAULT 0,
    pb3_time          REAL DEFAULT 0,
    manual_left       REAL DEFAULT 0,
    manual_right      REAL DEFAULT 0,
    current_lap       INTEGER DEFAULT 0,
    is_finished       INTEGER DEFAULT 0,
    is_false_start    INTEGER DEFAULT 0,
    dsq_code          TEXT,
    dsq_leg           INTEGER DEFAULT 0,    -- 接力 DSQ 判在第几棒
    dsq_backup_splits TEXT,                 -- 撤销 DSQ 时还原用
    -- 2026-09-18 见 MeetSchema.heat_entries 同名几列的说明：撤销 DSQ 要还原的不止分段。
    dsq_backup_final_time       REAL DEFAULT 0,
    dsq_backup_start_block_time REAL DEFAULT 0,
    dsq_backup_leg_reaction_times TEXT      -- JSON 数组
);

-- ══ 分段 ════════════════════════════════════════════════════════════
-- 1500 米一个人 29 段，比赛中每触一次板只插/改其中一行。
CREATE TABLE IF NOT EXISTS live_splits (
    lane            INTEGER NOT NULL REFERENCES live_lanes(lane) ON DELETE CASCADE,
    distance        INTEGER NOT NULL,
    cumulative_time REAL DEFAULT 0,
    lap_time        REAL DEFAULT 0,
    rank_at         INTEGER DEFAULT 0,      -- 途中名次
    timing_source   TEXT,
    is_manual       INTEGER DEFAULT 0,
    PRIMARY KEY (lane, distance)
);

-- ══ 接力棒次 ════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS live_legs (
    lane            INTEGER NOT NULL REFERENCES live_lanes(lane) ON DELETE CASCADE,
    leg_order       INTEGER NOT NULL,
    athlete_id      INTEGER,
    swimmer_name    TEXT,
    swimmer_bib     TEXT,
    swimmer_id_no   TEXT,
    swimmer_birth   TEXT,
    reaction_time   REAL DEFAULT 0,         -- 交接反应时，可为负
    leg_time        REAL DEFAULT 0,
    cumulative_time REAL DEFAULT 0,
    rank_at         INTEGER DEFAULT 0,
    PRIMARY KEY (lane, leg_order)
);

-- ══ 本组原始计时流水（排查用，随本组一起清）═══════════════════════
-- 出过成绩对不上的争议时要能回看原始帧；只留本组，不累积。
CREATE TABLE IF NOT EXISTS live_timing_log (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    at       TEXT,
    lane     INTEGER,
    cmd_type TEXT,
    side     TEXT,
    seconds  REAL,
    raw      TEXT
);
";
    }
}
