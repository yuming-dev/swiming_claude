/* 2026-05-29 HwLapRemainingTest
 *   Unit test for swimplay.c case Set_LapRemaining (protocol 0x61).
 *   Logic is copied verbatim from APP/swimplay.c lines 6352-6457 with all
 *   UI / LCD / sprintf calls stubbed to no-op via macros below.
 *   Pure state-machine reasoning: verifies arrays TP_Open_Close_State,
 *   MB_Open_Close_State, Startbox_Open_Close_State, Lane_TP_MB_State,
 *   MB_Pressed_Bitmap, laps -- given (lane, side, val, d6_open, d7_sb).
 *   Compiled with MSVC cl /utf-8 (build.bat).
 */
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

/* === Mock types & UI stubs === */
typedef unsigned char  u8;
typedef unsigned short u16;
typedef unsigned int   u32;
/* UI funcs: no-op for unit test */
#define Display_TP_State(...)        ((void)0)
#define Display_MB_State(...)        ((void)0)
#define Display_Startbox_State(...)  ((void)0)
#define display_swim_dir(...)        ((void)0)
#define LLaps_diaplay(_l)            ((void)(_l))
#define RLaps_diaplay(_l)            ((void)(_l))
/* sprintf inside the case writes into lcd_Dis (display buffer); harmless to keep */
static char lcd_Dis[64];
/* Geometry constants -- values are placeholders, don't affect logic */
#define btnh       32
#define btnhy      32
#define LaneStep_y 32
#define MB_CR      8
#define fsize      32
/* matches swimplay.h:113-114 */
#define Display_Dir_Max_len   10
#define Display_Dir_Max_Time  (20 * Display_Dir_Max_len)
static int TPsx[2]        = {0, 1};
static int TPsy[2]        = {0, 0};
static int MBsx[2]        = {0, 1};
static int MBsy[2]        = {0, 0};
static int Startboxsx[2]  = {0, 1};
static int Startboxsy[2]  = {0, 0};
static int dir_posx       = 0;
/* Colors -- placeholders */
#define Open_TP_Color    0xFF00
#define Open_MB_Color    0xFF01
#define Close_Color      0x0000
#define Bad_Color        0xF000
#define UnInstall_Color  0x8000

/* === Global state arrays (subset relevant to Set_LapRemaining) === */
/* Real types matched against swimplay.c declarations (lines 290-410) */
static u8  TP_Open_Close_State[10][2];        /* 0=close 1=open 3=broken 4=notinstalled */
static u8  MB_Open_Close_State[3][20];        /* [blind_idx][lane*2 + side_offset] */
static u8  Startbox_Open_Close_State[10][2];
static u8  Lane_TP_MB_State[10][2];           /* swimplay.c:294 u8 */
static u8  Lane_Display_State[10][2];         /* swimplay.c per-lane 显示状态 (0=不累计 1=累计 >>>>>) */
static u16 Lane_TP_MB_Time_Difference[10];    /* swimplay.c:295 u16 */
static u16 Lane_Display_MSecond[10][2];       /* swimplay.c:290 u16 */
static u16 TP_DelayClose_Time[10];            /* swimplay.c:380 u16 */
static u16 Relay_SB_DelayClose_Time[10];      /* swimplay.c:377 u16 */
static u8  MB_Pressed_Bitmap[20];             /* swimplay.c:327 u8 */
static u16 MB_Result[20][3][4];               /* swimplay.c:326 u16 */
static u8  laps[10][2];                       /* swimplay.c:410 u8 */
static u8  Lap_Place[80];                     /* swimplay.c:370 Lap_Place[40*2] u8 */
static u8  Left_MB_Num  = 2;
static u8  Right_MB_Num = 1;

static void reset_state(void) {
    memset(TP_Open_Close_State, 0, sizeof(TP_Open_Close_State));
    memset(MB_Open_Close_State, 0, sizeof(MB_Open_Close_State));
    memset(Startbox_Open_Close_State, 0, sizeof(Startbox_Open_Close_State));
    memset(Lane_TP_MB_State, 0, sizeof(Lane_TP_MB_State));
    memset(Lane_Display_State, 0, sizeof(Lane_Display_State));
    memset(Lane_TP_MB_Time_Difference, 0, sizeof(Lane_TP_MB_Time_Difference));
    memset(Lane_Display_MSecond, 0, sizeof(Lane_Display_MSecond));
    memset(TP_DelayClose_Time, 0, sizeof(TP_DelayClose_Time));
    memset(Relay_SB_DelayClose_Time, 0, sizeof(Relay_SB_DelayClose_Time));
    memset(MB_Pressed_Bitmap, 0, sizeof(MB_Pressed_Bitmap));
    memset(MB_Result, 0, sizeof(MB_Result));
    memset(laps, 0, sizeof(laps));
    memset(Lap_Place, 0, sizeof(Lap_Place));
}

