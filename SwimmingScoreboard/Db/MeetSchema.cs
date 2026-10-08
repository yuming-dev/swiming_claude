namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 竞赛管理库 表结构（SQLite，meet.db）                      2026-08-24
    //
    // 设计依据：全运会游泳竞委会的正式报表（竞赛日程 / 参赛名单 / 比赛结果 /
    // 比赛结果摘要 / 名次公告），加上青少年·U系列比赛的两条硬要求：
    //     1. 必须有组别（全运会没有组别，青少年赛没组别就没法录取）
    //     2. 多个组别、甚至男女，可以混在同一组下水比赛，赛完再把成绩分开排名
    //   第 2 条决定了整个骨架：物理的「组」不属于任何单一项目。
    //
    // 【铁律一】一条泳道 = 一行。检录、成绩、名次、DSQ、破纪录标注、争议
    //   全写在 heat_entries 那一行上。不搞一对一拆表。
    //
    // 【铁律二】只做部分读写。要哪几行取哪几行，改哪一行更新哪一行。
    //   —— 用户原话：运行哪一部就调哪一部，不要整包数据库都调，都要重写。
    //
    // 【正在进行的那一组不在这个库里】另有单独文件 current_heat.db，
    //   见 LiveHeatSchema。比赛中只写那个小库，确认成绩才回写这里。
    // ══════════════════════════════════════════════════════════════════════
    public static class MeetSchema
    {
        public const int Version = 3;

        public const string Sql = @"
PRAGMA foreign_keys = ON;

-- ══ 1. 赛事（只有一行）═════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS competition (
    id                       INTEGER PRIMARY KEY CHECK (id = 1),
    name                     TEXT NOT NULL,
    name_en                  TEXT,
    mode                     TEXT,          -- domestic / international
    rule                     TEXT,          -- 国际比赛 / 国内大赛 / U系列青少年游泳比赛
    use_age_group            INTEGER DEFAULT 1,  -- 青少年·U系列必须为 1；全运会一类无组别的置 0
    start_date               TEXT,
    end_date                 TEXT,
    location                 TEXT,          -- 场馆，报表右上角那行
    city                     TEXT,
    -- 泳池。原来单独放在包外面，换台机器就丢；泳道数直接决定排道和大屏行数。
    pool_length              INTEGER DEFAULT 50,
    lane_count               INTEGER DEFAULT 8,
    lane_numbers             TEXT,          -- 逗号分隔。注意 0 是真泳道（全运会 0~9 十道）
    start_position           TEXT,          -- 发令端 left / right
    organizer                TEXT,
    host                     TEXT,
    technical_delegate       TEXT,
    referee                  TEXT,
    starter                  TEXT,
    arbiter                  TEXT,
    chief_judge              TEXT,
    display_record_label     TEXT,          -- 大屏主纪录简称
    display_record_type_name TEXT,
    schema_version           INTEGER NOT NULL
);

-- ══ 2. 场次（竞赛日程左侧那两列：日期 + 上午/下午/晚间）════════════
CREATE TABLE IF NOT EXISTS sessions (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    no         INTEGER,                     -- 全场排序，第几个场次
    date       TEXT,                        -- 2025-11-10
    name       TEXT,                        -- 上午 / 下午 / 晚间
    start_time TEXT,
    note       TEXT,
    UNIQUE(date, name)
);

-- ══ 3. 项目 ════════════════════════════════════════════════════════
-- 项目 = 录取单元。名次、晋级、破纪录、得分、发奖，全按它算。
-- 项目号跨赛次共享：竞赛日程里「项目号 2 男子400米自由泳」在预赛、决赛、
-- 颁奖仪式三行里都是 2，所以赛次不属于项目，属于日程行(rounds)。
--
-- 五要素中的 组别·性别·距离·泳姿 在这里，赛次在 rounds，组次在 heats。
-- 四个要素各自成列，不再糊成一个字符串 —— 之前「男女」混合接力那一串
-- 显示/打印/存盘的错，根子就是要素没落地成列，每处代码各自解析字符串。
--
-- 注意 gender='男女' 指的是「项目本身就是男女混合的」（混合泳接力 2男2女），
-- 跟「男子项目和女子项目混编在同一组下水」是两回事，后者由 round_events 表达。
CREATE TABLE IF NOT EXISTS events (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    ev_num       INTEGER,                   -- 项目号，报表上那个大号数字
    age_group    TEXT NOT NULL DEFAULT '',  -- 组别。青少年·U系列必填；无组别的赛事留空串
    gender       TEXT,                      -- 性别：男 / 女 / 男女
    distance     INTEGER,                   -- 距离(米)。接力填每棒距离，4x100 就填 100
    stroke       TEXT,                      -- 自由泳/仰泳/蛙泳/蝶泳/个人混合泳/混合泳接力
    relay_legs   INTEGER DEFAULT 1,         -- 棒数：个人=1，接力=4
    event_name   TEXT,                      -- 显示名。只用于打印和兼容旧档案，
                                            -- 任何判断都不许解析它
    award_places INTEGER DEFAULT 8,         -- 录取名次
    note         TEXT,
    UNIQUE(age_group, gender, distance, stroke, relay_legs)
);
CREATE INDEX IF NOT EXISTS ix_events_num ON events(ev_num);

-- ══ 4. 赛次 = 一次实际下水的比赛安排 ════════════════════════════════
-- 这是日程上的一行，也是物理意义上的一次比赛。只记「游什么距离、什么泳姿、
-- 哪个赛次、什么时候游、几个组」，不绑定具体项目 ——
-- 因为青少年赛常把多个组别、甚至男女混编在同一组下水，一个组里坐着
-- 少年组的也坐着青年组的，这个组归不到任何单一项目名下。
-- 它涵盖哪些项目，见 round_events。
--
-- 颁奖仪式也占日程一行（19:40 男子400米自由泳 颁奖仪式），用 is_ceremony 区分，
-- 它没有组数也没有分组名单。
CREATE TABLE IF NOT EXISTS rounds (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id  INTEGER REFERENCES sessions(id) ON DELETE SET NULL,
    stage       TEXT NOT NULL,              -- 预赛/半决赛/决赛/计时决赛/颁奖仪式
    distance    INTEGER,                    -- 同一赛次内必须一致，否则不可能同池比
    stroke      TEXT,
    relay_legs  INTEGER DEFAULT 1,
    ord         INTEGER DEFAULT 0,          -- 本场次内的先后
    time        TEXT,                       -- 开始时间 09:25
    heat_count  INTEGER DEFAULT 0,          -- 组数（物理的组数）
    lane_count  INTEGER,                    -- 本赛次道数（决赛可能只用 1~8）
    is_ceremony INTEGER DEFAULT 0,          -- 1 = 颁奖仪式，不比赛
    status      TEXT,                       -- 未开始 / 进行中 / 已结束
    title       TEXT,                       -- 日程上的显示标题（混编时如「男女50米自由泳」）
    note        TEXT
);
CREATE INDEX IF NOT EXISTS ix_rounds_session ON rounds(session_id, ord);

-- ══ 5. 赛次涵盖哪些项目（混编的关键）════════════════════════════════
-- 一个赛次可以同时录取多个项目：少年组男子50自 + 青年组男子50自 + 女子50自
-- 三个项目的人混编成 4 个组一起游完，成绩再按各自项目分开排名、分开录取。
--
-- 所以「组」和「项目」是多对多，中间靠泳道（heat_entries → entries → events）
-- 连起来：每条泳道的成绩归属于它报名的那个项目。
-- 排名、晋级、破纪录、得分，一律按项目算，跟同组坐着谁没关系。
CREATE TABLE IF NOT EXISTS round_events (
    round_id      INTEGER NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    event_id      INTEGER NOT NULL REFERENCES events(id) ON DELETE CASCADE,
    ord           INTEGER DEFAULT 0,        -- 打印顺序
    promote_count INTEGER DEFAULT 0,        -- 本项目晋级下阶段人数（打 Q 的个数），各组别各算各的
    reserve_count INTEGER DEFAULT 0,        -- 本项目替补人数（打 R 的个数）
    PRIMARY KEY (round_id, event_id)
);
-- 一个项目的同一个赛次只能出现在一个赛次行里，由代码保证（stage 在 rounds 上，
-- 这里加不了跨表唯一约束）。
CREATE INDEX IF NOT EXISTS ix_re_event ON round_events(event_id);

-- ══ 6. 参赛单位 ════════════════════════════════════════════════════
-- 团体总分并进来了：总分是单位的属性，不值得单开一张表。
-- 但分项得分要分列 —— 团体总分公告要逐项列个人分/接力分/破纪录加分/金银铜。
CREATE TABLE IF NOT EXISTS units (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    name              TEXT NOT NULL UNIQUE,
    short_name        TEXT,
    full_name         TEXT,
    leader            TEXT,
    coach             TEXT,
    doctor            TEXT,
    phone             TEXT,
    address           TEXT,
    bib_ranges        TEXT,                 -- 号段，如 101-150,301-320
    base_points       REAL DEFAULT 0,
    score             REAL DEFAULT 0,       -- 团体总分
    individual_points REAL DEFAULT 0,
    relay_points      REAL DEFAULT 0,
    record_bonus      REAL DEFAULT 0,
    gold              INTEGER DEFAULT 0,
    silver            INTEGER DEFAULT 0,
    bronze            INTEGER DEFAULT 0,
    score_rank        INTEGER DEFAULT 0,
    note              TEXT
);

-- ══ 7. 运动员 ══════════════════════════════════════════════════════
-- 去重规则（用户 2026-08-24 定）：只认身份证号 / 注册号；两者都为空就
-- 各算各的、绝不合并。同一单位真有两个同名运动员时，按姓名合并会把两个人
-- 的成绩混成一个人 —— 那是一块奖牌的事。
CREATE TABLE IF NOT EXISTS athletes (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    bib_number       TEXT,
    name             TEXT NOT NULL,
    name_en          TEXT,
    gender           TEXT,                  -- 男 / 女
    birth_date       TEXT,                  -- 摘要表要打出生日期；青少年赛还要靠它核组别
    id_number        TEXT,                  -- 身份证号
    unit_id          INTEGER REFERENCES units(id),
    joint_unit       TEXT,                  -- 联合培养单位。名次公告有这一列，逐人填
    coach            TEXT,                  -- 教练员。名次公告最后一列，逐人填
    phone            TEXT,
    csa_number       TEXT,                  -- 中国游泳协会注册号
    fina_number      TEXT,
    health_cert_date TEXT,
    age_category     TEXT,                  -- 注册组别（跟报名项目的组别可能不同，留档备查）
    note             TEXT
);
CREATE INDEX IF NOT EXISTS ix_athletes_unit ON athletes(unit_id);
CREATE INDEX IF NOT EXISTS ix_athletes_idno ON athletes(id_number);

-- ══ 8. 报名 ════════════════════════════════════════════════════════
-- 个人和接力队合成一张：有 athlete_id 就是个人，有 team_name 就是接力队。
-- 分成两张的代价是每条查询都要双 LEFT JOIN、每处代码都要判一次是不是接力
-- —— 旧程序里满地的 IsRelayProxy 特判，根子就在参赛主体分了两种。
--
-- 报名报的是「项目」，不含赛次。一个人预赛决赛都游，只有一条报名，
-- 成绩分别挂在两个赛次的分组行上，互不覆盖。
-- 报名挂在项目上，也就同时钉死了这个人算哪个组别、哪个性别 ——
-- 哪怕他跟别的组别混在同一组下水。
CREATE TABLE IF NOT EXISTS entries (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id           INTEGER NOT NULL REFERENCES events(id) ON DELETE CASCADE,
    athlete_id         INTEGER REFERENCES athletes(id) ON DELETE CASCADE,
    unit_id            INTEGER REFERENCES units(id),
    team_name          TEXT,                -- 有值 = 接力队
    bib_number         TEXT,                -- 本项目参赛号
    entry_time_seconds REAL DEFAULT 0,
    entry_time         TEXT,                -- NT 或 mm:ss.ff
    coach              TEXT,                -- 本项目教练（留空则用 athletes.coach）
    is_qualified       INTEGER DEFAULT 1,
    status             TEXT,                -- 弃权 / 退赛
    note               TEXT,
    CHECK ((athlete_id IS NOT NULL) OR (team_name IS NOT NULL))
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_entries_ath
    ON entries(event_id, athlete_id) WHERE athlete_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ix_entries_team
    ON entries(event_id, team_name)  WHERE team_name IS NOT NULL;

-- ══ 9. 组 ══════════════════════════════════════════════════════════
-- 物理的组：实际一起下水的那一批人。属于赛次，不属于项目 ——
-- 组里可能同时坐着少年组和青年组、男子和女子。
-- 取消/并组的组没有泳道行可挂，只能有张组表。顺带记发令时刻和确认时间。
CREATE TABLE IF NOT EXISTS heats (
    round_id      INTEGER NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    heat          INTEGER NOT NULL,
    state         TEXT DEFAULT 'normal',    -- normal / cancelled
    merged_into   INTEGER,                  -- 并入了第几组
    cancel_reason TEXT,
    cancelled_at  TEXT,
    gun_time      TEXT,                     -- 发令时刻
    started_at    TEXT,
    confirmed_at  TEXT,                     -- 成绩确认时间（确认后才算数）
    confirmed_by  TEXT,
    operator      TEXT,
    note          TEXT,
    PRIMARY KEY (round_id, heat)
);

-- ══ 10. 分组表 ═════════════════════════════════════════════════════
-- 一条泳道一行，这一道的全部东西都在这行上：检录、成绩、名次、晋级标记、
-- DSQ、破纪录标注、争议。不再有单独的成绩表 —— 成绩和泳道是一对一。
--
-- 这一道算哪个组别哪个性别，由 entry_id → entries.event_id 决定，
-- 跟同组别人无关。所以混编的组打印时每行显示各自的组别，
-- 排名时按各自项目分开排 —— 这就是「赛完再将成绩分开」。
--
-- 泳道可以为空：半决赛参赛名单末尾的「替补运动员 R1 / R2」没有道次，
-- 用 reserve_no 标 R1/R2。注意不能拿 lane=0 表示替补，0 是真泳道。
CREATE TABLE IF NOT EXISTS heat_entries (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    round_id          INTEGER NOT NULL REFERENCES rounds(id) ON DELETE CASCADE,
    heat              INTEGER,              -- 替补为空
    lane              INTEGER,              -- 替补为空；0 是合法泳道
    reserve_no        INTEGER,              -- R1=1 R2=2；正选为空
    entry_id          INTEGER NOT NULL REFERENCES entries(id) ON DELETE CASCADE,

    -- 本赛次的报名成绩。半决赛名单上那列「预赛成绩」就是它。
    seed_time_seconds REAL DEFAULT 0,
    seed_time         TEXT,

    -- 检录。原来只能塞进 Swimmer.Status，和 DSQ/DNS 挤一个字段，
    -- 检录标「未到」就和裁判判的 DNS 分不清了。
    checkin_status    TEXT,                 -- 空=未检录 present=已到 absent=未到 withdrawn=退赛
    checkin_at        TEXT,

    -- 晋级来源：这条决赛/半决赛记录是从哪条上一赛次记录上来的
    promoted_from     INTEGER REFERENCES heat_entries(id) ON DELETE SET NULL,
    promoted_rank     INTEGER DEFAULT 0,

    -- ── 成绩 ──
    final_time        REAL DEFAULT 0,
    rank              INTEGER DEFAULT 0,    -- 在【本项目 + 本赛次】里的跨组大排名。
                                            -- 混编时同组的别组别选手不参与这个排名。
                                            -- 组内名次不存，按同组成绩现算；并列的 = 前缀也现算
    promotion_mark    TEXT,                 -- Q=晋级下阶段  R=下阶段替补
    score             REAL DEFAULT 0,       -- 名次得分
    status            TEXT,                 -- DSQ / DNS / DNF / TRI
    dsq_code          TEXT,                 -- 犯规条款
    dsq_leg           INTEGER DEFAULT 0,    -- 接力 DSQ 判在第几棒：保留 1~(N-1) 棒分段，
                                            -- 清掉第 N 棒起。撤销 DSQ 要还原，所以备份原分段
    dsq_backup_splits TEXT,                 -- JSON 数组
    -- 2026-09-18 撤销 DSQ 要还原的不只是分段——最终成绩/出发反应时/接力各棒反应时
    --   同样得备份。这三列一直没加, dsq_backup_splits 也一直没人真的写过(建表时就晾在这,
    --   UpdateLane/CommitHeatFrom 都不碰它)——MainWindow.LaneResult.DsqBackup* 那份快照
    --   只活在内存和 JSON 存档里, 换机器/库回读撤销 DSQ 时把成绩恢复成 0, 状态却已经不是
    --   DSQ 了, 界面上看着像「参赛但没成绩」, 看不出哪里错了。
    dsq_backup_final_time      REAL DEFAULT 0,
    dsq_backup_start_block_time REAL DEFAULT 0,
    dsq_backup_leg_reaction_times TEXT,     -- JSON 数组
    record_note       TEXT,                 -- MR / =MR / NR …

    timing_source     TEXT,                 -- 触板 / 按钮 / 手计时
    reaction_time     REAL DEFAULT 0,
    touchpad_time     REAL DEFAULT 0,
    start_block_time  REAL DEFAULT 0,
    pb1_time          REAL DEFAULT 0,
    pb2_time          REAL DEFAULT 0,
    pb3_time          REAL DEFAULT 0,
    manual_left       REAL DEFAULT 0,
    manual_right      REAL DEFAULT 0,
    result_at         TEXT,

    note              TEXT,
    dispute_note      TEXT,                 -- 争议 / 申诉
    UNIQUE(round_id, heat, lane)            -- 一条泳道只能站一个人。混编时这条尤其要紧：
                                            -- 不同项目的人抢同一道，这里直接挡下。
                                            -- 替补 heat/lane 为 NULL，SQLite 里 NULL 互不相等，
                                            -- 多条替补不冲突
);
CREATE INDEX IF NOT EXISTS ix_he_round ON heat_entries(round_id, heat, lane);
CREATE INDEX IF NOT EXISTS ix_he_entry ON heat_entries(entry_id);

-- ══ 11. 接力棒次 ═══════════════════════════════════════════════════
-- 挂在分组行上，不是挂在接力队上 —— 预赛和决赛可以换人。名次公告里
-- 山东队列了决赛 4 棒 + 只游过预赛的 2 人，两批人都要记。
--
-- 赛前一小时才拿到名单，那时人可能还没进 athletes（临时替补），
-- 所以 athlete_id 允许为空，身份证/生日/参赛号先原样存着。
CREATE TABLE IF NOT EXISTS relay_legs (
    heat_entry_id   INTEGER NOT NULL REFERENCES heat_entries(id) ON DELETE CASCADE,
    leg_order       INTEGER NOT NULL,       -- 1..4
    athlete_id      INTEGER REFERENCES athletes(id),
    swimmer_name    TEXT,
    swimmer_bib     TEXT,
    swimmer_id_no   TEXT,
    swimmer_birth   TEXT,
    swimmer_gender  TEXT,                   -- 混合接力要核 2男2女
    reaction_time   REAL DEFAULT 0,         -- 第1棒是出发反应时，第2棒起是交接反应时。
                                            -- 可以是负数（四川队第2棒 -0.01），校验不许用 >0
    leg_time        REAL DEFAULT 0,         -- 本棒用时
    cumulative_time REAL DEFAULT 0,         -- 交接时累计
    rank_at         INTEGER DEFAULT 0,      -- 交接时名次，结果表上括号里那个数
    PRIMARY KEY (heat_entry_id, leg_order)
);

-- ══ 12. 分段 ═══════════════════════════════════════════════════════
-- 男子1500米自由泳一个人 29 个分段（50m..1450m），每段带途中名次 ——
-- 结果表上 50m (1)28.22 那个括号。所以必须是表，不能挤成一列：
-- 比赛中每次触板只更新其中一段，做成文本列就得整串读改写，
-- 那正是这次要根除的整包读写，只是缩小版。
CREATE TABLE IF NOT EXISTS splits (
    heat_entry_id   INTEGER NOT NULL REFERENCES heat_entries(id) ON DELETE CASCADE,
    distance        INTEGER NOT NULL,       -- 50 / 100 / … / 1450
    cumulative_time REAL DEFAULT 0,
    lap_time        REAL DEFAULT 0,
    rank_at         INTEGER DEFAULT 0,      -- 途中名次
    timing_source   TEXT,
    is_manual       INTEGER DEFAULT 0,
    PRIMARY KEY (heat_entry_id, distance)
);

-- ══ 13. 纪录 ═══════════════════════════════════════════════════════
-- 报表顶上那个纪录栏：WR/AR/NR/WJ/NJ 各一行，带成绩、运动员、队伍、
-- 创纪录地、创纪录日期。青少年赛还有分组别的赛会纪录，所以带 age_group。
-- 破纪录不覆盖旧行 —— 新破的加一行 is_current=1，旧行置 0 自动成为历史。
-- 这样证书上要写的「原纪录 X 由 Y 保持」就还在，不用另建破纪录台账。
CREATE TABLE IF NOT EXISTS records (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    abbr           TEXT,                    -- WR / AR / NR / WJ / NJ / MR / 省R
    record_type    TEXT,                    -- 世界纪录 / 亚洲纪录 / 全国纪录 / 赛会纪录 …
    ord            INTEGER DEFAULT 0,       -- 纪录栏里的显示顺序
    age_group      TEXT NOT NULL DEFAULT '',
    gender         TEXT,
    distance       INTEGER,
    stroke         TEXT,
    relay_legs     INTEGER DEFAULT 1,
    event_name     TEXT,
    time_seconds   REAL DEFAULT 0,
    holder_name    TEXT,                    -- 纪录运动员
    holder_country TEXT,                    -- 队伍
    date           TEXT,                    -- 创纪录日期
    location       TEXT,                    -- 创纪录地
    is_current     INTEGER DEFAULT 1,       -- 1=现行纪录  0=已被打破的历史行
    broken_by      INTEGER REFERENCES heat_entries(id) ON DELETE SET NULL,
    note           TEXT
);
CREATE INDEX IF NOT EXISTS ix_records_evt
    ON records(age_group, gender, distance, stroke, relay_legs, is_current);

-- ══ 14. 工作人员 / 裁判 ════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS staff (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    ord           INTEGER DEFAULT 0,
    name          TEXT,
    title         TEXT,
    group_name    TEXT,
    gender        TEXT,
    referee_level TEXT,
    country       TEXT,
    phone         TEXT,
    is_official   INTEGER DEFAULT 0,        -- 1 = 列入秩序册裁判员名单
    note          TEXT
);

-- ══ 15. 配置 ═══════════════════════════════════════════════════════
-- 团体计分名次分表、项目用时表、秩序册/成绩册排版、大屏纪录可选项、
-- 各下拉列表、秩序册向导草稿。这些本来就是整块用整块存的东西，拆成
-- 关系表没有意义，也没人按行查。按 key 存 JSON —— 这不违反部分读写：
-- 取用时配置就只读 duration 这一行，不会把整个比赛拖出来。
-- ══ 组排名表 ═════════════════════════════════════════════════════
-- 2026-08-31 该项目【所有组比完并确认】之后生成的总排名。
--   跟本组排名不是一回事: 本组排名是第X组之内的名次(读时现算, 不入库);
--   这张表是全项目跨组的总名次 —— 有几个赛次时是【晋级的依据】,
--   直接决赛的项目, 它就是【最终名次】。
--   生成之后定稿, 不许自动改。
CREATE TABLE IF NOT EXISTS event_rankings (
    rank           INTEGER,          -- 名次(组排名, 跨全部组) —— 这张表就是按它排的, 放第一列
    athlete_id     INTEGER,          -- 运动员ID
    bib_number     TEXT,             -- 号码
    athlete_name   TEXT,             -- 姓名
    unit_name      TEXT,             -- 代表队
    final_time     REAL,             -- 成绩
    heat           INTEGER,          -- 第几组
    total_heats    INTEGER,          -- 总组数(取消的组不算), 打印时显示 第几组/总组数
    lane           INTEGER,          -- 道次
    remark         TEXT,             -- 备注: 判罚(DSQ/DNS/DNF) 优先, 否则晋级 Q/R, 否则纪录标识
    status         TEXT,
    promotion_mark TEXT,
    record_note    TEXT,
    round_id       INTEGER NOT NULL,
    event_id       INTEGER NOT NULL,
    heat_entry_id  INTEGER,
    generated_at   TEXT,             -- 什么时候定的稿
    generated_by   TEXT,             -- 谁定的
    PRIMARY KEY (round_id, event_id, heat_entry_id)
);

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT,
    at    TEXT
);

-- ══ 16. 操作流水 ═══════════════════════════════════════════════════
-- 只追加，不跟任何表关联。省运会出成绩争议时要能追溯：
-- 这个成绩被改过吗、谁改的、改前是多少。原来只有界面日志（300 条上限、
-- 关掉程序就没了），追不了。
CREATE TABLE IF NOT EXISTS audit_log (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    at        TEXT NOT NULL,
    operator  TEXT,
    action    TEXT,                         -- 改成绩 / 判DSQ / 并组 / 改分组 / 撤销 …
    target    TEXT,
    target_id INTEGER,
    old_value TEXT,
    new_value TEXT,
    note      TEXT
);
CREATE INDEX IF NOT EXISTS ix_audit_at ON audit_log(at);

-- ══ 视图：比赛日程表 ═══════════════════════════════════════════════
-- 秩序册打印、大屏下一项、编排界面都查这一个，不用各自写 JOIN。
-- 混编的赛次会出多行（每个项目一行），跟秩序册按项目列的习惯一致；
-- 要按物理赛次看就 GROUP BY round_id。
CREATE VIEW IF NOT EXISTS v_schedule AS
SELECT r.id            AS round_id,
       s.no            AS 场次序,
       s.date          AS 日期,
       s.name          AS 场次,
       r.time          AS 开始时间,
       r.ord           AS 场内序,
       e.ev_num        AS 项目号,
       e.age_group     AS 组别,
       e.gender        AS 性别,
       e.event_name    AS 项目,
       e.distance      AS 距离,
       e.stroke        AS 泳姿,
       e.relay_legs    AS 棒数,
       r.stage         AS 赛次,
       r.heat_count    AS 组数,
       r.is_ceremony   AS 是颁奖,
       r.status        AS 状态,
       r.title         AS 赛次标题,
       re.promote_count AS 晋级人数,
       e.id            AS event_id,
       s.id            AS session_id
FROM rounds r
JOIN round_events re ON re.round_id = r.id
JOIN events e        ON e.id = re.event_id
LEFT JOIN sessions s ON s.id = r.session_id;

-- ══ 视图：出场名单 / 成绩单 ════════════════════════════════════════
-- 个人和接力共用一行：姓名列个人取运动员名、接力取队名。
-- 组别/性别/项目跟着每条泳道自己的报名走，所以混编的组打出来
-- 每行显示的是各自的组别，一眼能看出这一组坐了哪几个组别的人。
CREATE VIEW IF NOT EXISTS v_startlist AS
SELECT he.id            AS heat_entry_id,
       he.round_id      AS round_id,
       r.stage          AS 赛次,
       he.heat          AS 组次,
       he.lane          AS 道次,
       he.reserve_no    AS 替补号,
       e.id             AS event_id,
       e.ev_num         AS 项目号,
       e.age_group      AS 组别,
       e.gender         AS 性别,
       e.event_name     AS 项目,
       COALESCE(en.team_name, a.name) AS 姓名,
       en.bib_number    AS 参赛号,
       u.name           AS 单位,
       a.birth_date     AS 出生日期,
       a.joint_unit     AS 联合培养单位,
       COALESCE(en.coach, a.coach)    AS 教练员,
       he.seed_time     AS 报名成绩,
       he.checkin_status AS 检录,
       he.final_time    AS 成绩,
       he.rank          AS 名次,
       he.promotion_mark AS 晋级,
       he.status        AS 状态,
       he.record_note   AS 破纪录,
       he.reaction_time AS 反应时,
       en.id            AS entry_id,
       CASE WHEN en.team_name IS NULL THEN 0 ELSE 1 END AS 是接力
FROM heat_entries he
JOIN rounds   r  ON r.id  = he.round_id
JOIN entries  en ON en.id = he.entry_id
JOIN events   e  ON e.id  = en.event_id
LEFT JOIN athletes a ON a.id = en.athlete_id
LEFT JOIN units    u ON u.id = COALESCE(en.unit_id, a.unit_id);
";
    }
}
