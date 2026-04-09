using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
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
    }

    public partial class LoginWindow : Window
    {
        public LoginWindow() {
            InitializeComponent();
            string savedUser, savedPass;
            if (AuthHelper.TryLoadRemembered(out savedUser, out savedPass)) {
                UsernameBox.Text = savedUser;
                PasswordBox.Password = savedPass;
                RememberMe.IsChecked = true;
                PasswordBox.Focus();
            } else {
                UsernameBox.Focus();
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e) {
            DoLogin();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.Key == Key.Enter) DoLogin();
        }

        private void DoLogin() {
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password;
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) {
                ShowError("请输入用户名和密码。"); return;
            }
            if (AuthHelper.Verify(username, password)) {
                if (RememberMe.IsChecked == true)
                    AuthHelper.SaveRemembered(username, password);
                else
                    AuthHelper.ClearRemembered();
                DialogResult = true;
                Close();
            } else {
                ShowError("用户名或密码错误，请重试。");
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }

        private void ShowError(string msg) {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