/* === Subject under test: code copied 1:1 from swimplay.c case Set_LapRemaining === */
static void handle_set_lap_remaining(u8 _lane, u8 _side, u8 _val, u8 _open, u8 _sb_side) {
    u8 _open_side, _close_side, _mb_num, _close_mb_num, _mb_idx, _close_mb_idx, _k;
    u16 _jj;
    int goto_sb_only = 0;
    if(_lane >= 10 || _side >= 2) return;
    /* 2026-05-30 fix #2: Lap_Place 名位回退 (跟 Display_Laps_Place_Direct 4621-4622 同源) */
    {
        u8 _old_val = laps[_lane][_side];
        laps[_lane][_side] = _val;
        if(_val < _old_val) {
            u8 _new_total = (u8)(laps[_lane][0] + laps[_lane][1]);
            if(_new_total < 40) {
                Lap_Place[_new_total]++;
                if(Lap_Place[_new_total] > 10) Lap_Place[_new_total] = 10;
            }
        } else if(_val > _old_val) {
            u8 _old_total;
            if(_side == 0) _old_total = (u8)(_old_val + laps[_lane][1]);
            else            _old_total = (u8)(laps[_lane][0] + _old_val);
            if(_old_total < 40 && Lap_Place[_old_total] > 0) Lap_Place[_old_total]--;
        }
    }
    if(_side == 0) LLaps_diaplay(_lane);
    else           RLaps_diaplay(_lane);
    if(_open != 1 && _open != 2) {
        goto_sb_only = 1;
    }
    if(!goto_sb_only) {
        _open_side    = (_open == 1) ? (u8)(1-_side) : _side;
        _close_side   = (u8)(1 - _open_side);
        _mb_num       = (_open_side == 0)  ? Left_MB_Num  : Right_MB_Num;
        _close_mb_num = (_close_side == 0) ? Left_MB_Num  : Right_MB_Num;
        _mb_idx       = (_open_side == 0)  ? _lane : (u8)(_lane + 10);
        _close_mb_idx = (_close_side == 0) ? _lane : (u8)(_lane + 10);
        _jj           = (u16)(_lane + 1);
        if(TP_Open_Close_State[_lane][_open_side] != 3
           && TP_Open_Close_State[_lane][_open_side] != 4) {
            TP_Open_Close_State[_lane][_open_side] = 1;
            Display_TP_State(TPsx[_open_side], TPsy[_open_side]+_jj*btnhy, 8, btnh, Open_TP_Color);
        }
        if(TP_Open_Close_State[_lane][_close_side] != 3
           && TP_Open_Close_State[_lane][_close_side] != 4) {
            TP_Open_Close_State[_lane][_close_side] = 0;
            Display_TP_State(TPsx[_close_side], TPsy[_close_side]+_jj*btnhy, 8, btnh, Close_Color);
        }
        for(_k = 0; _k < _mb_num; _k++) {
            if(MB_Open_Close_State[_k][_mb_idx] != 3
               && MB_Open_Close_State[_k][_mb_idx] != 4) {
                MB_Open_Close_State[_k][_mb_idx] = 1;
                sprintf((char*)lcd_Dis, (_open_side==0)?"L%d":"R%d", _lane);
                Display_MB_State(MBsx[_open_side], MBsy[_open_side]+(_lane+1)*LaneStep_y, MB_CR, fsize, Open_MB_Color, lcd_Dis);
            }
        }
        for(_k = 0; _k < _close_mb_num; _k++) {
            if(MB_Open_Close_State[_k][_close_mb_idx] != 3
               && MB_Open_Close_State[_k][_close_mb_idx] != 4) {
                MB_Open_Close_State[_k][_close_mb_idx] = 0;
                sprintf((char*)lcd_Dis, (_close_side==0)?"L%d":"R%d", _lane);
                Display_MB_State(MBsx[_close_side], MBsy[_close_side]+(_lane+1)*LaneStep_y, MB_CR, fsize, Close_Color, lcd_Dis);
            }
        }
        Lane_TP_MB_State[_lane][_open_side] = 0;
        Lane_TP_MB_State[_lane][_close_side] = 0;
        Lane_TP_MB_Time_Difference[_lane] = 0;
        /* 2026-05-29 fix v3: State[close_side]=1 direction flag + MSecond[close_side]=Max让屏立即满 10 个箭头, 等下次物理触板转向 */
        Lane_Display_State[_lane][_close_side] = 1;
        Lane_Display_State[_lane][_open_side]  = 0;
        Lane_Display_MSecond[_lane][_close_side] = Display_Dir_Max_Time;
        Lane_Display_MSecond[_lane][_open_side]  = 0;
        TP_DelayClose_Time[_lane] = 0;
        Relay_SB_DelayClose_Time[_lane] = 0;
        MB_Pressed_Bitmap[_mb_idx] = 0;
        MB_Pressed_Bitmap[_close_mb_idx] = 0;
        {
            u8 _kk;
            for(_kk=0; _kk<3; _kk++) {
                MB_Result[_mb_idx][_kk][0]=0; MB_Result[_mb_idx][_kk][1]=0;
                MB_Result[_mb_idx][_kk][2]=0; MB_Result[_mb_idx][_kk][3]=0;
                MB_Result[_close_mb_idx][_kk][0]=0; MB_Result[_close_mb_idx][_kk][1]=0;
                MB_Result[_close_mb_idx][_kk][2]=0; MB_Result[_close_mb_idx][_kk][3]=0;
            }
        }
        /* 2026-05-30 fix v4: xy=close_side matches Process_Display_SiwmDir 5396/5478 active code
           (NOT 5236-5371 dead code). v3 had xy=1-close_side which flipped direction. */
        display_swim_dir(dir_posx, _lane, _close_side, Display_Dir_Max_len);
    }
    /* sb_only_label: */
    if(_sb_side == 1 || _sb_side == 2) {
        u8 _sb_open_idx = (_sb_side == 1) ? 0 : 1;
        u8 _sb_close_idx = (u8)(1 - _sb_open_idx);
        u16 _jj2 = (u16)(_lane + 1);
        if(Startbox_Open_Close_State[_lane][_sb_open_idx] != 3
           && Startbox_Open_Close_State[_lane][_sb_open_idx] != 4) {
            Startbox_Open_Close_State[_lane][_sb_open_idx] = 1;
            Display_Startbox_State(Startboxsx[_sb_open_idx], Startboxsy[_sb_open_idx]+_jj2*btnhy+8, 24, 24, 0);
        }
        if(Startbox_Open_Close_State[_lane][_sb_close_idx] != 3
           && Startbox_Open_Close_State[_lane][_sb_close_idx] != 4) {
            Startbox_Open_Close_State[_lane][_sb_close_idx] = 0;
            Display_Startbox_State(Startboxsx[_sb_close_idx], Startboxsy[_sb_close_idx]+_jj2*btnhy+8, 24, 24, Close_Color);
        }
    }
    else {
        u16 _jj3 = (u16)(_lane + 1);
        if(Startbox_Open_Close_State[_lane][0] != 3
           && Startbox_Open_Close_State[_lane][0] != 4) {
            Startbox_Open_Close_State[_lane][0] = 0;
            Display_Startbox_State(Startboxsx[0], Startboxsy[0]+_jj3*btnhy+8, 24, 24, Close_Color);
        }
        if(Startbox_Open_Close_State[_lane][1] != 3
           && Startbox_Open_Close_State[_lane][1] != 4) {
            Startbox_Open_Close_State[_lane][1] = 0;
            Display_Startbox_State(Startboxsx[1], Startboxsy[1]+_jj3*btnhy+8, 24, 24, Close_Color);
        }
    }
}

