using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RemoteTimingControl
{
    // 用户凭据持久化 — SHA256(salt + password) hex 存到 EXE 同目录 timing_credentials.json
    // 首次运行没文件 → 用默认 admin/admin 并写盘；登录后用户可改密码
    internal static class CredentialStore
    {
        private const string DefaultUser = "admin";
        private const string DefaultPassword = "admin";

        private static string FilePath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "timing_credentials.json"); }
        }

        public static bool Verify(string user, string password) {
            if (string.IsNullOrEmpty(user) || password == null) return false;
            EnsureFile();
            try {
                JObject root = JObject.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                string storedUser = root["user"] != null ? root["user"].ToString() : DefaultUser;
                string storedSalt = root["salt"] != null ? root["salt"].ToString() : "";
                string storedHash = root["hash"] != null ? root["hash"].ToString() : "";
                if (!string.Equals(user.Trim(), storedUser, StringComparison.Ordinal)) return false;
                return Hash(password, storedSalt) == storedHash;
            } catch {
                return false;
            }
        }

        public static bool Change(string oldPassword, string newUser, string newPassword) {
            EnsureFile();
            JObject root = JObject.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
            string storedUser = root["user"] != null ? root["user"].ToString() : DefaultUser;
            string storedSalt = root["salt"] != null ? root["salt"].ToString() : "";
            string storedHash = root["hash"] != null ? root["hash"].ToString() : "";
            // 验证旧密码（用户名不参与旧凭据校验，只校验密码 — 改用户名也走这条路径）
            if (Hash(oldPassword, storedSalt) != storedHash) return false;
            string salt = NewSalt();
            string hash = Hash(newPassword, salt);
            var newRoot = new JObject();
            newRoot["user"] = string.IsNullOrEmpty(newUser) ? storedUser : newUser.Trim();
            newRoot["salt"] = salt;
            newRoot["hash"] = hash;
            File.WriteAllText(FilePath, newRoot.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            return true;
        }

        public static string CurrentUser() {
            EnsureFile();
            try {
                JObject root = JObject.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                return root["user"] != null ? root["user"].ToString() : DefaultUser;
            } catch { return DefaultUser; }
        }

        // 首次启动如果文件不存在或损坏 → 写默认 admin/admin
        private static void EnsureFile() {
            if (File.Exists(FilePath)) return;
            string salt = NewSalt();
            string hash = Hash(DefaultPassword, salt);
            var root = new JObject();
            root["user"] = DefaultUser;
            root["salt"] = salt;
            root["hash"] = hash;
            try {
                File.WriteAllText(FilePath, root.ToString(Newtonsoft.Json.Formatting.Indented), Encoding.UTF8);
            } catch { }
        }

        private static string NewSalt() {
            byte[] b = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return ToHex(b);
        }

        private static string Hash(string password, string saltHex) {
            using (var sha = SHA256.Create()) {
                byte[] saltBytes = FromHex(saltHex);
                byte[] pwdBytes = Encoding.UTF8.GetBytes(password ?? "");
                byte[] combined = new byte[saltBytes.Length + pwdBytes.Length];
                Buffer.BlockCopy(saltBytes, 0, combined, 0, saltBytes.Length);
                Buffer.BlockCopy(pwdBytes, 0, combined, saltBytes.Length, pwdBytes.Length);
                return ToHex(sha.ComputeHash(combined));
            }
        }

        private static string ToHex(byte[] b) {
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }

        private static byte[] FromHex(string hex) {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            byte[] r = new byte[hex.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return r;
        }

        // ═══════════════════════════════════════════════════════════════
        // 记住登录名和密码 — 与主服务器 LoginWindow 行为一致：
        // 用户名明文，密码用当前 Windows 用户的 DPAPI 加密后 Base64 存盘
        // ═══════════════════════════════════════════════════════════════
        private static string RememberPath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "timing_remember.json"); }
        }

        public static void SaveRemembered(string username, string password) {
            try {
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(password ?? ""), null, DataProtectionScope.CurrentUser);
                var obj = new JObject();
                obj["Username"] = username ?? "";
                obj["EncryptedPassword"] = Convert.ToBase64String(enc);
                File.WriteAllText(RememberPath, obj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8);
            } catch { }
        }

        public static void ClearRemembered() {
            try { if (File.Exists(RememberPath)) File.Delete(RememberPath); } catch { }
        }

        public static bool TryLoadRemembered(out string username, out string password) {
            username = null; password = null;
            try {
                if (!File.Exists(RememberPath)) return false;
                JObject obj = JObject.Parse(File.ReadAllText(RememberPath, Encoding.UTF8));
                string u = obj["Username"] != null ? obj["Username"].ToString() : "";
                string enc = obj["EncryptedPassword"] != null ? obj["EncryptedPassword"].ToString() : "";
                byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                username = u;
                password = Encoding.UTF8.GetString(dec);
                return true;
            } catch { return false; }
        }
    }
}
