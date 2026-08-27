# 广播回归卡口 BroadcastRegress

改完广播相关的代码，跑一遍这个再提交。

它扮演两个客户端连上主服务器：

```
DISPLAY_IDENTITY      收【大屏专用包】(BuildDisplayPayload 那条路)
TIMING_WEB_IDENTITY   收【整包】       (GetStatusData 那条路)
```

然后驱动一整组比赛 + 逐个切七个视图，**逐字段比对两条流**——
大屏专用包里该有的字段一个不能少、值必须跟整包一模一样。

## 用法

```powershell
# 先起主服务器（首次启动到 3002 可用要 10~120 秒，脚本不等，自己确认）
powershell -ExecutionPolicy Bypass -File tools\BroadcastRegress\BroadcastRegress.ps1
```

全过时退出码 0，有不合格项退出码 1。

## 它测什么

**比赛实况**（每 100ms 都在发的那一路，最费）：复位后 / 选组后 / 就位后 /
发令后 / 8 道触板后，共 5 个节点。触板成绩用 `TIMING_DATA` 报文直接灌，
不需要计时硬件，也不需要模拟器。

**七个静态视图**：出发表 / 组成绩 / 总排名 / 团体总分 / 纪录 / 裁判介绍 / 颁奖。

字段清单不是照文档抄的，是从 `Web/display.html` 里把每个 `render` 函数
用到的 `data.xxx` 逐个抠出来的。文档漏过两处（EventRanking 的
`applicableRecords`/`currentStage`、Records 的 `refereeList`），照文档做会
重演"比赛中纪录行被冲掉"那次事故。

## 两类【已知的假阳性】，脚本已自动排除

1. **LITE 帧把静态字段置空。** 比赛中整包那一路发的是 `SHOW_LIVE_RACE_LITE`，
   会把一批静态字段置空并随帧带 `staticOmitted` 告知客户端合并；而大屏专用包
   **始终发真值**。这种"不同"是大屏更对，脚本按 `staticOmitted` 跳过。

2. **时钟在跑时 `swimmers` / `runningTime` 每帧都变。** 归入 `$VOLATILE`，
   只查在不在、不比值。

## 一个【尚未解决】的方法学缺陷

两条流是**先后**抽干的，中间有约 1 秒时差。这段时间里真发生变化的字段会误报。

实测踩过一次：灌完触板成绩后破了一条纪录，`records` 里那条的 `time` 从
`2:52.08` 变成 `52.08`，长度差 2 字符，于是"选组后"那一格报 `records` 不同。
两条路径其实调的是同一个 `BuildRecordsPayload()`，不可能产出不同 ——
`SHOW_RECORDS` 视图那一格比的就是 `records`，逐字节相同、通过，可以互相印证。

**判读要领：某一格报了不同，先看 `SHOW_RECORDS` / `SHOW_START_LIST` 这些
静态视图过没过。** 静态视图过了而比赛实况报同一个字段不同，多半是这个时差，
不是真问题。要根治得给帧加序号或时间戳，那要改产品代码，不值得为测试去改。

稳妥的做法是：**把比对放在确认成绩之后**（状态彻底静止）再做一次。