/* === Test harness === */
static int g_total = 0, g_pass = 0, g_fail = 0;

#define CHK(cond, fmt, ...) do { \
    g_total++; \
    if(cond) { g_pass++; } \
    else { g_fail++; printf("  [FAIL] " fmt "\n", __VA_ARGS__); } \
} while(0)

#define CHK_EQ(actual, expected, label) do { \
    g_total++; \
    if((actual) == (expected)) { g_pass++; } \
    else { g_fail++; printf("  [FAIL] %s: expected=%d actual=%d\n", label, (int)(expected), (int)(actual)); } \
} while(0)

static void test_defensive_lane_out_of_range(void) {
    printf("\n=== Test 1: defensive _lane >= 10 ===\n");
    reset_state();
    handle_set_lap_remaining(10, 0, 5, 1, 1);
    /* nothing should change */
    CHK_EQ(laps[0][0], 0, "laps[0][0] unchanged");
    CHK_EQ(TP_Open_Close_State[0][0], 0, "TP[0][0] unchanged");
    CHK_EQ(Startbox_Open_Close_State[0][0], 0, "SB[0][0] unchanged");
    printf("  PASS: lane >= 10 rejected\n");
}

static void test_defensive_side_out_of_range(void) {
    printf("\n=== Test 2: defensive _side >= 2 ===\n");
    reset_state();
    handle_set_lap_remaining(3, 2, 5, 1, 1);
    CHK_EQ(laps[3][0], 0, "laps unchanged");
    CHK_EQ(laps[3][1], 0, "laps unchanged");
    printf("  PASS: side >= 2 rejected\n");
}

