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
        }

        private void EnsureSchema()
        {
            using (var tx = _cn.BeginTransaction())
            {
                Exec(_schemaSql, tx);
                tx.Commit();
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

        public int ExecuteNonQuery(string sql, params object[] ps)
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
            lock (_gate)
            {
                using (var cmd = Cmd(sql, null, ps)) return cmd.ExecuteScalar();
            }
        }

        // 只读查询：把结果读成 DataTable 再返回，连接不外泄，调用方也不用管释放
        public DataTable Query(string sql, params object[] ps)
        {
            lock (_gate)
            {
                using (var cmd = Cmd(sql, null, ps))
                using (var rd = cmd.ExecuteReader())
                {
                    var t = new DataTable();
                    t.Load(rd);
                    return t;
                }
            }
        }

        // 一批语句放进同一个事务。用于"导入""并组"这种要么全成要么全不动的操作。
        public void InTransaction(Action<Func<string, object[], int>> body)
        {
            lock (_gate)
            {
                using (var tx = _cn.BeginTransaction())
                {
                    try
                    {
                        Func<string, object[], int> run = delegate(string sql, object[] ps)
                        {
                            using (var cmd = Cmd(sql, tx, ps ?? new object[0])) return cmd.ExecuteNonQuery();
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
