using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RemoteTimingControl
{
    internal class SimpleWebSocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;

        public event Action<string> OnMessage;
        public event Action OnDisconnected;

        public bool IsConnected {
            get { return _client != null && _client.Connected && _stream != null; }
        }

        public void Connect(string host, int port) {
            _client = new TcpClient();
            _client.Connect(host, port);
            _stream = _client.GetStream();

            string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            string handshake = string.Format(
                "GET / HTTP/1.1\r\nHost: {0}:{1}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {2}\r\nSec-WebSocket-Version: 13\r\n\r\n",
                host, port, key);
            byte[] req = Encoding.UTF8.GetBytes(handshake);
            _stream.Write(req, 0, req.Length);

            var sb = new StringBuilder();
            while (true) {
                int b = _stream.ReadByte();
                if (b < 0) break;
                sb.Append((char)b);
                if (sb.Length >= 4 && sb.ToString(sb.Length - 4, 4) == "\r\n\r\n") break;
            }
            if (!sb.ToString().Contains("101"))
                throw new Exception("WebSocket handshake failed");

            var t = new Thread(ReceiveLoop) { IsBackground = true, Name = "WS-Recv" };
            t.Start();
        }

        public void Send(string message) {
            if (!IsConnected) throw new InvalidOperationException("WebSocket not connected");
            byte[] payload = Encoding.UTF8.GetBytes(message);
            byte[] mask = new byte[4];
            new Random().NextBytes(mask);
            byte[] header;
            if (payload.Length < 126)
                header = new byte[] { 0x81, (byte)(0x80 | payload.Length), mask[0], mask[1], mask[2], mask[3] };
            else if (payload.Length < 65536)
                header = new byte[] { 0x81, 0xFE, (byte)(payload.Length >> 8), (byte)(payload.Length & 0xFF), mask[0], mask[1], mask[2], mask[3] };
            else
                header = new byte[] { 0x81, 0xFF, 0, 0, 0, 0, (byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)(payload.Length & 0xFF), mask[0], mask[1], mask[2], mask[3] };

            byte[] masked = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++) masked[i] = (byte)(payload[i] ^ mask[i % 4]);

            lock (this) {
                _stream.Write(header, 0, header.Length);
                _stream.Write(masked, 0, masked.Length);
                _stream.Flush();
            }
        }

        public void Close() {
            try { if (_stream != null) _stream.Write(new byte[] { 0x88, 0x80, 0, 0, 0, 0 }, 0, 6); } catch { }
            try { if (_client != null) _client.Close(); } catch { }
            _client = null; _stream = null;
        }

        private void ReceiveLoop() {
            try {
                while (_client != null && _client.Connected) {
                    int b0 = _stream.ReadByte(); if (b0 < 0) break;
                    int b1 = _stream.ReadByte(); if (b1 < 0) break;
                    bool masked = (b1 & 0x80) != 0;
                    long length = b1 & 0x7F;
                    if (length == 126) { byte[] ext = ReadExact(2); length = (ext[0] << 8) | ext[1]; }
                    else if (length == 127) { byte[] ext = ReadExact(8); length = 0; for (int i = 0; i < 8; i++) length = (length << 8) | ext[i]; }
                    byte[] maskKey = masked ? ReadExact(4) : null;
                    byte[] data = ReadExact((int)length);
                    if (masked && maskKey != null) for (int i = 0; i < data.Length; i++) data[i] ^= maskKey[i % 4];
                    int opcode = b0 & 0x0F;
                    if (opcode == 8) break;
                    if (opcode == 1 || opcode == 0) { string msg = Encoding.UTF8.GetString(data); Action<string> h = OnMessage; if (h != null) h(msg); }
                }
            } catch { }
            _client = null; _stream = null;
            Action d = OnDisconnected; if (d != null) d();
        }

        private byte[] ReadExact(int count) {
            byte[] buf = new byte[count]; int offset = 0;
            while (offset < count) { int read = _stream.Read(buf, offset, count - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
            return buf;
        }
    }
}