static void test_d6_0_d7_0_pure_laps(void) {
    printf("\n=== Test 3: d6=0 d7=0 -> only laps + close both SB ===\n");
    reset_state();
    /* pre-open SB[3][0] and SB[3][1] so we can see them get closed */
    Startbox_Open_Close_State[3][0] = 1;
    Startbox_Open_Close_State[3][1] = 1;
    TP_Open_Close_State[3][0] = 1;
    TP_Open_Close_State[3][1] = 1;
    handle_set_lap_remaining(3, 0, 7, 0, 0);
    CHK_EQ(laps[3][0], 7, "laps[3][0] = 7");
    CHK_EQ(TP_Open_Close_State[3][0], 1, "TP[3][0] unchanged (d6=0 skip TP)");
    CHK_EQ(TP_Open_Close_State[3][1], 1, "TP[3][1] unchanged");
    CHK_EQ(Startbox_Open_Close_State[3][0], 0, "SB[3][0] closed by d7=0");
    CHK_EQ(Startbox_Open_Close_State[3][1], 0, "SB[3][1] closed by d7=0");
}

static void test_d6_1_d7_1_relay_left_side(void) {
    printf("\n=== Test 4: d6=1 (miss-touch) side=0 d7=1 (open left SB) ===\n");
    reset_state();
    handle_set_lap_remaining(4, 0, 3, 1, 1);
    /* _open_side = 1 - 0 = 1 (right TP opens), _close_side=0 (left TP closes) */
    CHK_EQ(laps[4][0], 3, "laps[4][0] = 3");
    CHK_EQ(TP_Open_Close_State[4][1], 1, "TP[4][right] opened");
    CHK_EQ(TP_Open_Close_State[4][0], 0, "TP[4][left] closed");
    /* MB: open_side=1 -> mb_idx = lane+10 = 14; mb_num = Right_MB_Num = 1 */
    CHK_EQ(MB_Open_Close_State[0][14], 1, "MB[0][14] (right) opened");
    /* close_side=0 -> close_mb_idx = lane = 4; close_mb_num = Left_MB_Num = 2 */
    CHK_EQ(MB_Open_Close_State[0][4], 0, "MB[0][4] (left) closed");
    CHK_EQ(MB_Open_Close_State[1][4], 0, "MB[1][4] (left) closed");
    /* SB d7=1: open SB[lane][0]=left, close SB[lane][1]=right */
    CHK_EQ(Startbox_Open_Close_State[4][0], 1, "SB[4][left] opened");
    CHK_EQ(Startbox_Open_Close_State[4][1], 0, "SB[4][right] closed");
}

