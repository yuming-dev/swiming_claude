$ErrorActionPreference='Stop'
$bin='C:\游泳2026\swiming_claude\SwimmingScoreboard\bin\x64\Release'
Add-Type -Path (Join-Path $bin 'Newtonsoft.Json.dll')
$J=[Newtonsoft.Json.Linq.JObject]; $F=[Newtonsoft.Json.Formatting]::None

# display.html 各视图真正读的字段(从 render 函数抠出来的)
$LIVE = @('applicableRecords','clockPaused','competitionName','currentAgeGroup','currentEvent',
          'currentEventNumber','currentGender','currentHeat','currentStage','displayRecordLabel',
          'displayRecordTypeName','laneCloseSettings','poolConfig','raceState','records',
          'resultConfirmed','runningTime','swimmers','totalHeats')
$VIEWS = @{
 'SHOW_START_LIST'     = @('currentAgeGroup','currentEvent','currentGender','currentHeat','currentStage','swimmers','competitionName')
 'SHOW_HEAT_RESULT'    = @('applicableRecords','currentAgeGroup','currentEvent','currentGender','currentHeat','currentStage','swimmers','competitionName')
 'SHOW_EVENT_RANKING'  = @('applicableRecords','currentAgeGroup','currentEvent','currentGender','currentStage','eventRanking','eventRankingSplit','competitionName')
 'SHOW_TEAM_STANDINGS' = @('teamScores','competitionName')
 'SHOW_RECORDS'        = @('records','refereeList','competitionName')
 'SHOW_REFEREES'       = @('refereeList','competitionName')
 'SHOW_AWARDS'         = @('awardRanking','currentAgeGroup','currentEvent','currentGender','eventRanking','competitionName')
}
# 会随时间自然变化的字段, 不做相等比较(只查在不在)
# 时钟在跑时这两个每帧都变, 只查在不在、不比值
$VOLATILE = @('runningTime','swimmers')

$script:pt=@{}; $script:pb=@{}
function NewWs { $w=New-Object System.Net.WebSockets.ClientWebSocket
  $w.ConnectAsync([Uri]'ws://localhost:3002/',[Threading.CancellationToken]::None).Wait(8000)|Out-Null
  if($w.State -ne 'Open'){throw '连不上 3002'}; return $w }
function SendMsg($w,$t){ $b=[Text.Encoding]::UTF8.GetBytes($t); $g=New-Object System.ArraySegment[byte] -ArgumentList @(,$b)
  $w.SendAsync($g,'Text',$true,[Threading.CancellationToken]::None).Wait(15000)|Out-Null }
function RecvMsg($w,$ms){ $k=$w.GetHashCode(); $sb=New-Object Text.StringBuilder
  try{ do{ if($script:pt[$k]){$t=$script:pt[$k];$buf=$script:pb[$k]}
           else{ $buf=New-Object byte[] 1048576
                 $g=New-Object System.ArraySegment[byte] -ArgumentList @(,$buf)
                 $t=$w.ReceiveAsync($g,[Threading.CancellationToken]::None)
                 $script:pt[$k]=$t;$script:pb[$k]=$buf }
           if(-not $t.Wait($ms)){return $null}
           $script:pt[$k]=$null; $r=$t.Result
           [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf,0,$r.Count)) }while(-not $r.EndOfMessage)
       return $sb.ToString() } catch { return $null } }
# 抓 $sec 秒内该类型的【最后一帧】—— 状态已稳定, 两条流描述的是同一个状态
# 等到【静默】再取最后一帧: 连续 900ms 收不到新帧就认为状态稳定了。
# 原来按固定窗口取"最后一帧", 两条流的最后一帧可能描述不同时刻的状态 —— 会误报。
function LastOf($w,$types,$sec){ $last=$null; $s=[Diagnostics.Stopwatch]::StartNew()
  while($s.Elapsed.TotalSeconds -lt $sec){
    $m=RecvMsg $w 900
    if(-not $m){ if($last -ne $null){ break }; continue }   # 静默且已拿到帧 -> 收工
    try{$o=$J::Parse($m)}catch{continue}
    if($types -contains [string]$o['type']){ $last=$o } }
  return ,$last }

