using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard.Db
{
    // ══════════════════════════════════════════════════════════════════════
    // IMeetRpcTransport 的 WebSocket 实现                       2026-08-25
    //
    // 连到主服务器的 ws://host:3002, 把 MEET_RPC 请求发出去、按 id 等应答。
    //
    // 【为什么要按 id 配对】这条连接上跑的不只有 RPC 应答, 还有主服务器
    //   广播的 SHOW_LIVE_RACE 之类。收到的东西先看 type, 是 MEET_RPC_REPLY
    //   才按 id 交给在等的那个调用, 其余原样丢给 OnOtherMessage ——
    //   否则广播帧会被当成应答, 把调用方喂一堆垃圾。
    //
    // 【超时一定要有】比赛现场卡住不动比报错更糟: 报错了操作员知道该重连
    //   还是切本地模式, 卡住了只能干等。默认 8 秒(在 RemoteMeetService 上)。
    //
    // 【断线不重连】故意的。计时端在比赛中根本不需要这条连接 —— 只有选组和
    //   确认成绩两个时刻才用。断了就报错让操作员看见, 不要在背后偷偷重试,
    //   那会把"服务器其实早就没了"这件事藏起来, 直到确认成绩时才爆。
    // ══════════════════════════════════════════════════════════════════════
    public class WebSocketRpcTransport : IMeetRpcTransport, IDisposable
    {
        private readonly string _url;
        private readonly Action<string> _log;
        private ClientWebSocket _ws;
        private Thread _rx;
        private volatile bool _run;

        // 在等应答的调用：id → 信号 + 结果
        private class Pending
        {
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public string Reply;
        }
        private readonly Dictionary<long, Pending> _pending = new Dictionary<long, Pending>();
        private readonly object _gate = new object();

        /// <summary>非 RPC 应答的消息(广播等)原样给出去, 调用方想处理就处理。</summary>
        public Action<string> OnOtherMessage;

        public WebSocketRpcTransport(string host, int port, Action<string> log)
        {
            _url = "ws://" + host + ":" + port;
            _log = log ?? delegate { };
        }

        public bool IsConnected
        { get { return _ws != null && _ws.State == WebSocketState.Open; } }

        public bool Connect(int timeoutMs)
        {
            try
            {
                Close();
                _ws = new ClientWebSocket();
                if (!_ws.ConnectAsync(new Uri(_url), CancellationToken.None).Wait(timeoutMs)) return false;
                if (_ws.State != WebSocketState.Open) return false;
                _run = true;
                _rx = new Thread(RxLoop) { IsBackground = true, Name = "MeetRpcRx" };
                _rx.Start();
                _log("已连上竞赛服务 " + _url);
                return true;
            }
            catch (Exception ex) { _log("连竞赛服务失败: " + ex.Message); return false; }
        }

        private void RxLoop()
        {
            var buf = new byte[64 * 1024];
            var acc = new List<byte>();
            while (_run && _ws != null && _ws.State == WebSocketState.Open)
            {
                try
                {
                    var r = _ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    for (int i = 0; i < r.Count; i++) acc.Add(buf[i]);
                    if (!r.EndOfMessage) continue;
                    string s = Encoding.UTF8.GetString(acc.ToArray());
                    acc.Clear();
                    Deliver(s);
                }
                catch { break; }
            }
            // 断线时把还在等的全部唤醒, 别让调用方傻等到超时
            lock (_gate)
            {
                foreach (var kv in _pending) { kv.Value.Reply = null; kv.Value.Done.Set(); }
                _pending.Clear();
            }
        }

        private void Deliver(string s)
        {
            long id = 0;
            try
            {
                // 先便宜地看一眼是不是应答, 不是就别费劲解析
                if (s.IndexOf(MeetRpc.ReplyType, StringComparison.Ordinal) < 0)
                { var h = OnOtherMessage; if (h != null) h(s); return; }
                var o = JObject.Parse(s);
                if (!string.Equals((string)o["type"], MeetRpc.ReplyType, StringComparison.Ordinal))
                { var h = OnOtherMessage; if (h != null) h(s); return; }
                id = o["id"] != null ? (long)o["id"] : 0;
            }
            catch { return; }

            lock (_gate)
            {
                Pending p;
                if (!_pending.TryGetValue(id, out p)) return;   // 已超时被丢掉的, 忽略
                p.Reply = s;
                p.Done.Set();
            }
        }

        public string SendAndWait(string requestJson, int timeoutMs)
        {
            if (!IsConnected) throw new MeetDataException("跟竞赛服务断开了，请检查主服务器");

            long id;
            try { id = (long)JObject.Parse(requestJson)["id"]; }
            catch { throw new MeetDataException("请求格式错误"); }

            var p = new Pending();
            lock (_gate) _pending[id] = p;
            try
            {
                var b = Encoding.UTF8.GetBytes(requestJson);
                _ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true,
                              CancellationToken.None).Wait(timeoutMs);

                if (!p.Done.Wait(timeoutMs))
                    throw new MeetDataException("竞赛服务超时未应答（" + timeoutMs + "ms）");
                if (p.Reply == null)
                    throw new MeetDataException("等应答时连接断开了");
                return p.Reply;
            }
            finally
            {
                lock (_gate) _pending.Remove(id);
                p.Done.Dispose();
            }
        }

        public void Close()
        {
            _run = false;
            try
            {
                if (_ws != null && _ws.State == WebSocketState.Open)
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1500);
            }
            catch { }
            try { if (_ws != null) _ws.Dispose(); } catch { }
            _ws = null;
        }

        public void Dispose() { Close(); }
    }
}