static void test_d6_2_d7_2_relay_right_side(void) {
    printf("\n=== Test 5: d6=2 (mis-touch) side=0 d7=2 (open right SB) ===\n");
    reset_state();
    handle_set_lap_remaining(5, 0, 4, 2, 2);
    /* _open_side = 0 (same as side), _close_side=1 */
    CHK_EQ(laps[5][0], 4, "laps[5][0] = 4");
    CHK_EQ(TP_Open_Close_State[5][0], 1, "TP[5][left] opened");
    CHK_EQ(TP_Open_Close_State[5][1], 0, "TP[5][right] closed");
    /* MB: open_side=0 -> mb_idx=5, mb_num=Left_MB_Num=2 */
    CHK_EQ(MB_Open_Close_State[0][5], 1, "MB[0][5] (left) opened");
    CHK_EQ(MB_Open_Close_State[1][5], 1, "MB[1][5] (left) opened");
    /* close: mb_idx=15, mb_num=Right_MB_Num=1 */
    CHK_EQ(MB_Open_Close_State[0][15], 0, "MB[0][15] (right) closed");
    /* SB d7=2: open SB[lane][1]=right, close SB[lane][0]=left */
    CHK_EQ(Startbox_Open_Close_State[5][0], 0, "SB[5][left] closed");
    CHK_EQ(Startbox_Open_Close_State[5][1], 1, "SB[5][right] opened");
}

static void test_d6_1_side_1_personal(void) {
    printf("\n=== Test 6: d6=1 side=1 d7=0 (personal: SB both closed) ===\n");
    reset_state();
    Startbox_Open_Close_State[6][0] = 1;  /* pre-open: expect closed */
    handle_set_lap_remaining(6, 1, 2, 1, 0);
    /* _open_side = 1-1 = 0 (left opens), close_side=1 */
    CHK_EQ(laps[6][1], 2, "laps[6][1] = 2");
    CHK_EQ(TP_Open_Close_State[6][0], 1, "TP[6][left] opened");
    CHK_EQ(TP_Open_Close_State[6][1], 0, "TP[6][right] closed");
    CHK_EQ(Startbox_Open_Close_State[6][0], 0, "SB[6][left] closed by d7=0");
    CHK_EQ(Startbox_Open_Close_State[6][1], 0, "SB[6][right] closed by d7=0");
}

static void test_broken_tp_protected(void) {
    printf("\n=== Test 7: Broken TP (state=3) is protected ===\n");
    reset_state();
    TP_Open_Close_State[7][1] = 3;  /* right TP broken */
    handle_set_lap_remaining(7, 0, 1, 1, 1);
    /* would normally open TP[7][1], but broken -> stay 3 */
    CHK_EQ(TP_Open_Close_State[7][1], 3, "TP[7][right] stays broken");
    CHK_EQ(TP_Open_Close_State[7][0], 0, "TP[7][left] still closed");
}

static void test_notinstalled_tp_protected(void) {
    printf("\n=== Test 8: NotInstalled TP (state=4) is protected ===\n");
    reset_state();
    TP_Open_Close_State[8][0] = 4;
    handle_set_lap_remaining(8, 1, 1, 1, 0);
    /* _open_side = 1-1 = 0, would open TP[8][0], but =4 -> stay 4 */
    CHK_EQ(TP_Open_Close_State[8][0], 4, "TP[8][left] stays notinstalled");
}

static void test_broken_mb_protected(void) {
    printf("\n=== Test 9: Broken MB (state=3) is protected ===\n");
    reset_state();
    MB_Open_Close_State[0][9] = 3;
    handle_set_lap_remaining(9, 1, 1, 2, 0);  /* d6=2 side=1 -> open_side=1, MB index=lane+10=19 */
    /* The opening side here is RIGHT, so MB[0][19] should open */
    CHK_EQ(MB_Open_Close_State[0][19], 1, "MB[0][19] (right) opened");
    /* MB[0][9] is on the LEFT side (close side), it would be set to 0 but is broken=3 */
    CHK_EQ(MB_Open_Close_State[0][9], 3, "MB[0][9] stays broken");
}

static void test_broken_sb_protected(void) {
    printf("\n=== Test 10: Broken SB (state=3) is protected ===\n");
    reset_state();
    Startbox_Open_Close_State[2][0] = 3;
    handle_set_lap_remaining(2, 0, 1, 1, 1);
    /* d7=1 would open SB[2][0]=left, but =3 -> stay 3 */
    CHK_EQ(Startbox_Open_Close_State[2][0], 3, "SB[2][left] stays broken");
    CHK_EQ(Startbox_Open_Close_State[2][1], 0, "SB[2][right] closed normal");
}

