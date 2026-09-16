using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SwimmingScoreboard
{
    /// <summary>
    /// 2026-09-16 设置/修改"裁判长改成绩"专用密码 —— 跟系统账号密码是分开的两套。
    /// 入口: 主服务器"设置"标签页 →"裁判长权限"→"设置/修改 裁判长改成绩密码"。
    /// 只有密码, 没有用户名(裁判长这个角色不需要区分是谁, 知道这个密码就是裁判长)。
    /// </summary>
    public class ChiefJudgePasswordWindow : Window
    {
        private PasswordBox _currentBox, _newBox, _confirmBox;
        private TextBlock _msg;

        public ChiefJudgePasswordWindow()
        {
            Title = "设置/修改 裁判长改成绩密码";
            Width = 420; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock {
                Text = "这个密码只用于「成绩与排名」界面的「裁判长改成绩」按钮，跟系统账号密码无关。",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(new TextBlock { Text = "当前密码:", Margin = new Thickness(0, 0, 0, 4) });
            _currentBox = new PasswordBox { FontSize = 14, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(_currentBox);

            root.Children.Add(new TextBlock { Text = "新密码(至少6位):", Margin = new Thickness(0, 0, 0, 4) });
            _newBox = new PasswordBox { FontSize = 14, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(_newBox);

            root.Children.Add(new TextBlock { Text = "确认新密码:", Margin = new Thickness(0, 0, 0, 4) });
            _confirmBox = new PasswordBox { FontSize = 14, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(_confirmBox);

            _msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
            root.Children.Add(_msg);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var saveBtn = new Button { Content = "保存", Width = 90, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelBtn = new Button { Content = "取消", Width = 90, Height = 32, IsCancel = true };
            saveBtn.Click += Save_Click;
            btns.Children.Add(saveBtn); btns.Children.Add(cancelBtn);
            root.Children.Add(btns);

            Content = root;
            _currentBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string current = _currentBox.Password;
            string newPwd = _newBox.Password;
            string confirm = _confirmBox.Password;

            if (!AuthHelper.VerifyChiefJudgePassword(current)) {
                ShowMessage("当前密码错误。", false);
                return;
            }
            if (string.IsNullOrEmpty(newPwd) || newPwd.Length < 6) {
                ShowMessage("新密码长度不能少于6位。", false);
                return;
            }
            if (!string.Equals(newPwd, confirm, StringComparison.Ordinal)) {
                ShowMessage("两次输入的新密码不一致。", false);
                return;
            }

            var creds = new AuthHelper.ChiefJudgeCredentials { PasswordHash = AuthHelper.HashPassword(newPwd) };
            AuthHelper.SaveChiefJudgeCredentials(creds);
            ShowMessage("裁判长改成绩密码已修改成功！", true);
            _currentBox.Clear(); _newBox.Clear(); _confirmBox.Clear();
        }

        private void ShowMessage(string msg, bool success)
        {
            _msg.Text = msg;
            _msg.Foreground = success
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
    }
}
