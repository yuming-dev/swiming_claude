# 自动化测试

交付之前跑一遍，全绿再给人测。

```
powershell -ExecutionPolicy Bypass -File tools\DbSelfTest\RunAll.ps1
```

## DbSelfTest.ps1 — 竞赛库规则

直接驱动真实的 `LocalMeetService`，不经过界面。只动真库的一份拷贝（`%TEMP%`），原库不动。

- 组内名次：最快者必须第 1；成绩相同必须并列
- 项目名次：跨组最快者必须第 1；快的名次不能比慢的大；相同必须并列
- 并列口径：库与 `ResultOrdering.IsTie` 一致（1/100 秒取整，AwayFromZero）
- 成绩差：`Gap = 本人成绩 − 本组最快`
- 判罚/弃权：DSQ/DQ/DNF/DNS 的项目名次必须是 0

**先用当前代码把名次重算一遍再断言** —— 否则测的是历史数据里旧算法留下的值。
（写这个测试时第一次跑就是这么抓到"存量名次是旧口径"的。）

## E2ETest.ps1 — 端到端

把整个 Release 目录复制到 `%TEMP%` 再跑，原目录和原库全程不动。

起真的主服务器进程 → 连 WebSocket → 发一组成绩（前两道故意同成绩）→
等 `HEAT_CONFIRMED_ACK` → 关进程 → 查库断言：

- 成绩确实写进了主服务器的库
- 最快者项目名次 = 1
- 成绩相同的两人并列同名次
- 并列之后名次跳号（1,1,3）
- 该组已标记为已确认

## 还没覆盖的（如实说明）

- **打印输出本身**（HTML/PDF 的排版、列宽、备注列）：生成方法挂在 `MainWindow` /
  `EventResultPrintWindow` 上，要真实 WPF 控件才能调，得先把 HTML 生成从 UI 里剥出来。
- **界面显示**：赛程树、大屏渲染。
- **计时端 → 主服务器**整条链：现在是直接模拟计时端发报文，没有真的起 RTC 进程。