static void test_state_clearing(void) {
    printf("\n=== Test 11: timing state cleared on d6=1/2 ===\n");
    reset_state();
    /* setup: non-zero state to be cleared */
    Lane_TP_MB_State[1][0] = 5;
    Lane_TP_MB_State[1][1] = 7;
    Lane_TP_MB_Time_Difference[1] = 100;
    Lane_Display_MSecond[1][0] = 1234;
    Lane_Display_MSecond[1][1] = 5678;
    TP_DelayClose_Time[1] = 50;
    Relay_SB_DelayClose_Time[1] = 30;
    MB_Pressed_Bitmap[1] = 0xFF;
    MB_Pressed_Bitmap[11] = 0xAA;
    MB_Result[1][0][0] = 9;
    handle_set_lap_remaining(1, 0, 2, 1, 1);
    /* d6=1 side=0 -> _open_side=1, _close_side=0.
       v3: MSecond[close=0] = Display_Dir_Max_Time (200 = full bar);
           MSecond[open=1]  = 0. (Test 14 covers this in detail.) */
    CHK_EQ(Lane_TP_MB_State[1][0], 0, "Lane_TP_MB_State[1][0] cleared");
    CHK_EQ(Lane_TP_MB_State[1][1], 0, "Lane_TP_MB_State[1][1] cleared");
    CHK_EQ(Lane_TP_MB_Time_Difference[1], 0, "TP_MB_TimeDiff cleared");
    CHK_EQ(Lane_Display_MSecond[1][0], Display_Dir_Max_Time, "Display_MSecond[close=left]=Max (v3)");
    CHK_EQ(Lane_Display_MSecond[1][1], 0, "Display_MSecond[open=right]=0");
    CHK_EQ(TP_DelayClose_Time[1], 0, "TP_DelayClose_Time cleared");
    CHK_EQ(Relay_SB_DelayClose_Time[1], 0, "Relay_SB_DelayClose_Time cleared");
    CHK_EQ(MB_Pressed_Bitmap[1], 0, "MB_Pressed_Bitmap[1] cleared (open_side left)");
    CHK_EQ(MB_Pressed_Bitmap[11], 0, "MB_Pressed_Bitmap[11] cleared (close_side right)");
    CHK_EQ(MB_Result[1][0][0], 0, "MB_Result cleared");
}

static void test_d6_0_skips_tp_clear(void) {
    printf("\n=== Test 12: d6=0 does NOT clear TP/MB state arrays ===\n");
    reset_state();
    Lane_TP_MB_Time_Difference[3] = 999;
    MB_Pressed_Bitmap[3] = 0x77;
    handle_set_lap_remaining(3, 0, 5, 0, 0);
    /* d6=0: only laps + SB; clearing block skipped */
    CHK_EQ(laps[3][0], 5, "laps written");
    CHK_EQ(Lane_TP_MB_Time_Difference[3], 999, "TimeDiff NOT cleared (d6=0)");
    CHK_EQ(MB_Pressed_Bitmap[3], 0x77, "Bitmap NOT cleared (d6=0)");
}

/* === Cross-check against PC LapAdjustLogic.Compute expected behavior ===
 *   PC computes openAction (d6) and sbOpenSide (d7) and sends them.
 *   Mapping table (matches LapAdjustLogic.cs):
 *     delta<0 (miss-touch  V) -> d6=1, openTpMbSide = otherSide (= 1 - userSide)
 *     delta>0 (mis-touch   ^) -> d6=2, openTpMbSide = userSide
 *     relay  -> d7 = (openTpMbSide==left) ? 1 : 2
 *     personal -> d7 = 0
 *   The test below verifies these mappings produce the right state on hardware.
 */