function Cmp($tag,$fields,$a,$b){
  if($a -eq $null -or $b -eq $null){ Write-Host ("  {0,-26} X 有一侧没收到帧" -f $tag); return 1 }
  $da=$a['data']; $db=$b['data']
  $omitted=@()
  if($db.Property('staticOmitted')){ foreach($x in $db['staticOmitted']){ $omitted += [string]$x } }
  $miss=@(); $diff=@()
  foreach($k in $fields){
    $pa=$da.Property($k); $pb=$db.Property($k)
    if($pb -eq $null){ continue }                     # 整包里就没有 -> 不要求专用包有
    # 整包端的 LITE 帧会把一批静态字段置空(随帧带 staticOmitted 告知客户端),
    # 而大屏专用包【始终发真值】。这种"不同"是大屏更对, 不算问题。
    if($omitted -contains $k){ continue }
    if($pa -eq $null){ $miss+=$k; continue }
    if($VOLATILE -contains $k){ continue }
    if($pa.Value.ToString($F) -ne $pb.Value.ToString($F)){ $diff+=$k }
  }
  $la=$a.ToString($F).Length; $lb=$b.ToString($F).Length
  $ok = ($miss.Count -eq 0 -and $diff.Count -eq 0)
  Write-Host ("  {0,-26} {1}  大屏 {2,7:N0}B / 整包 {3,7:N0}B{4}{5}" -f $tag,$(if($ok){'√'}else{'X'}),$la,$lb,
     $(if($miss.Count){"  缺:"+($miss -join ',')}else{''}),
     $(if($diff.Count){"  值不同:"+($diff -join ',')}else{''}))
  return $(if($ok){0}else{1})
}

$disp=NewWs; SendMsg $disp '{"type":"DISPLAY_IDENTITY"}'
$ctl =NewWs; SendMsg $ctl  '{"type":"TIMING_WEB_IDENTITY"}'
Start-Sleep -Seconds 2
$bad=0

"══ 比赛实况: 驱动一整组, 每步比一次 ══"
SendMsg $ctl '{"type":"TIMING_CMD","command":"TIMER_RESET"}'
$bad += Cmp '复位后' $LIVE (LastOf $disp @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6) (LastOf $ctl @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6)

SendMsg $ctl '{"type":"TIMING_CMD","command":"SET_AGEGROUP","data":"青年组"}'; Start-Sleep -Milliseconds 300
SendMsg $ctl '{"type":"TIMING_CMD","command":"SET_GENDER","data":"女"}';       Start-Sleep -Milliseconds 300
SendMsg $ctl '{"type":"TIMING_CMD","command":"SET_EVENT","data":"200米蛙泳"}'; Start-Sleep -Milliseconds 500
SendMsg $ctl '{"type":"TIMING_CMD","command":"SET_STAGE","data":"决赛"}';      Start-Sleep -Milliseconds 500
SendMsg $ctl '{"type":"TIMING_CMD","command":"SET_HEAT","data":1}'
$_c=(LastOf $ctl @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6); $_d=(LastOf $disp @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6)
$bad += Cmp '选组后' $LIVE $_d $_c

SendMsg $ctl '{"type":"TIMING_CMD","command":"READY"}'
$bad += Cmp '就位后' $LIVE (LastOf $disp @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6) (LastOf $ctl @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6)

SendMsg $ctl '{"type":"TIMING_CMD","command":"START_RACE"}'
$bad += Cmp '发令后' $LIVE (LastOf $disp @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6) (LastOf $ctl @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6)

# 灌触板成绩: 8 道依次到边
$t = 140.11
foreach($lane in 1..8){
  $t = $t + 0.83
  SendMsg $ctl ('{"type":"TIMING_DATA","data":{"lane":' + $lane + ',"commandType":"TouchPad","time":' + $t + '}}')
  Start-Sleep -Milliseconds 250
}
$bad += Cmp '8 道触板后' $LIVE (LastOf $disp @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6) (LastOf $ctl @('SHOW_LIVE_RACE','SHOW_LIVE_RACE_LITE') 6)

"`n══ 七个静态视图 ══"
foreach($m in @('SHOW_START_LIST','SHOW_HEAT_RESULT','SHOW_EVENT_RANKING','SHOW_TEAM_STANDINGS','SHOW_RECORDS','SHOW_REFEREES','SHOW_AWARDS')){
  SendMsg $ctl ('{"type":"REMOTE_CONTROL","command":"' + $m + '"}')
  $bad += Cmp $m $VIEWS[$m] (LastOf $disp @($m) 6) (LastOf $ctl @($m) 6)
}

SendMsg $ctl '{"type":"TIMING_CMD","command":"TIMER_RESET"}'; Start-Sleep -Seconds 1
"`n不合格项: $bad"
$disp.Dispose(); $ctl.Dispose()
if($bad -gt 0){ exit 1 }
