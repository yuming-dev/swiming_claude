using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwimmingScoreboard
{
    public class Credentials
    {
        public string Username { get; set; }
        public string PasswordHash { get; set; }
    }

    public static class AuthHelper
    {
        private static string CredentialsPath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.json"); }
        }

        public static string HashPassword(string password) {
            using (SHA256 sha256 = SHA256.Create()) {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static void EnsureDefaultCredentials() {
            if (!File.Exists(CredentialsPath)) {
                var creds = new Credentials {
                    Username = "admin",
                    PasswordHash = HashPassword("123456")
                };
                File.WriteAllText(CredentialsPath, JsonConvert.SerializeObject(creds, Formatting.Indented), Encoding.UTF8);
            }
        }

        public static Credentials LoadCredentials() {
            EnsureDefaultCredentials();
            string json = File.ReadAllText(CredentialsPath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<Credentials>(json);
        }

        public static void SaveCredentials(Credentials creds) {
            File.WriteAllText(CredentialsPath, JsonConvert.SerializeObject(creds, Formatting.Indented), Encoding.UTF8);
        }

        private static string RememberPath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remember.json"); }
        }

        public static void SaveRemembered(string username, string password) {
            try {
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser);
                var obj = new { Username = username, EncryptedPassword = Convert.ToBase64String(enc) };
                File.WriteAllText(RememberPath, JsonConvert.SerializeObject(obj), Encoding.UTF8);
            } catch { }
        }

        public static void ClearRemembered() {
            try { if (File.Exists(RememberPath)) File.Delete(RememberPath); } catch { }
        }

        public static bool TryLoadRemembered(out string username, out string password) {
            username = null; password = null;
            try {
                if (!File.Exists(RememberPath)) return false;
                var obj = JObject.Parse(File.ReadAllText(RememberPath, Encoding.UTF8));
                string u = obj["Username"].ToString();
                string enc = obj["EncryptedPassword"].ToString();
                byte[] dec = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                username = u;
                password = Encoding.UTF8.GetString(dec);
                return true;
            } catch { return false; }
        }

        public static bool Verify(string username, string password) {
            var creds = LoadCredentials();
            return creds != null &&
                   string.Equals(creds.Username, username, StringComparison.Ordinal) &&
                   string.Equals(creds.PasswordHash, HashPassword(password), StringComparison.Ordinal);
        }

        // ══════════════════════════════════════════════════════════════
        // 2026-09-16 "裁判长改成绩"专用密码 —— 跟系统账号密码(admin/xxx, 上面那套)
        //   是完全分开的两套凑证, 存在独立的文件里。
        //
        //   为什么要分开: 上面那套系统账号密码, 是 query.html/register.html 等网页端
        //   登录用的, 知道的人可能不少(现场好几台机器的操作员都可能知道)。"裁判长改
        //   成绩"是直接改竞赛库、跳过所有正常锁定检查的最高权限操作, 用同一套密码就
        //   等于"很多人都能改" —— 用户明确要求必须是另一套、只有裁判长自己知道的密码。
        // ══════════════════════════════════════════════════════════════
        public class ChiefJudgeCredentials {
            public string PasswordHash { get; set; }
        }

        private static string ChiefJudgeCredentialsPath {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chief_judge_credentials.json"); }
        }

        /// <summary>默认密码 998877 —— 跟系统账号密码(默认 123456)不一样, 首次用完务必在
        /// "设置→裁判长权限→设置/修改 裁判长改成绩密码"里改掉, 别一直用默认值。</summary>
        public static void EnsureDefaultChiefJudgeCredentials() {
            if (!File.Exists(ChiefJudgeCredentialsPath)) {
                var c = new ChiefJudgeCredentials { PasswordHash = HashPassword("998877") };
                File.WriteAllText(ChiefJudgeCredentialsPath, JsonConvert.SerializeObject(c, Formatting.Indented), Encoding.UTF8);
            }
        }

        public static ChiefJudgeCredentials LoadChiefJudgeCredentials() {
            EnsureDefaultChiefJudgeCredentials();
            string json = File.ReadAllText(ChiefJudgeCredentialsPath, Encoding.UTF8);
            return JsonConvert.DeserializeObject<ChiefJudgeCredentials>(json);
        }

        public static void SaveChiefJudgeCredentials(ChiefJudgeCredentials creds) {
            File.WriteAllText(ChiefJudgeCredentialsPath, JsonConvert.SerializeObject(creds, Formatting.Indented), Encoding.UTF8);
        }

        public static bool VerifyChiefJudgePassword(string password) {
            var creds = LoadChiefJudgeCredentials();
            return creds != null &&
                   string.Equals(creds.PasswordHash, HashPassword(password ?? ""), StringComparison.Ordinal);
        }
    }
}