static void test_display_state_simulates_touch(void) {
    printf("\n=== Test 14: Display_State + MSecond set for instant full arrow display ===\n");
    /* d6=1 side=0 -> open_side=1, close_side=0 (left was 'touched') */
    reset_state();
    Lane_Display_MSecond[2][0] = 80;  /* prior partial accumulation */
    handle_set_lap_remaining(2, 0, 3, 1, 0);
    CHK_EQ(Lane_Display_State[2][0], 1, "State[close=left]=1");
    CHK_EQ(Lane_Display_State[2][1], 0, "State[open=right]=0");
    CHK_EQ(Lane_Display_MSecond[2][0], Display_Dir_Max_Time, "MSecond[close]=Max (= 200, full bar)");
    CHK_EQ(Lane_Display_MSecond[2][1], 0, "MSecond[open]=0");

    /* d6=2 side=1 -> open_side=side=1 (right), close_side=0 (left) */
    reset_state();
    handle_set_lap_remaining(3, 1, 4, 2, 0);
    CHK_EQ(Lane_Display_State[3][0], 1, "d6=2 side=1: State[close=left]=1");
    CHK_EQ(Lane_Display_State[3][1], 0, "d6=2 side=1: State[open=right]=0");
    CHK_EQ(Lane_Display_MSecond[3][0], Display_Dir_Max_Time, "d6=2 MSecond[close=left]=Max");

    /* d6=1 side=1 -> open_side = 0 (left), close_side = 1 (right) */
    reset_state();
    handle_set_lap_remaining(4, 1, 5, 1, 0);
    CHK_EQ(Lane_Display_State[4][1], 1, "d6=1 side=1: State[close=right]=1");
    CHK_EQ(Lane_Display_State[4][0], 0, "d6=1 side=1: State[open=left]=0");
    CHK_EQ(Lane_Display_MSecond[4][1], Display_Dir_Max_Time, "d6=1 side=1: MSecond[close=right]=Max");
}

static void test_display_state_d6_0_preserved(void) {
    printf("\n=== Test 15: d6=0 does NOT clear Lane_Display_State (only laps + SB) ===\n");
    reset_state();
    Lane_Display_State[4][0] = 1;
    Lane_Display_State[4][1] = 0;
    handle_set_lap_remaining(4, 0, 5, 0, 0);
    /* d6=0 takes SB-only path; Lane_Display_State must NOT be touched */
    CHK_EQ(Lane_Display_State[4][0], 1, "Lane_Display_State[4][0] preserved (d6=0)");
    CHK_EQ(Lane_Display_State[4][1], 0, "Lane_Display_State[4][1] preserved");
}

static void test_lap_place_rollback(void) {
    printf("\n=== Test 16: Lap_Place 名位回退 (▼/▲ 时 ++ / --) ===\n");
    /* setup: lane 5 起 left, 完成 3 圈 → laps[5][0]=3 laps[5][1]=2 (left max=4 right max=4)
       Lap_Place[3+2]=Lap_Place[5] 已经 ++ 过, 设为 1 */
    reset_state();
    laps[5][0] = 3; laps[5][1] = 2;
    Lap_Place[5] = 1;

    /* d6=1 漏触补救完成第 6 圈 (左触板) -> side=0, val=2 (laps[5][0] 从 3→2) */
    handle_set_lap_remaining(5, 0, 2, 1, 0);
    /* new_total = 2+2 = 4. Lap_Place[4] 应该 ++ 到 1 */
    CHK_EQ(Lap_Place[4], 1, "▼ 漏触: Lap_Place[new_total=4] ++ to 1");
    CHK_EQ(Lap_Place[5], 1, "Lap_Place[old_total=5] not changed");
    CHK_EQ(laps[5][0], 2, "laps[5][0] = 2");

    /* d6=2 误触回退该次触板 -> side=0, val=3 (laps[5][0] 从 2→3) */
    handle_set_lap_remaining(5, 0, 3, 2, 0);
    /* old_total = _old_val(2) + laps[5][1](2) = 4. Lap_Place[4] -- 回 0 */
    CHK_EQ(Lap_Place[4], 0, "▲ 误触: Lap_Place[old_total=4] -- to 0");
    CHK_EQ(Lap_Place[5], 1, "Lap_Place[5] still 1");
    CHK_EQ(laps[5][0], 3, "laps[5][0] back to 3");

    /* _val == _old_val: 不动 Lap_Place */
    Lap_Place[5] = 7;
    handle_set_lap_remaining(5, 0, 3, 1, 0);  /* val=3 same as current */
    CHK_EQ(Lap_Place[5], 7, "_val==_old_val: Lap_Place not touched");

    /* 上限保护: Lap_Place[N] > 10 时不再 ++ */
    reset_state();
    laps[6][0] = 3; laps[6][1] = 2;
    Lap_Place[4] = 10;  /* 已经满 */
    handle_set_lap_remaining(6, 0, 2, 1, 0);  /* new_total = 4 */
    CHK_EQ(Lap_Place[4], 10, "Lap_Place capped at 10");

    /* 下限保护: Lap_Place[N] == 0 时不 -- */
    reset_state();
    laps[7][0] = 2; laps[7][1] = 2;
    Lap_Place[4] = 0;
    handle_set_lap_remaining(7, 0, 3, 2, 0);  /* old_total = 2+2 = 4 */
    CHK_EQ(Lap_Place[4], 0, "Lap_Place doesn't go below 0");
}

