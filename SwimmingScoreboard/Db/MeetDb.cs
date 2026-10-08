using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 竞赛管理库 访问层                                        2026-08-24
    //
    // 【本类的铁律】只提供"部分读写"，不提供 LoadAll / SaveAll。
    //   原来那套是把 1.2 MB 的整包读进内存、改一个字段再整包写回、整包推给别的机器，
    //   由此直接造成 400 米比赛内存涨几 GB、编排端改动存不住、大屏黑屏三起现场事故。
    //   以后要加接口，也请照这个原则：要哪几行取哪几行，改哪一行更新哪一行。
    //   —— 用户 2026-08-24 原话："运行哪一部就调哪一部，不要整包数据库都调，都要重写"
    //
    // 线程：SQLite 连接不是线程安全的，本类所有公开方法内部串行化（lock）。
    //   写操作用事务包住，中途失败整体回滚，不会留下半截数据。
    // ══════════════════════════════════════════════════════════════════════
    public class MeetDb : IDisposable
    {
        private readonly string _path;
        private readonly string _schemaSql;
        private SQLiteConnection _cn;
        private readonly object _gate = new object();

        // ── 2026-08-31 写入优先, 查询排队 ─────────────────────────────
        //   比赛过程中【写入不能等】: 每一次触板都要立刻落库。而报表/刷新那类查询
        //   可以慢一点。原来两者平等抢 _gate, 一个大查询正在跑, 触板写入就得干等。
        //
        //   现在:
        //     写入  —— 先把"有写要来了"这个计数加上, 再去抢锁; 不排队, 谁先到谁进。
        //     查询  —— ① 先在 _readOrder 上排队(先到先服务, 不会插队/饿死);
        //               ② 进去之后, 只要还有写在排队就让路, 等写完再拿库。
        //   效果: 写入永远不会被查询挡住; 查询之间按到达顺序来。
        private int _pendingWrites;                       // 有多少个写在等/在跑
        private readonly object _readOrder = new object(); // 查询之间的排队闸

        // 本线程是不是正在写。写的过程里如果又发查询, 那个"有写在跑"其实是它自己 —— 不能空等自己。
        [ThreadStatic] private static int _writeDepth;

        private void EnterWrite() { _writeDepth++; System.Threading.Interlocked.Increment(ref _pendingWrites); }
        private void LeaveWrite() { System.Threading.Interlocked.Decrement(ref _pendingWrites); _writeDepth--; }

        /// <summary>查询前调: 先排队, 再给写入让路。</summary>
        private void WaitForWriters() {
            if (_writeDepth > 0) return;   // 自己就是写, 别等自己
            // 让路上限 2 秒 —— 万一某个写卡住, 查询也不能永远不返回
            var t0 = DateTime.Now;
            while (System.Threading.Volatile.Read(ref _pendingWrites) > 0
                   && (DateTime.Now - t0).TotalMilliseconds < 2000) {
                System.Threading.Thread.Sleep(1);
            }
        }

        public string FilePath { get { return _path; } }

        public MeetDb(string path) : this(path, MeetSchema.Sql) { }

        // 当前组库 current_heat.db 用同一套访问层，只是换一份表结构
        public MeetDb(string path, string schemaSql)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException("path");
            _path = path;
            _schemaSql = schemaSql;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var csb = new SQLiteConnectionStringBuilder();
            csb.DataSource = path;
            csb.Version = 3;
            csb.ForeignKeys = true;
            // WAL: 读写可并发 —— 主服务器在写成绩时，别的读取(打印/查询)不会被挡住
            csb.JournalMode = SQLiteJournalModeEnum.Wal;
            csb.SyncMode = SynchronizationModes.Normal;
            csb.BusyTimeout = 5000;

            _cn = new SQLiteConnection(csb.ToString());
            _cn.Open();
            EnsureSchema();
            EnsureLateColumns();
        }

        /// <summary>2026-08-31 供 DROP 掉某张表后重建用(建表语句都是 IF NOT EXISTS, 重复调无害)。</summary>
        public void EnsureSchemaPublic() { EnsureSchema(); EnsureLateColumns(); }

        private void EnsureSchema()
        {
            using (var tx = _cn.BeginTransaction())
            {
                // 2026-10-08 视图不像表那样能 ALTER 加列——"CREATE VIEW IF NOT EXISTS"
                // 对已经建过的老库永远不会生效, 给 v_startlist 新加的列(比如这次的
                // 参赛号)老赛事(从开赛那天起就是同一个 meet.db 文件)永远读不到。
                // 视图不存数据, 每次开库先删了让下面重建, 零风险。
                Exec("DROP VIEW IF EXISTS v_startlist; DROP VIEW IF EXISTS v_schedule;", tx);
                Exec(_schemaSql, tx);
                tx.Commit();
            }
        }

        // 2026-09-18 EnsureSchema 只管"建表"——CREATE TABLE IF NOT EXISTS 对已经建好的
        //   老表不会补列。真实赛事的 meet.db/current_heat.db 从开赛那天起就是同一个文件,
        //   后续版本加的新列(比如这次撤销 DSQ 要补的几列)老库里永远不会自动冒出来,
        //   一用就是 "no such column" —— 老赛事升级新版本装上就直接崩。
        //   这里补一道通用的"补列"：建完表之后, 把"后来才加的列"按名单挨个查一遍,
        //   缺了就 ALTER TABLE 补上（SQLite 加列是轻量操作, 不用整表重写）。
        //   以后再往 heat_entries/live_lanes 这类已经有真实数据的表加列, 照这个名单
        //   加一行就行——干净的新表(CREATE TABLE IF NOT EXISTS 那条已经带了新列)不受影响。
        private static readonly string[][] _lateColumns = new string[][] {
            new[] { "heat_entries", "dsq_backup_final_time",       "REAL DEFAULT 0" },
            new[] { "heat_entries", "dsq_backup_start_block_time", "REAL DEFAULT 0" },
            new[] { "heat_entries", "dsq_backup_leg_reaction_times", "TEXT" },
            new[] { "live_lanes",   "dsq_backup_final_time",       "REAL DEFAULT 0" },
            new[] { "live_lanes",   "dsq_backup_start_block_time", "REAL DEFAULT 0" },
            new[] { "live_lanes",   "dsq_backup_leg_reaction_times", "TEXT" },
        };
        private void EnsureLateColumns()
        {
            foreach (var col in _lateColumns)
            {
                string table = col[0], name = col[1], decl = col[2];
                try
                {
                    var ti = Query("PRAGMA table_info(" + table + ")");
                    if (ti.Rows.Count == 0) continue;   // 这个库里压根没这张表(比如 meet.db 没有 live_lanes), 跳过
                    bool exists = false;
                    foreach (DataRow r in ti.Rows)
                        if (string.Equals(Convert.ToString(r["name"]), name, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    if (!exists) ExecuteNonQuery("ALTER TABLE " + table + " ADD COLUMN " + name + " " + decl);
                }
                catch { /* 补列失败不阻断开库——大不了这一列继续没有, 不能让老赛事打不开 */ }
            }
        }

        // ── 底层小工具 ──────────────────────────────────────────────────
        private void Exec(string sql, SQLiteTransaction tx)
        {
            using (var cmd = _cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        private SQLiteCommand Cmd(string sql, SQLiteTransaction tx, params object[] ps)
        {
            var cmd = _cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            for (int i = 0; i < ps.Length; i++)
                cmd.Parameters.AddWithValue("@p" + (i + 1), ps[i] ?? DBNull.Value);
            return cmd;
        }

        // 2026-08-31 写入优先: 进来先登记"有写在跑", 查询看到就让路
        public int ExecuteNonQuery(string sql, params object[] ps)
        {
            EnterWrite();
            try { return ExecuteNonQueryCore(sql, ps); }
            finally { LeaveWrite(); }
        }

        private int ExecuteNonQueryCore(string sql, params object[] ps)
        {
            lock (_gate)
            {
                using (var tx = _cn.BeginTransaction())
                using (var cmd = Cmd(sql, tx, ps))
                {
                    int n = cmd.ExecuteNonQuery();
                    tx.Commit();
                    return n;
                }
            }
        }

        public long ExecuteInsert(string sql, params object[] ps)
        {
            lock (_gate)
            {
                using (var tx = _cn.BeginTransaction())
                {
                    using (var cmd = Cmd(sql, tx, ps)) cmd.ExecuteNonQuery();
                    long id;
                    using (var c2 = Cmd("SELECT last_insert_rowid()", tx)) id = Convert.ToInt64(c2.ExecuteScalar());
                    tx.Commit();
                    return id;
                }
            }
        }

        public object ExecuteScalar(string sql, params object[] ps)
        {
            lock (_readOrder)          // 查询排队: 先到先服务
            {
                WaitForWriters();      // 写入优先: 有写在等就让路
                lock (_gate)
                {
                    using (var cmd = Cmd(sql, null, ps)) return cmd.ExecuteScalar();
                }
            }
        }

        // 只读查询：把结果读成 DataTable 再返回，连接不外泄，调用方也不用管释放
        //
        // 2026-09-17 【原来 t.Load(rd) 是"未能启用约束"的真正病根】——这个重载会按
        //   IDataReader.GetSchemaTable() 里各列"来自哪张源表、是不是那张表的主键"
        //   自动给结果 DataTable 扣一个 PrimaryKey(等价 MissingSchemaAction.AddWithKey)。
        //   一旦 SQL 是多表 JOIN, 这个自动推断经常挑错列——用户实拍到的接力项目
        //   "生成组排名表"那条查询里 LEFT JOIN athletes(接力是按队报名, 没有单个
        //   athlete_id, 这一列整批都是 NULL), ADO.NET 把 athletes.id 当成结果表的
        //   主键列之一, 而 .NET DataTable 的唯一约束把多个 NULL 当"相等"处理(不是
        //   SQL 的 NULL≠NULL 语义)——只要一个 JOIN 结果里有 2 行以上同一列都是 NULL,
        //   EndLoadData()→EnableConstraints() 就直接抛 ConstraintException, 而且
        //   每次都在同一句 SQL 上百分百复现(不是偶发的脏数据, 是 ADO.NET 对这类
        //   JOIN 的通病)。改用 SQLiteDataAdapter + MissingSchemaAction.Add——
        //   只补列、不推断/不强加主键, 这层本来就没人指望它替调用方去重或校验。
        public DataTable Query(string sql, params object[] ps)
        {
            lock (_readOrder)          // 查询排队: 先到先服务, 不插队也不饿死
            {
                WaitForWriters();      // 写入优先: 有写在等就让路(报表可以慢, 触板不能等)
                lock (_gate)
                {
                    using (var cmd = Cmd(sql, null, ps))
                    using (var da = new SQLiteDataAdapter(cmd))
                    {
                        da.MissingSchemaAction = MissingSchemaAction.Add;
                        var t = new DataTable();
                        da.Fill(t);
                        return t;
                    }
                }
            }
        }

        // 一批语句放进同一个事务。用于"导入""并组"这种要么全成要么全不动的操作。
        // 2026-08-31 写入优先(同 ExecuteNonQuery)
        public void InTransaction(Action<Func<string, object[], int>> body)
        {
            EnterWrite();
            try { InTransactionCore(body); }
            finally { LeaveWrite(); }
        }

        private void InTransactionCore(Action<Func<string, object[], int>> body)
        {
            lock (_gate)
            {
                using (var tx = _cn.BeginTransaction())
                {
                    try
                    {
                        // 2026-08-29 原来这里出错只往上抛一句 "constraint failed" ——
                        //   一个事务里几万条 INSERT, 哪条炸的、什么值炸的, 全无线索。
                        //   导入卡了很久没人查得动, 根子就在这。现在把 SQL 和参数带上。
                        Func<string, object[], int> run = delegate(string sql, object[] ps)
                        {
                            try
                            {
                                using (var cmd = Cmd(sql, tx, ps ?? new object[0])) return cmd.ExecuteNonQuery();
                            }
                            catch (Exception ex)
                            {
                                var sb = new System.Text.StringBuilder();
                                sb.Append(ex.Message).Append(" || SQL: ");
                                string one = (sql ?? "").Replace("\r", " ").Replace("\n", " ");
                                while (one.Contains("  ")) one = one.Replace("  ", " ");
                                sb.Append(one.Length > 300 ? one.Substring(0, 300) : one);
                                if (ps != null && ps.Length > 0)
                                {
                                    sb.Append(" || 参数: ");
                                    for (int i = 0; i < ps.Length && i < 20; i++)
                                    {
                                        if (i > 0) sb.Append(", ");
                                        sb.Append(ps[i] == null ? "<null>" : ps[i].ToString());
                                    }
                                }
                                throw new Exception(sb.ToString(), ex);
                            }
                        };
                        body(run);
                        tx.Commit();
                    }
                    catch
                    {
                        try { tx.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        // 事务内取自增 id（配合 InTransaction 用）
        public long LastInsertId()
        {
            lock (_gate)
            {
                using (var cmd = Cmd("SELECT last_insert_rowid()", null)) return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        // ── 维护 ────────────────────────────────────────────────────────
        public void Vacuum() { lock (_gate) { using (var c = Cmd("VACUUM", null)) c.ExecuteNonQuery(); } }

        // 把 WAL 里的内容落回主文件。备份前必须先调，否则复制出去的是半截数据。
        public void Checkpoint()
        {
            lock (_gate) { using (var c = Cmd("PRAGMA wal_checkpoint(TRUNCATE)", null)) c.ExecuteNonQuery(); }
        }

        public Dictionary<string, int> TableCounts()
        {
            var r = new Dictionary<string, int>();
            var names = Query("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
            foreach (DataRow row in names.Rows)
            {
                string t = Convert.ToString(row["name"]);
                object n = ExecuteScalar("SELECT COUNT(*) FROM \"" + t + "\"");
                r[t] = Convert.ToInt32(n);
            }
            return r;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_cn != null)
                {
                    try { _cn.Close(); } catch { }
                    _cn.Dispose();
                    _cn = null;
                }
            }
        }
    }
}
