using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // 竞赛数据服务 —— 网络协议                                  2026-08-25
    //
    // 【为什么要这一层】meet.db 是 SQLite, 是文件库。多台机器同时开共享目录里
    //   的同一个文件, SMB 上的锁不可靠, 迟早坏库。所以主服务器独占它,
    //   别人走这个协议请过去。
    //
    // 【对计时端的意义】计时端比赛中只写自己那个 current_heat.db, 跟主服务器
    //   零往来; 只在【选组时取一次名单】和【确认成绩时回写一次】跟服务器说话。
    //   主服务器卡了、在 GC、甚至挂了, 这一组照样游完, 成绩一条不丢 ——
    //   这就是"计时器专心做好计时"那句话落到代码上的样子。
    //
    // 【协议形状】一问一答, 谁发请求谁配 id, 应答按 id 对回去。
    //     请求  { "type":"MEET_RPC",       "id":17, "op":"GetHeat", "args":{...} }
    //     应答  { "type":"MEET_RPC_REPLY", "id":17, "ok":true,  "result":{...} }
    //           { "type":"MEET_RPC_REPLY", "id":17, "ok":false, "error":"本组比赛中…",
    //                                                "errorKind":"HeatLocked" }
    //
    // 【错误要能还原成异常】服务端抛 HeatLockedException, 客户端就得抛出同一个
    //   异常, 否则调用方那套 try/catch 全白写。所以 errorKind 必须带过去。
    // ══════════════════════════════════════════════════════════════════════
    public static class MeetRpc
    {
        public const string RequestType = "MEET_RPC";
        public const string ReplyType   = "MEET_RPC_REPLY";

        // 序列化设置：两端必须完全一致, 否则 DateTime / 空值 的处理会对不上。
        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings {
            NullValueHandling = NullValueHandling.Include,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            // DTO 里全是公开字段(不是属性), 默认成员查找覆盖不到, 显式打开
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver()
        };

        // ── 错误分类 ──────────────────────────────────────────────────
        // 只有这两类是"业务上有意义"的, 调用方会按它决定怎么办;
        // 其余一律 Unknown, 当成服务端故障处理。
        public const string KindHeatLocked = "HeatLocked";
        public const string KindData       = "MeetData";
        public const string KindUnknown    = "Unknown";

        public static string KindOf(Exception ex)
        {
            if (ex is HeatLockedException) return KindHeatLocked;
            if (ex is MeetDataException)   return KindData;
            return KindUnknown;
        }

        /// <summary>把服务端回来的错误还原成本地异常, 让上层的 catch 照常生效。</summary>
        public static Exception ToException(string kind, string message, long roundId, int heat)
        {
            if (kind == KindHeatLocked) return new HeatLockedException(roundId, heat);
            if (kind == KindData)       return new MeetDataException(message ?? "数据错误");
            return new MeetDataException("竞赛服务出错: " + (message ?? "未知"));
        }

        // ── 组包 ──────────────────────────────────────────────────────
        public static string BuildRequest(long id, string op, object args)
        {
            var o = new JObject();
            o["type"] = RequestType;
            o["id"]   = id;
            o["op"]   = op;
            o["args"] = args == null ? null : JToken.FromObject(args, JsonSerializer.Create(Json));
            return o.ToString(Formatting.None);
        }

        public static string BuildOkReply(long id, object result)
        {
            var o = new JObject();
            o["type"]   = ReplyType;
            o["id"]     = id;
            o["ok"]     = true;
            o["result"] = result == null ? null : JToken.FromObject(result, JsonSerializer.Create(Json));
            return o.ToString(Formatting.None);
        }

        public static string BuildErrReply(long id, Exception ex)
        {
            var o = new JObject();
            o["type"]      = ReplyType;
            o["id"]        = id;
            o["ok"]        = false;
            o["error"]     = ex == null ? "未知错误" : ex.Message;
            o["errorKind"] = KindOf(ex);
            var hl = ex as HeatLockedException;
            if (hl != null) { o["roundId"] = hl.RoundId; o["heat"] = hl.Heat; }
            return o.ToString(Formatting.None);
        }

        // ── 取参数 ────────────────────────────────────────────────────
        // 服务端从 args 里按名字取, 缺了就给默认值 —— 客户端老一版少传一个字段,
        // 不该让整台服务器抛异常。
        public static T Arg<T>(JToken args, string name, T dflt = default(T))
        {
            try
            {
                if (args == null) return dflt;
                var t = args[name];
                if (t == null || t.Type == JTokenType.Null) return dflt;
                return t.ToObject<T>(JsonSerializer.Create(Json));
            }
            catch { return dflt; }
        }
    }
}