static void test_pc_hw_mapping(void) {
    printf("\n=== Test 13: PC -> HW mapping integrity ===\n");
    int lane;
    /* Scenario A: relay, user presses LEFT spinner V (miss-touch) -> PC sends d6=1,d7=?
     * For side=0 (left), open_side becomes 1-0=1=right. PC's openTpMbSide=right -> d7=2 (open right SB) */
    reset_state();
    lane = 0;
    handle_set_lap_remaining((u8)lane, 0, 5, 1, 2);
    CHK_EQ(TP_Open_Close_State[lane][1], 1, "A: right TP opened (PC openTpMbSide=right)");
    CHK_EQ(TP_Open_Close_State[lane][0], 0, "A: left TP closed");
    CHK_EQ(Startbox_Open_Close_State[lane][1], 1, "A: right SB opened (d7=2)");
    CHK_EQ(Startbox_Open_Close_State[lane][0], 0, "A: left SB closed");

    /* Scenario B: relay, user presses RIGHT spinner V (miss-touch) -> side=1, d6=1
     * open_side = 1-1 = 0 = left. PC openTpMbSide=left -> d7=1 */
    reset_state();
    lane = 1;
    handle_set_lap_remaining((u8)lane, 1, 5, 1, 1);
    CHK_EQ(TP_Open_Close_State[lane][0], 1, "B: left TP opened");
    CHK_EQ(TP_Open_Close_State[lane][1], 0, "B: right TP closed");
    CHK_EQ(Startbox_Open_Close_State[lane][0], 1, "B: left SB opened");
    CHK_EQ(Startbox_Open_Close_State[lane][1], 0, "B: right SB closed");

    /* Scenario C: relay, user presses LEFT spinner ^ (mis-touch undo) -> side=0, d6=2
     * open_side = side = 0 = left. PC openTpMbSide=left -> d7=1 */
    reset_state();
    lane = 2;
    handle_set_lap_remaining((u8)lane, 0, 5, 2, 1);
    CHK_EQ(TP_Open_Close_State[lane][0], 1, "C: left TP opened");
    CHK_EQ(TP_Open_Close_State[lane][1], 0, "C: right TP closed");
    CHK_EQ(Startbox_Open_Close_State[lane][0], 1, "C: left SB opened");

    /* Scenario D: personal race, user presses LEFT ^ -> d6=2,d7=0
     * SB both ends should be closed regardless of pre-state */
    reset_state();
    Startbox_Open_Close_State[3][0] = 1;
    Startbox_Open_Close_State[3][1] = 1;
    handle_set_lap_remaining(3, 0, 5, 2, 0);
    CHK_EQ(Startbox_Open_Close_State[3][0], 0, "D: SB left closed (personal d7=0)");
    CHK_EQ(Startbox_Open_Close_State[3][1], 0, "D: SB right closed (personal d7=0)");
}

int main(void) {
    printf("====================================================\n");
    printf("  HwLapRemainingTest -- swimplay.c case Set_LapRemaining\n");
    printf("====================================================\n");

    test_defensive_lane_out_of_range();
    test_defensive_side_out_of_range();
    test_d6_0_d7_0_pure_laps();
    test_d6_1_d7_1_relay_left_side();
    test_d6_2_d7_2_relay_right_side();
    test_d6_1_side_1_personal();
    test_broken_tp_protected();
    test_notinstalled_tp_protected();
    test_broken_mb_protected();
    test_broken_sb_protected();
    test_state_clearing();
    test_d6_0_skips_tp_clear();
    test_display_state_simulates_touch();
    test_display_state_d6_0_preserved();
    test_lap_place_rollback();
    test_pc_hw_mapping();

    printf("\n====================================================\n");
    printf("  SUMMARY: total=%d pass=%d fail=%d\n", g_total, g_pass, g_fail);
    printf("====================================================\n");
    return (g_fail == 0) ? 0 : 1;
}
