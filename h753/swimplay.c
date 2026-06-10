#include "swimplay.h" 
#include "gradienter.h" 
#include "key.h"

#include "exti.h"

//#include "netplay.h" 
#include "audioplay.h"

#include "swimtime.h"  

#include "calendar.h" 	      						  

//#include "stdafx.h"  
#include "stdio.h"  
#include "fs.h"

//////////////////////////////////////////////////////////////////////////////////	 
//SWIM STM32H7核心开发
//定时器 驱动代码	   
//创建日期:2023/10/24
//版本：V1.3
//版权所有，盗版必究。
//Copyright(C) 北京华亿创新信息技术股份有限公司昆明分公司 2024-2029
//All rights reserved									  
//********************************************************************************
//修改说明
//20231107
//删除发送区显示窗口 保留接收区显示窗口
//20231108
//删除接收区显示窗口
////////////////////////////////////////////////////////////////////////////////// 	 
//********************************************************************************
//修改说明
//20240201
//增加出发台上升沿检测功能，以适应两种出发台（一种：下降沿有效；另一种：上升沿有效）的要求
//增加StartBox_Edge_Bit :=1:下降沿有效；=0：上升沿有效
//2024-03-28
//修改程序 在没有触板成绩用盲表成绩代替时，中间显示成绩该消隐的消隐（不显示）
//增加触板成绩显示位：当显示触板成绩后，就不再显示盲表成绩TP_Display_State[10][2],=0:没显示；=1：正在显示TP成绩
//2024-06-8，9
//修改程序 增加改变发令点和道次号顺序功能按键
//2024-06-10
//增加 道次号查找表数组，当道次号顺序改变时，数组数据跟着改变

//2024-6-12
//初始化本机IP地址是 	:192.168.1.100
//目标计算机IP地址是	:192.168.1.108
//2024-6-13
//计算机通过发送命令，控制改变道次顺序号和发令点位置
//2024-7-15
//按控制器“准备就绪”按键或接收到计算机”准备就绪“命令后，控制器发送“准备就绪“命令给计算机

	//2024-10-15  取消读盲表按钮0-9道
	//2024-10-17  网络不显示接收和发送数据的数量
	//2024-11-10  增加显示日期、时间信息  
	//2024-11-10  增加设置触板封闭时间编辑窗口
		//					将设置发令位置按键功能，从主控制窗口改到参数设置子窗口
	//2024-11-21	修改，分辨显示左、右两边剩余圈数的x0  2024-11-21
	//						增加接力比赛位=0：非接力比赛；=1：接力比赛
	// 增加接力第几棒次  变量 Baton_No[10];
		
	//2024-11-24	修改左、右触板运动员触璧次数
	//						编写接力比赛出发台打开状态程序 允许出发台检测

//2024-11-25  出发台信号检测问题：
//		1.出发时，发令枪响后，延迟一段时间后（如5秒），出发台关闭，不再接收信号
//		2.接力项目，对第2，3，4棒，当运动员触板后，延迟一段时间后（如5秒），出发台关闭，不再接收信号
//		3.出发台打开为Green,第一次出发信号来后，出发台变为Yellow，延迟时间后，变为Black;

//2024-11-27  0：左（Left）  ；  1：右（Right)
//Startbox_Open_Close_State[10][2];


//#define SD_CARD 0 //SD卡,卷标为0
//#define EX_FLASH 1 //外部spi flash,卷标为 1
//#define EX_NAND 2 //外部 nand flash,卷标为 2

//2024-12-7 编写存储和读入参数的程序 OnReadMatchData();	OnWriteMatchData();
//2024-12-8 编写 测量电池电压程序，用PA5口，电池电压经电阻分压后输入到PA5,经A/D转换后，计算出对应的电压值，低于10.5V，提示充电
//2024-12-9 编写：设置是否关闭触板计时状态位Open_State，=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板。

//2024-12-12  增加处理触板延迟时间程序 以防误触发后，没有正常的运动员触板信号
//2024-12-14  增加设置"成绩显示时间(S):","触板打开延迟时间(S):","出发台打开延迟时间(S):","盲表代替成绩延迟时间(S):"
//2024-12-15   增加显示"触板状态："

//2024-12-17 当TP_DelayCloseValue=0；Relay_SB_DelayCloseValue=0时，直接关闭延迟功能
//						当Result_Display_Time输入0时，置为10；该值不能为0，最小为10
//2024-12-22 调试测试部分程序：不显示游泳方向，在出发台检测程序中，多了一次处理，删除

////////////////////////////////////////////////////////////////////////////////// 	 
//2025-1-5  增加设置"50m/25m泳池","单/两端安装触板"按键

//2025-1-26  修改存储和读取计时参数文件，增加存储和读取Left_MB_Num,Right_MB_Num内容

////////////////////////////////////////////////////////////////////////////////// 	 


u8 StartBox_Edge_Bit=1;  //2024-2-1
u8	StartBox_Bit=0;  			//处理出发台标志位  2024-11-25
u8	TP_Bit=0;  			//处理触板标志位  2024-12-12

u8 Pool50mOr25mbit=0;		//泳池是标准50m=0; ;短池25m=1;   2025-1-2
u8 PoolSingleOrDoubleTPbit=0;	//泳池安装触板是一端=1; 两端=0  2025-1-2



u8 led0sta=1,led1sta=1;
u8  Check_State_Bit=0;

//TCP IP Server
u16 		recLength;
u8		FLAG;

u8		TCPIP_CommandBuf[RX_DATA_MaxLEN];						//接收命令缓冲区  2023-8-11
u16 	TCPIP_send_len,TCPIP_recv_len;
u8		TCPIP_send_buff[UART4_MAX_SEND_LEN];							//发送命令缓冲区  2023-8-11
u8		TCPIP_Con_command,TCPIP_Con_para,TCPIP_Controlbuf;
u8		TCPIP_Con_para1,TCPIP_Con_para2,TCPIP_Con_para3,TCPIP_Con_para4,TCPIP_Con_para5,TCPIP_Con_para6;
u8		TCPIP_Con_para7,TCPIP_Con_para8;

u8	SW_Serve_Send_Command,SW_Serve_Send_Command_Para;
u8	SW_Serve_Send_cmd0,SW_Serve_Send_cmd1,SW_Serve_Send_cmd2,SW_Serve_Send_cmd3,SW_Serve_Send_cmd4,SW_Serve_Send_cmd5;
u8	SW_Serve_Send_cmd6,SW_Serve_Send_cmd7,SW_Serve_Send_cmd8,SW_Serve_Send_cmd9,SW_Serve_Send_cmd10,SW_Serve_Send_cmd11;

u8	TCPIP_bConnected;
u8	TCPIP_Receive_Server_Bit;


//SWIM
u8 TimingstrOutPut[32];
u8 Receive_Data_Bit;


u8 ArmDelayNormalTime;				//?????? ?100? ?????50?   
u8 ArmDelayAfterStartTime;			//?????? ?50? ?????17?  
u8 StartingBlockOpenTime;			//??????? ?????3?
u8 StartingPosition;				//=1:?END???,??0-9????;=0:?RIGHT???,??10-19????

u8 RXD_Data_Buffer[4*TxRx_Data_Length];		//接收到数命令缓冲区   2023-10-24

u16 TCPIP_Rec_Char_Ptr;


u8 SW_Command0,SW_Command1;
u8 SW_Start_Num,SW_StartingBlock_Num;
u8 Control_Port_Num;

u8 Left_MB_Num=2;				//左边 盲表数量  最大三个
u8 Right_MB_Num=1;				//右边 盲表数量  最大三个
u8 L_MB_State_Line[3];		//左边 盲表的状态，接还是不连接
u8 R_MB_State_Line[3];		//右边 盲表的状态，接还是不连接

u8	Open_State=0;						//设置是否关闭触板计时状态位，=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板。2024-12-9
u8	g_in_net_test=0;					//2026-05-16 标记当前是否处于 net_test(参数设置)子界面：=1 时跳过 Display_TP/SB/MB_State 绘图，避免污染该界面

extern	u8*const netplay_remindmsg_tbl[5][GUI_LANGUAGE_NUM];
extern	u8*const netplay_ipmsg[5][GUI_LANGUAGE_NUM];
extern	u8*const netplay_netspdmsg[GUI_LANGUAGE_NUM];
extern	u8*const netplay_testmsg_tbl[3][GUI_LANGUAGE_NUM];
extern	u8*const netplay_memoremind_tb[2][GUI_LANGUAGE_NUM];
extern	u8*const netplay_tbtncaption_tb[GUI_LANGUAGE_NUM];
extern	u8*const netplay_protcaption_tb[GUI_LANGUAGE_NUM];
extern	u8*const netplay_protname_tb[3];
extern	u8*const netplay_ipcaption_tb[2][GUI_LANGUAGE_NUM];
extern	u8*const netplay_btncaption_tbl[5][GUI_LANGUAGE_NUM];
extern	u8*const netplay_mode_tbl[3];
extern	u8*const netplay_connmsg_tbl[4][GUI_LANGUAGE_NUM];
extern	u8*const netplay_portcaption_tb[GUI_LANGUAGE_NUM];
//netplay提示信息
/*
u8*const netplay_remindmsg_tbl[5][GUI_LANGUAGE_NUM]=
{
{"请插入网线!正在初始化网卡...","請插入網線!正在初始化網卡...","Pls insert cable!Ethernet Initing..",}, 
{"初始化失败!请检查网线!","初始化失敗!請檢查網線!","Init failed!Check the cable!",},  
{"正在DHCP获取IP...","正在DHCP獲取IP...","DHCP IP configing...",},  
{"DHCP获取IP成功!","DHCP獲取IP成功!","DHCP IP config OK!",},  
{"DHCP获取IP失败,使用默认IP!","DHCP獲取IP失敗,使用默認IP!","DHCP IP config fail!Use default IP",},  
};

//netplay IP信息

u8*const netplay_ipmsg[5][GUI_LANGUAGE_NUM]=
{
{"本机MAC地址:","本機MAC地址:","Local MAC Addr:",}, 
{" 远端IP地址:"," 遠端IP地址:","Remote IP Addr:",}, 
{" 本机IP地址:"," 本机IP地址:"," Local IP Addr:",}, 
{"   子网掩码:","   子網掩碼:","   Subnet MASK:",},
{"       网关:","       網關:","       Gateway:",},  
}; 
//网速提示 
//u8*const netplay_netspdmsg[GUI_LANGUAGE_NUM]={"   网络速度:","   網絡速度:","Ethernet Speed:"};
//netplay 测试提示信息
u8*const netplay_testmsg_tbl[3][GUI_LANGUAGE_NUM]=
{
{"可检查连接状态.","可檢查連接狀態.","to check the connection.",}, 
{"2,在浏览器输入:","2,在瀏覽器輸入:","2,Input:",}, 	
{"可登录web界面。","可登錄web界面。","in browser,you can log on to website.",}, 	
};
//netplay memo提示信息
u8*const netplay_memoremind_tb[2][GUI_LANGUAGE_NUM]=
{
{"接收区:","接收區:","Receive:",},
{"发送区:","發送區:","Send:",},
};
//netplay 测试按钮标题
u8*const netplay_tbtncaption_tb[GUI_LANGUAGE_NUM]={"开始测试","開始測試","Start Test",};
//netplay 协议标题
u8*const netplay_protcaption_tb[GUI_LANGUAGE_NUM]={"协议","協議","PROT",};
//netplay 协议名字
u8*const netplay_protname_tb[3]={"TCP Server","TCP Client","UDP",};
//netplay 端口标题
u8*const netplay_portcaption_tb[GUI_LANGUAGE_NUM]={"端口:","端口:","Port:",};
//netplay IP地址标题
u8*const netplay_ipcaption_tb[2][GUI_LANGUAGE_NUM]=
{
{"目标IP:","目標IP:","Target IP:",},
{"本机IP:","本機IP:"," Local IP:",},
};
//netplay 按钮标题
u8*const netplay_btncaption_tbl[5][GUI_LANGUAGE_NUM]=
{
{"协议选择","協議選擇","PROT SEL",},
{"连接","連接","Conn",},
{"断开","斷開","Dis Conn",},
{"清除接收","清除接收","Clear",},
{"发送","發送","Send",},
};
//网络模式选择
u8*const netplay_mode_tbl[3]={"TCP Server","TCP Client","UDP"};
//网络连接提示信息
u8*const netplay_connmsg_tbl[4][GUI_LANGUAGE_NUM]=
{
{"正在连接...","正在連接...","Connecting...",},
{"连接失败!","連接失敗!","Connect fail!",},
{"连接成功!","連接成功!","Connect OK!",},
{"LwIP错误!","LwIP錯誤!","LwIP Error!",}, 
};
*/


u16 hour,minute,second;
u16 msecond;

u16 Start_hour,Start_minute,Start_second,Start_msecond;   //2024-8-31 发令对应时刻的时间

//2026-05-11 新增：出发抢跳计时器（分/秒/毫秒，10ms 分辨率）
//             在"准备就绪"按下后开始计数，并一直累加（发令前后均如此）。
//             用以记录各道出发台信号出现时刻的读数，以便在发令后"开放时间"
//             结束后计算每道相对发令的时间（可负，即抢跳/犯规）。
u16 PreStart_minute=0,PreStart_second=0,PreStart_msecond=0;

//2026-05-11 发令枪响时刻在抢跳计时器中的读数（=发令瞬间的 PreStart_*）
u16 Gun_minute=0,Gun_second=0,Gun_msecond=0;

//2026-05-11 每道运动员出发台触发的时间（在抢跳计时器中的读数）  [i][0]:左 [i][1]:右
//          多次触发按迭代覆盖（最近一次有效），不立即显示/发送
u16 LaneStart_minute[10][2]={{0}},LaneStart_second[10][2]={{0}},LaneStart_msecond[10][2]={{0}};
u8  LaneStart_Valid[10][2]={{0}};     //=1:已记录到出发台信号; =0:无数据，到时显示"--"
u8  LaneStart_Computed[10][2]={{0}};
//2026-05-31 relay reaction time computation (hardware side): TouchPad_Process stores last touch time per lane, StartBox_RecordSignal in relay leg reads delta as reaction
u16 LastTouchTime_minute[10]={0}, LastTouchTime_second[10]={0}, LastTouchTime_msecond[10]={0};
u8  LastTouchTime_Valid[10]={0};  //=1:已在窗口结束后计算并广播过相对时间，=0:待处理

//2026-05-11 发令后出发台开放窗口（默认 3s = 300×10ms）的计时器/触发位
u16 PostGun_OpenWait_Time=0;          //发令后已等待时间（10ms 为单位）
u8  GunFired_PostOpenDoneBit=0;       //=0:窗口未结束 =1:窗口结束，主循环开始计算并广播每道结果

u16	Final_timer_posx=220,Final_timer_posy=40;	
u16	Middle_timer_posx=480,Middle_timer_posy=40;	

//u16 Result[50][10][2][5][4];   //成绩 第几次  触板成绩 道次 左0/右1 出发/触板/盲表1/盲表2/盲表3 时/分/秒/千分之一秒
//u8 Result[10][10][2][5][4];   //成绩 第几次  触板成绩 道次 左0/右1 出发/触板/盲表1/盲表2/盲表3 时/分/秒/千分之一秒

//u8 Result_TP[10][10][6];		//成绩 第几道  第几趟 第几名  触板成绩 道次 左0/右1 出发/触板/盲表1/盲表2/盲表3 时/分/秒/千分之一秒


u8  Race_No[10][2],Lane;		//
u16	All_Lap;			//游的总趟数（50米）  2023-10-16
u16	LAll_Lap;			//游的左边总趟数（50米）  2024-11-24
u16	RAll_Lap;			//游的右边总趟数（50米）  2024-11-24

u8 Dir_Dis[16];				//存放LCD ID字符串

u8 scmd_buf[TxRx_Data_Length+16];				//存放发送命令缓冲区

u8 lcd_id[32*2];				//存放LCD ID字符串
u8	timer_bit;				//计时位=0：不计时；=1：开始计时
u8	Ready_timer_bit;				//准备就绪计时器开始计时，计时位=0：不计时；=1：开始计时 2024-8-31


//[i][0]:左边 ；[i][1]:右边 ；  2024-11-27
u16 Lane_Display_MSecond[10][2];		//每道成绩的显示时间3000毫秒
u8 	Lane_Display_State[10][2];				//每道成绩的显示状态=0：不显示；=1：显示
u8	TP_Display_State[10][2];				//=1：TP成绩正在显示；=0：TP成绩没有显示  2024-3-28

u8 	Lane_TP_MB_State[10][2];							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
u16 Lane_TP_MB_Time_Difference[10];	//每道运动员触板和裁判按盲表的时间差   2023-11-5

u8	TP_MB_Bit;  //处理触板和盲表之间关系标志位  2023-11-5

u16	dir_posx,dir_posy;
u16	Start_Dir;								//发令点对应游泳方向   2024-6-9
u16	RMBbtn_posx,RMBbtn_posy;

u16 Open_Color=GUI_COLOR_WHITE;			//TP 打开

u16 Open_TP_Color=YELLOW;			//TP 打开，运动员可以触板：触板后有成绩
u16 Open_SB_Color=GREEN;			//SB 打开， 运动员可以出发：有出发反应时间
u16 Delay_Color=0XD000;	//GUI_COLOR_WHITE;			//TP SB 延迟期，运动员可以出发：有出发反应时间  2024-11-25
u16 Open_MB_Color=YELLOW;			//MB 打开，裁判可以按盲表，有盲表成绩 
u16 Close_Color=GRAY;				//TP 关闭，运动员触板后没成绩：触板无效
u16 Valid_Color=RED;				//TP 被触后，有成绩，红色提示
u16 Invalid_Color=GRAYBLUE;		//TP 未开启，颜色提示
u16 Bad_Color=BLACK;					//TP 坏，黑色提示
u16 UnInstall_Color=GUI_COLOR_BLACK;					//没有安装，黑色提示  2025-1-6

u16 ControlArea_Color=0X2ACC;	//0X2ADC;			//控制区域颜色	 2023-11-8

u8 TP_Wait_Open_Time[10];		//10道 触板被触后，另一边触板等待时间 后才打开
u8 TP_Open_Close_State[10][2];			//10道 触板状态：打开或关闭 =0：关闭；=1：打开。=2：延迟关闭（打开状态）=3：坏  =4：没有安装（单边安装）  =0：左，=1：右，   2025-1-6
u8 Startbox_Open_Close_State[10][2];		//10道 出发台，=0:左:0-9; =1:右:0-9  状态：打开或关闭，=0：关闭；=1：打开；=2：延迟关闭（打开状态）； =3：坏  =4：没有安装（单边安装）  2025-1-6
u8 prev_Startbox_State[10][2] = {{0}};	//2026-05-30 SB 状态上次快照, 跟当前比对触发上报
u8 prev_TP_State[10][2] = {{0}};	//2026-05-30 TP 状态上次快照
u8 prev_MB_State[3][20] = {{0}};	//2026-05-30 MB 状态上次快照 [mb_idx][lane*2+side]
u8 MB_Open_Close_State[3][20];			//10道 盲表，左:0-9，右:10-19    状态：打开或关闭，=0：关闭；=1：打开； =3：坏  =4：没有安装（单边安装） 2025-1-6


//2026-05-27 按比赛规则改: 支持每道 3 块盲表独立存成绩, 由 CalculateMBFinalTime 按规则计算最终成绩
//  MB_Result[20][3][4]: [道(左0-9 右10-19)][第几块 0/1/2][hour/min/sec/msec]
//  MB_Pressed_Bitmap[20]: [道] bit0/1/2 = 第 1/2/3 块是否已按下
u16 MB_Result[20][3][4];
u8  MB_Pressed_Bitmap[20];

u16	Lane_NoTbl[20];						//道次号查找表  2024-6-10
u8 KeyState[20];

u8 key;
u16	KeyValue,keyline,keycol;

	u16 MBsx[2];							//[0]:泳池左边MB,SB,TP,时间显示的X方向的位置  [1]:泳池右边MB,SB,TP,时间显示的X方向的位置 2024-11-28
	u16 Startboxsx[2];
	u16 TPsx[2];
	u16	Timer_posx[2];
	u16 MBsy[2];							//[0]:泳池左边MB,SB,TP,时间显示的X方向的位置  [1]:泳池右边MB,SB,TP,时间显示的X方向的位置 2024-11-28
	u16 Startboxsy[2];
	u16 TPsy[2];
	u16	Timer_posy[2];	
		
	u16 Lapsx[2];						//[0]:显示左边游的趟数的位置 [1]:显示右边游的趟数的位置 2024-11-28


	u16 MB_CR; 										//盲表显示半径
	u16	LaneStep_y;								//道次间显示的点间距
	
	u16 Final_MBsx,Final_MBsy;
	u16 Final_Startboxsx,Final_Startboxsy;
	u16 Final_TPsx,Final_TPsy;
	u16 Final_lapsx;						//显示终点游的趟数的位置
	
	u16 Middle_MBsx,Middle_MBsy;
	u16 Middle_Startboxsx,Middle_Startboxsy;
	u16 Middle_TPsx,Middle_TPsy;
	u16 Middle_lapsx;						//显示中间游的趟数的位置
	
	u8 fsize=0;				//key字体大小
	
	u8 keyold=0XFF;			//按键和之前的按键值
	u8 key_oldstate[5][20];			//按键和之前的按键值

	u16  scanline=0;		//扫描行数据
	u16  TouchPadscanline=0;		//触板扫描行数据
	u16 readcol=0;		//读入列数据

	u8	Place[20];		//运动员的比赛名次；
	u8	Lap_Place[40*2];		//每圈对应的名次；
	
	u8 ds0sta=1,ds1sta=1;		//发光LED状态
	
	u8	RelayBit=0;					//接力比赛位 =1：接力比赛； =0：非接力比赛 2024-11-21
	//2026-06-02 PC 端 "硬件设备 一直打开" 开关 (Set_MatchEvent 0x43 d9): =1 忽略 *_Open_Close_State==0 关闭判定, 只跳 ==3 坏/==4 未装. =0 原比赛流程
	u8	HardwareAlwaysOpenBit=0;
	u8 	Baton_No[10];				//接力第几棒 2024-11-21
	u8 	RelayLaps=0;					//接力距离（圈数）2024-11-24
	u16	Relay_SB_DelayClose_Time[10];				//接力比赛运动员跳台出发信号关闭延迟时间 2024-11-25
	u8	Relay_SB_DelayCloseBit[10];			//接力比赛运动员跳台出发信号关闭延迟时间位 =1：接力比赛运动员出发后开始延迟计时   =0：还不延迟计时 2024-11-25

	u16	TP_DelayClose_Time[10];				//运动员触板TP信号关闭延迟时间 2024-12-12
	u8	TP_DelayCloseBit[10];			//运动员触板TP信号关闭延迟时间位 =1:运动员触板TP后开始延迟计时   =0：还不延迟计时 2024-12-12


	u8	CloseLaneState[10];					//关闭道次状态=2：打开；=3：关闭

	//2026-05-14 0x47 Set_LaneOpenClose 用：道次整体启用/禁用标志
	//   =1：该道纳入比赛，触板/出发台/盲表按正常状态机响应（默认）
	//   =0：该道完全屏蔽，硬件不接受其任何信号（即使 0x4C 全开也无视）
	//   与 CloseLaneState 区别：CloseLaneState 是 0x43 Set_MatchEvent 下来的"本组空道位图"，
	//   LaneEnabled 是 0x47 单独的"动态启用/禁用"；两者均为 0 时该道屏蔽。
	u8	LaneEnabled[10] = {1,1,1,1,1,1,1,1,1,1};	//默认全部启用

	u32 tx_overflow_cnt = 0;	//2026-05-30 步骤 2: Send_Data_buf ring buffer 溢出计数 (调试用)
	u8	Testing_bit;							//正在进行测试位 =1：正在测试； =0：停止测试   2023-8-5

	u16 btnw,btnh;				//按钮参数
	u16 btnw1,btnh1;				//按钮参数
	u16 CMD_btnw,CMD_btnh;				//Command按钮宽度，高度参数
	u16 resultw,resulth;					//成绩显示区域的宽度和高度参数

	u16 btnds0x,btnds0y,btnds1x,btnds1y;	//按钮坐标参数
	u16 carea_x0,carea_y0,Lbtnwx,Lbtnhy;	//左边按钮坐标参数
	u16 btndsx,btnwx,btnhy;	//右边按钮坐标参数
	  
	u16 cds0x,cr; 		//圆坐标参数
	u16 Inf_area_x0,Inf_area_y0; 		//信息显示区起点坐标参数
	u16 RunningTime_x0,RunningTime_y0; 		//滚动时间显示区起点坐标参数
	u16 StartFinalPlace_x0,StartFinalPlace_y0;			//发令位置标志显示位置x,y起点坐标
	
	u8 btnfsize;				//按钮字体大小   
	u8	laps[10][2];					//[i][0]已游50米左边的趟数 ; [i][1]:已游50米右边的趟数  2024-11-27

	u16 Placex;						//显示名次的位置

	
	u16	Rec_send_num;											//记录将发送数据的次数  2023-7-11
	u8	Send_Data_buf[TxRx_Data_Length*Rec_Loop];		//	即将发送数据缓冲区  2023-8-14
	u8	Send_buf[TxRx_Data_Length*Rec_Loop];				//	正在发送数据缓冲区  2023-8-14

	u8	Procee_SwimDir_Bit;		//处理游泳运动员游的方向和滚动时间 2023-7-6 
 
u8 lcd_Dis[32*2];				//存放LCD ID字符串
u8 line_height1=64;//34;//26;//28;		//行间距
									
u8 dir_len;		//运动员游的时长，显示用  2023-7-27
			
u8 	RS_TX_Bit;							//串口发送状态位
u16 RS_TX_No,RS_TX_len;
u16 RS_TX_Ptr;							//串口发送数据指针   2023-10-25
u8  UART4_TX_BUF[UART4_MAX_SEND_LEN]; 		//发送缓冲,最大UART4_MAX_SEND_LEN字节
u8 	UART4_RX_BUF[UART4_MAX_RECV_LEN]; 		//接收缓冲,最大UART4_MAX_RECV_LEN个字节.
//[15]:0,没有接收到数据;1,接收到了一批数据.
//[14:0]:接收到的数据长度
vu16 UART4_RX_STA=0;   	 
u16		UART4_RX_PTR=0;

//2024-6-8 
u8 SwimmingPool_Arrage;	//=0:正方向布置，道次从上到下0-9道； =1:反方向布置，道次从上到下9-0道。 
u8 StartFinalPlace;	//=0:发令点和终点参数。  2024-11-27
u8 StartPlace;	//比赛发令点位置 =0:左边 ； =1:右边
u8 FinalPlace;	//终点位置 比赛终点位置 =0:终点位置在屏幕左边； =1：终点位置在屏幕右边。
	
u16	Close_Time=60;//200;	2024-11-24				//泳道关闭时间 ，泳池两端安装触板  2025-1-2
u16	All_Close_Time=400;			//全泳道关闭时间    ，泳池单边安装触板 2025-1-2
u16 Result_Display_Time=Result_Display_Time_Value;
u16 FalseStartThreshold=10;	//2026-05-26 抢跳判定阈值, 单位 0.01s, 默认 10 = 0.1s				//不低于1秒，每道成绩的显示停留时间3000毫秒
u16	TP_DelayCloseValue=50;		//运动员触板TP信号关闭延迟时间初始设置5秒 2024-12-12
u16	Relay_SB_DelayCloseValue=50;		//不低于1秒，接力比赛运动员跳台出发信号关闭延迟时间初始设置5秒 2024-11-25
u16	MBdelay_Time=40;				//不低于2秒，在没有TP成绩的情况下，已有盲表成绩，等待MBdelay_Time时间后，仍然没有TP成绩，就用盲表成绩代替此道成绩 2023-11-3
//////////////////////////////////////////////////////////////////////////////////	 
//ALIENTEK STM32开发板
//创建日期:2023/10/24
//版本：V1.0
//版权所有，盗版必究。
//Copyright(C) 北京华亿创新信息技术股份有限公司 2023-2029
//All rights reserved									  
//*******************************************************************************
//修改信息
//无
////////////////////////////////////////////////////////////////////////////////// 	   

//#define TP_PRES_DOWN 0x80 //????? 
//#define TP_CATH_PRES 0x40 //??????
 //Cmd按钮标题
u8*const Hcmd_btncaption_tbl[10]= 
{"触板0","触板1","触板2","触板3","触板4","触板5","触板6","触板7","触板8","触板9"};

u8*const Hcmd_Lbtncaption_tbl[10]= 
{"触板0","触板1","触板2","触板3","触板4","触板5","触板6","触板7","触板8","触板9"};

//2024-6-8 反方向显示道次
u8*const Hcmd_Inv_btncaption_tbl[10]= 
{"触板9","触板8","触板7","触板6","触板5","触板4","触板3","触板2","触板1","触板0"};

u8*const Hcmd_Inv_Lbtncaption_tbl[10]= 
{"触板9","触板8","触板7","触板6","触板5","触板4","触板3","触板2","触板1","触板0"};


u8	Distance_Max=6;
u8 	laps_No_tbl[6]={1,2,4,8,16,30};
u8 	Llaps_No_tbl[6]={1,1,2,4,8,15};		//2024-11-24
u8 	Rlaps_No_tbl[6]={0,1,2,4,8,15};		//2024-11-24
u8	Laps_No=1;

//短池25m 游泳运动员 每边触壁的次数 2025-1-4
u8 	laps25m_No_tbl[6]={2,4,8,16,32,60};
u8 	Llaps25m_No_tbl[6]={1,2,4,8,16,30};		//2025-1-4
u8 	Rlaps25m_No_tbl[6]={1,2,4,8,16,30};		//2025-1-4

//DS0按钮标题
u8*const Hds0_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"M.发令","M.Start","Timer ON",},
{"M.Start","Pause","Timer OFF",},  
};
//DS1按钮标题
u8*const Hds1_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"计时复位","DS1亮","DS1 ON",},
{"RESET","DS1滅","DS1 OFF",},  
};

//Relay按钮标题  2024-11-21
u8*const Relay_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"非接力","非接力","No Relay",},  
{"接力","接力","Relay",},
};

//Test按钮标题
u8*const Test_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"测试","DS1亮","DS1 ON",},
{"正在测试","DS1滅","DS1 OFF",},  
};

//Lane反向按钮标题  2024-6-8
u8*const Lane_Inv_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"道次顺序","DS1亮","DS1 ON",},
{"LaneInv","DS1滅","DS1 OFF",},  
};



//Ready按钮标题
u8*const Ready_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"准备就绪","DS1亮","DS1 ON",},
{"Ready","DS1滅","DS1 OFF",},  
};


extern vu8 ledplay_ds0_sta;		//ledplay任务,DS0的控制状态
extern u8 net_test(void);				//2024-10-23 网络设置入口（含连接按钮）
extern void net_toggle_connect(void);	//2026-05-13 主界面顶部网络连接按钮专用：直接切换 connstatus

u8 lcd_btnDis[32];				//存放LCD ID字符串 2024-11-24

//2024-10-23
/*
extern u8 connstatus;//0,未连接,1,已连接
extern struct netbuf *recvbuf;//接收缓冲区
extern struct netbuf *sendbuf;//发送缓冲区	
extern struct netbuf *sendcmdbuf;//发送command缓冲区	
extern struct netconn *netconnnew;//新TCP/UDP网络连接结构体指针(只有TCP Server会用到这个)
extern struct netconn *netconncom;//通用TCP/UDP网络连接结构体指针(TCP Server/TCP Client/UDP通用)
*/
u8 connstatus;//0,未连接,1,已连接
 struct netbuf *recvbuf;//接收缓冲区
 struct netbuf *sendbuf;//发送缓冲区	
 struct netbuf *sendcmdbuf;//发送command缓冲区	
 struct netconn *netconnnew;//新TCP/UDP网络连接结构体指针(只有TCP Server会用到这个)
 struct netconn *netconncom;//通用TCP/UDP网络连接结构体指针(TCP Server/TCP Client/UDP通用)

	u8 editflag=0;	//0,编辑的是smemo
					//1,编辑的是eip
					//2,编辑的是eport
	u8 *p,*ptemp; 
	u32 rxcnt=0;
	u32 txcnt=0;
	u8 protocol=0;	//默认TCP Server协议
					//0,TCP Server协议
					//1,TCP Client协议
					//2,UDP协议
	u8 oldconnstatus=0;//老的状态
	u8 tcpconn=0;	//TCP连接是否建立:0,未建立;1,建立了
	u32 oldaddr=0;	//最近一次数据来自的ip地址
	u16 oldport=0;	//最近一次数据来自的port

	ip_addr_t tipaddr;	//本机IP地址   2024-11-1
	u16	tport=8088;		//本机端口号,(要连接的端口号)默认为8088;		 


	ip_addr_t Remote_tipaddr;	//远程、临时IP地址
	u16	Remote_tport=8088;		//远程、临时端口号,(要连接的端口号)默认为8088;		2024-11-1 


u8	Display_RollingTime_Bit;
u8	Send_Bit;
u16	Prot_Sx,IP_Sx,Port_Sx,TX_Sx,RX_Sx;
u16 connbtn_ux,protbtn_ux;

/*
//Prot_Sx=5,IP_Sx=220-20,Port_Sx=500-20,TX_Sx=900+20,RX_Sx=1030+20;

//显示提示信息
//y:y坐标,x坐标恒定从0开始
//height:区域高度
//fsize:字体大小
//tx:发送字节数
//rx:接收字节数
//prot:协议类型
//     0:TCP Server 
//     1:TCP Client
//     2:UDP
//flag:更新标记,详见下面的描述
//bit0,1,更新tx数据,0,不更新
//bit1,1,更新rx数据,0,不更新
//bit2,1,更新port数据,0,不更新
void net_msg_show(u16 y,u16 height,u8 fsize,u32 tx,u32 rx,u8 prot,u8 flag)
{
	u8 *pbuf;
	pbuf=gui_memin_malloc(100);
	if(pbuf==NULL)return;//内存申请失败
	if(prot>2)prot=2;
//	xdis=(lcddev.width-(35*fsize/2))/3;//间隙
	
//	BACK_COLOR=GRAYBLUE;//LIGHTBLUE;// DARKBLUE;//2023-5-12 NET_MSG_BACK_COLOR;
		y=5;		
		fsize=24;
	
	//2024-10-17  网络不显示接收和发送数据
	   
	if(flag&1<<0)//更新TX数据
	{
		//		xdis=1100;
		gui_fill_rectangle(TX_Sx,y+(height-fsize)/2,10*fsize/2,fsize,NET_MSG_BACK_COLOR);//清除之前的显示
		sprintf((char*)pbuf,"TX:%d",tx);
		gui_show_string(pbuf,TX_Sx,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//TX字节数显示
	}
	if(flag&1<<1)//更新RX数据
	{ 
//		xdis=10;
		gui_fill_rectangle(RX_Sx,y+(height-fsize)/2,10*fsize/2,fsize,NET_MSG_BACK_COLOR);//清除之前的显示
		sprintf((char*)pbuf,"RX:%d",rx);
		gui_show_string(pbuf,RX_Sx,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//RX字节数显示
	}
	
	if(flag&1<<2)//更新prot数据
	{
//		xdis=10;
//		gui_fill_rectangle(xdis/2+20*fsize/2+xdis*2,y+(height-fsize)/2,15*fsize/2,fsize,NET_MSG_BACK_COLOR);//清除之前的显示
//		sprintf((char*)pbuf,"%s:%s",netplay_protcaption_tb[gui_phy.language],netplay_protname_tb[prot]);//协议
//		gui_show_string(pbuf,xdis/2+20*fsize/2+xdis*2,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//显示协议
		gui_fill_rectangle(Prot_Sx,y+(height-fsize)/2,15*fsize/2,fsize,NET_IP_BACK_COLOR);//NET_MSG_BACK_COLOR);//清除之前的显示
		sprintf((char*)pbuf,"%s:%s",netplay_protcaption_tb[gui_phy.language],netplay_protname_tb[prot]);//协议
		gui_show_string(pbuf,Prot_Sx,y+(height-fsize)/2,lcddev.width,fsize,fsize,WHITE);//NET_MSG_FONT_COLOR);//显示协议
//		gui_show_string(pbuf,xdis,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//显示协议
	}
	gui_memin_free(pbuf);//释放内存
	
}
//设置编辑框颜色
//ipx:ip编辑框
//portx:port编辑框
//prot:协议
//connsta:连接状态
void net_edit_colorset(_edit_obj *ipx,_edit_obj *portx,u8 prot,u8 connsta)
{
	if(connsta==1)//连接成功?没的说,都是不可编辑
	{
		ipx->textcolor=WHITE;
		portx->textcolor=WHITE;
	}else//非连接状态
	{
		switch(prot)
		{
			case 0://TCP Server协议
				portx->textcolor=GREEN;	//绿色,表示可以编辑
				ipx->textcolor=WHITE;	//白色,固定死了
				break;
			case 1://TCP Client协议
			case 2://UDP协议
				portx->textcolor=GREEN;	//绿色,表示可以编辑
				ipx->textcolor=GREEN;	//绿色,表示可以编辑 
				break;
		}		
	}
	edit_draw(ipx);		//画编辑框
	edit_draw(portx);	//画编辑框
} 
//将字符串形式的port转换为数字形式的port
//str:字符串形式的port号
//返回值:转换成数字形式的port号
u16 net_get_port(u8 *str)
{
	u16 port;
	port=atoi((char*)str);
	return port;
}
//将字符串形式的ip地址转换为数字形式的ip
//返回值:0,错误的IP,其他,正确的IP.
u32 net_get_ip(u8 *str)
{
	u8 *p1,*p2,*ipstr;
	struct ip_addr ipx;
	u8 ip[4];
	ipstr=gui_memin_malloc(30);
	if(ipstr==NULL)return 0;
	strcpy((char*)ipstr,(char*)str);//拷贝字符串
	p1=ipstr;p2=(u8*)strstr((const char*)p1,".");
	if(p2==NULL){gui_memin_free(ipstr);return 0;}//IP错误
	p2[0]=0;ip[0]=atoi((char*)p1);//得到第一个值
	p1=p2+1;p2=(u8*)strstr((const char*)p1,".");
	if(p2==NULL){gui_memin_free(ipstr);return 0;}//IP错误
	p2[0]=0;ip[1]=atoi((char*)p1);//得到第二个值 
	p1=p2+1;p2=(u8*)strstr((const char*)p1,".");
	if(p2==NULL){gui_memin_free(ipstr);return 0;}//IP错误
	p2[0]=0;ip[2]=atoi((char*)p1);//得到第三个值 
	p1=p2+1;ip[3]=atoi((char*)p1);//得到第四个值 
	IP4_ADDR(&ipx,ip[0],ip[1],ip[2],ip[3]);
	gui_memin_free(ipstr);
	return ipx.addr;//返回得到的IP地址
}
extern void tcp_pcb_purge(struct tcp_pcb *pcb);	//在 tcp.c里面 
extern struct tcp_pcb *tcp_active_pcbs;			//在 tcp.c里面 
extern struct tcp_pcb *tcp_tw_pcbs;				//在 tcp.c里面  
//强制删除TCP Server主动断开时的time wait
void net_tcpserver_remove_timewait(void)
{
	struct tcp_pcb *pcb,*pcb2; 
	while(tcp_active_pcbs!=NULL)delay_ms(10);//等待tcp_active_pcbs为空 
	pcb=tcp_tw_pcbs;
	while(pcb!=NULL)//如果有等待状态的pcbs
	{
		tcp_pcb_purge(pcb); 
		tcp_tw_pcbs=pcb->next;
		pcb2=pcb;
		pcb=pcb->next;
		memp_free(MEMP_TCP_PCB,pcb2);	
	}
}
//断开连接
//netconn1:网络连接结构体1
//netconn2:网络连接结构体2
void net_disconnect(struct netconn *netconn1,struct netconn *netconn2)
{
	if(netconn1!=NULL)//连接结构体有效?
	{
		if(netconn1->type==NETCONN_TCP)netconn_close(netconn1);//关闭TCP netconn1连接
		else if(netconn1->type==NETCONN_UDP)netconn_disconnect(netconn1);//关闭UDP netconn1连连接
		netconn_delete(netconn1);  //删除netconn1连接
	}
	if(netconn2!=NULL)//连接结构体有效?
	{
		if(netconn2->type==NETCONN_TCP)netconn_close(netconn2);//关闭TCP netconn2连接
		else if(netconn2->type==NETCONN_UDP)netconn_disconnect(netconn2);//关闭UDP netconn2连连接
		netconn_delete(netconn2);  //删除netconn2连接
	}
}
*/

void OnSendSWData(u8 command0,u8 command1,u8 Control_Port)
{
	scmd_buf[SW_Code0_Pos]=SW_Start_Code;
	scmd_buf[SW_Code1_Pos]='S';
	scmd_buf[SW_Command0_Pos]=command0;
	scmd_buf[SW_Command1_Pos]=command1;
	scmd_buf[SW_Control_Port_Pos]=Control_Port;

	scmd_buf[SW_Time_Minute_Pos]=minute;
	scmd_buf[SW_Time_Second_Pos]=second;
	scmd_buf[SW_Time_100ms_Pos]= msecond/10;							//2023-7-4
	scmd_buf[SW_Time_Hour_1ms_Pos]=hour*16+msecond%10;		//2023-7-4
	scmd_buf[SW_Time_Hour_Pos]=hour;									//2024-8-31

	//2026-05-13(3) 显式清零 D10（SW_Back1_Pos）。否则 scmd_buf 是全局共享缓冲，上一次
	//   OnSendSWCommand_Data 写入的 sign=1（抢跳标志）会残留到下一帧。后果：触板/盲表/
	//   普通出发台触发帧上"莫名"带上 D10=1，PC 端按 D10≠0 判抢跳，反应时被错误取负。
	//   对于本函数（不带 sign 语义的简易发送），D10 永远应该是 0。
	scmd_buf[SW_Back1_Pos]=0;

	scmd_buf[SW_End_Code_Pos]=SW_End_Code;
	
	scmd_buf[TxRx_Data_Length-1]=SW_End_Code;
	
	if(Rec_send_num>=Rec_Loop) { tx_overflow_cnt++; Rec_send_num=0; }   //2026-05-30 步骤 2: 量化 ring buffer 溢出丢包
	for(u16 i=0;i<TxRx_Data_Length;i++)
	{
		Send_Data_buf[Rec_send_num*TxRx_Data_Length+i]=scmd_buf[i];
	}
	Rec_send_num++;
					
	RS_TX_No++;				//需要发送数据次数  2023-10-26

}

//??????????
void OnSendSWCommand_Data(u8 command0,u8 command1,u8 Zigbee_Port,u8 para0,u8 para1,u8 para2,u8 para3,u8 para4,u8 para5) 
{
	scmd_buf[SW_Code0_Pos]=SW_Start_Code;
	scmd_buf[SW_Code1_Pos]='S';
	scmd_buf[SW_Command0_Pos]=command0;
	scmd_buf[SW_Command1_Pos]=command1;
	scmd_buf[SW_Control_Port_Pos]=Zigbee_Port;

	scmd_buf[SW_Time_Minute_Pos]=para0;
	scmd_buf[SW_Time_Second_Pos]=para1;
	scmd_buf[SW_Time_100ms_Pos]=para2;
	scmd_buf[SW_Time_Hour_1ms_Pos]=para3;
	scmd_buf[SW_Time_Hour_Pos]=para4;
	scmd_buf[SW_Back1_Pos]=para5;

	scmd_buf[SW_End_Code_Pos]=SW_End_Code;

	scmd_buf[TxRx_Data_Length-1]=SW_End_Code;
	
	if(Rec_send_num>=Rec_Loop) { tx_overflow_cnt++; Rec_send_num=0; }   //2026-05-30 步骤 2: 量化 ring buffer 溢出丢包
	for(u16 i=0;i<TxRx_Data_Length;i++)
	{
		Send_Data_buf[Rec_send_num*TxRx_Data_Length+i]=scmd_buf[i];
	}
	Rec_send_num++;
	RS_TX_No++;				//需要发送数据次数  2023-10-26
	
}

 
//显示圆形提示信息
//x,y:要显示的圆中心坐标
//r:半径
//fsize:字体大小
//color:圆的颜色
//str:显示的字符串
void key_show_circle(u16 x,u16 y,u16 r,u8 fsize,u16 color,u8 *str)
{ 
	gui_fill_circle(x,y,r,color);
	gui_show_strmid(x-r,y-fsize/2,2*r,fsize,BLUE,fsize,str);//显示标题  
}

	_btn_obj* CloseLanebtn[10];			//控制关闭道次按钮
	u16 	CloseLanebtn_width=160+40;		//2024-11-24  控制关闭道次按钮的宽度；

	_btn_obj* RMBLanebtn[10];			//读盲表成绩补充无TP成绩的对应道次按钮 2023-11-3
	
 	_btn_obj* cmdRbtn[10];			//控制右边按钮
 	_btn_obj* cmdLbtn[10];			//控制左边按钮

 	_btn_obj* Startbtn=0;			//控制按钮
 	_btn_obj* Resetbtn=0;			//控制按钮
	_btn_obj* Readybtn=0;			//Ready准备就绪控制按钮
	_btn_obj* Testbtn=0;			//Test测试控制按钮

	_btn_obj* Relaybtn=0;			//接力比赛控制按钮  2024-11-21

 	_btn_obj* SendStartTimerbtn=0;			//发送发令时间 控制按钮   2024-9-1
	
//	_btn_obj* LaneInvbtn=0;			//LaneInv道次反向控制按钮  2024-11-3
 
	_btn_obj* Distance_Addbtn=0;			//比赛距离+1控制按钮
	_btn_obj* Distance_Decbtn=0;			//比赛距离-1控制按钮

 	_btn_obj* Setupbtn=0;			//设置参数 控制按钮   2024-10-23

	_btn_obj* ExitShutdownbtn=0;	//退出/关机 控制按钮   2026-05-11

	_btn_obj* NetConnbtn=0;			//网络连接/断开 控制按钮（主界面顶部）   2026-05-13

//2026-05-13(2nd) 帮手：给左/右道次按钮（BTN_TYPE_ANG）涂"更淡"的淡黄色背景+黑字
static void Setup_LaneBtn_LightYellow(_btn_obj *btn)
{
	if(!btn || !btn->bkctbl) return;
	btn->bkctbl[0]=0XFFE0;	//边框 纯黄（保留辨识度）
	btn->bkctbl[1]=0XFFFC;	//顶线 几近白的浅黄
	btn->bkctbl[2]=0XFFFC;	//上半 几近白的浅黄
	btn->bkctbl[3]=0XFFF4;	//下半 略带黄的渐变
	btn->bcfucolor=BLACK;
	btn->bcfdcolor=WHITE;
}

	u8 ip_height,ip_fsize;			//IP/PORT区域高度和字体大小

	//2024-11-10
	u8 Csecond=0;
	short temperate=0;	//温度值		   
	u8 t=0;
	u8 tempdate=0;
	
	u16	Voltage_x0=400,Voltage_y0=1;   //电压显示x,y坐标



//SWIM测试
//═════════════════════════════════════════════════════════════════════════════
// 2026-05-18(3) STM32 H7 Backup SRAM (4KB @0x38800000, VBAT 备份持久) 持久状态
//   存放参数 + TP/SB/MB 状态 + 比赛进行数据 + 当前计时数字+计时位
//   开机 swim_play 初始化时自动加载 (Load)；主循环每秒自动保存 (Save)
//   BKPSRAM 写不损耗，可频繁写 (跟板上 NAND Flash 相反, NAND 写入次数有限制)
//═════════════════════════════════════════════════════════════════════════════
//2026-05-20 BKPSRAM 持久化用户改过的本机 IP 需要的依赖
#include "lwip/netif.h"
extern struct netif lwip_netif;   //lwip_comm.c 中定义
u8 bkp_local_ip_valid = 0;        //net_test 改 IP 时置 1, Save 时写入 BKPSRAM

#define SWIM_STATE_MAGIC  0x53574D31u    // "SWM1" 标识 BKPSRAM 区已被本程序占用
#define SWIM_STATE_VERSION 2u            // 数据布局版本号 (v2: +local_ip[4], 2026-05-20)

typedef struct {
	u32 magic;          // SWIM_STATE_MAGIC
	u32 version;        // SWIM_STATE_VERSION
	u32 checksum;       // 后续字节 XOR
	u32 _pad0;          // 对齐占位
	//---- 参数 ----
	u8  StartFinalPlace, StartPlace, FinalPlace, SwimmingPool_Arrage;
	u8  Pool50mOr25mbit, PoolSingleOrDoubleTPbit, Left_MB_Num, Right_MB_Num;
	u8  Open_State;
	u8  _pad1[3];
	u16 Close_Time, All_Close_Time;
	u16 Result_Display_Time, TP_DelayCloseValue;
	u16 Relay_SB_DelayCloseValue, MBdelay_Time;
	u16 tport;
	u16 _pad2;
	//---- TP/SB/MB 状态数组 ----
	u8 TP_Open_Close_State[10][2];
	u8 Startbox_Open_Close_State[10][2];
	u8 MB_Open_Close_State[3][20];
	//---- 比赛进行数据 ----
	u8 CloseLaneState[10];
	u8 laps[10][2];
	u8 LAll_Lap, RAll_Lap, All_Lap, RelayBit;
	u16 RelayLaps;
	u16 _pad3;
	//---- 当前计时数字 + 计时位 ----
	u16 hour, minute, second, msecond;
	u16 Start_hour, Start_minute, Start_second, Start_msecond;
	u8  timer_bit, Ready_timer_bit, _pad4, _pad5;
	//---- 2026-05-20 用户修改后的本机 IP (lwip_comm_default 默认 192.168.1.30, 用户改后覆盖) ----
	u8  local_ip[4];
	u8  local_ip_valid;     // 1=有效(用 local_ip 覆盖默认), 0=未改过, 用默认
	u8  _pad6[3];
} SwimMatchState_t;

//启用 BKPSRAM 时钟 + Backup 域访问 + 调节器，确保 VBAT 备份生效
void BkpSRAM_Init(void)
{
	//2026-05-18(3) 直接寄存器操作，避免 HAL 隐式声明警告
	RCC->AHB4ENR |= RCC_AHB4ENR_BKPRAMEN;   //开 BKPSRAM 时钟
	PWR->CR1     |= PWR_CR1_DBP;             //允许写 Backup 域
	PWR->CR2     |= (1u << 0);               //BREN: 开 Backup 调节器 (VBAT/standby 保持 BKPSRAM)
	while(!(PWR->CR2 & (1u << 16)));         //BRRDY: 等调节器就绪
}

//计算 checksum（magic/version/checksum 之外的所有字节 XOR）
static u8 _SwimState_CalcChecksum(SwimMatchState_t *st)
{
	u8 *p = (u8*)st;
	u32 i;
	u8 cs = 0;
	// 跳过 magic(4) + version(4) + checksum(4) + _pad0(4) = 16 字节
	for(i=16; i<sizeof(SwimMatchState_t); i++) cs ^= p[i];
	return cs;
}

//把全局变量打包到 BKPSRAM
void Save_State_To_BkpSRAM(void)
{
	SwimMatchState_t *bkp = (SwimMatchState_t*)D3_BKPSRAM_BASE;
	SwimMatchState_t st;
	u16 i, k;
	st.magic = SWIM_STATE_MAGIC;
	st.version = SWIM_STATE_VERSION;
	st.checksum = 0; st._pad0 = 0;
	st.StartFinalPlace = StartFinalPlace; st.StartPlace = StartPlace;
	st.FinalPlace = FinalPlace; st.SwimmingPool_Arrage = SwimmingPool_Arrage;
	st.Pool50mOr25mbit = Pool50mOr25mbit; st.PoolSingleOrDoubleTPbit = PoolSingleOrDoubleTPbit;
	st.Left_MB_Num = Left_MB_Num; st.Right_MB_Num = Right_MB_Num;
	st.Open_State = Open_State;
	st._pad1[0]=0; st._pad1[1]=0; st._pad1[2]=0;
	st.Close_Time = Close_Time; st.All_Close_Time = All_Close_Time;
	st.Result_Display_Time = Result_Display_Time; st.TP_DelayCloseValue = TP_DelayCloseValue;
	st.Relay_SB_DelayCloseValue = Relay_SB_DelayCloseValue; st.MBdelay_Time = MBdelay_Time;
	st.tport = tport; st._pad2 = 0;
	for(i=0; i<10; i++){
		st.TP_Open_Close_State[i][0] = TP_Open_Close_State[i][0];
		st.TP_Open_Close_State[i][1] = TP_Open_Close_State[i][1];
		st.Startbox_Open_Close_State[i][0] = Startbox_Open_Close_State[i][0];
		st.Startbox_Open_Close_State[i][1] = Startbox_Open_Close_State[i][1];
		st.CloseLaneState[i] = CloseLaneState[i];
		st.laps[i][0] = laps[i][0]; st.laps[i][1] = laps[i][1];
	}
	for(k=0; k<3; k++) for(i=0; i<20; i++) st.MB_Open_Close_State[k][i] = MB_Open_Close_State[k][i];
	st.LAll_Lap = LAll_Lap; st.RAll_Lap = RAll_Lap; st.All_Lap = All_Lap;
	st.RelayBit = RelayBit; st.RelayLaps = RelayLaps; st._pad3 = 0;
	st.hour = hour; st.minute = minute; st.second = second; st.msecond = msecond;
	st.Start_hour = Start_hour; st.Start_minute = Start_minute;
	st.Start_second = Start_second; st.Start_msecond = Start_msecond;
	st.timer_bit = timer_bit; st.Ready_timer_bit = Ready_timer_bit;
	st._pad4 = 0; st._pad5 = 0;
	//2026-05-20 保存用户修改后的本机 IP (跨重启持久化)
	st.local_ip[0] = lwipdev.ip[0]; st.local_ip[1] = lwipdev.ip[1];
	st.local_ip[2] = lwipdev.ip[2]; st.local_ip[3] = lwipdev.ip[3];
	st.local_ip_valid = bkp_local_ip_valid;  //仅当用户调用 net_test 改过 IP 后置 1
	st._pad6[0]=0; st._pad6[1]=0; st._pad6[2]=0;
	st.checksum = _SwimState_CalcChecksum(&st);
	memcpy((void*)bkp, &st, sizeof(st));
}

//从 BKPSRAM 读取并恢复全局变量；返回 1=加载成功, 0=BKPSRAM 无效/首次启动
u8 Load_State_From_BkpSRAM(void)
{
	SwimMatchState_t *bkp = (SwimMatchState_t*)D3_BKPSRAM_BASE;
	SwimMatchState_t st;
	u16 i, k;
	memcpy(&st, (const void*)bkp, sizeof(st));
	if(st.magic != SWIM_STATE_MAGIC) return 0;     //首次启动 / VBAT 断电
	if(st.version != SWIM_STATE_VERSION) return 0; //版本不兼容
	if(_SwimState_CalcChecksum(&st) != st.checksum) return 0; //数据损坏
	//校验通过：恢复全局变量
	StartFinalPlace = st.StartFinalPlace; StartPlace = st.StartPlace;
	FinalPlace = st.FinalPlace; SwimmingPool_Arrage = st.SwimmingPool_Arrage;
	Pool50mOr25mbit = st.Pool50mOr25mbit; PoolSingleOrDoubleTPbit = st.PoolSingleOrDoubleTPbit;
	Left_MB_Num = st.Left_MB_Num; Right_MB_Num = st.Right_MB_Num;
	Open_State = st.Open_State;
	Close_Time = st.Close_Time; All_Close_Time = st.All_Close_Time;
	Result_Display_Time = st.Result_Display_Time; TP_DelayCloseValue = st.TP_DelayCloseValue;
	Relay_SB_DelayCloseValue = st.Relay_SB_DelayCloseValue; MBdelay_Time = st.MBdelay_Time;
	tport = st.tport;
	for(i=0; i<10; i++){
		TP_Open_Close_State[i][0] = st.TP_Open_Close_State[i][0];
		TP_Open_Close_State[i][1] = st.TP_Open_Close_State[i][1];
		Startbox_Open_Close_State[i][0] = st.Startbox_Open_Close_State[i][0];
		Startbox_Open_Close_State[i][1] = st.Startbox_Open_Close_State[i][1];
		CloseLaneState[i] = st.CloseLaneState[i];
		laps[i][0] = st.laps[i][0]; laps[i][1] = st.laps[i][1];
	}
	for(k=0; k<3; k++) for(i=0; i<20; i++) MB_Open_Close_State[k][i] = st.MB_Open_Close_State[k][i];
	LAll_Lap = st.LAll_Lap; RAll_Lap = st.RAll_Lap; All_Lap = st.All_Lap;
	RelayBit = st.RelayBit; RelayLaps = st.RelayLaps;
	hour = st.hour; minute = st.minute; second = st.second; msecond = st.msecond;
	Start_hour = st.Start_hour; Start_minute = st.Start_minute;
	Start_second = st.Start_second; Start_msecond = st.Start_msecond;
	timer_bit = st.timer_bit; Ready_timer_bit = st.Ready_timer_bit;
	//2026-05-20 用户改过的本机 IP 恢复 (调 netif_set_ipaddr 实时应用)
	if(st.local_ip_valid==1 && (st.local_ip[0]|st.local_ip[1]|st.local_ip[2]|st.local_ip[3])!=0){
		struct ip_addr _bkp_ip;
		lwipdev.ip[0]=st.local_ip[0]; lwipdev.ip[1]=st.local_ip[1];
		lwipdev.ip[2]=st.local_ip[2]; lwipdev.ip[3]=st.local_ip[3];
		IP4_ADDR(&_bkp_ip, lwipdev.ip[0], lwipdev.ip[1], lwipdev.ip[2], lwipdev.ip[3]);
		netif_set_ipaddr(&lwip_netif, &_bkp_ip);
		bkp_local_ip_valid = 1;
		printf("BKPSRAM 恢复本机 IP -> %d.%d.%d.%d\r\n", lwipdev.ip[0], lwipdev.ip[1], lwipdev.ip[2], lwipdev.ip[3]);
	}
	return 1;
}


void swim_play(void)
{
//	_edit_obj* eip=0;	//IP编辑框
//	_edit_obj* eport=0; //端口编辑框
//  _btn_obj* protbtn=0;//协议选择按钮
 // _btn_obj* sendbtn=0;//发送按钮
//  _btn_obj* connbtn=0;//连接按钮
//  _btn_obj* clrbtn=0;	//清除按钮
//	_memo_obj * rmemo=0;//,* smemo=0;	//memo控件 
//	_t9_obj * t9=0;					//输入法  
			
	u8 	Lkey_state=1,Rkey_state=1;
	u8  tmp[128];
	u16	USART4_RX_len;
	u8	buff[RX_DATA_MaxLEN];
	u16 	i,j;
	u16	SendLength;					//发送数据长度  2023-7-11
	
	u8 msg_height;					//信息区域高度和字体大小
	u16 memo_width,btn_width;		//memo控件宽度,按钮的宽度
	u16 rmemo_height,smemo_height;	//接收memo和发送memo的高度
	u16 rbtn_height;				//接收区按钮的高度
	u8 m_offx,sm_offy,rm_offy; 		//memo x方向的偏移;smemo和rmemo y方向偏移  
	u8 fsize,sbtnfsize;				//字体大小,和发送按钮字体大小
//	u16 t9height; 					//输入法的高度
	u16 tempx,tempy;

	
	gui_phy.language=0;
	u8 *ipcaption=netplay_ipcaption_tb[1][gui_phy.language];//默认是TCP Server模式,显示本机IP
	
	u16 res; 
	u8 rval=0;
	/////////////////////////////////
	err_t err; 			//错误标志 
	u16 *bkcolor;

	BACK_COLOR=GRAYBLUE;//LIGHTBLUE;// DARKBLUE;//2023-5-12 NET_MSG_BACK_COLOR;


	Timer_posx[0]=220;	
	Timer_posx[1]=480;	

	Left_MB_Num=2;				//左边 盲表数量  最大三个  2025-1-26
	Right_MB_Num=1;				//右边 盲表数量  最大三个

	StartFinalPlace=0;			//=0:发令点和终点在屏幕左边； =1：发令点和终点在屏幕右边。  2024-11-27
	StartPlace=0;						//=0:发令点在屏幕左边； =1：发令点在屏幕右边。
	FinalPlace=0;						//=0:终点在屏幕左边； =1：终点在屏幕右边。			

	All_Close_Time=400;			//全泳道关闭时间    ，泳池单边安装触板 2025-1-17
	
	MBdelay_Time=30;
	
	SwimmingPool_Arrage=0;	//=0:正方向布置，道次从上到下0-9道； =1:反方向布置，道次从上到下9-0道。   2024-6-8

	tport=8088;		//本机端口号,(要连接的端口号)默认为8088;						

	TP_DelayCloseValue=40;		//运动员触板TP信号关闭延迟时间初始设置5秒 2024-12-12

	OnReadDeviceData();			//2026-05-26 启动加载设备状态数组 (TP/SB/MB) from 2:/swimdev.cfg
	OnReadMatchData();			//读存储参数 2024-12-7

	
	for(i=0;i<20;i++)
	{
			Lane_NoTbl[i]=i;
	}


	Beep(1);			// 蜂鸣器输出1 没声音输出  2023-8-2
	
	delay_init(180);		//初始化延时函数 

	MB_CR=12;//lcddev.width/(2*10);
	
	if(lcddev.width<=272)
	{
		fsize=12;	
	}else if(lcddev.width==320)
	{
		fsize=16;	
	}else if(lcddev.width>=480)
	{
		fsize=24;	
	}
	LCD_Clear(BLUE);//LGRAY);
	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//下分界线	 
	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[22][gui_phy.language]);//显示标题  
	system_task_return=0;
	

	if(lcddev.width>=480)
	{
		btnfsize=32;	
	}else if(lcddev.width>=320)
	{
		btnfsize=24;	
	}else if(lcddev.width>=240)
	{
		btnfsize=16;	
	}
	
	/*
	if(audiodev.status&(1<<7))//当前在放歌??
	{
		audio_stop_req(&audiodev);	//停止音频播放
		audio_task_delete();		//删除音乐播放任务.
	} 
	*/
	
	Prot_Sx=1;
	IP_Sx=Prot_Sx+200-15;
	Port_Sx=IP_Sx+300-20;
	TX_Sx=Port_Sx+450+15;
	RX_Sx=TX_Sx+130;
	protbtn_ux=Port_Sx+160;
	connbtn_ux=protbtn_ux+100;
	
	
	
	window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[0][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);
  	res=lwip_comm_init();	//lwip初始化 LwIP_Init一定要在OSInit之后和其他LWIP线程创建之前初始化!!!!!!!!
	//if(res==0)				//网卡初始化成功
	{
		
		lwip_comm_dhcp_creat();	//创建DHCP任务 
		//提示正在DHCP获取IP
		window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[2][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);
		while(lwipdev.dhcpstatus==0||lwipdev.dhcpstatus==1)//等待DHCP分配成功
		{
			delay_ms(10);//等待.
		}
		if(lwipdev.dhcpstatus==2)window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[3][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//DHCP成功
		else window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[4][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//DHCP失败

		//2026-05-11 修复bug：注释掉下面这行 lwipdev.ip[3]=100;
		//因为它会强制覆盖DHCP/静态获得的真实本机IP最后一段，
		//导致TCP Server页面显示固定的 .100 而非实际IP。
		//lwipdev.ip[3]=100;

		delay_ms(100);
//		net_load_ui_init();	//加载主界面UI
		httpd_init(); 	//初始化http
 //2023-9-28



		
//	LCD_Clear(NET_MEMO_BACK_COLOR);//清屏 

		btnw=100;
		btnh=40;
	
		btnw1=80;		//按钮1的宽度
		btnh1=32;			//按钮1的高度
	
		CMD_btnw=140-10;		//command按钮的宽度
		CMD_btnh=40+10+10;			//command按钮的高度
		
	resultw=200;			//成绩显示区域的宽度和高度参数
	resulth=32;	
		
	btnds0x=lcddev.width-150;
	btnds0y=lcddev.height-150;
	
	btnds1x=btnds0x;
	btnds1y=24+20;

	Inf_area_x0=40;	
	Inf_area_y0=48;



//	carea_x0=10;			//左边按键的x0
	carea_x0=10*4;			//左边按键的x0  2023-11-2
//	carea_y0=20+10;
	carea_y0=20+10+12*5;

	//2026-05-14 Fix #2: 滚动时间显示区右边与"触板0"右侧齐平。
	//   "触板0"列 X 起点 = TPsx[1] = Middle_TPsx = 927 (Display_TP_State 宽 8 px → 右沿 935)。
	//   背景宽 280 px → 左沿 655 → RunningTime_x0 (=文字左基点) = 659。
	//   工作状态圆点(RunningTime_x0-32 = 627) 自动跟随到背景左侧外。
	RunningTime_x0=carea_x0+781;	//2026-05-16 整体右移+162：右沿对齐"触板0"按钮右沿(=btndsx+btnw)
	RunningTime_y0=carea_y0+10; 		//滚动时间显示区起点坐标参数

	StartFinalPlace_x0=200+50+162;							//2024-6-8
	StartFinalPlace_y0=carea_y0+10; 		//滚动时间显示区起点坐标参数

	btnwx=100;
	btnhy=64;
	
//	MB_CR=btnh/2;	//	2023-11-2 	//20;//28;	
	MB_CR=btnh/4;//		2023-11-2 	//20;//28;	
	Final_MBsx=carea_x0+130-14;//2023-11-3				//左边园点手动按钮的x0
//	Final_MBsy=carea_y0+MB_CR;//32+20;
	Final_MBsy=carea_y0+2*MB_CR;//  2023-11-2
	LaneStep_y=btnhy;			//64;
			
//	Final_Startboxsx=Final_MBsx+30;			//左边出发台按钮的x0
	Final_Startboxsx=Final_MBsx+15+1;			//左边出发台按钮的x0  2023-11-3
	Final_Startboxsy=carea_y0;
 
	Final_TPsx=Final_Startboxsx+30;			//左边触板按钮的x0
	Final_TPsy=carea_y0;
 
 	Placex=Final_TPsx+15;
 
  Final_timer_posx=Placex+10+10+20;			//左边显示成绩的x0
	Final_timer_posy=carea_y0;

	Lapsx[0]=Final_timer_posx+170+15;    		//左边显示剩余圈数的x0  2024-11-21

	dir_posx=Lapsx[0]+40;    		//左边显示运动方向的x0
	dir_posy=carea_y0;

	Start_Dir=0;							//游泳箭头指示的方向  =0：left->right；=1：left<-right  2024-12-1

	MBsx[0]=Final_MBsx;							//泳池左边MB,SB,TP,时间显示的X方向的位置  2024-6-9
	Startboxsx[0]=Final_Startboxsx;
	TPsx[0]=Final_TPsx;
	Timer_posx[0]=Final_timer_posx;	
	MBsy[0]=Final_MBsy;							//泳池左边MB,SB,TP,时间显示的X方向的位置  2024-6-10
	Startboxsy[0]=Final_Startboxsy;
	TPsy[0]=Final_TPsy;
	Timer_posy[0]=Final_timer_posy;	



	RMBbtn_posx=dir_posx+170;    		//左边显示用盲表成绩补充无触板成绩按键的x0  2023-11-3
	RMBbtn_posy=carea_y0;

	Lapsx[1]=dir_posx+170+40;    		//右边显示剩余圈数的x0  2024-11-21


  Middle_timer_posx=RMBbtn_posx+90;					//右边显示成绩的x0
	Middle_timer_posy=Final_timer_posy;	
	
	Middle_TPsx=Middle_timer_posx+180+5;			//右边触板按钮的x0
	Middle_TPsy=carea_y0;

//	Middle_Startboxsx=Middle_TPsx+20;			//右边出发台按钮的x0
	Middle_Startboxsx=Middle_TPsx+20-5-1;			//右边出发台按钮的x0
	Middle_Startboxsy=carea_y0;
	
 //	Middle_MBsx=Middle_Startboxsx+50;			//右边盲表按钮的x0
	Middle_MBsx=Middle_Startboxsx+50-10;			//右边盲表按钮的x0  2023-11-3
	Middle_MBsy=carea_y0+2*MB_CR;						//  2024-6-10

	MBsx[1]=Middle_MBsx;							//泳池右边MB,SB,TP,时间显示的X方向的位置  2024-6-9
	Startboxsx[1]=Middle_Startboxsx;
	TPsx[1]=Middle_TPsx;
	Timer_posx[1]=Middle_timer_posx;	
	MBsy[1]=Middle_MBsy;							//泳池右边MB,SB,TP,时间显示的X方向的位置
	Startboxsy[1]=Middle_Startboxsy;
	TPsy[1]=Middle_TPsy;
	Timer_posy[1]=Middle_timer_posy;	



//	btndsx=Middle_MBsx+30;					//lcddev.width-400;//660;
	btndsx=Middle_MBsx+30-14;					//2023-11-3

	cr=12+4;//16;
//	cds0x=Port_Sx+140;  2024-10-27
	cds0x=lcddev.width-486;  //2026-05-19 移到 NetConnbtn(lcddev.width-460,1)左侧 10px 处, cr=16

	Relaybtn=btn_creat(Inf_area_x0+500,Inf_area_y0,CMD_btnw,btnh1,0,0);	//2024-11-21
	
//	Testbtn=btn_creat(Inf_area_x0+500,Inf_area_y0,CMD_btnw,CMD_btnh,0,0);
	Testbtn=btn_creat(Inf_area_x0+500+150,Inf_area_y0,CMD_btnw,btnh1,0,0);

	
//2024-6-8

	//2026-05-12(2nd) 6个主控按钮全部用 BTN_TYPE_ANG (=2) 类型，让背景能填充颜色（非默认灰色）
	//                同时字体颜色用与背景对比强烈的色，确保看清。

	//退出/关机按钮放到屏幕左上角(避开右侧主控按钮，防止误碰)。
	//原"比赛距离 +1/-1"按钮和"比赛距离"显示字符串向右平移140像素让出位置。
	ExitShutdownbtn=btn_creat(5,1,CMD_btnw,CMD_btnh*3/4,0,BTN_TYPE_ANG);  //2026-05-19 移到屏幕左上角

	//右侧主控按钮列(顶到下：参数设置/发令时刻/复位/...准备/开始计时)
	Setupbtn=btn_creat(btnds0x,Inf_area_y0-00,CMD_btnw,CMD_btnh*3/4,0,BTN_TYPE_ANG);		//2024-10-23  设置参数控制按钮

	SendStartTimerbtn=btn_creat(btnds0x,Inf_area_y0+70,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);		//2024-9-1  发送发令时刻按钮

	Resetbtn=btn_creat(btnds0x,Inf_area_y0+150,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);

	Readybtn=btn_creat(btnds0x,btnds0y-250,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);

	Startbtn=btn_creat(btnds0x,btnds0y,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);

	//2026-05-12 比赛距离 "+1"/"-1" 按钮右移140像素，避让左上角的"退出/关机"按钮
	Distance_Addbtn=btn_creat(Inf_area_x0+140,Inf_area_y0,btnw1,btnh1,0,0);
	Distance_Decbtn=btn_creat(Inf_area_x0+240,Inf_area_y0,btnw1,btnh1,0,0);

	//2026-05-13 主界面顶部"网络连接"按钮 —— 与设置→网络协议→连接 同功能，避免反复进设置
	//放在右侧 Setupbtn 左边，避免与"开始/复位/接力/测试"等大按钮重叠
	NetConnbtn=btn_creat(lcddev.width-460,1,150,btnh1,0,BTN_TYPE_ANG);  //2026-05-19 移到日期左侧(日期画在 lcddev.width-300,1，宽约160，留10px间距)

	for(i=0;i<5; i++)
	{
		for(j=0;j<20;j++)
		{
			key_oldstate[i][j]=1;
		}
	}
	for(i=0;i<10; i++)
	{
		for(j=0;j<2;j++)
		{
			Lane_Display_State[i][j]=0;	
			Lane_Display_MSecond[i][j]=0;	
			TP_Display_State[i][j]=0;				//2024-3-28
		}
	}

	for	(i=0;i<Max_MB_Num;i++)
	{
		L_MB_State_Line[i]=0;		//左边 盲表的状态，接还是不连接
		R_MB_State_Line[i]=0;		//右边 盲表的状态，接还是不连接
	}
	for	(i=0;i<Left_MB_Num;i++)
	{
		L_MB_State_Line[i]=1;		//左边LINE 盲表的状态，接还是不连接
	}
	for	(i=0;i<Right_MB_Num;i++)
	{
		R_MB_State_Line[i]=1;		//右边LINE 盲表的状态，接还是不连接
	}
	
	
	timer_bit=0;				//计时位=0：不计时；
	
// 	TIM3_Int_Init(100-1,2*8400-1);	//定时器时钟84M，分频系数8400，所以84M/8400=10Khz的计数频率，计数5000次为500ms     
 //	TIM3_Int_Init(100-1,9600-1);	//10Khz的计数频率，计数5K次为500ms     
 //    TIM3_Int_Init(100-1,4000-1);       //定时器3初始化，定时器时钟为90M，分频系数为9000-1，
 	
//	BEEP_Init();        //初始化蜂鸣器端口
	EXTIX_Init();       //初始化外部中断输入 


	POINT_COLOR=WHITE;//RED;	 
	LCD_Clear(BLUE);//GREEN);	
	//设置LCD显示方向
	//dir:0,竖屏；1,横屏
	LCD_Display_Dir(1);		//2023-3-17

	Init_Key_Pin();			//2023-5-11

	//填充比赛控制区域背景  2023-11-8
	gui_fill_rectangle(carea_x0-10,carea_y0,carea_x0-10+1045,carea_y0+595,ControlArea_Color);	

	if(Startbtn&&Resetbtn)
	{
//			LCD_Clear(LGRAY);
	//	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//下分界线	 
	//	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[23][gui_phy.language]);//显示标题  
 	
		//2026-05-12(2nd) 6个主控按钮整体着色（按钮背景=主色，文字=高对比色）：
		//   开始计时  背景GREEN(0x07E0)    文字WHITE       —— "GO" 出发
		//   准备就绪  背景YELLOW(0xFFE0)   文字BLACK       —— 浅色背景必须用黑字
		//   复位      背景BLUE(0x001F)     文字WHITE
		//   发令时刻  背景MAGENTA(0xF81F)  文字WHITE
		//   参数设置  背景CYAN(0x07FF)     文字BLACK       —— 浅色背景必须用黑字
		//   退出/关机 背景RED(0xF800)      文字WHITE

		//—— 开始计时 GREEN ——
		Startbtn->caption=Hds0_btncaption_tbl[0][gui_phy.language];
		Startbtn->font=btnfsize;
		Startbtn->bkctbl[0]=0X0420;	//暗绿边框
		Startbtn->bkctbl[1]=0X07E0;	//亮绿顶线
		Startbtn->bkctbl[2]=0X07E0;	//上半 亮绿
		Startbtn->bkctbl[3]=0X0500;	//下半 暗绿
		Startbtn->bcfucolor=WHITE;
		Startbtn->bcfdcolor=BLACK;

		//—— 复位 RED（与"退出/关机"同款，因蓝底与屏幕蓝色背景重叠看不清。2026-05-12(3rd)）——
		Resetbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		Resetbtn->font=btnfsize;
		Resetbtn->bkctbl[0]=0X9000;
		Resetbtn->bkctbl[1]=0XF800;
		Resetbtn->bkctbl[2]=0XF800;
		Resetbtn->bkctbl[3]=0X9000;
		Resetbtn->bcfucolor=WHITE;
		Resetbtn->bcfdcolor=BLACK;

		btn_draw(Startbtn);		//画按钮
		btn_draw(Resetbtn);		//画按钮


		Startbtn->caption=Hds0_btncaption_tbl[1][gui_phy.language];

		//—— 参数设置 CYAN（浅底，必须用 BLACK 文字） ——
		Setupbtn->caption="参数设置";
		Setupbtn->font=btnfsize;
		Setupbtn->bkctbl[0]=0X041F;
		Setupbtn->bkctbl[1]=0X07FF;
		Setupbtn->bkctbl[2]=0X07FF;
		Setupbtn->bkctbl[3]=0X0410;
		Setupbtn->bcfucolor=BLACK;
		Setupbtn->bcfdcolor=WHITE;
		btn_draw(Setupbtn);		//画发送发令时刻 按钮  2024-10-23

		//—— 退出/关机 RED（按下需确认；字体小一号确保 5 字全显示） ——
		if(ExitShutdownbtn)
		{
			ExitShutdownbtn->bkctbl[0]=0X9000;
			ExitShutdownbtn->bkctbl[1]=0XF800;
			ExitShutdownbtn->bkctbl[2]=0XF800;
			ExitShutdownbtn->bkctbl[3]=0X9000;
			ExitShutdownbtn->bcfucolor=WHITE;
			ExitShutdownbtn->bcfdcolor=BLACK;
			ExitShutdownbtn->caption="退出/关机";
			ExitShutdownbtn->font=24;
			btn_draw(ExitShutdownbtn);
		}

		//—— 网络连接 GREEN（断开时显示"网络连接"；连接成功后切换为"网络断开"并变暗）——
		if(NetConnbtn)
		{
			//2026-05-18(5) "网络连接"按钮配色：未连接=红色醒目；已连接(显示"网络断开")=灰红
			if(connstatus==1){	//已连接：灰红
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0X6800;
				NetConnbtn->bkctbl[2]=0X6800;	NetConnbtn->bkctbl[3]=0X4000;
			}else{	//未连接：醒目红
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0XF800;
				NetConnbtn->bkctbl[2]=0XF800;	NetConnbtn->bkctbl[3]=0X8000;
			}
			NetConnbtn->bcfucolor=WHITE;
			NetConnbtn->bcfdcolor=BLACK;
			NetConnbtn->caption=(connstatus==1)?"网络断开":"网络连接";
			NetConnbtn->font=24;
			btn_draw(NetConnbtn);
		}

		//—— 发令时刻 MAGENTA ——
		SendStartTimerbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		SendStartTimerbtn->caption="发令时刻";	//"发送发令时刻";
		SendStartTimerbtn->font=btnfsize;
		SendStartTimerbtn->bkctbl[0]=0X9010;
		SendStartTimerbtn->bkctbl[1]=0XF81F;
		SendStartTimerbtn->bkctbl[2]=0XF81F;
		SendStartTimerbtn->bkctbl[3]=0XA014;
		SendStartTimerbtn->bcfucolor=WHITE;
		SendStartTimerbtn->bcfdcolor=BLACK;
		btn_draw(SendStartTimerbtn);		//画发送发令时刻 按钮  2024-9-1

		
	//画+1按钮
		Distance_Addbtn->caption="+1";
		Distance_Addbtn->font=btnfsize;
		btn_draw(Distance_Addbtn);		//画+1按钮

	//画-1按钮
		Distance_Decbtn->caption="-1";
		Distance_Decbtn->font=btnfsize;
		btn_draw(Distance_Decbtn);		//画-1按钮
										
				if(Pool50mOr25mbit==0)
				{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
					sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,标准泳池50m  2025-1-2
				}
				else
				{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
					sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);					//=1,短池 25m  		2025-1-2
				}		
				LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//显示比赛距离  2026-05-12 右移140
			
		gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color); 

//接力按钮 Relay	2024-11-21
		Relaybtn->caption=Relay_btncaption_tbl[0][gui_phy.language];
		Relaybtn->font=btnfsize;
		btn_draw(Relaybtn);		//画按钮


//测试按钮 Test		
		Testbtn->caption=Test_btncaption_tbl[0][gui_phy.language];
		Testbtn->font=btnfsize;
		btn_draw(Testbtn);		//画按钮


//		Testbtn->caption=Test_btncaption_tbl[1][gui_phy.language];
		
//准备就绪按钮 Ready —— YELLOW 背景（浅色），文字 BLACK 高对比
		Readybtn->caption=Ready_btncaption_tbl[0][gui_phy.language];
		Readybtn->font=btnfsize;
		Readybtn->bkctbl[0]=0XA500;	//暗黄边框
		Readybtn->bkctbl[1]=0XFFE0;	//亮黄顶线
		Readybtn->bkctbl[2]=0XFFE0;	//上半 亮黄
		Readybtn->bkctbl[3]=0XC600;	//下半 暗黄
		Readybtn->bcfucolor=BLACK;	Readybtn->bcfdcolor=WHITE;	//2026-05-12(2nd) 黄底黑字
		btn_draw(Readybtn);		//画按钮
		
//		Readybtn->caption=Ready_btncaption_tbl[1][gui_phy.language];
		
		system_task_return=0;
		
  }

	Exchange_StartFinalPlace();    //交换发令点  2024-11-27	
	
	
	for(i=0;i<10;i++)
	{
		CloseLanebtn[i]=btn_creat(dir_posx,carea_y0+(i+1)*btnhy,CloseLanebtn_width,btnh,0,BTN_TYPE_ANG);

		CloseLanebtn[i]->bkctbl[0]=0X6BF6;	//边框颜色
		CloseLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.第一行的颜色				
		CloseLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,上半部分颜色
		CloseLanebtn[i]->bkctbl[3]=0X2ADC;	//下半部分颜色	 
		CloseLanebtn[i]->bcfucolor=WHITE;	//松开时为白色
		CloseLanebtn[i]->bcfdcolor=BLACK;	//按下时为黑色 
//		CloseLanebtn[i]->caption=netplay_btncaption_tbl[4][gui_phy.language];
//		CloseLanebtn[i]->font=sbtnfsize;



		CloseLaneState[i]=2 ;					//关闭道次状态=2：打开；=3：关闭
		CloseLanebtn[i]->caption="打开";	//Hcmd_Lbtncaption_tbl[i];
		CloseLanebtn[i]->font=btnfsize;
		btn_draw(CloseLanebtn[i]);		//画打开/关闭道次按钮
	}	

	//取消 不要读盲表成绩  2024-10-15
/*
	for(i=0;i<10;i++)
	{
		RMBLanebtn[i]=btn_creat(RMBbtn_posx,RMBbtn_posy+(i+1)*btnhy,80,btnh,0,BTN_TYPE_ANG);

		RMBLanebtn[i]->bkctbl[0]=0X6BF6;	//边框颜色
		RMBLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.第一行的颜色				
		RMBLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,上半部分颜色
		RMBLanebtn[i]->bkctbl[3]=0X2ADC;	//下半部分颜色	 
		RMBLanebtn[i]->bcfucolor=WHITE;	//松开时为白色
		RMBLanebtn[i]->bcfdcolor=BLACK;	//按下时为黑色 

		RMBLanebtn[i]->caption="读MB";	//Hcmd_Lbtncaption_tbl[i];
		RMBLanebtn[i]->font=btnfsize;
		btn_draw(RMBLanebtn[i]);		//画打开/关闭道次按钮
	}	
*/

	
	for(i=0;i<10;i++)
	{
		cmdLbtn[i]=btn_creat(carea_x0,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 改为带色按钮
		//2024-6-8
		if(SwimmingPool_Arrage==0) 
		{
			cmdLbtn[i]->caption=Hcmd_Lbtncaption_tbl[i];			//画左边按钮 正向显示道次
			Lane_NoTbl[i]=i;
		}
		else 
		{
			cmdLbtn[i]->caption=Hcmd_Inv_Lbtncaption_tbl[i];			//画左边按钮 反向显示道次
			Lane_NoTbl[i]=9-i;
		}
			
		Setup_LaneBtn_LightYellow(cmdLbtn[i]);
		cmdLbtn[i]->font=btnfsize;
		btn_draw(cmdLbtn[i]);		//画左边按钮

		sprintf((char*)lcd_Dis,"L%d",(i));
		//2026-05-17 MB 左重画按 MB_Open_Close_State[0][i] 状态选色
		{
			u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i];
			if(_mbs==4)      _mbc=UnInstall_Color;
			else if(_mbs==3) _mbc=Bad_Color;
			else if(_mbs==0) _mbc=Close_Color;
			else             _mbc=Open_MB_Color;
			Display_MB_StateGroup(0,i,_mbc,lcd_Dis);
		}

		//2026-05-14 Fix #3: 重画时按 Startbox_/TP_Open_Close_State 决定颜色，
		//     原始无条件画 GREEN/YELLOW 会把 "未安装(4)" / "坏(3)" 状态覆盖掉。
		if(Startbox_Open_Close_State[i][0]==4)
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,UnInstall_Color);
		else if(Startbox_Open_Close_State[i][0]==3)
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Bad_Color);
		else if(Startbox_Open_Close_State[i][0]==0)
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Close_Color);
		else
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);

		if(TP_Open_Close_State[i][0]==4)
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,UnInstall_Color);
		else if(TP_Open_Close_State[i][0]==3)
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);
		else if(TP_Open_Close_State[i][0]==0)
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 封闭=灰
		else
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 打开=Open_TP_Color

		if(Startbox_Open_Close_State[i][1]==4)
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,UnInstall_Color);
		else if(Startbox_Open_Close_State[i][1]==3)
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Bad_Color);
		else if(Startbox_Open_Close_State[i][1]==0)
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Close_Color);
		else
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);

		if(TP_Open_Close_State[i][1]==4)
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,UnInstall_Color);
		else if(TP_Open_Close_State[i][1]==3)
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);
		else if(TP_Open_Close_State[i][1]==0)
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 封闭=灰
		else
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 打开=Open_TP_Color


			sprintf((char*)lcd_Dis,"R%d",(i));
			//2026-05-17 MB 右重画按 MB_Open_Close_State[0][i+10] 状态选色
			{
				u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i+10];
				if(_mbs==4)      _mbc=UnInstall_Color;
				else if(_mbs==3) _mbc=Bad_Color;
				else if(_mbs==0) _mbc=Close_Color;
				else             _mbc=Open_MB_Color;
				Display_MB_StateGroup(1,i,_mbc,lcd_Dis);
			}

			cmdRbtn[i]=btn_creat(btndsx,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 改为带色按钮

			//2024-6-8
			if(SwimmingPool_Arrage==0)
			{
				cmdRbtn[i]->caption=Hcmd_btncaption_tbl[i];			//画右边按钮 正向显示道次
				Lane_NoTbl[i+10]=10+i;
			}
			else
			{
				cmdRbtn[i]->caption=Hcmd_Inv_btncaption_tbl[i];			//画右边按钮 反向显示道次
				Lane_NoTbl[i+10]=10+9-i;
			}
			//2026-05-13 右道次按钮配色：淡黄色背景 + 黑字
			cmdRbtn[i]->bkctbl[0]=0XC600;	//暗黄边框
			cmdRbtn[i]->bkctbl[1]=0XFFF8;	//淡黄顶线
			cmdRbtn[i]->bkctbl[2]=0XFFF8;	//上半淡黄
			cmdRbtn[i]->bkctbl[3]=0XEFE0;	//下半略暗淡黄
			cmdRbtn[i]->bcfucolor=BLACK;
			cmdRbtn[i]->bcfdcolor=WHITE;
/*	
	u8 type;						//按钮类型
									//[7]:0,模式A,按下是一种状态,松开是一种状态.
									//	  1,模式B,每按下一次,状态改变一次.按一下按下,再按一下弹起.
									//[6:4]:保留
									//[3:0]:0,标准按钮;1,图片按钮;2,边角按钮;3,文字按钮(背景透明),4,文字按钮(背景单一)
	u8 sta;							//按钮状态
									//[7]:坐标状态 0,松开.1,按下.(并不是实际的TP状态)
									//[6]:0,此次按键无效;1,此次按键有效.(根据实际的TP状态决定)
									//[5:2]:保留
									//[1:0]:0,激活的(松开);1,按下;2,未被激活的
	u8 *caption;					//按钮名字
	u8 font;						//caption文字字体
	u8 arcbtnr;						//圆角按钮时圆角的半径										
	u16 bcfucolor; 				  	//button caption font up color
	u16 bcfdcolor; 				  	//button caption font down color

	u16 *bkctbl;					//对于文字按钮:
									//背景色表(按钮为文字按钮的时候使用)
									//a,当为文字按钮(背景透明时),用于存储背景色
									//b,当为文字按钮(背景单一是),bkctbl[0]:存放松开时的背景色;bkctbl[1]:存放按下时的背景色.
									//对于边角按钮:
									//bkctbl[0]:圆角按钮边框的颜色
									//bkctbl[1]:圆角按钮第一行的颜色
									//bkctbl[2]:圆角按钮上半部分的颜色
									//bkctbl[3]:圆角按钮下半部分的颜色	

	u8 *picbtnpathu;				//图片按钮松开时的图片路径
	u8 *picbtnpathd;		 		//图片按钮按下时的图片路径
*/

			Setup_LaneBtn_LightYellow(cmdRbtn[i]);
			cmdRbtn[i]->font=btnfsize;
			btn_draw(cmdRbtn[i]);		//画右边按钮
		}

	
		GT9271_Init(); 
		tp_dev.scan=GT9271_Scan;		//扫描函数指向GT271触摸屏扫描		
		tp_dev.touchtype|=0X80;			//电容屏 
	//	tp_dev.touchtype|=lcddev.dir&0X01;//横屏还是竖屏 
			tp_dev.touchtype|=lcddev.dir&0X00;//横屏还是竖屏 

		
//	LCD_Clear(NET_MEMO_BACK_COLOR);//清屏 
	
//	if(lcddev.width==1280)
	{
		ip_height=36,ip_fsize=24;
		msg_height=28;
	//	memo_width=400,btn_width=100;
		memo_width=400+400+200,btn_width=100-15;			//2023-11-7
		rmemo_height=48;//,smemo_height=48;
		rbtn_height=32;
		m_offx=16,sm_offy=10,rm_offy=9;
		fsize=16,sbtnfsize=24;
//		t9height=266;
	}

//	2026-05-19 取消顶部横贯蓝条: 它原本是 "网络连接："标签的背景, 但会盖住左/右两端 y<36 的按钮.
//	gui_fill_rectangle(0,0,lcddev.width,ip_height,NET_IP_BACK_COLOR);			//填充IP地址区域背景
//	gui_fill_rectangle(0,ip_height,lcddev.width,msg_height,NET_MSG_BACK_COLOR);	//信息区域背景
//	gui_draw_hline(0,ip_height+msg_height-1+25,lcddev.width,NET_COM_RIM_COLOR);	//分割线  2023-11-9
//	tempy=ip_height+msg_height+rmemo_height+fsize+2*rm_offy; 
	tempy=720; 
 //	gui_draw_hline(0,tempy,lcddev.width,NET_COM_RIM_COLOR);	//分割线  2023-11-9
//显示IP 数据
//	tempx=(lcddev.width-35*ip_fsize/2)/3-50;
		gui_show_string("网络连接：",lcddev.width-630,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//显示“网络连接 ”  2024-10-27

//2024-10-27	gui_show_string(ipcaption,IP_Sx,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//本地IP/目标IP
//	tempx=lcddev.width-tempx-10*ip_fsize/2-50;
//2024-10-27	gui_show_string(netplay_portcaption_tb[gui_phy.language],Port_Sx,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//端口:

	tempy=800-60;//ip_height+msg_height+rm_offy+fsize; 
//	gui_show_string(netplay_memoremind_tb[0][gui_phy.language],m_offx,tempy-fsize-rm_offy/3,lcddev.width,fsize,fsize,WHITE);//NET_MSG_FONT_COLOR);//显示接收区
//	rmemo=memo_creat(m_offx,tempy,memo_width,rmemo_height,0,0,fsize,NET_RMEMO_MAXLEN);//创建memo控件,最多NET_RMEMO_MAXLEN个字符	
	
	
//	tempy=ip_height+msg_height+rm_offy*2+rmemo_height+fsize*2+sm_offy; 
//	gui_show_string(netplay_memoremind_tb[1][gui_phy.language],m_offx+lcddev.width/3,tempy-fsize-sm_offy/3,lcddev.width,fsize,fsize,WHITE);//NET_MSG_FONT_COLOR);//显示发送区
//	smemo=memo_creat(m_offx+memo_width+50,tempy,memo_width,smemo_height,0,1,fsize,NET_SMEMO_MAXLEN);//最多NET_SMEMO_MAXLEN个字符	

	//2023-5-15
//	tempx=lcddev.width-tempx-10*ip_fsize/2;	
//	eip=edit_creat(strlen((char*)ipcaption)*ip_fsize/2+IP_Sx,(ip_height-ip_fsize-6)/2,15*ip_fsize/2+6,ip_fsize+6,0,4,ip_fsize);//创建ip编辑框

// 	tempx=lcddev.width-tempx-10*ip_fsize/2;	
// 	eport=edit_creat(Port_Sx+5*ip_fsize/2,(ip_height-ip_fsize-6)/2,5*ip_fsize/2+6,ip_fsize+6,0,4,ip_fsize);//创建eport编辑框

//	tempy=ip_height+msg_height+rm_offy*2+rmemo_height+fsize*2+sm_offy*2+smemo_height; 
//	t9=t9_creat((lcddev.width%5)/2,tempy,lcddev.width-(lcddev.width%5),t9height,0);//t9的宽度必须是5的倍数	
	tempy=ip_height+msg_height+rm_offy+fsize; 
 
	memo_width=(rmemo_height-3*rbtn_height)/2;//借用一下memo_width.
	if(memo_width>rbtn_height/2)memo_width=rbtn_height/2;

// 	protbtn=btn_creat(protbtn_ux,2,btn_width,rbtn_height,0,0);
//	connbtn=btn_creat(connbtn_ux,2,btn_width,rbtn_height,0,0);	
//	clrbtn=btn_creat(RX_Sx+150-20,2,btn_width,rbtn_height,0,0);	

//	sendbtn=btn_creat(btnds1x,btnds1y+2*rbtn_height,btn_width,48,0,2);	//创建边角按钮
// 	sendbtn=btn_creat(Inf_area_x0+500+200,Inf_area_y0,CMD_btnw,btnh1,0,2);	//创建边角按钮  2023-11-9

//  p=gui_memin_malloc(1024);	//申请1500字节内存
// 	ptemp=gui_memin_malloc(1024);//申请100字节内存
  p=gui_memin_malloc(2048);	//申请1500字节内存
 	ptemp=gui_memin_malloc(1024);//(100);//申请100字节内存   2024-10-26
//	if(!rmemo||!eip||!eport||!smemo||!t9||!protbtn||!connbtn||!clrbtn||!sendbtn||!p||!ptemp)rval=1;//创建失败. 
//	if(!rmemo||!eip||!eport||!smemo||!protbtn||!connbtn||!clrbtn||!sendbtn||!p||!ptemp)rval=1;//创建失败. 
//	if(!eip||!eport||!protbtn||!connbtn||!clrbtn||!p||!ptemp)rval=1;//创建失败. 
	if(!p||!ptemp)rval=1;//创建失败. 
//	if(!rmemo||!eip||!eport||!smemo||!connbtn||!clrbtn||!sendbtn||!p||!ptemp)rval=1;//创建失败. 
		    

	if(rval==0)//创建成功
	{ 
//		protbtn->caption=netplay_btncaption_tbl[0][gui_phy.language];
//		protbtn->font=fsize;
	//	connbtn->caption=netplay_btncaption_tbl[1][gui_phy.language];
//		connbtn->font=fsize;
	//	clrbtn->caption=netplay_btncaption_tbl[3][gui_phy.language];  2024-10-17
	//	clrbtn->font=fsize;
/*
		sendbtn->bkctbl[0]=0X6BF6;	//边框颜色
		sendbtn->bkctbl[1]=0X545E;	//0X8C3F.第一行的颜色				
		sendbtn->bkctbl[2]=0X5C7E;	//0X545E,上半部分颜色
		sendbtn->bkctbl[3]=0X2ADC;	//下半部分颜色	 
		sendbtn->bcfucolor=WHITE;	//松开时为白色
		sendbtn->bcfdcolor=BLACK;	//按下时为黑色 
		sendbtn->caption=netplay_btncaption_tbl[4][gui_phy.language];
		sendbtn->font=sbtnfsize;
*/
/*
		eip->textbkcolor=NET_IP_BACK_COLOR;
		eip->textcolor=WHITE;
		eport->textbkcolor=NET_IP_BACK_COLOR;
		eport->textcolor=GREEN;//GREEN,表示可以编辑
*/
//		rmemo->textbkcolor=WHITE;
//		rmemo->textcolor=BLACK;
//		smemo->textbkcolor=WHITE;
//		smemo->textcolor=BLACK; 
/*
sprintf((char*)ptemp,"%d.%d.%d.%d",lwipdev.ip[0],lwipdev.ip[1],lwipdev.ip[2],lwipdev.ip[3]);
 		strcpy((char*)eip->text,(const char *)ptemp);	//拷贝IP地址
		sprintf((char*)ptemp,"%d",tport);
		strcpy((char*)eport->text,(const char *)ptemp);	//拷贝端口号
 		edit_draw(eip);			//画编辑框
 		edit_draw(eport);		//画编辑框
*/
//		memo_draw_memo(smemo,0);//画memo控件
//		memo_draw_memo(rmemo,0);//画memo控件
//		btn_draw(protbtn);
	//	btn_draw(connbtn);
//		btn_draw(clrbtn);   2024-10-17
//		btn_draw(sendbtn); 
//		t9_draw(t9);	
//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,0X07);//显示信息 2024-10-27
	}
						
		sendcmdbuf=netbuf_new();
		netbuf_alloc(sendcmdbuf,32*TxRx_Data_Length);

		gui_fill_circle(cds0x,1+cr,cr,Close_Color); 			//网络连接指示灯 红：连接  灰：不连接
								
		display_closetime();	//显示泳道触板关闭时间  2023-10-17

				RS_TX_No=0;
	
		RS_TX_Ptr=0;
		RS_TX_Bit=0;
		RS_TX_len=0;		//发送数据长度   2023-10-23

//arr：自动重装值。
//psc：时钟预分频数
//定时器溢出时间计算方法:Tout=((arr+1)*(psc+1))/Ft us.
//Ft=定时器工作频率,单位:Mhz
	//	u16 Osc4Mhz_arr=10-1,Osc4Mhz_psc=400-1;			
	//	u16 Osc4Mhz_arr=3999-1,Osc4Mhz_psc=1-1;				//2023-7-14
	//	u16 Osc4Mhz_arr=4000-1,Osc4Mhz_psc=1-1;				//2023-7-14
		u16 Osc4Mhz_arr=4000-1,Osc4Mhz_psc=10-1;				//2023-7-28   10毫秒中断一次
//		u16 Osc4Mhz_arr=400-1,Osc4Mhz_psc=10-1;				//2023-9-25   10毫秒中断一次
		TIM8_Int_Init_ETR(Osc4Mhz_arr,Osc4Mhz_psc);       //定时器8初始化，定时器时钟为4M，分频系数为4000-1，


	
	Voltage_x0=400;		//2024-12-8
	Voltage_y0=1;

	Adc_Init();						//初始化ADC
	//	LCD_ShowString(Voltage_x0+250,Voltage_y0,200,16,16,"ADC1_CH19_VAL:");	      
//	LCD_ShowString(Voltage_x0,Voltage_y0+20,200,16,2*16,"ADC1_CH19_VOL:0.000V");//先在固定位置显示小数点  	
	LCD_ShowString(Voltage_x0,Voltage_y0,200,16,2*16,"BatVol:00.00V");//先在固定位置显示小数点  	
//	LCD_ShowString(Voltage_x0,Voltage_y0,200,16,2*16,"工作电压:00.00V");//先在固定位置显示小数点  	

//	calendar_display();
 	u8 led0sta=1;  
 	u16 adcx;
	float temp;

										
	if(PoolSingleOrDoubleTPbit==1)   //泳池安装触板在一端=1; 两端=0  2025-1-6
	{
			for(i=0;i<10;i++)			
			{
				TP_Open_Close_State[i][1-FinalPlace]=4;			//触板没有安装 =4; // FinalPlace=0:终点在屏幕左边； =1：终点在屏幕右边。
				TP_Open_Close_State[i][FinalPlace]=0;			//触板安装 =0; // FinalPlace=0:终点在屏幕左边； =1：终点在屏幕右边。
			}
	}
	else 
	{
			for(i=0;i<10;i++)			
			{
				TP_Open_Close_State[i][1-FinalPlace]=0;			//触板安装 =0; // FinalPlace=0:终点在屏幕左边； =1：终点在屏幕右边。
				TP_Open_Close_State[i][FinalPlace]=0;			//触板安装 =0; // FinalPlace=0:终点在屏幕左边； =1：终点在屏幕右边。
			}
	}

	Reset_Timer();

	//2026-05-18(3) 启用 STM32 H7 Backup SRAM (VBAT 持久) 并尝试加载上次保存的状态
	//   若 BKPSRAM 校验通过（magic+version+checksum），覆盖参数/TP/SB/MB/比赛数据/计时位等
	//   若校验失败（首次启动/VBAT 断电），保留 Reset_Timer 设的默认值
	BkpSRAM_Init();
	Load_State_From_BkpSRAM();
				
//	UART4->CR3|=1<<28;				// 位28 RXFTIE:RXFIFO 阈值中断使能 (RXFIFO threshold interrupt enable)
														//1:当接收 FIFO 达到 RXFTCFG 中编程的阈值时,生成 USART 中断

//		UART4->CR3|=1<<23;				// 位23 TXFTIE:TXFIFO 阈值中断使能 (TXFIFO threshold interrupt enable)
														//1:当TXFIFO 达到 TXFTCFG 中编程的阈值时,生成 USART 中断
//		UART4->ICR|=(1<<5);    			//TXFECF:TXFIFO 为空清零标志 ；向此位写入1时,USART_ISR 寄存器中的 TXFE 标志将清零
//	UART4->CR1|=1<<30;	 			
		//位 30 TXFEIE:TXFIFO 为空时中断使能 (TXFIFO empty interrupt enable)
		//此位由软件置 1 或清零
		//0:禁止中断
		//1:当USART_ISR 寄存器中的 TXFE=1,生成 USART 中断
//	UART4->ISR|=1<<23;  				//位 23 TXFE:TXFIFO 为空 (TXFIFO Empty)
		//如果 USART_CR1 寄存器中的 TXFEIE 位 =1（位 30),则会产生中断
		//0:TXFIFO 非为空
		//1:TXFIFO 为空	
		
//	UART4->CR3|=1<<23;				// 位23 TXFTIE:TXFIFO 阈值中断使能 (TXFIFO threshold interrupt enable)
														//1:当TXFIFO 达到 TXFTCFG 中编程的阈值时,生成 USART 中断
		
//	UART4->ICR|=(1<<6);    			//TCCF=1
//	UART4->CR1|=1<<6;  			//串口发送中断使能   CR1 中的 TCIE=1，则会产生中断
	UART4->CR1|=1<<3;  			//串口发送使能  Transmitter enable
	
	while(1)
	{ 
		calendar_get_time(&calendar);	//更新时间    2024-11-10 
//		if(system_task_return)break;	//需要返回	  
 		if(Csecond!=calendar.sec)//秒钟改变了
		{ 	
  			Csecond=calendar.sec;  
			//2026-05-26 (方案 B): 硬件未接 VBAT 备份电池, BKPSRAM 断电就丢, 不再周期写。
			//   原 "每 10 秒 Save_State_To_BkpSRAM()" 已禁用。所有持久化集中到事件触发点
			//   调用 OnWriteMatchData() 写板上 NAND Flash (2:/swimtime.cfg, 断电保留)。
			//   触发点: case Set_MatchEvent / Set_ArmDelay_Time / Set_PoolConfiguration_Com1
			//          / Set_MB_Num / Set_PoolSingleOrDoubleTP / 本地 +1/-1 距离按钮 等。
			sprintf((char*)lcd_id,"%2d:%02d:%02d",calendar.hour,calendar.min,calendar.sec);//将LCD ID打印到lcd_Dis数组。	
			LCD_ShowString(lcddev.width-128,1,240,32*2,32,lcd_id);		//显示LCD ID	  2024-11-10    					 
		
			calendar_get_date(&calendar);	//更新日期		
			if(calendar.w_date!=tempdate)
			{
				tempdate=calendar.w_date;	//修改tempdate，防止重复进入
				sprintf((char*)lcd_id,"%4d-%02d-%02d",calendar.w_year,calendar.w_month,calendar.w_date);//将LCD ID打印到lcd_Dis数组。	
				LCD_ShowString(lcddev.width-300,1,240,32*2,32,lcd_id);		//显示LCD ID	  2024-11-10    					 
			}
		
			
//		if(Ready_timer_bit==0)  		//在不计时时才检测电池电压，在准备就绪计时器开始计时时不检测电池电压 2024-12-9
			if((t%5)==0)
		{
//	  adcx=Get_Adc_Average(ADC1_CH19,20);		//获取通道5的转换值，20次取平均
			adcx=Get_Adc_Average(ADC1_CH19,1);		//获取通道5的转换值，1次取平均

	//	LCD_ShowxNum(Voltage_x0-30+142+250,Voltage_y0,adcx,5,16,0);		//显示ADCC采样后的原始值
//		temp=(float)adcx*(3.3/65536);			//获取计算后的带小数的实际电压值，比如3.1111
			temp=(float)adcx*(8.45/65536)*(8.3/7.8);			//获取计算后的带小数的实际电压值，比如3.1111    //2025-1-3 比例系数*(8.3/7.8)
			adcx=temp;								//赋值整数部分给adcx变量，因为adcx为u16整形
			LCD_ShowxNum(Voltage_x0-30+142,Voltage_y0,adcx,2,2*16,0);		//显示电压值的整数部分，3.1111的话，这里就是显示3
			temp-=adcx;								//把已经显示的整数部分去掉，留下小数部分，比如3.1111-3=0.1111
			temp*=100;								//小数部分乘以100，例如：0.1111就转换为11.11，相当于保留二位小数。
			LCD_ShowxNum(Voltage_x0-30+158+16+16,Voltage_y0,temp,2,2*16,0X80);	//显示小数部分（前面转换为了整形显示），这里显示的就是111.
//			temp*=10;								//小数部分乘以100，例如：0.1111就转换为11.11，相当于保留二位小数。
//			LCD_ShowxNum(Voltage_x0-30+158+16+16,Voltage_y0,temp,1,2*16,0X80);	//显示小数部分（前面转换为了整形显示），这里显示的就是111.

			//2026-05-13(2) 把当前电池电压上报给控制计算机（raw 0x4B，与 PC 端 BatteryVoltage 命令对齐）。
			//   单位：毫伏 mV  (e.g. 12.34V -> 12340)
			//   v_mV = 整数部分×1000 + 小数部分×10  （此时 temp 已经为 decimal×100）
			//   ★ 按 通讯协议变更说明_v2026.05.13.pdf 修正为 BIG-ENDIAN：
			//        d3 = v_mV 高字节, d4 = v_mV 低字节
			{
				u16 v_mV = (u16)((u32)adcx*1000 + (u32)temp*10);
				OnSendSWCommand_Data(Report_Voltage_RAW, (u8)((v_mV>>8) & 0xFF), (u8)(v_mV & 0xFF), 0, 0, 0, 0, 0, 0);
				Send_Bit=2;
			}
		}
			t++;

	 	} 
	
			tp_dev.scan(0);    
			tp_dev.touchtype|=lcddev.dir&0X00;//横屏还是竖屏 
			in_obj.get_key(&tp_dev,IN_TYPE_TOUCH);	//得到按键键值   
			res=btn_check(Startbtn,&in_obj);   
			if(res&&((Startbtn->sta&(1<<7))==0)&&(Startbtn->sta&(1<<6)))//有输入,有按键M.Start按下且松开,并且TP松开了
			{
				StartTiming();
			}

		//2026-05-17 参数设置按钮：比赛进行中(timer_bit==1)或准备就绪态(Ready_timer_bit==1)时
		//   置灰禁用，确保比赛中不改参数；复位后(两 bit 都为 0)恢复青色可用。
		{
			static u8 _setup_btn_last = 255;
			u8 _setup_btn_busy = (timer_bit || Ready_timer_bit) ? 1 : 0;
			if(_setup_btn_busy != _setup_btn_last && Setupbtn){
				if(_setup_btn_busy){
					//禁用：暗灰背景 + 灰字
					Setupbtn->bkctbl[0]=0X2104;
					Setupbtn->bkctbl[1]=0X4208;
					Setupbtn->bkctbl[2]=0X4208;
					Setupbtn->bkctbl[3]=0X2104;
					Setupbtn->bcfucolor=0X8410;
				}else{
					//启用：恢复原青色 + 黑字
					Setupbtn->bkctbl[0]=0X041F;
					Setupbtn->bkctbl[1]=0X07FF;
					Setupbtn->bkctbl[2]=0X07FF;
					Setupbtn->bkctbl[3]=0X0410;
					Setupbtn->bcfucolor=BLACK;
				}
				btn_draw(Setupbtn);
				_setup_btn_last = _setup_btn_busy;
			}
		}
		//2024-10-23  参数设置
			res=btn_check(Setupbtn,&in_obj);   
			//2026-05-17 计时进行中或准备就绪态时不响应
			if(timer_bit==0 && Ready_timer_bit==0 && res&&((Setupbtn->sta&(1<<7))==0)&&(Setupbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{
		//		net_play();				//网络测试

				//2026-05-16 进入参数设置前先弹确认框，避免误触
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认进入参数设置？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);
				delay_ms(800);
				if(res==OkbtnValue)
				{
					//2026-05-18 进入参数设置前保存"非参数"状态（缺道 CloseLaneState / 剩余圈数 laps）
					//   参数设置界面不会改这些，但 SwimControl_init 末尾的 Reset_Timer 会重置它们
					u8 saved_CloseLaneState[10];
					u8 saved_laps[10][2];
					u8 _si;
					for(_si=0;_si<10;_si++){
						saved_CloseLaneState[_si] = CloseLaneState[_si];
						saved_laps[_si][0] = laps[_si][0];
						saved_laps[_si][1] = laps[_si][1];
					}
					
					net_test();
					OnWriteMatchData();			//2024-12-8
					
					SwimControl_init();			//2024-10-23
					
					//2026-05-18 恢复保存的"非参数"状态 + 重画相关 UI
					for(_si=0;_si<10;_si++){
						CloseLaneState[_si] = saved_CloseLaneState[_si];
						laps[_si][0] = saved_laps[_si][0];
						laps[_si][1] = saved_laps[_si][1];
						if(CloseLanebtn[_si]){
							if(CloseLaneState[_si]==3){
								CloseLanebtn[_si]->bkctbl[0]=0X3186;
								CloseLanebtn[_si]->bkctbl[1]=0X2A0F;
								CloseLanebtn[_si]->bkctbl[2]=0X2A0F;
								CloseLanebtn[_si]->bkctbl[3]=0X10A2;
								CloseLanebtn[_si]->bcfucolor=GRAY;
							}else{
								CloseLanebtn[_si]->bkctbl[0]=0X6BF6;
								CloseLanebtn[_si]->bkctbl[1]=0X545E;
								CloseLanebtn[_si]->bkctbl[2]=0X5C7E;
								CloseLanebtn[_si]->bkctbl[3]=0X2ADC;
								CloseLanebtn[_si]->bcfucolor=WHITE;
							}
							btn_draw(CloseLanebtn[_si]);
						}
						display_swim_dir(dir_posx, _si, CloseLaneState[_si], 0);
						LLaps_diaplay(_si);
						RLaps_diaplay(_si);
					}
					//2026-05-16 参数修改返回主控时，把 Open_State 通过 0x47 上报给 PC，使 PC 端"全部打开/全部关闭"按钮状态与硬件一致
					//   on-the-wire: D2=0x47 D3=0xFF(全部道) D4=Open_State(1=全开/0=全关) D5..D10=0
					OnSendSWCommand_Data(Set_LaneOpenClose+0x10, 0xFF, (Open_State==1)?1:0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-16 同步泳池触板安装方式: 0x3A D3=PoolSingleOrDoubleTPbit (0=两端 / 1=单端)
					OnSendSWCommand_Data(Set_PoolSingleOrDoubleTP+0x10, PoolSingleOrDoubleTPbit, 0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-16 同步泳池长度: 0x44 D3=泳道数(默认10) D4=泳池长度(50/25)
					OnSendSWCommand_Data(Set_PoolConfiguration_Com1+0x10, 10, (Pool50mOr25mbit==0)?50:25, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-17 同步道次顺序: 0x62 D3=SwimmingPool_Arrage (0=正向 / 1=反向)
					OnSendSWCommand_Data(Set_LaneOrder+0x10, SwimmingPool_Arrage, 0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-17 同步终点位置: 0x63 D3=FinalPlace (0=终点左端 / 1=终点右端)
					OnSendSWCommand_Data(Set_FinishPosition+0x10, (u8)(FinalPlace&0x01), 0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-17 同步 5 项时间数据: 0x64 (单位均为秒, 硬件内部 0.1s 单位需 /10 还原)
					//   D3=Close_Time/10 D4=TP_DelayCloseValue/10 D5=Relay_SB_DelayCloseValue/10 D6=MBdelay_Time/10 D7=Result_Display_Time/10
					OnSendSWCommand_Data(Set_TimingsBundle+0x10,
						(u8)(Close_Time/10), (u8)(TP_DelayCloseValue/10), (u8)(Relay_SB_DelayCloseValue/10),
						(u8)(MBdelay_Time/10), (u8)(Result_Display_Time/10), 0, 0, 0);
					Send_Bit=2;
					delay_ms(800);				//2026-05-16 让 SwimControl_init 重画的 TP 颜色（按 Open_State 整体变化）停留可见，再让主循环的赛时调度接管
				}
			}
			
		//2024-9-1
			res=btn_check(SendStartTimerbtn,&in_obj);   
			if(res&&((SendStartTimerbtn->sta&(1<<7))==0)&&(SendStartTimerbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{
				//2024-9-1
				OnSendSWCommand_Data(Start_Command+0x10,0,0,Start_minute,Start_second,Start_msecond/10,Start_hour*16+Start_msecond%10,Start_hour,0);
				Send_Bit=2;														//置发送标志
			}

			
			res=btn_check(Resetbtn,&in_obj);   
			if(res&&((Resetbtn->sta&(1<<7))==0)&&(Resetbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{    
//在制定位置显示一个msg box
//x,y,width,height:坐标尺寸
//str:字符串
//caption:消息窗口名字
//font:字体大小
//color:颜色
//mode:
//[7]:0,没有关闭按钮.1,有关闭按钮			   
//[6]:0,不读取背景色.1,读取背景色.					 
//[5]:0,标题靠左.1,标题居中.					 
//[4:2]:保留
//[1]:0,不显示取消按键;1,显示取消按键.
//[0]:0,不显示OK按键;1,显示OK按键.
//time:延时时间,单位:ms(仅在没有按键且需要读取背景色的时候,有效,最大65535)
//返回值:
//0,没有任何按键按下/产生了错误.
//1,确认按钮按下了.
//2,取消按钮按下了.	   
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认计时复位？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//显示计时复位的提示信息
	//			delay_ms(800);//延时等待提示
				if(res==OkbtnValue)
				{
					timer_bit=0;
					gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color);
		//		LCD_ShowString(Middle_timer_posx,Final_timer_posy+5*line_height1,200,32,32,lcd_Dis);		//显示LCD ID

//				ds1sta=!ds1sta;
//				Resetbtn->caption=Hds1_btncaption_tbl[ds1sta][gui_phy.language];

					Reset_Timer();
				}
			}

			//2026-05-11 退出/关机按钮：弹出确认，保存数据后清屏并提示关闭电源，停在 WFI 循环
			if(ExitShutdownbtn)
			{
				//2026-05-26 busy-state visual disable: 比赛中(timer_bit||Ready_timer_bit) 按钮显示暗灰禁用样式,
				//   非比赛态恢复原红色, 视觉提示用户当前能否点击。仅在状态变化时 btn_draw, 避免每次主循环重画。
				{
					static u8 prev_exit_busy = 255;
					u8 _exit_busy = (timer_bit||Ready_timer_bit) ? 1 : 0;
					if(_exit_busy != prev_exit_busy){
						if(_exit_busy){
							ExitShutdownbtn->bkctbl[0]=0X4208;
							ExitShutdownbtn->bkctbl[1]=0X8410;
							ExitShutdownbtn->bkctbl[2]=0X8410;
							ExitShutdownbtn->bkctbl[3]=0X4208;
							ExitShutdownbtn->bcfucolor=0XC618;
							ExitShutdownbtn->bcfdcolor=WHITE;
						}else{
							ExitShutdownbtn->bkctbl[0]=0X9000;
							ExitShutdownbtn->bkctbl[1]=0XF800;
							ExitShutdownbtn->bkctbl[2]=0XF800;
							ExitShutdownbtn->bkctbl[3]=0X9000;
							ExitShutdownbtn->bcfucolor=WHITE;
							ExitShutdownbtn->bcfdcolor=BLACK;
						}
						btn_draw(ExitShutdownbtn);
						prev_exit_busy = _exit_busy;
					}
				}
				res=btn_check(ExitShutdownbtn,&in_obj);
				//2026-05-26 比赛中(计时态/准备态)按下无效, 防止误关机丢成绩
				if(res&&((ExitShutdownbtn->sta&(1<<7))==0)&&(ExitShutdownbtn->sta&(1<<6))&&timer_bit==0&&Ready_timer_bit==0)
				{
					res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认退出/关机？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);
					if(res==OkbtnValue)
					{
						OnWriteMatchData();		//保存比赛参数到板上 NAND Flash (2:/, FatFs 卷 2)
						timer_bit=0;
						Ready_timer_bit=0;
						Timer_Reset(1-0);
						//2026-05-13 关机界面：红底，使用 gui_show_string（支持中文），并将大号
						//"请关闭电源！"和小号"数据已保存"分两行显示。LCD_ShowString 不支持中文。
						LCD_Clear(RED);
						BACK_COLOR=RED;
						{
							u8 *msg_big   = (u8*)"请关闭电源！";
							u8 *msg_small = (u8*)"数据已保存";
							//2026-05-13 主信息真正水平居中：
							//"请关闭电源" 5 个中文 + "！" 全角(占 1 个中文宽) = 6 char × 32 px = 192 px
							u16 big_w = 6 * 32;	//实际渲染宽度
							u16 big_x = (lcddev.width  - big_w) / 2;
							u16 big_y = (lcddev.height - 32  ) / 2;
							//font=32 + 偏移 1 px 重画一次形成加粗效果
							gui_show_string(msg_big, big_x,   big_y,   big_w+8, 64, 32, WHITE);
							gui_show_string(msg_big, big_x+1, big_y+1, big_w+8, 64, 32, WHITE);
							//副信息：font=24, 5 个中文 = 120 px
							u16 small_w = 5 * 24;
							u16 small_x = (lcddev.width  - small_w) / 2;
							u16 small_y = big_y - 48;
							gui_show_string(msg_small, small_x, small_y, small_w+8, 32, 24, YELLOW);
						}
						//本机不带硬件关机，停在 WFI 循环等待用户手动断电
						while(1)
						{
							delay_ms(500);
							__WFI();
						}
					}
				}
			}

			//2026-05-13 主界面顶部"网络连接/网络断开"按钮：调用 net_toggle_connect()
			//功能等同设置→网络协议选择→连接，但无需进入设置界面；按下后根据 connstatus
			//更新按钮标题。protbtn 的"暗化"在 net_test 退出后才生效，这里属独立通道，
			//因此 NetConnbtn 自身随 connstatus 切换"网络连接/网络断开"。
			if(NetConnbtn)
			{
				//2026-05-18(5) "网络连接/断开"按钮：状态(connstatus) + 比赛态(timer_bit/Ready_timer_bit) 决定颜色
				//   未连接 idle → 红 (醒目, 提醒连接)
				//   已连接 idle → 灰红 (低调, 提醒可断开)
				//   比赛中/准备态 → 暗灰禁用 (按下不响应, 同 Setupbtn)
				static u8 prev_connstatus_net=0xFF;
				static u8 prev_netbtn_busy=255;
				u8 _net_busy = (timer_bit||Ready_timer_bit) ? 1 : 0;
				if(_net_busy != prev_netbtn_busy || prev_connstatus_net != connstatus){
					if(_net_busy){
						NetConnbtn->bkctbl[0]=0X2104; NetConnbtn->bkctbl[1]=0X4208;
						NetConnbtn->bkctbl[2]=0X4208; NetConnbtn->bkctbl[3]=0X2104;
						NetConnbtn->bcfucolor=0X8410;
					}else if(connstatus==1){
						NetConnbtn->bkctbl[0]=0X4000; NetConnbtn->bkctbl[1]=0X6800;
						NetConnbtn->bkctbl[2]=0X6800; NetConnbtn->bkctbl[3]=0X4000;
						NetConnbtn->bcfucolor=WHITE;
					}else{
						NetConnbtn->bkctbl[0]=0X4000; NetConnbtn->bkctbl[1]=0XF800;
						NetConnbtn->bkctbl[2]=0XF800; NetConnbtn->bkctbl[3]=0X8000;
						NetConnbtn->bcfucolor=WHITE;
					}
					NetConnbtn->caption=(connstatus==1)?"网络断开":"网络连接";
					btn_draw(NetConnbtn);
					prev_netbtn_busy = _net_busy;
					prev_connstatus_net = connstatus;
				}
				//按下：仅 idle 态响应 (比赛中/准备态禁止切换网络连接状态)
				res=btn_check(NetConnbtn,&in_obj);
				if(_net_busy==0 && res&&((NetConnbtn->sta&(1<<7))==0)&&(NetConnbtn->sta&(1<<6)))
				{
					net_toggle_connect();
					//颜色+caption 在下一帧状态监控里自动更新 (因 connstatus 变了)
				}
			}

			//2024-11-21
			res=btn_check(Relaybtn,&in_obj);
			if(res&&((Relaybtn->sta&(1<<7))==0)&&(Relaybtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{  
				if((All_Lap%8)==0)  //比赛是400m,800m
				{
					res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认进行接力比赛？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//显示进行接力比赛的提示信息				
	//			delay_ms(800);//延时等待提示
					if(res==OkbtnValue)
					{
						RelayBit=1;  //置接力标志位=1 2024-11-24
						RelayLaps=All_Lap/8;  //接力距离 2024-11-24
						Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
						btn_draw(Relaybtn);		//画按钮
					}
				}
				else {
					RelayBit=0;  //清接力标志位=0 2024-11-24
					RelayLaps=0;  //不是接力比赛 2024-11-24
					Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
					btn_draw(Relaybtn);		//画按钮
				}
			}	
	
			res=btn_check(Testbtn,&in_obj);   
			if(res&&((Testbtn->sta&(1<<7))==0)&&(Testbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{    
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认进行测试？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//显示计时复位的提示信息				
	//			delay_ms(800);//延时等待提示
				if(res==OkbtnValue)
				{
					Test_Button();
	//				ds1sta=!ds1sta;

				}
			}	
	
			//2024-11-3
	/*
			res=btn_check(LaneInvbtn,&in_obj);   
			if(res&&((LaneInvbtn->sta&(1<<7))==0)&&(LaneInvbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{    
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认进行道次变换？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//显示计时复位的提示信息				
	//			delay_ms(800);//延时等待提示
				if(res==OkbtnValue)
				{
					if(SwimmingPool_Arrage==0) 
					{
						SwimmingPool_Arrage=1;
					}
					else	{
						SwimmingPool_Arrage=0;
					}
					
					SwimmingPool_ArrageSubject(SwimmingPool_Arrage);
			
				}
			}	
	*/
	
			
			res=btn_check(Readybtn,&in_obj);   
			if(res&&((Readybtn->sta&(1<<7))==0)&&(Readybtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
			{    
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认准备就绪？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//显示计时复位的提示信息				
		//		delay_ms(800);//延时等待提示
				if(res==OkbtnValue)
				{
	//				timer_bit=0;
	//				gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN); 
		//		LCD_ShowString(Middle_timer_posx,Final_timer_posy+5*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	      					 
		
//				ds1sta=!ds1sta;
//				Readybtn->caption=Ready_btncaption_tbl[ds1sta][gui_phy.language]; 

					TP_Ready_Init();
		//			display_rollingtime();		//显示滚动时间		2023-7-11
				}
			}	
	
			key=KEY_Scan(1);					//按键扫描
			if(key==WKUP_PRES)					//WKUP按键按下了
			{
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"确认准备就绪？",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//显示计时复位的提示信息				
		//		delay_ms(800);//延时等待提示
				if(res==OkbtnValue)
				{
			//		timer_bit=0;
			//		gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN); 

					TP_Ready_Init();
		//			display_rollingtime();		//显示滚动时间		2023-7-11
				}
			}	
		
			
			
			res=btn_check(Distance_Addbtn,&in_obj);   
			if(res&&((Distance_Addbtn->sta&(1<<7))==0)&&(Distance_Addbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且+1松开了
			{ 
				if(RelayBit==1) //之前是接力比赛 清除 2024-11-24
				{
					RelayBit=0;  //清接力标志位=0 2024-11-24
					RelayLaps=0;  //不是接力比赛 2024-11-24
					Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
					btn_draw(Relaybtn);		//画按钮
				}
					
				Laps_No++;
				if(Laps_No>=Distance_Max) Laps_No=0;
				
				if(Pool50mOr25mbit==0)
				{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
					sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,标准泳池50m  2025-1-2
				}
				else
				{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
					sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);					//=1,短池 25m  		2025-1-2
				}		
				LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//显示比赛距离  2026-05-12 右移140
			
				if((LAll_Lap+RAll_Lap)==1)	//2024-11-27
				{
					StartPlace=0x01;			//=50M,发令点改变  2024-11-27					
					StartFinalPlace=StartFinalPlace|0x02;			//=50M,发令点改变  2024-11-27					
					Display_StartFinalPlace(StartFinalPlace);   //改变发令点  2024-6-10
				}
				else 		//2024-11-27
				{
					StartPlace=0x00;			//!=50M,发令点不变  2024-11-27					
					StartFinalPlace=StartFinalPlace&0xFD;			//>50M,发令点不变  2024-6-17		
					Display_StartFinalPlace(StartFinalPlace);   //改变发令点  2024-6-10
				}
				
									
				if((LAll_Lap+RAll_Lap)==1)
				{
					if((StartFinalPlace&0x03)==0x02)	//  50m 发令 右边 ，终点：左边 2024-11-27
					{
						LAll_Lap=1;			//2024-11-27
						RAll_Lap=0;			//2024-11-27
					}
					if((StartFinalPlace&0x03)==0x03)	//  50m 发令 右边 ，终点：左边 2024-11-27
					{
						LAll_Lap=0;			//2024-11-27
						RAll_Lap=1;			//2024-11-27
					}
				}
				
				Exchange_StartFinalPlace();    //交换发令点  2024-11-27	

				for(i=0;i<10;i++)
				{
					laps[i][0]=LAll_Lap;
					laps[i][1]=RAll_Lap;					//2024-11-24
					LLaps_diaplay(i);
					RLaps_diaplay(i);					//2024-11-21
				}	
			}

			res=btn_check(Distance_Decbtn,&in_obj);   
			if(res&&((Distance_Decbtn->sta&(1<<7))==0)&&(Distance_Decbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且+1松开了
			{ 
				if(RelayBit==1) //之前是接力比赛 清除 2024-11-24
				{
					RelayBit=0;  //清接力标志位=0 2024-11-24
					RelayLaps=0;  //不是接力比赛 2024-11-24
					Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
					btn_draw(Relaybtn);		//画按钮
				}
				
				if(Laps_No==0) Laps_No=Distance_Max-1;
				else Laps_No--;
				if(Pool50mOr25mbit==0)
				{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
					sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,标准泳池50m  2025-1-2
				}
				else
				{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
					sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);					//=1,短池 25m  		2025-1-2
				}		
				LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//显示比赛距离  2026-05-12 右移140
			
				if((LAll_Lap+RAll_Lap)==1)	//2024-11-27
				{
					StartPlace=0x01;			//=50M,发令点改变  2024-11-27					
					StartFinalPlace=StartFinalPlace|0x02;			//=50M,发令点改变  2024-11-27					
					Display_StartFinalPlace(StartFinalPlace);   //改变发令点  2024-6-10
				}
				else 		//2024-11-27
				{
					StartPlace=0x00;			//!=50M,发令点不变  2024-11-27					
					StartFinalPlace=StartFinalPlace&0xFD;			//>50M,发令点不变  2024-6-17		
					Display_StartFinalPlace(StartFinalPlace);   //改变发令点  2024-6-10
				}
				
									
				if((LAll_Lap+RAll_Lap)==1)
				{
					if((StartFinalPlace&0x03)==0x02)	//  50m 发令 右边 ，终点：左边 2024-11-27
					{
						LAll_Lap=1;			//2024-11-27
						RAll_Lap=0;			//2024-11-27
					}
					if((StartFinalPlace&0x03)==0x03)	//  50m 发令 右边 ，终点：左边 2024-11-27
					{
						LAll_Lap=0;			//2024-11-27
						RAll_Lap=1;			//2024-11-27
					}
				}
				
				
				Exchange_StartFinalPlace();    //交换发令点  2024-11-27	
		
				for(i=0;i<10;i++)
				{
					laps[i][0]=LAll_Lap;
					laps[i][1]=RAll_Lap;					//2024-11-24
					LLaps_diaplay(i);
					RLaps_diaplay(i);					//2024-11-21
				}	
			}	
			
						
			if(Check_State_Bit==1)				//置检查触板、盲表、出发台状态  2023-8-15
			{
				Check_State_Bit=0;
	//			TouchPadSignalKey_Process();		
	//			SignalKey_Process();
	//			SignalKey_Process();
	//			SignalKey_Process();
	//			SignalKey_Process();
			}
			
			if(Procee_SwimDir_Bit) 							//处理显示方向和滚动时间  2023-7-11
			{
				if(Testing_bit==0)								//不是测试状态在处理  2024-12-22
				{		
					if(PoolSingleOrDoubleTPbit==0)	//泳池安装触板是一端=1; 两端=0  2025-1-16
							Process_Display_SiwmDir();		//泳池两边安装触板，处理游泳方向  2025-1-16 
					else Single_Process_Display_SiwmDir();		//泳池单边安装触板，处理游泳方向  2025-1-16 
					Procee_SwimDir_Bit=0;
				}
			}

			//泳道打开/关闭处理程序
			for(i=0;i<10;i++)
			{
				res=btn_check(CloseLanebtn[i],&in_obj);
				if(res&&((CloseLanebtn[i]->sta&(1<<7))==0)&&(CloseLanebtn[i]->sta&(1<<6)))//有输入,有按键按下且松开,并且松开了
				{
					if(CloseLaneState[i]==2) CloseLaneState[i]=3 ;					//关闭道次状态=2：打开；=3：关闭
					else CloseLaneState[i]=2 ;
					//2026-05-12 按下后立刻刷新按钮颜色：打开=明亮原色，关闭=变暗
					if(CloseLaneState[i]==3)
					{	//暗色调，明显表示该道已关闭
						CloseLanebtn[i]->bkctbl[0]=0X3186;	//暗边框
						CloseLanebtn[i]->bkctbl[1]=0X2A0F;	//暗第一行
						CloseLanebtn[i]->bkctbl[2]=0X2A0F;	//暗上半
						CloseLanebtn[i]->bkctbl[3]=0X10A2;	//暗下半
						CloseLanebtn[i]->bcfucolor=GRAY;	//灰色文字
					}
					else
					{	//原亮色
						CloseLanebtn[i]->bkctbl[0]=0X6BF6;
						CloseLanebtn[i]->bkctbl[1]=0X545E;
						CloseLanebtn[i]->bkctbl[2]=0X5C7E;
						CloseLanebtn[i]->bkctbl[3]=0X2ADC;
						CloseLanebtn[i]->bcfucolor=WHITE;
					}
					btn_draw(CloseLanebtn[i]);
					display_swim_dir(dir_posx,i,CloseLaneState[i],0);			//open/close
					LLaps_diaplay(i);
				}
			}	
			
			
				//取消 不要读盲表成绩  2024-10-15
/*
			//检测是否有读盲表补充成绩的按键按下   2023-11-3
			for(i=0;i<10;i++)
			{
				res=btn_check(RMBLanebtn[i],&in_obj);  
				if(res&&((RMBLanebtn[i]->sta&(1<<7))==0)&&(RMBLanebtn[i]->sta&(1<<6)))//有输入,有按键按下且松开,并且松开了
				{  
					//		display_time();		//将LCD ID打印到lcd_Dis数组。
					//		LCD_ShowString(Final_timer_posx,Final_timer_posy+(i+1)*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  

					Display_Laps_Place_Direct(i,1);				//在规定时间内，只有盲表成绩，无触板成绩，故取盲表成绩补充正式成绩   2023-11-5

					Display_MB_Time(MB_Result[i][0],MB_Result[i][1],MB_Result[i][2],MB_Result[i][3]);		//显示MB成绩  2023-11-7
					LCD_ShowString(Final_timer_posx,Final_timer_posy+(i+1)*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
					//发送此道的 盲表成绩   小时	分	秒	1/1000秒
	//				OnSendSWCommand_Data(Pushbutton1_Command+(0)+0x10,SW_Command1+1,i,MB_Result[i][1],MB_Result[i][2],MB_Result[i][3]/10,MB_Result[i][0]*16+MB_Result[i][3]%10,MB_Result[i][0],0);
					OnSendSWCommand_Data(Touchpad_Command+0x10,Pushbutton_Result,i,MB_Result[i][1],MB_Result[i][2],MB_Result[i][3]/10,MB_Result[i][0]*16+MB_Result[i][3]%10,MB_Result[i][0],0);
					Send_Bit=2+1;					//置发送盲表成绩代替触板成绩标志
				}
			}	
		*/	
			for(i=0;i<10;i++)
			{
			 if(CloseLaneState[i]==2)		//关闭道次状态  =2：打开；=3：关闭
			 {													//道上有运动员，按键才有效  2023-11-17
				res=btn_check(cmdLbtn[i],&in_obj);   
				if(((cmdLbtn[i]->sta&(1<<7))==0)&&(cmdLbtn[i]->sta&(1<<6))) {
					Lkey_state=1;//0;//有输入,有按键按下且松开,并且TP松开了
					cmdLbtn[i]->sta&=~(1<<6); //  ??????//b6:0,没有按键按下;1,有按键按下
				}
			  if(res&&(cmdLbtn[i]->sta&(1<<6))&&(Lkey_state==1))//有输入,有按键按下且松开,并且TP松开了
				{  
					if(((cmdLbtn[i]->sta&&TP_PRES_DOWN)==1))	
					{	
						if((TP_Open_Close_State[i][0]==1)||(MB_Open_Close_State[0][i]==1)
							||((TP_Open_Close_State[i][0]==3)&&(MB_Open_Close_State[0][i]==3)&&(Lane_Display_MSecond[i][1-0]>=Close_Time)))			//左边触板打开或者盲表打开，按键有效
						{
							OnSendSWData(Touchpad_Command+0x10,TouchButton_Result,Lane_NoTbl[i]);			//发送触摸按键成绩代替触板成绩，道次
							Send_Bit=2+2;					//置发送触摸按键成绩代替触板成绩标志

							display_time();		//将LCD ID打印到lcd_Dis数组。
			
							Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
							Lane_Display_State[i][1-0]=0;																				//此道显示成绩，显示状态为1;
							Lane_Display_MSecond[i][1-0]=0;								//显示时间清零
				
							LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  
							Lkey_state=0;
					
						 if(Testing_bit==0)						//2024-12-23
						 {
							display_swim_dir(dir_posx,i,0,1);	
		
							if(TP_Open_Close_State[i][0]==3)														//触板坏 =3：坏;
							{
								Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);			//左边触板坏颜色显示
							}
							else {																											//触板好
								TP_Open_Close_State[i][0]=0;									//左边触板关闭
								Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);						//左边触板关闭：运动员触板后无成绩
							}
						
							Display_Laps_Place_Direct(i,0);
							
							if(MB_Open_Close_State[0][i]==3)														//第0行左边盲表坏 =3：坏;
							{
								sprintf((char*)lcd_Dis,"L%d",(i));
								Display_MB_StateGroup(0,i,Bad_Color,lcd_Dis);		//左边盲表坏颜色显示
							}
							else {																											//盲表好
								if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;									//第0行左边盲表关闭
								if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=0;									//第1行左边盲表关闭
								if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=0;									//第2行左边盲表关闭
								sprintf((char*)lcd_Dis,"L%d",(i));
								Display_MB_StateGroup(0,i,Close_Color,lcd_Dis);		
							}
	
						}
					}
				 }
			  }
				res=btn_check(cmdRbtn[i],&in_obj);   
				if(((cmdRbtn[i]->sta&(1<<7))==0)&&(cmdRbtn[i]->sta&(1<<6))) {
						Rkey_state=1;	//有输入,有按键按下且松开,并且TP松开了
						cmdRbtn[i]->sta&=~(1<<6); //  ??????//b6:0,没有按键按下;1,有按键按下
				}
			
			  if(res&&(cmdRbtn[i]->sta&(1<<6))&&(Rkey_state==1))//有输入,有按键按下且松开,并且TP松开了
				{  
					if(((cmdRbtn[i]->sta&&TP_PRES_DOWN)==1))	
					{	
						if((TP_Open_Close_State[i][1]==1)||(MB_Open_Close_State[0][i+10]==1)
							||((TP_Open_Close_State[i][1]==3)&&(MB_Open_Close_State[0][i+10]==3)&&(Lane_Display_MSecond[i][0]>=Close_Time)))				//右边触板打开或者盲表打开，按键有效
						{
							OnSendSWData(Touchpad_Command+0x10,TouchButton_Result,Lane_NoTbl[i+10]);			//发送触摸按键成绩代替触板成绩，道次
							Send_Bit=2+2;					//置发送触摸按键成绩代替触板成绩标志

							display_time();		//将LCD ID打印到lcd_Dis数组。
							Lane_Display_State[i][0]=0;																				//此道显示成绩，显示状态为0;
							Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
							Lane_Display_MSecond[i][0]=0;								//显示时间清零
							LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i+1)*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  
							Rkey_state=0;
													 
						 if(Testing_bit==0)					//2024-12-23
						 {
							display_swim_dir(dir_posx,i,1,1);	
		
							if(TP_Open_Close_State[i][1]==3)														//触板坏 =3：坏;
							{
								Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);		//右边触板坏颜色显示
							}
							else {																											//触板好
								TP_Open_Close_State[i][1]=0;															//右边触板关闭，运动员触板无效
								Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);	//右边触板好颜色显示
							}
					
							Display_Laps_Place_Direct(i,1);
							
							if(MB_Open_Close_State[0][i+10]==3)														//盲表坏 =3：坏;
							{
								sprintf((char*)lcd_Dis,"R%d",(i));
								Display_MB_StateGroup(1,i,Bad_Color,lcd_Dis);			//右边盲表坏颜色显示
							}
							else {																											//盲表好
								if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=0;								//第0行右边盲表关闭
								if(MB_Open_Close_State[1][i+10]!=3 && MB_Open_Close_State[1][i+10]!=4) MB_Open_Close_State[1][i+10]=0;								//第1行右边盲表关闭
								if(MB_Open_Close_State[2][i+10]!=3 && MB_Open_Close_State[2][i+10]!=4) MB_Open_Close_State[2][i+10]=0;								//第2行右边盲表关闭
								sprintf((char*)lcd_Dis,"R%d",(i));
								Display_MB_StateGroup(1,i,Close_Color,lcd_Dis);		
							}
						}
					 }
				 }
				}
			 }				
		}
		
		if(connstatus==0)//仅在连接未建立的时候,可以切换输入窗口
		{
		/*
			if(smemo->top<in_obj.y&&in_obj.y<(smemo->top+smemo->height)&&(smemo->left<in_obj.x)&&in_obj.x<(smemo->left+smemo->width))//在smemo内部 
			{ 
				editflag=0;			//编辑的是smemo
				edit_show_cursor(eip,0);	//关闭edit的光标
				edit_show_cursor(eport,0);	//关闭eport的光标
				eip->type=0X04;		//eip光标不闪烁 
				eport->type=0X04;	//eport光标不闪烁 
				smemo->type=0X01;	//memo可编辑,闪烁光标  
			}
		*/
/*			
			if(eip->top<in_obj.y&&in_obj.y<(eip->top+eip->height)&&(eip->left<in_obj.x)&&in_obj.x<(eip->left+eip->width))//在eip框内部 
			{
				if(protocol==0)continue;//tcp server协议的时候,不需要设置IP地址
				editflag=1;			//编辑的是eip
		//		memo_show_cursor(smemo,0);	//关闭smemo的光标
				edit_show_cursor(eport,0);	//关闭eport的光标
				eip->type=0X06;		//eip光标闪烁 
				eport->type=0X04;	//eport光标不闪烁 
		//		smemo->type=0X00;	//smemo不可编辑,光标不闪烁
			}
			if(eport->top<in_obj.y&&in_obj.y<(eport->top+eport->height)&&(eport->left<in_obj.x)&&in_obj.x<(eport->left+eport->width))//在eport框内部 
			{
				editflag=2;			//编辑的是eport
		//		memo_show_cursor(smemo,0);	//关闭smemo的光标
				edit_show_cursor(eip,0);	//关闭eip的光标
				eport->type=0X06;	//eport光标闪烁 
				eip->type=0X04;		//eip光标不闪烁 
		//		smemo->type=0X00;	//smemo不可编辑,光标不闪烁
			}
			*/
		}
	//	edit_check(eip,&in_obj);
	//	edit_check(eport,&in_obj);
	//	t9_check(t9,&in_obj);		   
	//	memo_check(smemo,&in_obj);
//		memo_check(rmemo,&in_obj);//检测rmemo
	/*
		if(t9->outstr[0]!=NULL)//添加字符
		{
			if(editflag==1)//eip
			{
				if((t9->outstr[0]<='9'&&t9->outstr[0]>='0')||t9->outstr[0]=='.'||t9->outstr[0]==0X08)edit_add_text(eip,t9->outstr);
			}else if(editflag==2)//eport
			{
				if((t9->outstr[0]<='9'&&t9->outstr[0]>='0')||t9->outstr[0]==0X08)edit_add_text(eport,t9->outstr);
			}else //smemo
			{   
  				memo_add_text(smemo,t9->outstr);
			}
			t9->outstr[0]=NULL;//清空输出字符 
		}
	*/	
	/*  2024-10-26 取消主控制界面中的协议选择功能
		res=btn_check(protbtn,&in_obj);   
		if(res&&((protbtn->sta&(1<<7))==0)&&(protbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
		{  
			//先选择模式    
			tempx=protocol;
			app_items_sel((lcddev.width-180)/2,(lcddev.height-192)/2,180,72+40*3,(u8**)netplay_mode_tbl,3,(u8*)&tempx,0XD0,(u8*)netplay_btncaption_tbl[0][gui_phy.language]);//3个选择
		if(tempx!=protocol)
			{
				protocol=tempx;  
				if(protocol!=0)
				{
					lwipdev.ip[3]=108;					//2023-5-22

					sprintf((char*)ptemp,"%d.%d.%d.%d",lwipdev.ip[0],lwipdev.ip[1],lwipdev.ip[2],lwipdev.ip[3]);
		//			strcpy((char*)eip->text,(const char *)ptemp);	//恢复默认IP地址 
					ipcaption=netplay_ipcaption_tb[0][gui_phy.language];//TCP Client/UDP模式,显示目标IP
				}
				else 
				{ 
					lwipdev.ip[3]=100;					//2023-5-22

					sprintf((char*)ptemp,"%d.%d.%d.%d",lwipdev.ip[0],lwipdev.ip[1],lwipdev.ip[2],lwipdev.ip[3]);
		//			strcpy((char*)eip->text,(const char *)ptemp);	//恢复默认IP地址 
					ipcaption=netplay_ipcaption_tb[1][gui_phy.language];//默认是TCP Server/UDP模式,显示本机IP  
				}
		//		tempx=(lcddev.width-35*ip_fsize/2)/3-50;
				gui_fill_rectangle(IP_Sx,(ip_height-ip_fsize)/2,ip_fsize*strlen((char*)ipcaption)/2,ip_fsize,NET_IP_BACK_COLOR);//清除原来的显示
				gui_show_string(ipcaption,IP_Sx,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//本地IP/目标IP
		//		net_edit_colorset(eip,eport,protocol,connstatus);//重画edit框 
				net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<2);//更新prot信息 
			}
		} 
*/
//   2024-10-26
/*		res=btn_check(connbtn,&in_obj);   
		if(res&&((connbtn->sta&(1<<7))==0)&&(connbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
		{   
			connstatus=!connstatus;
			tcpconn=0;				//标记TCP连接未建立
			if(connstatus==1)//建立连接
			{
				bkcolor=gui_memex_malloc(200*80*2);//申请内存
				if(bkcolor==NULL)//读取背景色失败了,直接继续运行,不执行后续操作
				{
					connstatus=0;
					printf("netplay ex outof memory\r\n");
					continue;
				}
				app_read_bkcolor((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,bkcolor);//读取背景色
				window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,(u8*)netplay_connmsg_tbl[0][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//显示正在连接	
		//	2024-10-26			
				tipaddr.addr=net_get_ip(eip->text);
						if(tipaddr.addr!=0)
						{
							netconncom=netconn_new(NETCONN_UDP);  	//创建一个UDP链接
							netconncom->recv_timeout=10;  			//接收超时函数
							tport=net_get_port(eport->text); 
							err=netconn_bind(netconncom,IP_ADDR_ANY,tport);	//绑定UDP_PORT端口
  							if(err==ERR_OK)err=netconn_connect(netconncom,&tipaddr,tport);//连接到远端主机端口
							if(err!=ERR_OK)//连接失败 
							{ 
								connstatus=0;//连接失败
								net_disconnect(netconncom,NULL);//关闭连接
							} 
						}
	//

				switch(protocol)
				{
					case 0://TCP Server协议 
			//			tport=net_get_port(eport->text);		//得到port号
						netconnnew=netconn_new(NETCONN_TCP);  	//创建一个TCP链接
						netconnnew->recv_timeout=10;  			//禁止阻塞线程
						err=netconn_bind(netconnnew,IP_ADDR_ANY,tport);//绑定端口
						if(err==ERR_OK)err=netconn_listen(netconnnew);  //进入监听模式
						else
						{
							connstatus=0;//连接失败
							net_disconnect(netconnnew,NULL);//关闭连接 
						}
						break;
					case 1://TCP Client协议 
			//			tipaddr.addr=net_get_ip(eip->text);
						if(tipaddr.addr!=0)
						{
							netconncom=netconn_new(NETCONN_TCP); //创建一个TCP链接
							netconncom->recv_timeout=10;
					//		tport=net_get_port(eport->text); 
 							err=netconn_connect(netconncom,&tipaddr,tport);//连接服务器 
							if(err==ERR_OK)tcpconn=1;//连接成功 
							else
							{
								connstatus=0;//连接失败
								net_disconnect(netconncom,NULL);//关闭连接
							}
						} 
						break;
					case 2://UDP协议  
			//			tipaddr.addr=net_get_ip(eip->text);
						if(tipaddr.addr!=0)
						{
							netconncom=netconn_new(NETCONN_UDP);  	//创建一个UDP链接
							netconncom->recv_timeout=10;  			//接收超时函数
			//				tport=net_get_port(eport->text); 
							err=netconn_bind(netconncom,IP_ADDR_ANY,tport);	//绑定UDP_PORT端口
  							if(err==ERR_OK)err=netconn_connect(netconncom,&tipaddr,tport);//连接到远端主机端口
							if(err!=ERR_OK)//连接失败 
							{ 
								connstatus=0;//连接失败
								net_disconnect(netconncom,NULL);//关闭连接
							} 
						}
						break;
				}
				
				if(err==ERR_OK)window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,(u8*)netplay_connmsg_tbl[2][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//显示连接成功
				else window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,(u8*)netplay_connmsg_tbl[1][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//显示连接失败
				delay_ms(800);//延时等待提示
				app_recover_bkcolor((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,bkcolor);//恢复背景色
				gui_memex_free(bkcolor);//释放内存
			}				
 		} 
	*/	
//	 2024-10-17 取消“  清除接收 ”按键
	/*
		res=btn_check(clrbtn,&in_obj);   
		if(res&&((clrbtn->sta&(1<<7))==0)&&(clrbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
		{   
			rxcnt=0;//发送总数清零
			txcnt=0;//接收总数清零
//			rmemo->text[0]=0;//清空rmemo,从头开始
//			memo_draw_memo(rmemo,1);//重画rmemo 		 
			net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,0X07);//更新所有信息 
		} 
	*/
		
		//显示滚动时间  2023-7-27
		if(Display_RollingTime_Bit==1)	
		{
				display_rollingtime();
				Display_RollingTime_Bit=0;
		}

		//处理触板和盲表之间关系程序  2023-11-5
		if(TP_MB_Bit==1)	
		{
				Process_TP_MB();
				TP_MB_Bit=0;
		}
	
		//处理出发台延迟时间程序  2024-11-25
		if(StartBox_Bit==1)
		{
				Process_StartBox_DelayClose();   //处理出发台延迟时间 2025-11-25
				Process_StartboxStateChange();   //2026-05-30 SB 状态变化扫描+上报
				Process_TPStateChange();         //2026-05-30 TP 状态变化扫描+上报
				Process_MBStateChange();         //2026-05-30 MB 状态变化扫描+上报
				StartBox_Bit=0;
		}

		//2026-05-11 发令后"开放时间"结束的时刻，计算并广播/显示每道运动员相对发令的出发时间
		if(GunFired_PostOpenDoneBit==1)
		{
			Process_StartBox_LaneTime();
		}
	
		//处理触板延迟时间程序  2024-12-12
		if(TP_Bit==1)	
		{
				Process_TP_DelayClose();   //处理触板延迟时间 2025-12-12
				TP_Bit=0;
		}
		
		//发送滚动时间  2023-7-27
		if((Send_Bit!=0))//有数据,且连接OK
		{
			Send_Bit=0;
			/*
			if(Send_RollingTime_Bit==1)	
			{
				if((Send_Bit==0))
				{
		//			OnSendSWData(0x7f,SW_Command1,Control_Port_Num);  //发送滚动时间
					Send_RollingTime_Bit=0;
				}
		  }
	*/
					
	
			if(Rec_send_num>Rec_Loop)
			{
				SendLength=Rec_Loop*TxRx_Data_Length;   //2023-10-23
			
				for(u16 i=0;i<SendLength;i++)
				{
					Send_buf[i]=Send_Data_buf[i];
				}
				Rec_send_num=Rec_send_num-Rec_Loop;
				for(u16 i=0;i<Rec_send_num*TxRx_Data_Length;i++)
				{
					Send_Data_buf[i]=Send_Data_buf[i+Rec_Loop*TxRx_Data_Length];
				}
				Send_Bit=0;
				
				Rec_send_num=0;
			}
			else 
			{
				SendLength=Rec_send_num*TxRx_Data_Length;  //Rec_send_num<Rec_Loop
			
				for(i=0;i<SendLength;i++)
				{
					Send_buf[i]=Send_Data_buf[i];
				}
				Send_Bit=0;
				Rec_send_num=0;
			}

		if(connstatus==1)//在连接状态时发送数据  2024-10-15
		{	
			if(tcpconn==1&&protocol!=2)//TCP Client/TCP Server发送数据
			{ 
					err=netconn_write(netconncom ,Send_buf,SendLength,NETCONN_COPY);//发送smemo->text中的数据 
					if(err==ERR_OK)//发送成功
					{
				//		txcnt+=Rec_send_num*TxRx_Data_Length;//总发送长度增加 	 
				//				txcnt+=SendLength;//总发送长度增加 	 
				//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//更新TX信息  2024-10-27
					}
			}else
				
		//		if(connstatus==1)
				{
					netbuf_alloc(sendcmdbuf,SendLength);
					sendcmdbuf->p->payload=Send_buf;//拷贝数据到sendbuf数组

					err=netconn_send(netconncom,sendcmdbuf);//将netbuf中的数据发送出去
		//			if(err!=ERR_OK)printf("netconn_send fail\r\n"); 
		//			err=netconn_send(netconncom,sendbuf);//将netbuf中的数据发送出去
				if(err==ERR_OK)		//2023-7-26
				{
				//		txcnt+=strlen((char*)sendbuf);//总发送长度增加 	
				//		txcnt+=SendLength;//总发送长度增加 	 
				//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//更新TX信息  2024-10-27
					}

			}
		}	
	//	u1_SendData(Send_buf,SendLength);	
	//	u2_SendData(Send_buf,SendLength);	
		/*
			if(SendLength>0)
			{
				for(u16 i=0;i<SendLength;i++)
				{
					UART4_TX_BUF[RS_TX_len+i]=Send_buf[i];
				}
				RS_TX_len=RS_TX_len+SendLength;		//发送数据长度
				SendLength=0;	
			}
			*/
	//		if((RS_TX_Bit==0)&&(RS_TX_len>0))		 //有数据要发送
//			if((RS_TX_Bit==0)&&(RS_TX_len>=TxRx_Data_Length))		 //有数据要发送
	/*
			if((RS_TX_Bit==0)&&(RS_TX_No>0))		 //有数据要发送  2023-10-26
			{
				{
		//			RS_TX_No=0;
		//			UART4->TDR=UART4_TX_BUF[RS_TX_No]; //发送数据
					for(u8 i=0;i<TxRx_Data_Length;i++)
					{
			//			UART4->TDR=UART4_TX_BUF[i];
						UART4->TDR=Send_Data_buf[i+RS_TX_Ptr*TxRx_Data_Length];
					}
					RS_TX_Ptr++;
					if(RS_TX_Ptr>=Rec_Loop) RS_TX_Ptr=0;
					RS_TX_No--;
					if(RS_TX_No<=0)
					{
						RS_TX_No=0;
						Rec_send_num=0;
						RS_TX_Ptr=0;
					}
					RS_TX_Bit=1;
					UART4->CR1|=1<<30;	 	//位 30 TXFEIE:TXFIFO 为空时中断使能 (TXFIFO empty interrupt enable)
					
				}		
			}
			*/
		u4_SendData(Send_buf,SendLength);	
							

		}


		/*
		res=btn_check(sendbtn,&in_obj);   
		if(res&&((sendbtn->sta&(1<<7))==0)&&(sendbtn->sta&(1<<6)))//有输入,有按键按下且松开,并且TP松开了
		{  
			memo_add_text(smemo,"hong65");
			tempx=strlen((char*)smemo->text);//必须有数据才发送
			if(connstatus==1&&tempx)//有数据,且连接OK
			{
				if(tcpconn==1&&protocol!=2)//TCP Client/TCP Server发送数据
				{ 
					err=netconn_write(netconncom ,smemo->text,tempx,NETCONN_COPY);//发送smemo->text中的数据 
					if(err==ERR_OK)//发送成功
					{
						txcnt+=strlen((char*)smemo->text);//总发送长度增加 	 
						net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//更新TX信息 
					}
				}else
				{
	//		notice_len++;
	//		sprintf((char*)lcd_Dis,"S%d",notice_len);	 		
	//		LCD_ShowString(1050,500,300,32,24,lcd_Dis);		//显示LCD ID	  
					sendbuf=netbuf_new();
					netbuf_alloc(sendbuf,strlen((char *)smemo->text));
					strcpy(sendbuf->p->payload,(void*)smemo->text);//拷贝数据到sendbuf数组
					err=netconn_send(netconncom,sendbuf);//将netbuf中的数据发送出去
					if(err!=ERR_OK)printf("netconn_send fail\r\n"); 
					else 
					{
						txcnt+=strlen((char*)smemo->text);//总发送长度增加 	
						net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//更新TX信息 
					}
					netbuf_delete(sendbuf);  //删除buf									
				}	
			}
		} 
			*/
		
		if(connstatus==1)//连接状态
		{
			if(tcpconn==0&&protocol==0)//TCP Server模式下,连接还未建立,检查TCP连接
			{
				err=netconn_accept(netconnnew,&netconncom);//接收连接请求
				if(err==ERR_OK)//成功监测到连接
				{ 
					netconncom->recv_timeout=10; 
   					tcpconn=1;
				}
			}else
			{			
				//处理接收包
				err=netconn_recv(netconncom,&recvbuf);//查看是否接收到数据
				if(err==ERR_OK)  //接收到数据
				{		 
	//				notice_len++;
	//		sprintf((char*)lcd_Dis,"R%d",notice_len);	 		
//			LCD_ShowString(1050,500,300,32,24,lcd_Dis);		//显示LCD ID	  
					
					netconn_getaddr(netconncom,&Remote_tipaddr,&Remote_tport,0); //获取远端IP地址和端口号  2024-11-1
					if(Remote_tipaddr.addr!=oldaddr||Remote_tport!=oldport)//新地址/端口号  2024-11-1
					{
						oldaddr=Remote_tipaddr.addr;  //2024-11-1
						oldport=Remote_tport;					  //2024-11-1
						sprintf((char*)ptemp,"[From:%d.%d.%d.%d:%d]:\r\n",oldaddr&0XFF,(oldaddr>>8)&0XFF,(oldaddr>>16)&0XFF,(oldaddr>>24)&0XFF,oldport); 
//						tempx=strlen((char*)rmemo->text)+strlen((char*)ptemp);//得到新的总长度
//						if(tempx>=NET_RMEMO_MAXLEN)rmemo->text[0]=0;//清空rmemo,从头开始
//						strcat(((char*)rmemo->text),(char*)ptemp);//添加收到的数据	 
					}

					memcpy(buff,recvbuf->p->payload,recvbuf->p->tot_len);
//						tempx=strlen((char*)rmemo->text);//得到新的总长度
		
					rxcnt+=recvbuf->p->tot_len;//strlen((char*)p);//总接收长度增加

					for(i=0;i<recvbuf->p->tot_len;i++)
					{
						TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=buff[i];			//2023-7-17
						TCPIP_Rec_Char_Ptr++;
						if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
						tempx=tempx+3;//得到新的总长度
	//					if(tempx>=NET_RMEMO_MAXLEN)rmemo->text[0]=0;//清空rmemo,从头开始
	//					sprintf((char*)tmp,_T("%02x,"), buff[i]);										// 
	//					strcat(((char*)rmemo->text),(char*)tmp);//添加收到的数据	 
			
					}
					netbuf_delete(recvbuf); // 2023-8-15
							
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15

					
	//		memcpy(p,recvbuf->p->payload,recvbuf->p->tot_len);
	//		p[recvbuf->p->tot_len]=0;	//末尾加入结束符  
	//				tempx=strlen((char*)rmemo->text)+strlen((char*)p);//得到新的总长度
	//				if(tempx>NET_RMEMO_MAXLEN)rmemo->text[0]=0;//清空rmemo,从头开始
	//				strcat(((char*)rmemo->text),(char*)p);//添加收到的数据		
	//		rxcnt+=strlen((char*)p);//总接收长度增加
		//			rxcnt+=recvbuf->p->tot_len;//总接收长度增加
	//		memo_draw_memo(rmemo,1);//重画rmemo 		 
	//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//更新RX信息  2024-10-27
			//		netbuf_delete(recvbuf); // 2023-8-15
		}else if(err==ERR_CLSD)
			{
					if(protocol==0)tcpconn=0;//进入连接断开状态
					else connstatus=0;
					net_disconnect(netconncom,NULL);//断开netconncom连接  
				} 
			}				
		}
		if(oldconnstatus!=connstatus)//连接状态改变了
		{		
			oldconnstatus=connstatus;
			if(connstatus==0)//连接断开了?强制断开连接?
			{
				net_disconnect(netconnnew,netconncom);//断开连接 
				netconncom=NULL;
				netconnnew=NULL; 
				if(protocol==0)net_tcpserver_remove_timewait();//TCP Server,删除等待状态
	//			protbtn->sta=0;//协议选择按钮进入激活状态
	//			connbtn->caption=netplay_btncaption_tbl[1][gui_phy.language];  			
				gui_fill_circle(cds0x,1+cr,cr,Close_Color);  //没连接上，灰色
			}else//连接成功
			{
	//			protbtn->sta=2;//协议选择按钮进入非激活状态
	//			connbtn->caption=netplay_btncaption_tbl[2][gui_phy.language]; 
	//			editflag=0;			//只允许编辑smemo
	//			edit_show_cursor(eip,0);	//关闭edit的光标
	//			edit_show_cursor(eport,0);	//关闭eport的光标
	//			eip->type=0X04;		//eip光标不闪烁 
	//			eport->type=0X04;	//eport光标不闪烁 
		//		smemo->type=0X01;	//memo可编辑,闪烁光标  
				gui_fill_circle(cds0x,1+cr,cr,Valid_Color); //连接上 ，红色 
			}
	//		btn_draw(protbtn);//重画按钮
	//		btn_draw(connbtn);
		//	net_edit_colorset(eip,eport,protocol,connstatus);//重画edit框
					
		}
					
/*
		if(USART_RX_STA&0x8000)			//串口1接收到数据否？
		{					   
			USART1_RX_len=USART_RX_STA&0x3fff;//得到此次接收到的数据长度
			for(i=0;i<USART1_RX_len;i++)
			{
				TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=USART_RX_BUF[i];			//2023-7-17
				TCPIP_Rec_Char_Ptr++;
				if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
				sprintf((char*)tmp,_T("%02x,"), USART_RX_BUF[i]);										
				strcat(((char*)rmemo->text),(char*)tmp);//添加收到的数据	 
			}
			USART_RX_STA=0;
		
			rxcnt+=USART1_RX_len;//总接收长度增加
			memo_draw_memo(rmemo,1);//重画rmemo 		2023-8-11		 
			net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//更新RX信息 
		
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15
			
		}
		*/
/*	
		if(USART2_RX_STA&0x8000)			//串口1接收到数据否？
		{					   
			USART1_RX_len=USART2_RX_STA&0x3fff;//得到此次接收到的数据长度
			for(i=0;i<USART1_RX_len;i++)
			{
				TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=USART2_RX_BUF[i];			//2023-7-17
				TCPIP_Rec_Char_Ptr++;
				if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
				sprintf((char*)tmp,_T("%02x,"), USART2_RX_BUF[i]);									
				strcat(((char*)rmemo->text),(char*)tmp);//添加收到的数据	 
			}
			USART2_RX_STA=0;
		
			rxcnt+=USART1_RX_len;//总接收长度增加
			memo_draw_memo(rmemo,1);//重画rmemo 		2023-8-11		 
			net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//更新RX信息 
		
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15
			
		}
*/

		if(UART4_RX_STA&0x8000)			//串口4接收到数据否？
		{					   
			UART4->CR1&=~(1<<0);  			//串口使能
			USART4_RX_len=UART4_RX_PTR;		//UART4_RX_STA&0x3fff;//得到此次接收到的数据长度
			for(u8 i=0;i<USART4_RX_len;i++)
			{
				if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
				TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=UART4_RX_BUF[i];			//2023-7-17
				TCPIP_Rec_Char_Ptr++;
		//		sprintf((char*)tmp,_T("%02x,"), UART4_RX_BUF[i]);										/* ?????????			*/
		//		strcat(((char*)rmemo->text),(char*)tmp);//添加收到的数据	 
			}
			UART4_RX_PTR=0;
			UART4_RX_STA=0;
			UART4->CR1|=1<<0;  			//串口使能
		
			rxcnt+=USART4_RX_len;//总接收长度增加
	//		memo_draw_memo(rmemo,1);//重画rmemo 		2023-8-11		 
	//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//更新RX信息  2024-10-27
		
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15
			
		}
		system_task_return=0;
//		if(system_task_return)break;		//TPAD返回  
//		delay_ms(10);

	}
	ledplay_ds0_sta=0;
	Timer_State_LED(1);
	LED1(1);		//关闭LED
	btn_delete(Startbtn);	//删除开始计时按钮
	btn_delete(Resetbtn);	//删除复位按钮 
	btn_delete(Readybtn);	//删除准备就绪按钮 
	btn_delete(Relaybtn);	//删除接力按钮 		//2024-11-24
	btn_delete(Testbtn);	//删除测试按钮
	btn_delete(Distance_Addbtn);		//删除+1按钮 
	btn_delete(Distance_Decbtn);		//删除-1按钮 
	btn_delete(Setupbtn);						//删除参数设置按钮 
	btn_delete(SendStartTimerbtn);	//删除发令时刻按钮 

	if(connstatus)//连接状态退出?断开连接!
	{
		net_disconnect(netconnnew,netconncom);//断开连接  
		if(protocol==0)net_tcpserver_remove_timewait();//TCP Server,删除等待状态
	}
	gui_memin_free(ptemp); 
	gui_memin_free(p); 
 //	edit_delete(eip);	
 //	edit_delete(eport);	
//	memo_delete(rmemo);
//	memo_delete(smemo);
//	t9_delete(t9);
//	btn_delete(protbtn);
//	btn_delete(connbtn);
//	btn_delete(clrbtn);  2024-10-25
//	btn_delete(sendbtn);
	netbuf_delete(sendcmdbuf);  //删除buf									
	
	system_task_return=0;	
		
		
	}
	/*
	else//提示网卡初始化失败!
	{
		window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[1][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);
 		delay_ms(2000);
	} 
	*/
	system_task_return=0;
	lwip_comm_destroy(); 
	PCF8574_WriteBit(ETH_RESET_IO,1);//保持复位LAN8720,降低功耗
}

void StartTiming(void)
{
	u8 i;

	//2024-8-31
	Start_hour=hour;					//发令时间对应时间数据 小时
	Start_minute=minute;			//发令时间对应时间数据 	分
	Start_second=second;			//发令时间对应时间数据 	秒
	Start_msecond=msecond;		//发令时间对应时间数据 	1/1000秒

	//2026-05-11 快照"发令枪响时刻"在抢跳计时器中的读数（Gun_*）。
	//             每道运动员实际出发时间 = LaneStart_* - Gun_*（可负，即抢跳）。
	Gun_minute  = PreStart_minute;
	Gun_second  = PreStart_second;
	Gun_msecond = PreStart_msecond;

	//2026-05-11 重置出发台开放窗口的计时器与一次性触发位
	PostGun_OpenWait_Time   = 0;
	GunFired_PostOpenDoneBit= 0;

	//2024-9-1
	OnSendSWData(Start_Command+0x10,0,0);			//发送开始计时命令	2023-7-18
	Send_Bit=2;														//置发送标志

	if(timer_bit!=1)
	{
	//	if(laps_No_tbl[Laps_No]!=1)   //2024-6-18
		{
					for(i=0;i<10;i++)
					{
						if(CloseLaneState[i]==2)		//关闭道次状态=2：打开；=3：关闭
						{
							Lane_Display_State[i][Start_Dir] = 1;					//2026-06-09 棒1入口: 模拟起跳侧已触板让 Process_Display_SiwmDir 启动计时, 归零开棒1终点端 (= 棒2 SB+TP+MB), 同 PC L6464
							Lane_Display_State[i][1-Start_Dir] = 0;
							Lane_Display_MSecond[i][Start_Dir] = 0;					//倒计时归零
							display_swim_dir(dir_posx,i,Start_Dir,1);			//改变发令点，运动员游泳方向随之变化 2024-6-9
						
				//			laps[i][0]=LAll_Lap;
				//			laps[i][1]=RAll_Lap;					//2024-12-1
						}
					}
		}
		  //2024-6-18
/*		else {
					for(i=0;i<10;i++)
					{
						if(CloseLaneState[i]==2)		//关闭道次状态=2：打开；=3：关闭
						{
							Lane_Display_State[i][0]=1-Start_Dir;																				//此道显示成绩，显示状态为1-Start_Dir;
							Lane_Display_State[i][1]=Start_Dir;																				//此道显示成绩，显示状态为Start_Dir;
							Lane_Display_MSecond[i][0]=0;								//显示时间清零
							display_swim_dir(dir_posx,i,1-Start_Dir,1);			//改变发令点，运动员游泳方向随之变化 2024-6-9	
							laps[i][0]=All_Lap;
						}
					}
		}
		*/
		for(i=0;i<10;i++)		//2024-11-24
		{
			if((Startbox_Open_Close_State[i][Start_Dir]==1))									//左边第i个出发台是初次打开 2024-11-24，蹬出发台有效
			{
				Relay_SB_DelayClose_Time[i]=0;
				Startbox_Open_Close_State[i][Start_Dir]=2;																			//出发台打开延迟
			}
		}
	}
	ds0sta=0;
		//		Startbtn->caption=Hds0_btncaption_tbl[ds0sta][gui_phy.language]; 
				if(ds0sta)
				{     
					gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Open_Color);
					timer_bit=0;
				}else 
				{  
					gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Valid_Color);    
					timer_bit=1;
				}
				Timer_State_LED(ds0sta);
				ledplay_ds0_sta=!ds0sta;
				
			//					timer_bit=1;		//Start timing  2024-8-31
									
	Timer_Reset((1-timer_bit));			//计时器开始计时    2024-1-25
			
}



void display_swim_dir(u16 posx,u8 Lane,u8 xy,u8 xy_length)
{
	// 2026-06-03 直通模式不显示方向箭头 (xy=0/1)
	if (HardwareAlwaysOpenBit && (xy==0 || xy==1)) return;
	if(xy==0)  //left->right
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"          ");//从左->右		 	
		if(xy_length==1) 		sprintf((char*)Dir_Dis,">         ");//从左->右		 	
		if(xy_length==2) 		sprintf((char*)Dir_Dis,">>        ");//从左->右		 	
		if(xy_length==3) 		sprintf((char*)Dir_Dis,">>>       ");//从左->右		 	
		if(xy_length==4) 		sprintf((char*)Dir_Dis,">>>>      ");//从左->右		 	
		if(xy_length==5) 		sprintf((char*)Dir_Dis,">>>>>     ");//从左->右		 	
		if(xy_length==6) 		sprintf((char*)Dir_Dis,">>>>>>    ");//从左->右		 	
		if(xy_length==7) 		sprintf((char*)Dir_Dis,">>>>>>>   ");//从左->右		 	
		if(xy_length==8) 		sprintf((char*)Dir_Dis,">>>>>>>>  ");//从左->右		 	
		if(xy_length==9) 		sprintf((char*)Dir_Dis,">>>>>>>>> ");//从左->右		 	
		if(xy_length>9) 		sprintf((char*)Dir_Dis,">>>>>>>>>>");//从左->右		 	
	}
	if(xy==1)  //left<-right
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"          ");//从右->左		 	
		if(xy_length==1) 		sprintf((char*)Dir_Dis,"         <");//从右->左		 	
		if(xy_length==2) 		sprintf((char*)Dir_Dis,"        <<");//从右->左		 	
		if(xy_length==3) 		sprintf((char*)Dir_Dis,"       <<<");//从右->左		 	 	
		if(xy_length==4) 		sprintf((char*)Dir_Dis,"      <<<<");//从右->左		 			 	
		if(xy_length==5) 		sprintf((char*)Dir_Dis,"     <<<<<");//从右->左		 	
		if(xy_length==6) 		sprintf((char*)Dir_Dis,"    <<<<<<");//从右->左		 	
		if(xy_length==7) 		sprintf((char*)Dir_Dis,"   <<<<<<<");//从右->左		 	 	
		if(xy_length==8) 		sprintf((char*)Dir_Dis,"  <<<<<<<<");//从右->左		 			 	
		if(xy_length==9) 		sprintf((char*)Dir_Dis," <<<<<<<<<");//从右->左		 	 	
		if(xy_length>9) 		sprintf((char*)Dir_Dis,"<<<<<<<<<<");//从右->左		 			 	
	}
	if(xy==2)  //open
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"   打开   ");
	}		
	if(xy==3)  //Close
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"   关闭   ");
	}		
//	LCD_ShowString(posx,dir_posy+(Lane+1)*line_height1,200,btnh,32,Dir_Dis);		//显示LCD ID	      					 
		
	CloseLanebtn[Lane]->caption=Dir_Dis;	//Hcmd_Lbtncaption_tbl[i];
	btn_draw(CloseLanebtn[Lane]);		//画打开/关闭道次按钮

}


void Display_Startbox_State(u16 posx,u16 posy,u8 width,u8 height,u16 color)
{
		if(g_in_net_test) return;	//2026-05-16 net_test 子界面期间跳过主控绘图
		gui_fill_rectangle(posx,posy,width,height,color);//出发台显示
}

void Display_TP_State(u16 posx,u16 posy,u8 width,u8 height,u16 color)
{
		if(g_in_net_test) return;	//2026-05-16 net_test 子界面期间跳过主控绘图
		gui_fill_rectangle(posx,posy,width,height,color);//触板显示
}


#define MB_CR_Sub      6
#define MB_Y_Offset    18

void Display_MB_State(u16 posx,u16 posy,u16 MB_CR,u8 fsize,u16 color,u8 Dis[20])
{
	if(g_in_net_test) return;	//2026-05-16 net_test 子界面期间跳过主控绘图
	gui_fill_circle(posx,posy,MB_CR,color);		//2023-11-17
//	gui_show_strmid(x-r,y-fsize/2,2*r,fsize,BLUE,fsize,str);//显示标题  
}

// 2026-05-31 draw MB sub-position: sub_idx 0=mid MB0 / 1=up MB1 / 2=down MB2
void Display_MB_State_Sub(u8 side,u8 lane,u8 sub_idx,u16 color,u8 *label)
{
	u16 base_y = MBsy[side] + (lane+1) * LaneStep_y;
	u16 posy;
	if (sub_idx == 0) posy = base_y;
	else if (sub_idx == 1) posy = base_y - MB_Y_Offset;
	else posy = base_y + MB_Y_Offset;
	Display_MB_State(MBsx[side], posy, MB_CR_Sub, fsize, color, label);
}

// 2026-05-31 derive MB sub color from MB_Open_Close_State[sub_idx][lane+side*10]
void Display_MB_StateAuto(u8 side, u8 lane, u8 sub_idx, u8 *label)
{
	u8 idx;
	u8 state;
	u16 color;
	idx = (u8)(lane + (side ? 10 : 0));
	state = MB_Open_Close_State[sub_idx][idx];
	if (state == 3) color = Bad_Color;
	else if (state == 4) color = UnInstall_Color;
	else if (state == 1) color = Open_MB_Color;
	else if (state == 2) color = Delay_Color;
	else color = Close_Color;
	Display_MB_State_Sub(side, lane, sub_idx, color, label);
}

// 2026-05-31 draw 3 MB sub-positions: MB0 mid (explicit color) + MB1 up + MB2 down (from array)
void Display_MB_StateGroup(u8 side, u8 lane, u16 mb0_color, u8 *label)
{
	Display_MB_State_Sub(side, lane, 0, mb0_color, label);
	Display_MB_StateAuto(side, lane, 1, label);
	Display_MB_StateAuto(side, lane, 2, label);
}

/*
//GPIO设置专用宏定义
#define GPIO_MODE_IN    	0		//普通输入模式
#define GPIO_MODE_OUT		1		//普通输出模式
#define GPIO_MODE_AF		2		//AF功能模式
#define GPIO_MODE_AIN		3		//模拟输入模式

#define GPIO_SPEED_2M		0		//GPIO速度2Mhz
#define GPIO_SPEED_HIGH		1		//GPIO速度25Mhz
#define GPIO_SPEED_50M		2		//GPIO速度50Mhz
#define GPIO_SPEED_100M		3		//GPIO速度100Mhz
#define GPIO_PUPD_NONE		0		//不带上下拉
#define GPIO_PUPD_PU		1		//上拉
#define GPIO_PUPD_PD		2		//下拉
#define GPIO_PUPD_RES		3		//保留 

#define GPIO_OTYPE_PP		0		//推挽输出
#define GPIO_OTYPE_OD		1		//开漏输出 
*/

//按键初始化函数

void Init_Key_Pin(void)
{
	RCC->AHB1ENR|=1<<0;     //使能PORTA时钟 
	RCC->AHB1ENR|=1<<1;     //使能PORTB时钟 
	RCC->AHB1ENR|=1<<2;     //使能PORTC时钟 
	RCC->AHB1ENR|=1<<3;     //使能PORTD时钟
	RCC->AHB1ENR|=1<<4;     //使能PORTE时钟
	RCC->AHB1ENR|=1<<6;     //使能PORTG时钟 
	RCC->AHB1ENR|=1<<7;     //使能PORTH时钟 
	GPIO_Set(GPIOA,PIN4|PIN6,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																				//PA PIN4,6设置上拉输入,作为键盘的列输入信号
//	GPIO_Set(GPIOB,PIN6|PIN7|PIN8|PIN9|PIN12|PIN13|PIN14|PIN15,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PB PIN6,7,8,9,1,13,14,15设置上拉输入,作为键盘的列输入信号
	GPIO_Set(GPIOB,PIN7|PIN8|PIN9|PIN12|PIN13|PIN14|PIN15,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PB PIN6,7,8,9,1,13,14,15设置上拉输入,作为键盘的列输入信号
	GPIO_Set(GPIOC,PIN6|PIN7|PIN8|PIN9|PIN10|PIN11|PIN13,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);				//PC PIN6,7,8,9,10,11,13设置上拉输入,作为键盘的列输入信号
	GPIO_Set(GPIOD,PIN2|PIN3,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																		//PD PIN2,3设置上拉输入,作为键盘的列输入信号
	GPIO_Set(GPIOG,PIN10|PIN12,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																	//PG PIN10,12设置上拉输入,作为键盘的列输入信号
	GPIO_Set(GPIOH,PIN8,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																				//PH PIN8设置上拉输入,作为键盘的列输入信号

	GPIO_Set(GPIOE,PIN2|PIN3|PIN4|PIN5|PIN6,GPIO_MODE_OUT,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PE2 PIN3 PIN4 PIN5 PIN6推挽输出	作为键盘的行扫输出信号

	GPIO_Set(GPIOH,PIN3,GPIO_MODE_OUT,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PH PIN3推挽输出	作为 计时复位信号的输出  2024-1-25
	
//	GPIO_Set(GPIOC,PIN12,GPIO_MODE_AF,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PC12推挽输出	作为蜂鸣器的驱动输出信号
//	GPIO_Set(GPIOC,PIN12,GPIO_MODE_OUT,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PC12推挽输出	作为蜂鸣器的驱动输出信号

			Line0(1);
			Line1(1);
			Line2(1);
			Line3(1);
			Line4(1);
}

//按键处理函数
//返回按键值
//mode:0,不支持连续按;1,支持连续按;
//0，没有任何按键按下
//1，KEY0按下
//2，KEY1按下
//3，KEY2按下 
//4，KEY_UP按下 即WK_UP
//注意此函数有响应优先级,KEY0>KEY1>KEY2>KEY_UP!!

void Display_Button_State(u16 line)
{
	/*
	u8 lcd_Dis1[20];				//存放LCD ID字符串
	sprintf((char*)lcd_Dis1,"%d %d %d %d %d %d %d %d %d %d",KeyState[0],KeyState[1],KeyState[2],KeyState[3],KeyState[4],KeyState[5],KeyState[6],KeyState[7],KeyState[8],KeyState[9]);	 		
	LCD_ShowString(1050,100+32*(line+1),300,32,24,lcd_Dis1);		//显示LCD ID	  
*/
}


void Key_Process(u8 mode)
{
/*	if(key_up&&(KEY0==0||KEY1==0||KEY2==0||WK_UP==1))
	{
//		delay_ms(10);//去抖动 
		key_up=0;
		if(KEY0==0)return 1;
		else if(KEY1==0)return 2;
		else if(KEY2==0)return 3;
		else if(WK_UP==1)return 4;
	}else if(KEY0==1&&KEY1==1&&KEY2==1&&WK_UP==0)key_up=1; 	    
 //	return 0;// 无按键按下
*/	
	scanline++;
	
	if(scanline>10) scanline=0;
	keyline=scanline;//+1;
	switch(scanline)
	{
		case 1:
			Line0(0);
				
			delay_ms(1);	//消抖
			Read_ColKey();		

			Line0(1);
	
			TouchPad_Process(0);

			Line0(1);

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
		
			break;

		case 2:
			Line1(0);
				
			delay_ms(1);	//消抖
			Read_ColKey();		

			Line1(1);
	
			ManualBut_Process(1,L_MB_State_Line[0],R_MB_State_Line[0]);

			Line1(1);

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
		
		break;
		
		case 3:
			Line2(0);
			delay_ms(1);	//消抖
			Read_ColKey();		
			Line2(1);
	
			ManualBut_Process(2,L_MB_State_Line[1],R_MB_State_Line[1]);

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
	
			break;

		case 4:
			Line3(0);
		
			delay_ms(1);	//消抖
			Read_ColKey();		
			Line3(1);
	

			ManualBut_Process(3,L_MB_State_Line[2],R_MB_State_Line[2]);

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
	
			break;

		case 5:
			Line4(0);
		
			delay_ms(1);	//消抖
			Read_ColKey();		
			Line4(1);
	
			StartBox_Process(4);

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28

		break;
		
		
		default :
			Line0(1);
			Line1(1);
			Line2(1);
			Line3(1);
			Line4(1);
		
			break;
	}
}



void SignalKey_Process(void)
{
	scanline++;
	
	if(scanline>4) scanline=1;
	keyline=scanline;
	switch(scanline)
	{
		case 1:
			Line1(0);
			Read_ColKey();		
			Line1(1);
	
			if(Testing_bit==0) 			StartBox_Process(scanline);		//=0：计时处理
			else 	Test_StartBox_Process(scanline);  										//=1:测试触板

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
		
		break;
		
		case 2:
			Line2(0);
			Read_ColKey();		
			Line2(1);
	
			if(Testing_bit==0) 			ManualBut_Process(scanline,L_MB_State_Line[0],R_MB_State_Line[0]);		//=0：计时处理
			else 	Test_ManualBut_Process(scanline,L_MB_State_Line[0],R_MB_State_Line[0]);  										//=1:测试触板

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
	
			break;

		case 3:
			Line3(0);
			Read_ColKey();		
			Line3(1);

			if(Testing_bit==0) 			ManualBut_Process(scanline,L_MB_State_Line[1],R_MB_State_Line[1]);		//=0：计时处理
			else 	Test_ManualBut_Process(scanline,L_MB_State_Line[1],R_MB_State_Line[1]);  										//=1:测试触板

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
	
			break;

		case 4:
			Line4(0);
			Read_ColKey();		
			Line4(1);
	
			if(Testing_bit==0) 			ManualBut_Process(scanline,L_MB_State_Line[2],R_MB_State_Line[2]);		//=0：计时处理
			else 	Test_ManualBut_Process(scanline,L_MB_State_Line[2],R_MB_State_Line[2]);  										//=1:测试触板

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28

		break;

		
		case 5:
			Line0(0);
			Read_ColKey();		
			Line0(1);
	
			if(Testing_bit==0) TouchPad_Process(0);								//=0：计时处理
			else Test_TouchPad_Process(0);  //=1:测试触板

			Display_Button_State(scanline);		//显示触板，出发台，盲表的按下/放开的状态信息  2023-6-28
		
			break;
		
		default :
			Line0(1);
			Line1(1);
			Line2(1);
			Line3(1);
			Line4(1);
		
			break;
	}
}


void TouchPadSignalKey_Process(void)			//只处理触板信号   2023-7-6
{
	TouchPadscanline++;
	
	if(TouchPadscanline>1) TouchPadscanline=1;
	switch(TouchPadscanline)
	{
		case 1:
			Line0(0);
			Read_ColKey();		
			Line0(1);
	
			if(Testing_bit==0) TouchPad_Process(0);								//=0：计时处理
			else Test_TouchPad_Process(0);  //=1:测试触板
		
			break;


		default :
			Line0(1);
		
			break;
	}
}



void			Delay_us(u16 usecond)	//延迟微秒数值
{
	u16 i;

	for(i=0;i<usecond*400;i++)
	{
		
	}

}




void  ManualBut_Process(u8 line,u8 L_MB_Con_State,u8 R_MB_Con_State)
{
	u8 i,j;
	
	
 if(L_MB_Con_State==1)
 {
	for(i=0;i<10;i++)
	{
		j=i+1;
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (MB_Open_Close_State[line-2][i]!=3 && MB_Open_Close_State[line-2][i]!=4) : (MB_Open_Close_State[line-2][i]==1)))									//左边第line，第i个盲表打开，按下盲表有效
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				sprintf((char*)lcd_Dis,"L%d",i);
				Display_MB_State_Sub(0,i,line-2,Valid_Color,lcd_Dis);		
		//		display_time();																										//将时间输出到lcd_Dis数组。
				Display_MB();									//显示MB正常成绩		2023-11-7
				Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_State[i][1]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_MSecond[i][0]=0;																			//显示时间清零
								
				if(TP_Display_State[i][0]==0 && !HardwareAlwaysOpenBit && CloseLaneState[i]==2)																			//此道没有显示TP成绩，显示状态为0，可以显示盲表成绩;  2024-3-28
					LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);			//发送盲表成绩，道次
				Send_Bit=2;					//置发送盲表成绩标志
								
					
				//保持该道次的当前盲表成绩
					//2026-05-27 三维: [道][第line-2块][字段] + 置 bitmap
					MB_Result[i][line-2][0]=hour;
					MB_Result[i][line-2][1]=minute;
					MB_Result[i][line-2][2]=second;
					MB_Result[i][line-2][3]=msecond;
					MB_Pressed_Bitmap[i] |= (1<<(line-2));
				if(Lane_TP_MB_State[i][0]==1) 
				{
					//触板工作正常，不要盲表成绩
					Lane_TP_MB_State[i][0]=7;					//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				else if(Lane_TP_MB_State[i][0]==5){
					//触板坏，直接用盲表成绩补充  2023-11-6
					Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Bad_Color);						//左边触板坏，颜色显示  2023-11-7
					Display_MB_Time(hour,minute,second,msecond);		//显示MB成绩  2023-11-7
					Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
					Lane_Display_State[i][1]=0;																				//此道不显示成绩，显示状态为0;
					Lane_Display_MSecond[i][0]=0;																			//显示时间清零
					// 2026-06-03 直通模式 OR 关闭泳道 不显示成绩
					if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
						LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
					}
	//				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1+1,Lane_NoTbl[i]);			//发送盲表成绩代替触板成绩，道次
					OnSendSWData(Touchpad_Command+0x10,Pushbutton_Result,Lane_NoTbl[i]);			//发送盲表成绩代替触板成绩，道次
					Send_Bit=2+1;					//置发送盲表成绩代替触板成绩标志
				}
				else if(Lane_TP_MB_State[i][0]==0) {  //2026-05-31 ==0 guard, ==7 already-recorded skips
					//触板成绩还没来或者工作不正常，先记录盲表成绩，等到规定时间后，还没有触板成绩，就用盲表成绩补充  2023-11-6
					Lane_TP_MB_State[i][0]=2;					//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				sprintf((char*)lcd_Dis,"L%d",i);
	//		Display_MB_StateGroup(0,j-1,Open_MB_Color,lcd_Dis);		
				Display_MB_State_Sub(0,i,line-2,Open_MB_Color,lcd_Dis);		
			}
			key_oldstate[line][i]=KeyState[i];
		}
	}
 }
 
 if(R_MB_Con_State==1)
 {
	for(i=10;i<20;i++)
	{
		j=i-10+1;
		if(CloseLaneState[i-10]==2 && (HardwareAlwaysOpenBit ? (MB_Open_Close_State[line-2][i]!=3 && MB_Open_Close_State[line-2][i]!=4) : (MB_Open_Close_State[line-2][i]==1)))									//左边第line，第i个盲表打开，按下盲表有效
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				sprintf((char*)lcd_Dis,"R%d",i);
				Display_MB_State_Sub(1,i-10,line-2,Valid_Color,lcd_Dis);		
		//		display_time();																										//将时间输出到lcd_Dis数组。
				Display_MB();									//显示MB正常成绩		2023-11-7
				Lane_Display_State[i-10][0]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_State[i-10][1]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_MSecond[i-10][1]=0;																			//显示时间清零
				
				if(TP_Display_State[i-10][1]==0 && !HardwareAlwaysOpenBit && CloseLaneState[i-10]==2)																			//此道没有显示TP成绩，显示状态为0，可以显示盲表成绩;  2024-3-28
					LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);			//发送盲表成绩，道次
				Send_Bit=2;					//置发送盲表成绩标志
					
				//保持该道次的当前盲表成绩
					//2026-05-27 三维: i 已是 10-19 (右道索引), 写到 MB_Result[i][line-2][..] + 置 bitmap
					MB_Result[i][line-2][0]=hour;
					MB_Result[i][line-2][1]=minute;
					MB_Result[i][line-2][2]=second;
					MB_Result[i][line-2][3]=msecond;
					MB_Pressed_Bitmap[i] |= (1<<(line-2));

				if(Lane_TP_MB_State[i-10][1]==1) 
				{
					//触板工作正常，不要盲表成绩
					Lane_TP_MB_State[i-10][1]=7;					//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i-10]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				else if(Lane_TP_MB_State[i-10][1]==5){
					//触板坏，直接用盲表成绩补充  2023-11-6
					Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Bad_Color);						//左边触板坏，颜色显示  2023-11-7
					Display_MB_Time(hour,minute,second,msecond);		//显示MB成绩  2023-11-7
					Lane_Display_State[i-10][0]=0;																				//此道显示成绩，显示状态为1;
					Lane_Display_State[i-10][1]=1;																				//此道不显示成绩，显示状态为0;
					Lane_Display_MSecond[i-10][0]=0;																			//显示时间清零
					// 2026-06-03 直通模式 OR 关闭泳道 不显示成绩
					if (!HardwareAlwaysOpenBit && CloseLaneState[i-10]==2) {
						LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i-10+1)*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
					}
	//				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1+1,Lane_NoTbl[i]);			//发送盲表成绩代替触板成绩，道次
					OnSendSWData(Touchpad_Command+0x10,Pushbutton_Result,Lane_NoTbl[i]);			//发送盲表成绩代替触板成绩，道次
					Send_Bit=2+1;					//置发送盲表成绩代替触板成绩标志
				}
				else if(Lane_TP_MB_State[i-10][1]==0) {  //2026-05-31 same as left
					//触板成绩还没来或者工作不正常，先记录盲表成绩，等到规定时间后，还没有触板成绩，就用盲表成绩补充  2023-11-6
					Lane_TP_MB_State[i-10][1]=2;					//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i-10]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				sprintf((char*)lcd_Dis,"R%d",i);
				Display_MB_State_Sub(1,i-10,line-2,Open_MB_Color,lcd_Dis);		
			}
			key_oldstate[line][i]=KeyState[i];
		}
	}
 }
}


void  Test_ManualBut_Process(u8 line,u8 L_MB_Con_State,u8 R_MB_Con_State)
{
	u8 i,j;
	
 if(L_MB_Con_State==1)
 {
	for(i=0;i<10;i++)
	{
		j=i+1;
		if((MB_Open_Close_State[line-2][i]!=3)&&(MB_Open_Close_State[line-2][i]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
			sprintf((char*)lcd_Dis,"L%d",i);
			Display_MB_StateGroup(0,j-1,Valid_Color,lcd_Dis);		
		//display_time();																										//将时间输出到lcd_Dis数组。
			Display_MB();									//显示MB正常成绩		2023-11-7
			Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
			Lane_Display_State[i][1]=0;																				//此道不显示成绩，显示状态为0;
			Lane_Display_MSecond[i][0]=0;																			//显示时间清零
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

			OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);							//发送盲表成绩，道次
			Send_Bit=2;					//置发送盲表成绩标志
		}
		if((MB_Open_Close_State[line-2][i]!=3)&&(MB_Open_Close_State[line-2][i]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
			sprintf((char*)lcd_Dis,"L%d",i);
//			Display_MB_StateGroup(0,j-1,Open_MB_Color,lcd_Dis);		
			Display_MB_StateGroup(0,j-1,Open_MB_Color,lcd_Dis);		
		}
		key_oldstate[line][i]=KeyState[i];
	}
 }
 
 if(R_MB_Con_State==1)
 {
	for(i=10;i<20;i++)
	{
		j=i-10+1;
	
		if((MB_Open_Close_State[line-2][i]!=3)&&(MB_Open_Close_State[line-2][i]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
			sprintf((char*)lcd_Dis,"R%d",i);
			Display_MB_StateGroup(1,j-1,Valid_Color,lcd_Dis);		
		//display_time();																										//将时间输出到lcd_Dis数组。
			Display_MB();									//显示MB正常成绩		2023-11-7
			Lane_Display_State[i-10][0]=0;																				//此道不显示成绩，显示状态为0;
			Lane_Display_State[i-10][1]=1;																				//此道显示成绩，显示状态为1;
			Lane_Display_MSecond[i-10][1]=0;																			//显示时间清零
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  2024-9-1

			OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);					//发送盲表成绩，道次
			Send_Bit=2;					//置发送盲表成绩标志
		}
		if((MB_Open_Close_State[line-2][i]!=3)&&(MB_Open_Close_State[line-2][i]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
			sprintf((char*)lcd_Dis,"R%d",i);
			Display_MB_StateGroup(1,j-1,Open_MB_Color,lcd_Dis);		
		}
		key_oldstate[line][i]=KeyState[i];
	}
 }
}

//2026-05-11 出发台信号缓存/发送辅助
//   在"准备就绪"~"发令后 StartingBlock_Open_Time 秒"窗口内：
//     将本道出发台信号当时的"出发抢跳计时器"读数缓存到 LaneStart_*[i][side]，
//     多次后到信号按迭代覆盖（即"时间新数据覆盖旧数据"），"不显示、不发送"。
//   窗口外则按原有的立即显示/发送行为。
//   side: 0=左 1=右
static void StartBox_RecordSignal(u8 i,u8 j,u8 side)
{
	if(Ready_timer_bit==1 && GunFired_PostOpenDoneBit==0)
	{
		//在窗口内：仅缓存，不显示/不发送
		LaneStart_minute[i][side]  = PreStart_minute;
		LaneStart_second[i][side]  = PreStart_second;
		LaneStart_msecond[i][side] = PreStart_msecond;
		LaneStart_Valid[i][side]   = 1;
		LaneStart_Computed[i][side]= 0;
	}
	else
	{
		//窗口外：保留原有立即显示/发送行为
		// 2026-06-03 直通模式硬件 LCD 不显示成绩 (= PC 接管显示)
		// 2026-06-09 SB 按下不立即显反应时, 改保存 SB 时刻到 LaneStart_*, 由 Process_StartBox_LaneTime 在 SB 延迟关(state=0)时显
		LaneStart_minute[i][side]  = minute;
		LaneStart_second[i][side]  = second;
		LaneStart_msecond[i][side] = msecond;
		LaneStart_Valid[i][side]   = 1;
		LaneStart_Computed[i][side]= 0;
		// 2026-06-02 Phase 2: SB key press time sent as-is (= absolute swim_now), reaction time computed on PC side using known left-touch swim history
		if(side==0)
			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i]);
		else
			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i+10]);
		Send_Bit=2;
	}
}

//出发台处理程序
//增加上升沿触发 功能 2024-2-1
void  StartBox_Process(u8 line)
{
	u8 i,j;

 if(StartBox_Edge_Bit==1)	  //下降沿触发  2024-2-1
 {
	for(i=0;i<10;i++)
	{
		j=i+1;
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][0]!=3 && Startbox_Open_Close_State[i][0]!=4) : ((Startbox_Open_Close_State[i][0]==1)||(Startbox_Open_Close_State[i][0]==2))))									//左边第i个出发台打开或延迟打开 2024-11-24，蹬出发台有效
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				//2026-05-11 左边下降沿：出发台颜色变化作为反馈；时间记录/发送由 StartBox_RecordSignal 统一处理
				Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,0);
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 直通模式 SB 物理松开恢复 Open_SB_Color (= 绿), 不进业务关闭逻辑
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][0]==1))									//左边第i个出发台是初次打开 2024-11-24，蹬出发台有效
				{
					if(Relay_SB_DelayCloseValue==0)
					{
						Startbox_Open_Close_State[i][0]=0;																			//不延迟，左边出发台关闭  2024-12-17
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);//2024-11-24 Close_Color);//左边出发台显示
					}
					else {
						Startbox_Open_Close_State[i][0]=2;																			//左边出发台关闭
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//左边出发台显示
					}
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}

		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][1]!=3 && Startbox_Open_Close_State[i][1]!=4) : ((Startbox_Open_Close_State[i][1]==1)||(Startbox_Open_Close_State[i][1]==2))))								//右边第i个出发台打开，蹬出发台有效
		{
			if((KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {
				//2026-05-11 右边下降沿：出发台颜色变化作为反馈；时间记录/发送由 StartBox_RecordSignal 统一处理
				Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,1);
			}
			if((KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 直通模式 SB 物理松开恢复 Open_SB_Color (= 绿), 不进业务关闭逻辑
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][1]==1))									//右边第i个出发台是初次打开 2024-11-24，蹬出发台有效
				{
					if(Relay_SB_DelayCloseValue==0) 
					{
						Startbox_Open_Close_State[i][1]=0;																					//不延迟，右边出发台关闭		2024-12-17
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//右边出发台显示
					}
					else {
						Startbox_Open_Close_State[i][1]=2;																			//右边出发台关闭
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//右边出发台显示
					}
				}
			}
			key_oldstate[line][i+10]=KeyState[i+10];
		}
	}
 }
 else   //上升沿触发  2024-2-1
 {    
	for(i=0;i<10;i++)
	{
		j=i+1;
	//	if(Startbox_Open_Close_State[i][0]==1)									//左边第i个出发台打开，蹬出发台有效
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][0]!=3 && Startbox_Open_Close_State[i][0]!=4) : ((Startbox_Open_Close_State[i][0]==1)||(Startbox_Open_Close_State[i][0]==2))))			//左边第i个出发台打开或延迟打开 2024-11-24，蹬出发台有效
		{
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {  //2024-2-1
				//2026-05-11 左边上升沿：由 StartBox_RecordSignal 统一处理
				Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,0);
			}
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {    //2024-2-1
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 直通模式 SB 物理松开恢复 Open_SB_Color (= 绿), 不进业务关闭逻辑
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][0]==1))									//左边第i个出发台是初次打开 2024-11-24，蹬出发台有效
				{
					if(Relay_SB_DelayCloseValue==0) 
					{
						Startbox_Open_Close_State[i][0]=0;																			//不延迟，左边出发台关闭  2024-12-17
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);//2024-11-24 Close_Color);//左边出发台显示
					}
					else {
						Startbox_Open_Close_State[i][0]=2;																			//左边出发台关闭
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//左边出发台显示
					}
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
	//	if(Startbox_Open_Close_State[i][1]==1)												//右边第i个出发台打开，蹬出发台有效
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][1]!=3 && Startbox_Open_Close_State[i][1]!=4) : ((Startbox_Open_Close_State[i][1]==1)||(Startbox_Open_Close_State[i][1]==2))))									//左边第i+10个出发台打开或延迟打开 2024-11-24，蹬出发台有效
		{
			if((KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {  // 2024-2-1
				//2026-05-11 右边上升沿：由 StartBox_RecordSignal 统一处理
				Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,1);
			}
			if((KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {   //2024-2-1
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 直通模式 SB 物理松开恢复 Open_SB_Color (= 绿), 不进业务关闭逻辑
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][1]==1))									//左边第i+10个出发台是初次打开 2024-11-24，蹬出发台有效
				{
					if(Relay_SB_DelayCloseValue==0) 
					{
						Startbox_Open_Close_State[i][1]=0;																					//不延迟，右边出发台关闭		2024-12-17
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//右边出发台显示
					}
					else {
						Startbox_Open_Close_State[i][1]=2;																			//右边出发台关闭
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//右边出发台显示
					}
				}
			}
			key_oldstate[line][i+10]=KeyState[i+10];
		}
	}

 }
}


//2026-05-11 发令后出发台 "开放时间" 结束的时刻，一次性计算并广播/显示每道相对发令的出发时间。
//   每道运动员实际时间 = LaneStart_* - Gun_*（可负，即抢跳/犯规）。
//   若某道在窗口期内未触发出发台，则显示 "--"。
//   通过 LaneStart_Computed 标记防止重复处理。
void  Process_StartBox_LaneTime(void)
{
	u8 i,side;
	long swim_10ms,gun_10ms,delta_10ms;
	u8 sign;        //=0:运动员晚于发令（正常） =1:运动员早于发令（抢跳，犯规）
	u16 d_minute,d_second,d_msecond;

	if(GunFired_PostOpenDoneBit!=1) return;       //窗口期未结束，不处理

	for(i=0;i<10;i++)
	{
		for(side=0;side<2;side++)
		{
			if(LaneStart_Computed[i][side]) continue;   //已处理过则跳过

			// 2026-06-09 SB 延迟态 (state==2) 不显反应时, 等延迟关到 (state==0) 才显
			if (Startbox_Open_Close_State[i][side]==2) continue;
			//本道出发台必须处于"打开"或"延迟打"或"关闭(0)"状态，硬件坏(3)/未安装(4) 不参与
			if((Startbox_Open_Close_State[i][side]!=1)
				&&(Startbox_Open_Close_State[i][side]!=2)
				&&(Startbox_Open_Close_State[i][side]!=0))
			{
				LaneStart_Computed[i][side]=1;        //标记已处理（防止重复）
				continue;
			}

			if(LaneStart_Valid[i][side])
			{
				//以 10ms 为最小单位换算成带符号的差值
				swim_10ms = (long)LaneStart_minute[i][side]*60L*100L
					      + (long)LaneStart_second[i][side]*100L
					      + (long)(LaneStart_msecond[i][side]/10);
				gun_10ms  = (long)Gun_minute*60L*100L
					      + (long)Gun_second*100L
					      + (long)(Gun_msecond/10);
				delta_10ms = swim_10ms - gun_10ms;
				if(delta_10ms < 0)
				{
					sign = 1;
					delta_10ms = -delta_10ms;
				}
				else sign = 0;

				d_minute  = (u16)(delta_10ms / (60L*100L));
				delta_10ms = delta_10ms - (long)d_minute*60L*100L;
				d_second  = (u16)(delta_10ms / 100L);
				d_msecond = (u16)((delta_10ms - (long)d_second*100L) * 10);   //还原到 0~990ms

				//格式化为带正负号显示
				if(d_minute==0)
					sprintf((char*)lcd_Dis,"%s%2d.%02d ",(sign==1)?"-":"+",d_second,d_msecond/10);
				else
					sprintf((char*)lcd_Dis,"%s%2d:%02d.%02d",(sign==1)?"-":"+",d_minute,d_second,d_msecond/10);
				// 2026-06-03 直通模式不显示 SB 反应时 (= PC 接管显示)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Timer_posx[side],Timer_posy[side]+(i+1)*line_height1,180,32,32,lcd_Dis);
					// 2026-06-09 反应时显示后, Process_Display_SiwmDir 经 Result_Display_Time 后自动消隐
					Lane_Display_State[i][side] = 1;
					Lane_Display_State[i][1-side] = 0;
					Lane_Display_MSecond[i][side] = 0;
				}

				//发送：用 OnSendSWCommand_Data 携带差值，para5 = sign（0:正，1:抢跳）
				if(side==0)
					OnSendSWCommand_Data(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i],
						d_minute,d_second,d_msecond/10,
						(d_minute>>4)*16+d_msecond%10,
						0,sign);
				else
					OnSendSWCommand_Data(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i+10],
						d_minute,d_second,d_msecond/10,
						(d_minute>>4)*16+d_msecond%10,
						0,sign);
				Send_Bit=2;
			}
			else
			{
				//无出发台信号：显示 "--"
				sprintf((char*)lcd_Dis,"   --   ");
				// 2026-06-03 直通模式不显示 SB 反应时 (= PC 接管显示)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Timer_posx[side],Timer_posy[side]+(i+1)*line_height1,180,32,32,lcd_Dis);
				}
			}

			LaneStart_Computed[i][side]=1;
		}
	}
}


//测试出发台
//增加上升沿触发 功能 2024-2-1
void  Test_StartBox_Process(u8 line)
{
	u8 i,j;
	
 if(StartBox_Edge_Bit==1)	  //下降沿触发  2024-2-1
 {
	for(i=0;i<10;i++)
	{
		j=i+1;
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);//左边出发台显示
			Display_SB();																										//将时间输出到lcd_Dis数组。
			Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
			Lane_Display_State[i][1]=0;																				//此道不显示成绩，显示状态为0;
			Lane_Display_MSecond[i][0]=0;																			//显示时间清零
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i]);			//发送出发台出发时间，道次
			Send_Bit=2;					//置发送出发台出发标志
		}
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//左边出发台显示
		}
		key_oldstate[line][i]=KeyState[i];
		
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);//右边出发台显示
			Display_SB();																										//将时间输出到lcd_Dis数组。
			Lane_Display_State[i][0]=0;																				//此道不显示成绩，显示状态为0;
			Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
			Lane_Display_MSecond[i][1]=0;																			//显示时间清零
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i+10]);			//发送出发台出发时间，道次
			Send_Bit=2;																											//置发送出发台出发标志
		}
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//右边出发台显示
		}
		key_oldstate[line][i+10]=KeyState[i+10];
	}
 }
 else   //上升沿触发  2024-2-1
 {    
	for(i=0;i<10;i++)
	{
		j=i+1;
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {   //2024-2-1
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);//左边出发台显示
			Display_SB();																										//将时间输出到lcd_Dis数组。
			Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
			Lane_Display_State[i][1]=0;																				//此道不显示成绩，显示状态为0;
			Lane_Display_MSecond[i][0]=0;																			//显示时间清零
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i]);			//发送出发台出发时间，道次
			Send_Bit=2;					//置发送出发台出发标志
		}
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {   //2024-2-1
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//左边出发台显示
		}
		key_oldstate[line][i]=KeyState[i];
		
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {   //2024-2-1
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);//右边出发台显示
			Display_SB();																										//将时间输出到lcd_Dis数组。
			Lane_Display_State[i][0]=0;																				//此道不显示成绩，显示状态为0;
			Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
			Lane_Display_MSecond[i][1]=0;																			//显示时间清零
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i+10]);			//发送出发台出发时间，道次
			Send_Bit=2;																											//置发送出发台出发标志
		}
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {  //2024-2-1
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//右边出发台显示
		}
		key_oldstate[line][i+10]=KeyState[i+10];
	}
 }	
}


void TouchPad_Process(u8 line)
{
	u16 i,j;
/*
	for(i=0;i<10;i++)
	{
		j=i+1;
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][0]!=3 && TP_Open_Close_State[i][0]!=4) : (TP_Open_Close_State[i][0]==1)))									//左边触板打开，运动员触板有效
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Valid_Color);						//左边触板示意图显示
				display_time();																										//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_State[i][1]=0;																				//此道显示成绩，不显示状态为0;
				Lane_Display_MSecond[i][0]=0;																			//显示时间清零
				TP_Display_State[i][0]=1;																				//此道显示TP成绩，显示状态为1;  2024-3-28
				
				// 2026-06-03 直通模式硬件 LCD 不显示成绩 (= PC 接管显示)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Final_timer_posx,Final_timer_posy+j*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  
					
					Display_Laps_Place_Direct(i,1);
				}
					
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
				
				if(Lane_TP_MB_State[i][0]==2)
				{
					Lane_TP_MB_State[i][0]=0;							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				else if(Lane_TP_MB_State[i][0]==0)
				{
					Lane_TP_MB_State[i][0]=1;							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
							
	
				//左边第i个出发台接力打开延迟 ，在此延迟时间内蹬出发台有效  2024-11-26
				if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
				{
					if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
					{
						Relay_SB_DelayClose_Time[i]=0;						//在接力比赛中，运动员触板后，出发台可以打开延迟一定时间 2024-11-26
						Startbox_Open_Close_State[i][0]=2;																			//出发台打开延迟
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//左边出发台显示
					}
				}

			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				TP_Open_Close_State[i][0]=0;																			//左边触板关闭
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);						//左边触板关闭：运动员触板后无成绩
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][1]!=3 && TP_Open_Close_State[i][1]!=4) : (TP_Open_Close_State[i][1]==1)))									//右边触板打开，运动员触板有效
		{
			if((KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//右边触板示意图显示
				display_time();		//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_MSecond[i][1]=0;																			//显示时间清零
				TP_Display_State[i][1]=1;																				//此道显示TP成绩，显示状态为1;  2024-3-28

				// 2026-06-03 直通模式硬件 LCD 不显示成绩 (= PC 接管显示)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Middle_timer_posx,Middle_timer_posy+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID
					
					Display_Laps_Place_Direct(i,0);
				}
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);											//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
				
				if(Lane_TP_MB_State[i][1]==2)
				{
					Lane_TP_MB_State[i][1]=0;							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				else if(Lane_TP_MB_State[i][1]==0)
				{
					Lane_TP_MB_State[i][1]=1;							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
			}
			if((KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				TP_Open_Close_State[i][1]=0;																			//右边触板关闭
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Close_Color);						//右边触板关闭：运动员触板后无成绩
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
	}
	*/
	for(i=0;i<10;i++)
	{
		j=i+1;
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][0]!=3 && TP_Open_Close_State[i][0]!=4) : (TP_Open_Close_State[i][0]==1)))									//左边触板打开，运动员触板有效
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				Display_TP_State(TPsx[0],Timer_posy[0]+j*btnhy,8,btnh,Valid_Color);						//左边触板示意图显示
				display_time();																										//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_State[i][1]=0;																				//此道显示成绩，不显示状态为0;
				Lane_Display_MSecond[i][0]=0;																			//显示时间清零
				TP_Display_State[i][0]=1;																				//此道显示TP成绩，显示状态为1;  2024-3-28
				
				// 2026-06-03 直通模式 OR 关闭泳道 不显示成绩 (= PC 接管)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  
					
					Display_Laps_Place_Direct(i,0);
				}
					
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
				//2026-05-31 store LastTouchTime for relay reaction calc
				LastTouchTime_minute[i]=minute; LastTouchTime_second[i]=second; LastTouchTime_msecond[i]=msecond; LastTouchTime_Valid[i]=1;
				
				if(Lane_TP_MB_State[i][0]==2)
				{
					Lane_TP_MB_State[i][0]=7;  //2026-05-31 touchpad over BW, mark lap-recorded							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				else if(Lane_TP_MB_State[i][0]==0)
				{
					Lane_TP_MB_State[i][0]=7;  //2026-05-31 unified mark 7							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				
				if(!HardwareAlwaysOpenBit && (RelayBit==1)) //终点在左边  2024-12-1
				{
					//左边第i个出发台接力打开延迟 ，在此延迟时间内蹬出发台有效  2024-11-26
					if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
					{
						if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
						{
							Relay_SB_DelayClose_Time[i]=0;						//在接力比赛中，运动员触板后，出发台可以打开延迟一定时间 2024-11-26
							Startbox_Open_Close_State[i][0]=1;  // 2026-06-09 棒次交接立即 Open (= 不走延迟态)																			//出发台打开延迟
							Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//2024-11-24 Close_Color);//左边出发台显示
						}
					}
				}
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-10
					if(TP_DelayCloseValue==0)	
					{
						TP_Open_Close_State[i][0]=0;			//不延迟，左边触板关闭  2024-12-17
						Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);						//左边触板关闭：运动员触板后无成绩
					}
					else {
						TP_Open_Close_State[i][0]=2;																			//左边触把映馘关闭
						Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Delay_Color);						//左边触板关闭：运动员触板后无成绩
					}
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}
		else if(TP_Open_Close_State[i][0]==2)									//左边触板延迟打开，运动员触板有效  2024-12-12
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				Display_TP_State(TPsx[0],Timer_posy[0]+j*btnhy,8,btnh,Valid_Color);						//左边触板示意图显示
				display_time();																										//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_State[i][1]=0;																				//此道显示成绩，不显示状态为0;
				Lane_Display_MSecond[i][0]=0;																			//显示时间清零
				TP_Display_State[i][0]=1;																				//此道显示TP成绩，显示状态为1;  2024-3-28
				
				// 2026-06-03 直通模式 OR 关闭泳道 不显示成绩 (= PC 接管)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  
				}
				
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
				
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-10
		//			TP_Open_Close_State[i][0]=0;																			//左边触板关闭
					TP_Open_Close_State[i][0]=2;																			//左边触把映馘关闭
					Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);						//左边触板关闭：运动员触板后无成绩
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][1]!=3 && TP_Open_Close_State[i][1]!=4) : (TP_Open_Close_State[i][1]==1)))									//右边触板打开，运动员触板有效
		{
			if((KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//右边触板示意图显示
				display_time();		//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_MSecond[i][1]=0;																			//显示时间清零
				TP_Display_State[i][1]=1;																				//此道显示TP成绩，显示状态为1;  2024-3-28

				// 2026-06-03 直通模式 OR 关闭泳道 不显示成绩 (= PC 接管)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID
					
					Display_Laps_Place_Direct(i,1);
				}
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);											//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
				//2026-05-31 store LastTouchTime for relay reaction calc
				LastTouchTime_minute[i]=minute; LastTouchTime_second[i]=second; LastTouchTime_msecond[i]=msecond; LastTouchTime_Valid[i]=1;
				
				if(Lane_TP_MB_State[i][1]==2)
				{
					Lane_TP_MB_State[i][1]=7;  //2026-05-31 same as left							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				else if(Lane_TP_MB_State[i][1]==0)
				{
					Lane_TP_MB_State[i][1]=7;  //2026-05-31 unified mark 7							//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
					Lane_TP_MB_Time_Difference[i]=0;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
				}
				
				if(!HardwareAlwaysOpenBit && (RelayBit==1)) //终点在右边  2024-12-1
				{
					//右边第i个出发台接力打开延迟 ，在此延迟时间内蹬出发台有效  2024-11-26
					if((RelayBit==1)&&(Startbox_Open_Close_State[i][1]!=3)&&(laps[i][0]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
					{
						if(((RelayLaps==2)&&((laps[i][0]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
						{
							Relay_SB_DelayClose_Time[i]=0;						//在接力比赛中，运动员触板后，出发台可以打开延迟一定时间 2024-11-26
							Startbox_Open_Close_State[i][1]=1;  // 2026-06-09 棒次交接立即 Open																			//出发台打开延迟
							Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//2024-11-24 Close_Color);//右边出发台显示
						}
					}
				}
				
			}
			if((KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-10
					if(TP_DelayCloseValue==0)	
					{
						TP_Open_Close_State[i][1]=0;																			//不延迟，右边触板关闭  2024-12-17
						Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Close_Color);						//右边触板延迟关闭：运动员触板后有成
					}
					else {
						TP_Open_Close_State[i][1]=2;																			//右边触板延迟关闭
						Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Delay_Color);
					}
				}
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
		else if(TP_Open_Close_State[i][1]==2)									//右边触板打开，运动员触板有效   2024-12-12 
		{
			if((KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//右边触板示意图显示
				display_time();		//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_MSecond[i][1]=0;																			//显示时间清零
				TP_Display_State[i][1]=1;																				//此道显示TP成绩，显示状态为1;  2024-3-28

				// 2026-06-03 直通模式 OR 关闭泳道 不显示成绩 (= PC 接管)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID
				}
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);											//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
				
				
			}
			if((KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-10
		//			TP_Open_Close_State[i][1]=0;																			//右边触板关闭
					TP_Open_Close_State[i][1]=2;																			//右边触板延迟关闭
					Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Delay_Color);
				}
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
	/*					
		//左边第i个出发台接力打开延迟 ，在此延迟时间内蹬出发台有效  2024-11-26
				if((RelayBit==1)&&(Startbox_Open_Close_State[i][Start_Dir]!=3)&&(laps[i][1-Start_Dir]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
				{
					if(((RelayLaps==2)&&((laps[i][1-Start_Dir]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
					{
						Relay_SB_DelayClose_Time[i]=0;						//在接力比赛中，运动员触板后，出发台可以打开延迟一定时间 2024-11-26
						Startbox_Open_Close_State[i][Start_Dir]=2;																			//出发台打开延迟
						Display_Startbox_State(Startboxsx[Start_Dir],Startboxsy[Start_Dir]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//Start_Dir左边出发台显示
					}
				}
*/
	}

}

//测试触板子程序  2023-8-6
void Test_TouchPad_Process(u8 line)
{
	u16 i,j;
	
	for(i=0;i<10;i++)
	{
		j=i+1;	
		//测试左边触板  2023-8-6
		{
			if((TP_Open_Close_State[i][0]!=3)&&(TP_Open_Close_State[i][0]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Valid_Color);						//左边触板示意图显示
				display_time();																										//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_State[i][1]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_MSecond[i][0]=0;								//显示时间清零
				LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,200,32,32,lcd_Dis);		//显示LCD ID	  
					
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
			}
			if((TP_Open_Close_State[i][0]!=3)&&(TP_Open_Close_State[i][0]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Open_TP_Color);						//左边触板打开：触板后有成绩
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
		//测试右边触板  2023-8-6
		{
			if((TP_Open_Close_State[i][1]!=3)&&(TP_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//右边触板示意图显示
				display_time();		//将LCD ID打印到lcd_Dis数组。
				Lane_Display_State[i][0]=0;																				//此道不显示成绩，显示状态为0;
				Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
				Lane_Display_MSecond[i][1]=0;								//显示时间清零
				LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,200,32,32,lcd_Dis);		//显示LCD ID
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);			//发送触板道次及成绩
				Send_Bit=2;					//置发送触板时间标志
			}
			if((TP_Open_Close_State[i][1]!=3)&&(TP_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Open_TP_Color);						//右边触板打开：触板后有成绩
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
	}
}


void	Display_Laps_Place_Direct(u8 Lane,u8 Direct)
{
	if(Direct==0)  //泳池左边  2024-11-24
	{
		if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-11
			if(laps[Lane][0]>0)  	laps[Lane][0]--;
			LLaps_diaplay(Lane);
		}
			if(laps[Lane][0]>0)	display_swim_dir(dir_posx,Lane,0,1);	//display_swim_dir(dir_posx,Lane,Direct,1);	
			else 	display_swim_dir(dir_posx,Lane,0,0);	
	
	}
	else   //泳池右边  2024-11-24
	{
		if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-11
			if(laps[Lane][1]>0)  	laps[Lane][1]--;
			RLaps_diaplay(Lane);
		}
		if(laps[Lane][1]>0)	display_swim_dir(dir_posx,Lane,1,1);	//display_swim_dir(dir_posx,Lane,Direct,1);	
		else 	display_swim_dir(dir_posx,Lane,0,0);	
	}
		
	if(Open_State==0){  //=1：全部打开触板，不封闭；=0：按之前约定方式关闭、打开触板  2024-12-11
		Lap_Place[laps[Lane][0]+laps[Lane][1]]++;			//对应名次+1；
		if(Lap_Place[laps[Lane][0]+laps[Lane][1]]>10) Lap_Place[laps[Lane][0]+laps[Lane][1]]=10;			//十道，名次不超过10；
		Place_display(Lane);
	}
}


void Result_Process(u8 lane)
{
//	u8 i=lane-1;
	
	//Result[10][10][10][2][4];   //成绩 第几道  第几趟 =0:第几名  触板成绩  出发/触板/盲表1/盲表2/盲表3 时/分/秒/千分之一秒
	/*
	Result_TP[lane][laps[i][0]][0]=Lap_Place[laps[i][0]];				//触板名次
	Result_TP[lane][laps[i][0]][1]=hour;				//触板成绩  时
	Result_TP[lane][laps[i][0]][2]=minute;				//触板成绩  分
	Result_TP[lane][laps[i][0]][3]=second;				//触板成绩  秒
	Result_TP[lane][laps[i][0]][4]=msecond/10;				//触板成绩  百分之一秒
	Result_TP[lane][laps[i][0]][5]=msecond%10;				//触板成绩  千分之一秒
	*/
}


void Read_ColKey(void)
{
//	Delay_us(100);				//延迟200us去抖动  2023-7-28
	Delay_us(50);		//50		//延迟200us去抖动  2023-8-18
	
	KeyState[0]=KEYCol0;
	KeyState[1]=KEYCol1;
	KeyState[2]=KEYCol2;
	KeyState[3]=KEYCol3;
	KeyState[4]=KEYCol4;
	KeyState[5]=KEYCol5;
	KeyState[6]=KEYCol6;
	KeyState[7]=KEYCol7;
	KeyState[8]=KEYCol8;
	KeyState[9]=KEYCol9;
	KeyState[10]=KEYColF0;
	KeyState[11]=KEYColF1;
	KeyState[12]=KEYColF2;
	KeyState[13]=KEYColF3;
	KeyState[14]=KEYColF4;
	KeyState[15]=KEYColF5;
	KeyState[16]=KEYColF6;
	KeyState[17]=KEYColF7;
	KeyState[18]=KEYColF8;
	KeyState[19]=KEYColF9;
}

void Reset_Timer(void)
{
	u16 i,j,k;

	Ready_timer_bit=0;				//准备就绪计时器开始计时，计时位=0：不计时；=1：开始计时 2024-8-31

	timer_bit=0;								//计时位=0：不计时；
	
	Testing_bit=0;							//正在进行测试位 =1：正在测试； =0：停止测试   2023-8-5
	Testbtn->bcfucolor=BLACK;		//黑色  2024-12-24
	Testbtn->caption=Test_btncaption_tbl[Testing_bit][gui_phy.language]; 		//显示”测试“
	btn_draw(Testbtn);		//画按钮
	
	Timer_State_LED(0); 
						
	Timer_Reset((1-timer_bit));			//计时器复位    2024-1-25
							
	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color);
	hour=0;
	minute=0;
	second=0;
	msecond=0;

	Start_hour=hour;   				//2024-8-31
	Start_minute=minute;
	Start_second=second;
	Start_msecond=msecond;

	//2026-05-11 "复位"同步清零出发抢跳计时器与发令时刻/每道出发台缓存
	PreStart_minute=0;
	PreStart_second=0;
	PreStart_msecond=0;
	Gun_minute=0;
	Gun_second=0;
	Gun_msecond=0;
	PostGun_OpenWait_Time=0;
	GunFired_PostOpenDoneBit=0;
	for(i=0;i<10;i++)
	{
		for(j=0;j<2;j++)
		{
			LaneStart_minute[i][j]=0;
			LaneStart_second[i][j]=0;
			LaneStart_msecond[i][j]=0;
			LaneStart_Valid[i][j]=0;
			LaneStart_Computed[i][j]=0;
		}
	}

  display_rollingtime();		//显示滚动时间		2023-7-11

	Send_Bit=1;

	OnSendSWData(0x7f,0,0);  //发送滚动时间

	//2024-3-28
	for(i=0;i<10; i++)
	{
		for(j=0;j<2;j++)
		{
			Lane_Display_State[i][j]=0;	
			Lane_Display_MSecond[i][j]=0;	
			TP_Display_State[i][j]=0;				//2024-3-28
		}
	}


	//2026-05-27 三维 [20道][3块][4字段] + 清按下 bitmap
	for(i=0;i<20;i++)
	{
		for(k=0;k<3;k++)
			for(j=0;j<4;j++) MB_Result[i][k][j]=0;
		MB_Pressed_Bitmap[i]=0;
	}
	
	for(i=0;i<40*2;i++)
	{
		Lap_Place[i]=0;
	}		
	
	for(i=0;i<10;i++)
	{
		j=i+1;
									
		if(TP_Open_Close_State[i][0]==3)														//触板坏 =3：坏;
		{
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Bad_Color);			//左边触板坏颜色显示
		}
		else if(TP_Open_Close_State[i][0]==4)														//触板没有安装 =4 2025-1-6;																											//触板好
		{
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//左边触板没有安装颜色显示  2025-1-6
		}
		else {														//触板好
			TP_Open_Close_State[i][0]=Open_State;		//2026-05-17 尊重 Open_State：=1 时保持打开
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,(Open_State==1)?Open_TP_Color:Close_Color);
		}
		
		if(TP_Open_Close_State[i][1]==3)														//触板坏 =3：坏;
		{
			Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Bad_Color);		//右边触板坏颜色显示
		}
		else if(TP_Open_Close_State[i][1]==4)														//触板没有安装 =4 2025-1-6;																											//触板好
		{
			Display_TP_State(TPsx[1],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//右边触板没有安装颜色显示  2025-1-6
		}
		else {														//触板好
			TP_Open_Close_State[i][1]=Open_State;		//2026-05-17 尊重 Open_State
			Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,(Open_State==1)?Open_TP_Color:Close_Color);
		}
				

		if(Startbox_Open_Close_State[i][0]==3)														//出发台坏 =3：坏;
		{
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Bad_Color);			//左边出发台坏颜色显示
		}
		else if(Startbox_Open_Close_State[i][0]==4)										//2026-05-17 未装 =4 保留
		{
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,UnInstall_Color);
		}
		else {														//出发台好
			Startbox_Open_Close_State[i][0]=Open_State;	//2026-05-17 尊重 Open_State
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,(Open_State==1)?Open_SB_Color:Close_Color);
		}
		
		if(Startbox_Open_Close_State[i][1]==3)														//出发台坏 =3：坏;
		{
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Bad_Color);		//右边出发台坏颜色显示
		}
		else if(Startbox_Open_Close_State[i][1]==4)										//2026-05-17 未装 =4 保留
		{
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,UnInstall_Color);
		}
		else {														//出发台好
			Startbox_Open_Close_State[i][1]=Open_State;	//2026-05-17 尊重 Open_State
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,(Open_State==1)?Open_SB_Color:Close_Color);
		}
		
		if(MB_Open_Close_State[0][i]==3)														//第0行左边盲表坏 =3：坏;
		{
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateGroup(0,i,Bad_Color,lcd_Dis);		//左边盲表坏颜色显示
		}
		else if(MB_Open_Close_State[0][i]==4)										//2026-05-17 未装 =4 保留
		{
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateGroup(0,i,UnInstall_Color,lcd_Dis);
		}
		else {														//盲表好
			if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=Open_State;	//2026-05-17 尊重 Open_State
			if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=Open_State;
			if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=Open_State;
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateGroup(0,i,(Open_State==1)?Open_MB_Color:Close_Color,lcd_Dis);
		}
		if(MB_Open_Close_State[0][i+10]==3)														//盲表坏 =3：坏;
		{
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateGroup(1,j-1,Bad_Color,lcd_Dis);			//右边盲表坏颜色显示
		}
		else if(MB_Open_Close_State[0][i+10]==4)										//2026-05-17 未装 =4 保留
		{
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateGroup(1,j-1,UnInstall_Color,lcd_Dis);
		}
		else {														//盲表好
			if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=Open_State;	//2026-05-17 尊重 Open_State
			if(MB_Open_Close_State[1][i+10]!=3 && MB_Open_Close_State[1][i+10]!=4) MB_Open_Close_State[1][i+10]=Open_State;
			if(MB_Open_Close_State[2][i+10]!=3 && MB_Open_Close_State[2][i+10]!=4) MB_Open_Close_State[2][i+10]=Open_State;
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateGroup(1,j-1,(Open_State==1)?Open_MB_Color:Close_Color,lcd_Dis);
		}
		
		CloseLaneState[i]=2 ;					//关闭道次状态=2：打开；=3：关闭
		display_swim_dir(dir_posx,i,CloseLaneState[i],0);			//open
		

		Lane_Display_MSecond[i][0]=0;
		Lane_Display_MSecond[i][1]=0;
		
		laps[i][0]=LAll_Lap;
		laps[i][1]=RAll_Lap;					//2024-11-24

		LLaps_diaplay(i);
					
		RLaps_diaplay(i);					//2024-11-21
		
//		sprintf((char*)lcd_Dis,"           ");
		sprintf((char*)lcd_Dis,"          ");			//少一个空格
//		LCD_ShowString(Final_timer_posx,Final_timer_posy+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
		LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
		LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
								
		LCD_ShowString(Placex,Final_timer_posy+j*line_height1,200,32,32,"  ");		//清显示名次 
	}
}

void TP_Ready_Init(void)		//准备就绪，等待发令
{
	u16 i,j,k;
	
	timer_bit=0;				//计时位=0：不计时；
	Testing_bit=0;							//正在进行测试位 =1：正在测试； =0：停止测试   2023-8-5

	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN); 

	Timer_Reset((1-timer_bit));			//计时器复位   2024-1-25

	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN);
	hour=0;
	minute=0;
	second=0;
	msecond=0;

	Start_hour=hour;   				//2024-8-31
	Start_minute=minute;
	Start_second=second;
	Start_msecond=msecond;

	//2026-05-11 复位"出发抢跳计时器"与发令时刻、每道运动员出发台触发缓存
	PreStart_minute=0;
	PreStart_second=0;
	PreStart_msecond=0;
	Gun_minute=0;
	Gun_second=0;
	Gun_msecond=0;
	PostGun_OpenWait_Time=0;
	GunFired_PostOpenDoneBit=0;
	for(i=0;i<10;i++)
	{
		if (CloseLaneState[i] != 2) continue;  // 2026-06-04 关闭泳道不被 Ready 重置 (= 工作状态锁定)
		for(j=0;j<2;j++)
		{
			LaneStart_minute[i][j]=0;
			LaneStart_second[i][j]=0;
			LaneStart_msecond[i][j]=0;
			LaneStart_Valid[i][j]=0;
			LaneStart_Computed[i][j]=0;
		}
	}

	Ready_timer_bit=1;				//准备就绪计时器开始计时，计时位=0：不计时；=1：开始计时 2024-8-31

	display_rollingtime();		//显示滚动时间		2023-7-11
	
	OnSendSWData(Timer_Ready_Command+0x10,0,0);  //发送准备就绪命令给上位机（计算机）  2024-7-15
	Send_Bit=1;
		
	Timer_State_LED(0); 
	
	Exchange_StartFinalPlace();    //交换发令点  2024-11-27	

	//2024-3-28
	for(i=0;i<10; i++)
	{
		for(j=0;j<2;j++)
		{
			Lane_Display_State[i][j]=0;	
			Lane_Display_MSecond[i][j]=0;	
			TP_Display_State[i][j]=0;				//2024-3-28
		}
	}

	
	//2026-05-27 三维 [20道][3块][4字段] + 清按下 bitmap
	for(i=0;i<20;i++)
	{
		for(k=0;k<3;k++)
			for(j=0;j<4;j++) MB_Result[i][k][j]=0;
		MB_Pressed_Bitmap[i]=0;
	}
	
	for(i=0;i<40*2;i++)
	{
		Lap_Place[i]=0;
	}		
	
	for(i=0;i<10;i++)
	{
		if(CloseLaneState[i]==2)		//关闭道次状态  =2：打开；=3：关闭
		{
			j=i+1;
			if(TP_Open_Close_State[i][0]==3)														//触板坏 =3：坏;
			{
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Bad_Color);			//左边触板坏颜色显示
			}
			else if(TP_Open_Close_State[i][0]==4)														//触板没有安装 =4 2025-1-6;																											//触板好
			{
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//左边触板没有安装颜色显示  2025-1-6
			}
			else {																											//触板好
				TP_Open_Close_State[i][0]=0;															//左边触板关闭，运动员触板无效
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);		//左边触板好颜色显示
			}
			
			if(TP_Open_Close_State[i][1]==3)														//触板坏 =3：坏;
			{
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Bad_Color);		//右边触板坏颜色显示
			}
			else if(TP_Open_Close_State[i][1]==4)														//触板没有安装 =4 2025-1-6;																											//触板好
			{
				Display_TP_State(TPsx[1],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//右边触板没有安装颜色显示  2025-1-6
			}
			else {																											//触板好
				TP_Open_Close_State[i][1]=0;															//右边触板关闭，运动员触板无效
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Close_Color);	//右边触板好颜色显示
			}
		
			//2026-05-31 保留 ==3 (Bad), ==4 (UnInstall), 其他状态重置 0 (Close); 用 Auto helper 派生 3 圆颜色
			if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;
			if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=0;
			if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=0;
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateAuto(0, (u8)i, 0, lcd_Dis);
			Display_MB_StateAuto(0, (u8)i, 1, lcd_Dis);
			Display_MB_StateAuto(0, (u8)i, 2, lcd_Dis);
			
			//2026-05-31 同左侧, 保留 Bad/UnInstall, 其他重置 0
			if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=0;
			if(MB_Open_Close_State[1][i+10]!=3 && MB_Open_Close_State[1][i+10]!=4) MB_Open_Close_State[1][i+10]=0;
			if(MB_Open_Close_State[2][i+10]!=3 && MB_Open_Close_State[2][i+10]!=4) MB_Open_Close_State[2][i+10]=0;
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateAuto(1, (u8)i, 0, lcd_Dis);
			Display_MB_StateAuto(1, (u8)i, 1, lcd_Dis);
			Display_MB_StateAuto(1, (u8)i, 2, lcd_Dis);

/*
			if(Startbox_Open_Close_State[i][0]==3)														//出发台坏 =3：坏;
			{
				Display_Startbox_State(Startboxsx[0],Final_Startboxsy+j*btnhy+8,24,24,Bad_Color);			//左边出发台坏颜色显示
			}
			else {																											//出发台好
				if(((StartFinalPlace&0x03)==0x00)||((StartFinalPlace&0x03)==0x03))  //=0,3：发令在左边  2024-6-18
				{
					Startbox_Open_Close_State[i][0]=1;																							//左边出发台打开  2023-8-6
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//左边出发台显示
				}
				else {		 //发令在右边，左边出发台关闭
					Startbox_Open_Close_State[i][0]=0;								//
					Display_Startbox_State(Middle_Startboxsx,Middle_Startboxsy+j*btnhy+8,24,24,Close_Color);//左边出发台关闭 2024-11-27
				}
			}
	*/
/*			
		if(Startbox_Open_Close_State[i][1]==3)														//出发台坏 =3：坏;
		{
			Display_Startbox_State(Startboxsx[1],Middle_Startboxsy+j*btnhy+8,24,24,Bad_Color);		//右边出发台坏颜色显示
		}
		else {																											//出发台好
			if(((StartFinalPlace&0x03)==0x00)||((StartFinalPlace&0x03)==0x03))  //=0,3：发令在左边，右边出发台关闭 2024-6-19
			{
			 		Startbox_Open_Close_State[i][1]=0;						//右边出发台关闭
			 		Display_Startbox_State(Middle_Startboxsx,Middle_Startboxsy+j*btnhy+8,24,24,Close_Color);//右边出发台显示
			}
			else {		 //发令在右边，右边出发台打开  2023-8-19
					Startbox_Open_Close_State[i][1]=1;																							//右边出发台打开  2023-8-9
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//Close_Color);		//右边出发台显示打开状态
			}
		}
		*/
//		display_swim_dir(dir_posx,i,CloseLaneState[i],0);			//open
			
	//	if(((StartFinalPlace&0x03)==0x00)||((StartFinalPlace&0x03)==0x03))  //=0,3：发令在左边，右边出发台关闭 2024-6-19
		if(Start_Dir==0)  //=0,3：发令在左边，右边出发台关闭 2024-6-19
		{
//			Start_Dir=0;	//发令点在左边，从左到右 2023-12-1
			display_swim_dir(dir_posx,i,Start_Dir,0);			//open  从左到右 2024-12-1  
			if(Startbox_Open_Close_State[i][0]==3)														//出发台坏 =3：坏;
			{
				Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Bad_Color);		//左边出发台坏颜色显示
			}
			else {																											//出发台好
					Lane_Display_State[i][0]=1;																				//此道显示成绩，显示状态为1;  2024-12-3
					Lane_Display_State[i][1]=0;																				//此道显示成绩，显示状态为1;
					Startbox_Open_Close_State[i][0]=1;										//左边出发台打开  2023-8-9
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//Close_Color);		//左边出发台显示打开状态
			}
		}
		else {
	//		Start_Dir=1;	//发令点在右边，从右到左 2023-12-1

			display_swim_dir(dir_posx,i,Start_Dir,0);			//open  从右到左 2024-12-1
			if(Startbox_Open_Close_State[i][1]==3)														//右边出发台坏 =3：坏;
			{
				Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Bad_Color);		//右边出发台坏颜色显示
			}
			else {																											//出发台好
					Lane_Display_State[i][0]=0;																				//此道显示成绩，显示状态为1;  2024-12-3
					Lane_Display_State[i][1]=1;																				//此道显示成绩，显示状态为1;
					Startbox_Open_Close_State[i][1]=1;										//右边出发台打开  2023-8-9
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//Close_Color);		//右边出发台显示打开状态
			}
		}
		
		Lane_Display_MSecond[i][0]=0;
		Lane_Display_MSecond[i][1]=0;

		laps[i][0]=LAll_Lap;
		LLaps_diaplay(i);
					
		laps[i][1]=RAll_Lap;					//2024-11-21
		RLaps_diaplay(i);					//2024-11-21
		
	//		sprintf((char*)lcd_Dis,"           ");
			sprintf((char*)lcd_Dis,"          ");				//2023-11-3
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//显示LCD ID	  
								
			LCD_ShowString(Placex,Timer_posy[1]+j*line_height1,200,32,32,"  ");		//清显示名次 
		}
	}
	display_rollingtime();		//显示滚动时间		2023-7-11

}


void Test_Button(void)
{
	u8 i;
	
	timer_bit=0;
	Testing_bit=1;							//正在进行测试位 =1：正在测试； =0：停止测试   2023-8-5
					
	Testbtn->bcfucolor=RED;		//红色  2024-12-24
	Testbtn->caption=Test_btncaption_tbl[Testing_bit][gui_phy.language]; 		//显示”正在测试“
	btn_draw(Testbtn);		//画按钮
				
	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,YELLOW); 

	hour=0;
	minute=0;
	second=0;
	msecond=0;

	Start_hour=hour;   				//2024-8-31
	Start_minute=minute;
	Start_second=second;
	Start_msecond=msecond;


	display_rollingtime();		//显示滚动时间		2023-7-11
				
	Send_Bit=1;
	
	for(i=0;i<10;i++)
	{
		sprintf((char*)lcd_Dis,"L%d",(i));
		TP_Open_Close_State[i][0]=1;									//左边触板打开
		TP_Open_Close_State[i][1]=1;									//右边触板打开
		
								
		if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//盲表打开  2024-9-1
		if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//盲表打开
		if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//盲表打开
		if(MB_Open_Close_State[0][(1-0)*10+i]!=3 && MB_Open_Close_State[0][(1-0)*10+i]!=4) MB_Open_Close_State[0][(1-0)*10+i]=1;																			//盲表打开  2024-9-1
		if(MB_Open_Close_State[1][(1-0)*10+i]!=3 && MB_Open_Close_State[1][(1-0)*10+i]!=4) MB_Open_Close_State[1][(1-0)*10+i]=1;																			//盲表打开
		if(MB_Open_Close_State[2][(1-0)*10+i]!=3 && MB_Open_Close_State[2][(1-0)*10+i]!=4) MB_Open_Close_State[2][(1-0)*10+i]=1;																			//盲表打开
		
		Startbox_Open_Close_State[i][0]=1;																							//左边出发台打开  2024-9-1
		Startbox_Open_Close_State[i][1]=1;																							//右边出发台打开  2024-9-1
		
		
		Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		

		Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//左边出发台显示
			
		Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//左边触板示意图显示
		
		Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);//右边出发台显示
			
		Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//右边触板示意图显示
		
		sprintf((char*)lcd_Dis,"R%d",(i));
		Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);		
	}
	timer_bit=1;
	Timer_Reset((1-timer_bit));			//计时器复位   2024-1-25
	Ready_timer_bit=1;				//准备就绪计时器开始计时，计时位=0：不计时；=1：开始计时 2024-9-1
	
}

void LLaps_diaplay(u8 Lane)		//显示左边的剩余圈数  2024-11-21
{
	// 2026-06-03 always-open 模式清屏 LCD 圈数 (= 用空格覆盖, 防初始化残留)
	if (HardwareAlwaysOpenBit) { sprintf((char*)lcd_Dis,"  "); Lane=Lane+1; LCD_ShowString(Lapsx[0],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis); return; }
	if(CloseLaneState[Lane]==2)		//关闭道次状态=2：打开；=3：关闭
	{
		sprintf((char*)lcd_Dis,"%2d",laps[Lane][0]);
	}
	else 		sprintf((char*)lcd_Dis,"  ");
	Lane=Lane+1;
	LCD_ShowString(Lapsx[0],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis);		//显示左边游的趟数	2024-11-21  
}

void RLaps_diaplay(u8 Lane)			//显示右边的剩余圈数  2024-11-21
{
	// 2026-06-03 always-open 模式清屏 LCD 圈数
	if (HardwareAlwaysOpenBit) { sprintf((char*)lcd_Dis,"  "); Lane=Lane+1; LCD_ShowString(Lapsx[1],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis); return; }
	if(CloseLaneState[Lane]==2)		//关闭道次状态=2：打开；=3：关闭
	{
		sprintf((char*)lcd_Dis,"%2d",laps[Lane][1]);
	}
	else 		sprintf((char*)lcd_Dis,"  ");
	Lane=Lane+1;
	LCD_ShowString(Lapsx[1],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis);		//显示右边游的趟数	2024-11-21  
}

void Place_display(u8 Lane)
{
	// 2026-06-03 always-open 模式清屏名次
	if (HardwareAlwaysOpenBit) { sprintf((char*)lcd_Dis,"  "); Lane=Lane+1; LCD_ShowString(Placex,Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis); return; }
	sprintf((char*)lcd_Dis,"%2d",Lap_Place[laps[Lane][0]+laps[Lane][1]]);
	Lane=Lane+1;
	LCD_ShowString(Placex,Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis);		//显示名次 
}

void		display_rollingtime(void)	//Rolling-time display 2023-7-27 / 2026-05-16
{
	//2026-05-16 Rolling time display:
	//   * Right edge aligned to manual TP0 button right edge (= btndsx + btnw);
	//     text X is computed dynamically each call (right-aligned).
	//   * Font 32, color gold (0xFEA0), bold via two-pass +1px offset.
	//   * Precision: 0.1s (msecond/100); 0.01s digit hidden;
	//     leading zeros suppressed on the left:
	//       hour > 0    -> H:MM:SS.d
	//       minute > 0  -> M:SS.d   (e.g. 1:05.8)
	//       else        -> S.d      (e.g. 5.8)
	//   * Working-state circle (RunningTime_x0-32) and the two start-place
	//     S circles (StartFinalPlace_x0 / +300) shifted +162 px in init.
	{
		char buf[32];
		u16  len;
		u16  saved_back = BACK_COLOR;
		u16  saved_pt   = POINT_COLOR;
		u16  right_edge = btndsx + btnw;
		u16  text_x;

		if (hour > 0)
			sprintf(buf, "%d:%02d:%02d.%d ", hour, minute, second, msecond/100);
		else if (minute > 0)
			sprintf(buf, "%d:%02d.%d ",            minute, second, msecond/100);
		else
			sprintf(buf, "%d.%d ",                         second, msecond/100);

		len    = strlen(buf);
		text_x = right_edge - len * 16;

		gui_fill_rectangle(right_edge - 200, RunningTime_y0 - 4, 200, 40, BLACK);

		BACK_COLOR  = BLACK;
		POINT_COLOR = 0xFEA0;
		LCD_ShowString(text_x,   RunningTime_y0, 200, 32, 32, (u8*)buf);
		LCD_ShowString(text_x+1, RunningTime_y0, 200, 32, 32, (u8*)buf);

		BACK_COLOR  = saved_back;
		POINT_COLOR = saved_pt;
	}
}



//泳池两边安装触板，处理游泳方向  2024-11-28 
void  Process_Display_SiwmDir(void)
{
		u16 i,j;
		// 2026-06-03 直通模式不执行周期显示刷新
		if (HardwareAlwaysOpenBit) return;
/*
	for(i=0;i<10;i++)
		{
			if(CloseLaneState[i]==2)		//关闭道次状态  =2：打开；=3：关闭
			{
				if((laps[i][0]+laps[i][1])!=0){
		//			for(j=0;j<2;j++)
					{
												
						//j==0  2024-11-28
						if(Lane_Display_State[i][0]==1)
						{
							Lane_Display_MSecond[i][0]++;	
							if((Lane_Display_MSecond[i][0]==Result_Display_Time)||(Lane_Display_MSecond[i][0]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
					//				LCD_ShowString(Final_timer_posx+0*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//清显示  
					//				LCD_ShowString(Final_timer_posx+FinalPlace*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Final_timer_posx+FinalPlace*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Placex,Final_timer_posy+(i+1)*line_height1,200,32,32,"  ");		//清显示名次 
							}
							
							if(Lane_Display_MSecond[i][0]<=Display_Dir_Max_Time)	
							{								
								if((Lane_Display_MSecond[i][0]%20)==0)
								{
									dir_len=Lane_Display_MSecond[i][0]/20+1;
		//							display_swim_dir(dir_posx,i,1,dir_len);	
									display_swim_dir(dir_posx,i,1,dir_len);	
								}
							}
							
							if((TP_Open_Close_State[i][1]!=1)||(MB_Open_Close_State[0][10+i]!=1)||(MB_Open_Close_State[1][10+i]!=1)||(MB_Open_Close_State[2][10+i]!=1))
							{
								if(Lane_Display_MSecond[i][0]>=Close_Time)
								{
									if(TP_Open_Close_State[i][1]==0)						//触板没坏 =3：坏;
									{
										TP_Open_Close_State[i][1]=1;									//触板打开，运动员可以触板有效
											
										if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
										{
											if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
												Startbox_Open_Close_State[i][0]=1;								//左边终点出发台打开 =1
										}
										
										if(0==1)	
										{
											Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//终点触板示意图显示
		
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
												Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//左边 终点出发台打开状态显示  2024-11-24
											}
										}
										else Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//右边触板示意图显示
									}
									
									if(MB_Open_Close_State[0][10+i]==0)			//盲表没坏 =3：坏;
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=1;																			//盲表打开  2023-11-1
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=1;																			//盲表打开
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=1;																			//盲表打开
		
										if(0==1) Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		
										else Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);	
									}										
								}
							}
						}
						
						//j==1
						if(Lane_Display_State[i][1]==1)
						{
							Lane_Display_MSecond[i][1]++;	
							if((Lane_Display_MSecond[i][1]==Result_Display_Time)||(Lane_Display_MSecond[i][1]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
					//				LCD_ShowString(Final_timer_posx+1*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//清显示  
									LCD_ShowString(Final_timer_posx+(1)*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Placex,Final_timer_posy+(i+1)*line_height1,200,32,32,"  ");		//清显示名次 
							}
							
							if(Lane_Display_MSecond[i][1]<=Display_Dir_Max_Time)	
							{								
								if((Lane_Display_MSecond[i][1]%20)==0)
								{
									dir_len=Lane_Display_MSecond[i][1]/20+1;
	//								display_swim_dir(dir_posx,i,FinalPlace,dir_len);	
									display_swim_dir(dir_posx,i,0,dir_len);	
								}
							}
							
							if((TP_Open_Close_State[i][0]!=1)||(MB_Open_Close_State[0][i]!=1)||(MB_Open_Close_State[1][i]!=1)||(MB_Open_Close_State[2][i]!=1))
							{
								if(Lane_Display_MSecond[i][1]>=Close_Time)
								{
									if(TP_Open_Close_State[i][0]==0)						//触板没坏 =3：坏;
									{
										TP_Open_Close_State[i][0]=1;									//触板打开，运动员可以触板有效
											
										if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
										{
											if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
												Startbox_Open_Close_State[i][0]=1;								//左边终点出发台打开 =1
										}
										
										if(1==1)	
										{
											Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//终点触板示意图显示
		
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2024-11-24
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
												Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//左边 终点出发台打开状态显示  2024-11-24
											}
										}
										else Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//右边触板示意图显示
									}
									
									if(MB_Open_Close_State[0][i]==0)			//盲表没坏 =3：坏;
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//盲表打开  2023-10
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//盲表打开
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//盲表打开
		
										if(1==1) Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		
										else Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);	
									}										
								}
							}
						}
					}
				}
			}
		}
		*/
				
		for(i=0;i<10;i++)
		{
			if(CloseLaneState[i]==2)		//关闭道次状态  =2：打开；=3：关闭
			{
				if((laps[i][0]+laps[i][1])!=0){
				//	for(j=0;j<2;j++)
					{
						//j=0;
						if(Lane_Display_State[i][0]==1)
						{
							Lane_Display_MSecond[i][0]++;	
							if((Lane_Display_MSecond[i][0]==Result_Display_Time)||(Lane_Display_MSecond[i][0]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
					//				LCD_ShowString(Final_timer_posx+0*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//清显示  
									LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[0]+(i+1)*line_height1,200,32,32,"  ");		//清显示名次 
							}
							
							if(Lane_Display_MSecond[i][0]<=Display_Dir_Max_Time)	
							{								
								if((Lane_Display_MSecond[i][0]%20)==0)
								{
									dir_len=Lane_Display_MSecond[i][0]/20+1;
									display_swim_dir(dir_posx,i,0,dir_len);	
								}
							}
							
							if((TP_Open_Close_State[i][1]!=1)||(MB_Open_Close_State[0][10+i]!=1)||(MB_Open_Close_State[1][10+i]!=1)||(MB_Open_Close_State[2][10+i]!=1))
							{
								if(Lane_Display_MSecond[i][0]>=Close_Time)
								{
									if(TP_Open_Close_State[i][1]==0)						//触板没坏 =3：坏;
									{
										TP_Open_Close_State[i][1]=1;									//触板打开，运动员可以触板有效
										Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//右边触板示意图显示
																				
									//	if(0==1)	
					/*
										if(!HardwareAlwaysOpenBit && (RelayBit==1)&&(Start_Dir==0)) //终点在左边  2024-12-1
										{
											Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//终点触板示意图显示
		
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-11-24
												{
													Startbox_Open_Close_State[i][0]=1;								//左边终点出发台打开 =1
													Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//左边 终点出发台打开状态显示  2024-11-24
												}	
										}
										*/
																				
										if(!HardwareAlwaysOpenBit && (RelayBit==1)) //终点在右边  2024-12-1
										{
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][1]!=3)&&(laps[i][0]!=0))			//2026-06-09 撤回到对侧: 左触板路径 → 右 SB (= 棒K+2 起跳)
											{
												if(((RelayLaps==2)&&((laps[i][0]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-12-1
												{
													Startbox_Open_Close_State[i][1]=1;  // 2026-06-09 撤回对侧: 左触板 → 右 SB (= 棒K+2 起跳, PC L6306 同语义)
													Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);//2026-06-09 撤回对侧右

												}	
											}
										}

									}
									
									if(MB_Open_Close_State[0][10+i]==0)			//盲表没坏 =3：坏;
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=1;																			//盲表打开  2023-11-1
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=1;																			//盲表打开
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=1;																			//盲表打开
		
										Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);		
									}	
									
									if(MB_Open_Close_State[0][i]==1)			//盲表没坏 =3：坏;  若另一边盲表还是打开的，将其关闭
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;																			//盲表关闭  2024-12-17
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=0;																			//盲表关闭  2024-12-17
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=0;																			//盲表关闭  2024-12-17
		
										Display_MB_StateGroup(0,i,Close_Color,lcd_Dis);		
									}										


									
								}
							}
						}
						
						//j=1;
						if(Lane_Display_State[i][1]==1)
						{
							Lane_Display_MSecond[i][1]++;	
							if((Lane_Display_MSecond[i][1]==Result_Display_Time)||(Lane_Display_MSecond[i][1]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
					//				LCD_ShowString(Final_timer_posx+1*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//清显示  
									LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[1]+(i+1)*line_height1,200,32,32,"  ");		//清显示名次 
							}
							
							if(Lane_Display_MSecond[i][1]<=Display_Dir_Max_Time)	
							{								
								if((Lane_Display_MSecond[i][1]%20)==0)
								{
									dir_len=Lane_Display_MSecond[i][1]/20+1;
									display_swim_dir(dir_posx,i,1,dir_len);	
								}
							}
							
							if((TP_Open_Close_State[i][0]!=1)||(MB_Open_Close_State[0][i]!=1)||(MB_Open_Close_State[1][i]!=1)||(MB_Open_Close_State[2][i]!=1))
							{
								if(Lane_Display_MSecond[i][1]>=Close_Time)
								{
									if(TP_Open_Close_State[i][0]==0)						//触板没坏 =3：坏;
									{
										TP_Open_Close_State[i][0]=1;									//触板打开，运动员可以触板有效
										Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//终点触板示意图显示

										
	//									if(1==1)	
										if(!HardwareAlwaysOpenBit && (RelayBit==1)) //终点在左边  2024-12-1
										{
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//2026-06-09 撤回对侧: 右触板路径 → 左 SB (= 棒K+2 起跳)
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-12-1
												{
													Startbox_Open_Close_State[i][0]=1;  // 2026-06-09 撤回对侧: 右触板 → 左 SB (= 棒K+2 起跳, PC L6306 同语义)
													Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//2026-06-09 撤回对侧左

												}	
											}
										}
		//								else Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//右边触板示意图显示

						//				Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//左边触板示意图显示
									}
									
									if(MB_Open_Close_State[0][i]==0)			//盲表没坏 =3：坏;
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//盲表打开  2023-11-1
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//盲表打开
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//盲表打开
		
										Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		
									}	
								
									if(MB_Open_Close_State[0][10+i]==1)			//盲表没坏 =3：坏;  若另一边盲表还是打开的，将其关闭
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=0;																			//盲表关闭  2024-12-17
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=0;																			//盲表关闭  2024-12-17
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=0;																			//盲表关闭  2024-12-17
		
										Display_MB_StateGroup(1,i,Close_Color,lcd_Dis);		
									}										
									
								}
							}
						}						
					}
				}
			}
		}

}


//泳池单边安装触板，处理游泳方向  2025-1-16 
void  Single_Process_Display_SiwmDir(void)
{
		u16 i,j;
				
		// 2026-06-03 直通模式不执行周期显示刷新
		if (HardwareAlwaysOpenBit) return;
		for(i=0;i<10;i++)
		{
			if(CloseLaneState[i]==2)		//关闭道次状态  =2：打开；=3：关闭
			{
					if(FinalPlace==0)	//=0:终点在屏幕左边； =1：终点在屏幕右边。
					{
						//触板安装在左边 2025-1-16;
					 if((laps[i][0]!=0))
						if(Lane_Display_State[i][0]==1)
						{
							Lane_Display_MSecond[i][0]++;	
							if((Lane_Display_MSecond[i][0]==Result_Display_Time)||(Lane_Display_MSecond[i][0]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
									LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[0]+(i+1)*line_height1,200,32,32,"  ");		//清显示名次 
							}
							
							if(Lane_Display_MSecond[i][0]<=Display_Dir_Max_Time)	
							{								
								if((Lane_Display_MSecond[i][0]%20)==0)
								{
									dir_len=Lane_Display_MSecond[i][0]/20+1;
									display_swim_dir(dir_posx,i,0,dir_len);	
								}
							}
							else {
								if((Lane_Display_MSecond[i][0]%20)==0)
								{
									dir_len=(Lane_Display_MSecond[i][0]-Display_Dir_Max_Time)/20+1;
									display_swim_dir(dir_posx,i,1,dir_len);	
								}
							}
							
	//						if((TP_Open_Close_State[i][1]!=1)||(MB_Open_Close_State[0][10+i]!=1)||(MB_Open_Close_State[1][10+i]!=1)||(MB_Open_Close_State[2][10+i]!=1))
							{
								if(Lane_Display_MSecond[i][0]>=All_Close_Time)   //全泳道关闭时间 2025-1-16
								{
									if(TP_Open_Close_State[i][1]==0)						//触板没坏 =3：坏;
									{
										TP_Open_Close_State[i][1]=1;									//触板打开，运动员可以触板有效
										Display_TP_State(TPsx[1],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//左边触板示意图显示
																				
																				
										if((RelayBit==1)) //终点在左边  2024-12-1
										{
											if((Startbox_Open_Close_State[i][1]!=3)&&(laps[i][0]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2025-1-16
											{
												if(((RelayLaps==2)&&((laps[i][0]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2025-1-16
												{
													Startbox_Open_Close_State[i][1]=1;								//左边终点出发台打开 =1
													Display_Startbox_State(Startboxsx[1],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//左边 终点出发台打开状态显示  2025-1-16

												}	
											}
										}
									}
									if(MB_Open_Close_State[0][10+i]==0)			//盲表没坏 =3：坏;
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=1;																			//盲表打开  2025-1-16
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=1;																			//盲表打开
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=1;																			//盲表打开
		
										Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);		
									}	
								}
							}
						}
					}
					else {
						//触板安装在右边 2025-1-16;
					if((laps[i][1]!=0))
						if(Lane_Display_State[i][1]==1)
						{
							Lane_Display_MSecond[i][1]++;	
							if((Lane_Display_MSecond[i][1]==Result_Display_Time)||(Lane_Display_MSecond[i][1]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
									LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i+1)*line_height1,200,32,32,"          ");		//清显示 少一个空格 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[1]+(i+1)*line_height1,200,32,32,"  ");		//清显示名次 
							}
							
							if(Lane_Display_MSecond[i][1]<=Display_Dir_Max_Time)	
							{								
								if((Lane_Display_MSecond[i][1]%20)==0)
								{
									dir_len=Lane_Display_MSecond[i][1]/20+1;
									display_swim_dir(dir_posx,i,1,dir_len);	
								}
							}
							
			//				if((TP_Open_Close_State[i][0]!=1)||(MB_Open_Close_State[0][i]!=1)||(MB_Open_Close_State[1][i]!=1)||(MB_Open_Close_State[2][i]!=1))
							{
								if(Lane_Display_MSecond[i][1]>=All_Close_Time)   //全泳道关闭时间 2025-1-16
								{
									if(TP_Open_Close_State[i][0]==0)						//触板没坏 =3：坏;
									{
										TP_Open_Close_State[i][0]=1;									//触板打开，运动员可以触板有效
										Display_TP_State(TPsx[0],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//终点触板示意图显示

										if((RelayBit==1)) //终点在右边  2025-1-16
										{
											if((Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//接力标志位=1 并且 出发台不是坏的（=3）并且 不是最后一圈 2025-1-16
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m接力 和4*200米接力 时 打开起点出发台  2024-12-1
												{
													Startbox_Open_Close_State[i][0]=1;								//右边终点出发台打开 =1
													Display_Startbox_State(Startboxsx[0],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);//右边 终点出发台打开状态显示  2025-1-16

												}	
											}
										}
									}
									
									if(MB_Open_Close_State[0][i]==0)			//盲表没坏 =3：坏;
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//盲表打开  2025-1-16
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//盲表打开
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//盲表打开
		
										Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		
									}	
								}
							}
						}
					}						
			}
		}
}



void OnTCP_RS232_Receive_Data_Proc()   //TCP 或 RS232 接收数据预处理程序   2023-7-17
{
	u16 i,j,k;
	j=0;	
		if((TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)&&((TCPIP_CommandBuf[0]!=SOH)||(TCPIP_CommandBuf[1]!='S')||(TCPIP_CommandBuf[TxRx_Data_Length-1]!=EOT)))
		{
			j=1;
			while ((TCPIP_CommandBuf[j]!=SOH)||(TCPIP_CommandBuf[j+TxRx_Data_Length-1]!=EOT)) {
					j++;
					if(j>=(TCPIP_Rec_Char_Ptr-(TxRx_Data_Length-1))) 	break;
			}
							
			for( k=j;k<TCPIP_Rec_Char_Ptr;k++)
			{
				TCPIP_CommandBuf[k-j]=TCPIP_CommandBuf[k];
			}
			TCPIP_Rec_Char_Ptr=TCPIP_Rec_Char_Ptr-j;
		}
						
		if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
		{
			do {
				for(i=0;i<TCPIP_Rec_Char_Ptr;i++)
				{
					RXD_Data_Buffer[i]=TCPIP_CommandBuf[i];
				}
								
				OnTCP_RS232_Receive_Command_Proc();		//RS232 ????????
					
				if(TCPIP_Rec_Char_Ptr>TxRx_Data_Length)
				{
					for(i=TxRx_Data_Length;i<TCPIP_Rec_Char_Ptr;i++)
					{
						TCPIP_CommandBuf[i-TxRx_Data_Length]=TCPIP_CommandBuf[i];
					}
				}
				TCPIP_Rec_Char_Ptr=TCPIP_Rec_Char_Ptr-TxRx_Data_Length;
				if((TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)&&((TCPIP_CommandBuf[0]!=SOH)||(TCPIP_CommandBuf[1]!='S')||(TCPIP_CommandBuf[TxRx_Data_Length-1]!=EOT)))
				{
					j=1;
					while ((TCPIP_CommandBuf[j]!=SOH)||(TCPIP_CommandBuf[j+TxRx_Data_Length-1]!=EOT)) {
						j++;
						if(j>=(TCPIP_Rec_Char_Ptr-(TxRx_Data_Length-1))) 	break;
					}
					for( k=j;k<TCPIP_Rec_Char_Ptr;k++)
					{
						TCPIP_CommandBuf[k-j]=TCPIP_CommandBuf[k];
					}
					TCPIP_Rec_Char_Ptr=TCPIP_Rec_Char_Ptr-j;
				}
			} while (TCPIP_Rec_Char_Ptr>=TxRx_Data_Length);
		}
}

void OnTCP_RS232_Receive_Command_Proc()   //TCP 或 RS232 接收命令处理程序
{
	u8	Receive_Button_Command_Value;
	u8  i;
	u8	Receive_Command_buf;
	u32 Command_buf;
	
	if((RXD_Data_Buffer[0]==SOH)&&(RXD_Data_Buffer[1]=='S')&&(RXD_Data_Buffer[TxRx_Data_Length-1]==EOT))
	{
/*
				minute=RXD_Data_Buffer[5];			//2016-3-7 modify
				second=RXD_Data_Buffer[6];
				msecond=RXD_Data_Buffer[7];
				msecond1=RXD_Data_Buffer[8]%16;	//?????
				hour=RXD_Data_Buffer[8]/16;		//??????
*/
				if(RXD_Data_Buffer[2]!=0x7f)	
				{
					Receive_Button_Command_Value=RXD_Data_Buffer[2]-0x10;
					switch(Receive_Button_Command_Value)
					{
							case Start_Command:   //计时器开始计时  2023-7-17
								StartTiming();

						
							break;
							
							case Test_Command:   //计时器进行测试  2023-8-4
								Test_Button();
						
							break;

							case Timer_Ready_Command:   //计时器准备就绪  2023-7-17

								TP_Ready_Init();

					
							break;


							case Timer_Reset_Command:   //计时器复位  2023-7-17
								//2026-05-25 修复 #2 (按 PDF 硬件待改动需求 v2026.05.25)：
								//   收到 0x20 立即停止"非零 0x7F"——case 进入就清 timer_bit/Ready_timer_bit 和 RTC 字段,
								//   并立即发一帧 0x7F=0 给 PC, 作为"复位确认"。
								//   原 Reset_Timer 内有相同动作, 此处提前做是为了防止 0x20 到 Reset_Timer 之间
								//   100Hz 中断窗口仍发非零 0x7F (避免 PC 端 _runningTime 被 1-2 秒幻觉值覆盖)。
								timer_bit = 0;
								Ready_timer_bit = 0;
								hour = 0; minute = 0; second = 0; msecond = 0;
								OnSendSWData(0x7f, 0, 0);
								Send_Bit = 1;

								Reset_Timer();
								
								break;

							case Set_MatchEvent:   		//设置比赛项目趟数、参赛运动员道次号（缺道否）  2023-11-13
								//2026-05-13(2) 按 通讯协议变更说明_v2026.05.13.pdf 对齐：
								//   d3 = 总圈数 All_Lap
								//   d4 = 右端期望触板次数 RAll_Lap
								//   d5 = 左端期望触板次数 LAll_Lap
								//   d6 = 0-4 道开关位图   d7 = 5-9 道开关位图   d8 = 接力标志(0/1)
								//
								// 注：旧实现把 RelayBit 放在 d3、左右触板放在 d4/d5。新协议把总圈数放 d3，
								//     接力放 d8；为兼容旧 PC 端，可用 (All_Lap == d4+d5) 校验。
								All_Lap = RXD_Data_Buffer[3];                 //总圈数
								RAll_Lap = RXD_Data_Buffer[4];                //右端期望触板次数
								LAll_Lap = RXD_Data_Buffer[5];                //左端期望触板次数
								// 兼容回退：若 d3==0（旧版 PC 未填总圈数），从 d4+d5 推算
								if(All_Lap == 0) All_Lap = LAll_Lap + RAll_Lap;

								// 2026-05-13(2) d8 = 接力标志，由 PC 端指定本组比赛是否接力项目
								//   d8=1 → 接力比赛，硬件按 RelayLaps 决定何时再次打开起跳台测量每棒反应时
								//   d8=0 → 个人项目，硬件只测一次起跳反应时
								RelayBit = (RXD_Data_Buffer[8] != 0) ? 1 : 0;
								// 2026-06-02 d9 = HardwareAlwaysOpen flag (PC 端参数设置 "硬件设备: 一直打开")
								HardwareAlwaysOpenBit = (RXD_Data_Buffer[9] != 0) ? 1 : 0;
								// 2026-06-03 always-open 模式时主动清屏 (= 让守卫覆盖原有圈数/名次显示)
								if (HardwareAlwaysOpenBit) {
									u8 _ci;
									for (_ci = 0; _ci < 10; _ci++) {
										LLaps_diaplay(_ci);
										RLaps_diaplay(_ci);
										Place_display(_ci);
									}
						// 2026-06-03 直通启动也清 Timer 成绩栏 (= 防上次比赛残留显示反应时/成绩)
						sprintf((char*)lcd_Dis, "          ");
						LCD_ShowString(Timer_posx[0], Timer_posy[0] + (_ci+1) * line_height1, 200, 32, 32, lcd_Dis);
						LCD_ShowString(Timer_posx[1], Timer_posy[1] + (_ci+1) * line_height1, 200, 32, 32, lcd_Dis);
								}
								// RelayLaps 仍按既有约定：4×100m 时=1，4×200m 时=2（即 All_Lap/8）。
								// 非接力项目 RelayLaps=0，避免误触发起跳台重开逻辑。
								RelayLaps = (RelayBit == 1) ? ((All_Lap == 4) ? 1 : (All_Lap / 8)) : 0;  // 2026-06-09 加 4×50m (All_Lap=4) 特例 → RelayLaps=1, 走 4×100m 同分支
								// 同步更新主屏"接力/非接力"按钮显示
								if(Relaybtn)
								{
									Relaybtn->caption = Relay_btncaption_tbl[RelayBit][gui_phy.language];
									btn_draw(Relaybtn);
								}

								//2026-05-25 修复 #3: 原硬编码 50*All_Lap, 25 米池项目算出错误距离
								if(Pool50mOr25mbit==0) sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);
								else                    sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);
								LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//显示比赛距离  2026-05-12 右移140

								for(i=0;i<10;i++)
								{
									laps[i][0]=LAll_Lap;

									laps[i][1]=RAll_Lap;					//2024-11-21
							//		LLaps_diaplay(i);
								}

								//2023-11-13
								Receive_Command_buf=RXD_Data_Buffer[6];			//当前组，4-0道运动员参赛情况：000XXXXX; X=0:此道无运动员，=1：此道有运动员
								for(i=0;i<5;i++)
								{
									if((Receive_Command_buf&0x01)==1) CloseLaneState[i]=2;					//关闭道次状态  =2：打开；=3：关闭
									else CloseLaneState[i]=3;
									//2026-05-13 跟随 CloseLaneState 立刻更新关闭按钮颜色（关闭=暗，打开=亮）
									if(CloseLanebtn[i])
									{
										if(CloseLaneState[i]==3)
										{
											CloseLanebtn[i]->bkctbl[0]=0X3186; CloseLanebtn[i]->bkctbl[1]=0X2A0F;
											CloseLanebtn[i]->bkctbl[2]=0X2A0F; CloseLanebtn[i]->bkctbl[3]=0X10A2;
											CloseLanebtn[i]->bcfucolor=GRAY;
										}
										else
										{
											CloseLanebtn[i]->bkctbl[0]=0X6BF6; CloseLanebtn[i]->bkctbl[1]=0X545E;
											CloseLanebtn[i]->bkctbl[2]=0X5C7E; CloseLanebtn[i]->bkctbl[3]=0X2ADC;
											CloseLanebtn[i]->bcfucolor=WHITE;
										}
										btn_draw(CloseLanebtn[i]);
									}
									display_swim_dir(dir_posx,i,CloseLaneState[i],0);			//open/close
									LLaps_diaplay(i);
									RLaps_diaplay(i);					//2024-11-21
									Receive_Command_buf=Receive_Command_buf>>1;						//检测下一道
								}
								Receive_Command_buf=RXD_Data_Buffer[7];			//当前组，9-5道运动员参赛情况：000XXXXX; X=0:此道无运动员，=1：此道有运动员
								for(i=0;i<5;i++)
								{
									if((Receive_Command_buf&0x01)==1) CloseLaneState[i+5]=2;					//关闭道次状态  =2：打开；=3：关闭
									else CloseLaneState[i+5]=3;
									//2026-05-13 同样：立刻更新关闭按钮颜色
									if(CloseLanebtn[i+5])
									{
										if(CloseLaneState[i+5]==3)
										{
											CloseLanebtn[i+5]->bkctbl[0]=0X3186; CloseLanebtn[i+5]->bkctbl[1]=0X2A0F;
											CloseLanebtn[i+5]->bkctbl[2]=0X2A0F; CloseLanebtn[i+5]->bkctbl[3]=0X10A2;
											CloseLanebtn[i+5]->bcfucolor=GRAY;
										}
										else
										{
											CloseLanebtn[i+5]->bkctbl[0]=0X6BF6; CloseLanebtn[i+5]->bkctbl[1]=0X545E;
											CloseLanebtn[i+5]->bkctbl[2]=0X5C7E; CloseLanebtn[i+5]->bkctbl[3]=0X2ADC;
											CloseLanebtn[i+5]->bcfucolor=WHITE;
										}
										btn_draw(CloseLanebtn[i+5]);
									}
									display_swim_dir(dir_posx,i+5,CloseLaneState[i+5],0);			//open/close
									LLaps_diaplay(i+5);
									RLaps_diaplay(i+5);					//2024-11-21
									Receive_Command_buf=Receive_Command_buf>>1;						//检测下一道
								}
					
								
								//2026-05-26 (方案 B): 把 All_Lap / LAll_Lap / RAll_Lap / RelayBit 等关键比赛参数
								//   持久化到板上 NAND Flash, 硬件独立启动时自动恢复, 不依赖 VBAT 备份电池
								OnWriteMatchData();
								break;

							case Set_ArmDelay_Time:   		//设置触板封闭时间 2023-10-16
								All_Close_Time=RXD_Data_Buffer[3]*10;								//全泳道关闭时间  2024-12-27
								Close_Time=RXD_Data_Buffer[4]*10;								//关闭时间
								MBdelay_Time=RXD_Data_Buffer[5]*10;						//2026-05-18 d5 为整秒(PC 端 BlindReplaceDelay)，与其它字段统一；硬件 *10 转 0.1s 单位
								Result_Display_Time=RXD_Data_Buffer[6]*10;				//每道成绩的显示停留时间3000毫秒
								StartBox_Edge_Bit=RXD_Data_Buffer[7];							// 2024-4-21 receive Startbox Edge bit 

								StartFinalPlace=0x0f&RXD_Data_Buffer[8];									//发令和终点位置  2024-11-27
								TP_DelayCloseValue=RXD_Data_Buffer[9]*10;									//运动员触板TP信号关闭延迟时间初始设置5秒 2024-12-27
								Relay_SB_DelayCloseValue=RXD_Data_Buffer[10]*10;					//不低于1秒，接力比赛运动员跳台出发信号关闭延迟时间初始设置5秒 2024-12-27
							
							
							
								FinalPlace=0x01&StartFinalPlace;													//终点位置  2024-11-27
								StartPlace=(0x02&StartFinalPlace)>>1;											//发令位置  2024-11-27
								Display_StartFinalPlace(StartFinalPlace);   									//显示发令点  2024-6-10
								SwimmingPool_Arrage=(0x0f0&RXD_Data_Buffer[8])>>4;		//改变泳道号顺序  2024-6-13
								SwimmingPool_ArrageSubject(SwimmingPool_Arrage);

								display_closetime();																//显示泳道触板关闭时间  2023-10-17
								//2026-05-26 (方案 B): 把延迟/关闭时间等参数持久化到板上 NAND Flash
								OnWriteMatchData();
								break;

							case Set_MB_Num:   					//设置每边盲表数 2023-10-16

								Left_MB_Num=RXD_Data_Buffer[3];				//Final边 盲表数量  最大三个
								Right_MB_Num=RXD_Data_Buffer[4];				//Middle边 盲表数量  最大三个
	
								for	(i=0;i<Max_MB_Num;i++)
								{
									L_MB_State_Line[i]=0;		//左边 盲表的状态，接还是不连接
									R_MB_State_Line[i]=0;		//右边 盲表的状态，接还是不连接
								}
								for	(i=0;i<Left_MB_Num;i++)
								{
									L_MB_State_Line[i]=1;		//左边Lane 盲表的状态，接还是不连接
								}
								for	(i=0;i<Right_MB_Num;i++)
								{
									R_MB_State_Line[i]=1;		//右边Lane 盲表的状态，接还是不连接
								}
								//2026-05-31 sync MB_Open_Close_State: unused MB → UnInstall(=4), reinstall (4→0 Close), redraw LCD
								{
									u8 _m, _j;
									for (_j=0; _j<10; _j++) {
										for (_m=0; _m<Max_MB_Num; _m++) {
											if (_m < Left_MB_Num) {
												if (MB_Open_Close_State[_m][_j] == 4) MB_Open_Close_State[_m][_j] = 0;
											} else {
												MB_Open_Close_State[_m][_j] = 4;
											}
											if (_m < Right_MB_Num) {
												if (MB_Open_Close_State[_m][_j+10] == 4) MB_Open_Close_State[_m][_j+10] = 0;
											} else {
												MB_Open_Close_State[_m][_j+10] = 4;
											}
										}
										for (_m=0; _m<Max_MB_Num; _m++) {
											Display_MB_StateAuto(0, _j, _m, lcd_Dis);
											Display_MB_StateAuto(1, _j, _m, lcd_Dis);
										}
									}
								}
								
								OnWriteMatchData();   	//存储比赛数据  20254-1-26
								
								break;
							//2026-05-17 0x44 Set_PoolConfiguration_Com1: PC 改泳池长度/泳道数时下发
							//   d3 = 泳道数(硬件固定 10，忽略)  d4 = 泳池长度 25 或 50 米
							case Set_PoolConfiguration_Com1:
							{
								if(RXD_Data_Buffer[4] == 25)      Pool50mOr25mbit = 1;
								else if(RXD_Data_Buffer[4] == 50) Pool50mOr25mbit = 0;
								OnWriteMatchData();		//持久化到板上 NAND Flash (2:/)
							}
							break;

							case Set_TPSBMB_State:   		//设置TP,SB.MB好坏状态			2023-11-15
								Command_buf=RXD_Data_Buffer[5];				//接收到泳道TP的命令数据
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[4];
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[3];
								for(i=0;i<10;i++)			
								{
									if(((Command_buf&0x01)==0))
									{
										TP_Open_Close_State[i][0]=3;			//左边触板坏 =3：坏;
										Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);			//显示左边触板坏的原色
									}
									else if((TP_Open_Close_State[i][0]==3))
									{
										TP_Open_Close_State[i][0]=0;			//左边触板好 =0：好 关闭;
										Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);			//显示左边触板好（关闭）的原色
									}
									Command_buf=Command_buf>>1;						//检测下一位			
								}
								for(i=0;i<10;i++)			
								{
									if((Command_buf&0x01)==0)
									{
										TP_Open_Close_State[i][1]=3;			//右边触板坏 =3：坏;
										Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);			//显示右边触板坏的原色
									}
									else if(TP_Open_Close_State[i][1]==3)
									{
										TP_Open_Close_State[i][1]=0;			//右边触板好 =0：好 关闭;
										Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);			//显示右边触板好（关闭）的原色
									}
									Command_buf=Command_buf>>1;						//检测下一位			
								}
								
								Command_buf=RXD_Data_Buffer[10];				//接收到泳道SB的命令数据
								Command_buf=((Command_buf&0xF0)<<4)|RXD_Data_Buffer[7];
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[6];
								for(i=0;i<10;i++)			
								{
									if(((Command_buf&0x01)==0))
									{
										Startbox_Open_Close_State[i][0]=3;								//左边出发台坏 =3：坏;
										Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Bad_Color);//左边出发台显示
									}
									else if((Startbox_Open_Close_State[i][0]==3))
									{
										Startbox_Open_Close_State[i][0]=0;								//左边出发台好 =0：好 关闭;
										Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Close_Color);			//显示左边触板好（关闭）的原色
									}
									Command_buf=Command_buf>>1;						//检测下一位			
								}
								for(i=0;i<10;i++)			
								{
									if((Command_buf&0x01)==0)
									{
										Startbox_Open_Close_State[i][1]=3;						//右边出发台坏 =3：坏;
										Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Bad_Color);			//显示右边出发台坏的原色
									}
									else if(Startbox_Open_Close_State[i][1]==3)
									{
										Startbox_Open_Close_State[i][1]=0;						//右边出发台好 =0：好 关闭;
										Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Close_Color);//右边出发台显示好（关闭）的原色
									}
									Command_buf=Command_buf>>1;						//检测下一位			
								}
								
								Command_buf=RXD_Data_Buffer[10];				//接收到泳道MB的命令数据
								Command_buf=((Command_buf&0x0F)<<8)|RXD_Data_Buffer[9];
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[8];
								for(i=0;i<10;i++)			
								{
									if(((Command_buf&0x01)==0))
									{
										MB_Open_Close_State[0][i]=3;									//第0行左边盲表坏 =3：坏;
										sprintf((char*)lcd_Dis,"L%d",(i));
										Display_MB_StateGroup(0,i,Bad_Color,lcd_Dis);				//显示左边盲表坏的原色
									}
									else if((MB_Open_Close_State[0][i]==3))
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;									//第0行左边盲表好 =0：好 关闭;
										sprintf((char*)lcd_Dis,"L%d",(i));
										Display_MB_StateGroup(0,i,Close_Color,lcd_Dis);		
									}
									Command_buf=Command_buf>>1;						//检测下一位			
								}
								for(i=0;i<10;i++)			
								{
									if((Command_buf&0x01)==0)
									{
										MB_Open_Close_State[0][i+10]=3;								//第0行右边盲表坏 =3：坏;
										sprintf((char*)lcd_Dis,"R%d",(i));
										Display_MB_StateGroup(1,i,Bad_Color,lcd_Dis);			//显示右边盲表坏的原色
									}
									else if(MB_Open_Close_State[0][i+10]==3)
									{
										if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=0;								//第0行右边盲表好 =0：好 关闭;
										sprintf((char*)lcd_Dis,"R%d",(i));
										Display_MB_StateGroup(1,i,Close_Color,lcd_Dis);		
									}
									Command_buf=Command_buf>>1;						//检测下一位			
								}
									
									OnWriteDeviceData();	//2026-05-26 PC 发的 TP/SB/MB 状态持久化到 swimdev.cfg
							break;
						case Set_OpenOrClose_ALL_TP:   //2024-12-27 set close/open all TP
							Open_State=RXD_Data_Buffer[3];   //=1 keep TP open(no close); =0 default
							//2026-05-16 Sync main screen: update TP_Open_Close_State[] and redraw
							//   all non-bad(3)/non-uninstalled(4) TP icons to the new state.
							//   Without this, the Settings button press has no visible effect on
							//   the main control screen after returning.
							{
								u8  li, jj;
								u8  new_tp = (Open_State==1) ? 1 : 0;
								u16 col_tp = (Open_State==1) ? Open_TP_Color : Close_Color;
								for (li = 0; li < 10; li++) {
									jj = li + 1;
									if (TP_Open_Close_State[li][0] != 3 && TP_Open_Close_State[li][0] != 4) {
										TP_Open_Close_State[li][0] = new_tp;
										Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, col_tp);
									}
									if (TP_Open_Close_State[li][1] != 3 && TP_Open_Close_State[li][1] != 4) {
										TP_Open_Close_State[li][1] = new_tp;
										Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, col_tp);
									}
								}
							}

							break;

							//2026-05-12 控制计算机发送日期+时间，硬件 RTC 自动同步
							case Set_DateTime:
								{
									u16 sync_year  = (u16)RXD_Data_Buffer[3] | ((u16)RXD_Data_Buffer[4]<<8);
									u8  sync_month = RXD_Data_Buffer[5];
									u8  sync_day   = RXD_Data_Buffer[6];
									u8  sync_hour  = RXD_Data_Buffer[7];
									u8  sync_min   = RXD_Data_Buffer[8];
									u8  sync_sec   = RXD_Data_Buffer[9];
									if(sync_year>=2000 && sync_year<2200 && sync_month>=1 && sync_month<=12
										&& sync_day>=1 && sync_day<=31 && sync_hour<=23 && sync_min<=59 && sync_sec<=59)
									{
										RTC_Set_Time(sync_hour,sync_min,sync_sec,0);
										RTC_Set_Date((u8)(sync_year-2000),sync_month,sync_day,
													 RTC_Get_Week(sync_year,sync_month,sync_day));
									}
								}
								break;

							//2026-05-13 控制计算机强制 全开 / 恢复正常 整个或某道的所有设备(TP/SB/MB)
							case Set_LaneDeviceFullOpen:
								{
									u8 target=RXD_Data_Buffer[3];	// 0xFF=ALL  0..9=单道
									u8 mode  =RXD_Data_Buffer[4];	// 0=恢复正常关闭流程 1=全开（强制打开）
									u8 new_state=(mode==1)?1:0;
									u8 li,m,jj;
									u8 lane_a=0,lane_b=0;
									if(target==0xFF) { lane_a=0; lane_b=10; }
									else if(target<10) { lane_a=target; lane_b=target+1; }
									else break;

									for(li=lane_a; li<lane_b; li++)
									{
										jj=li+1;
										//TP（两侧）—— 状态 3 坏 / 4 未安装 保留不动
										if(TP_Open_Close_State[li][0]!=3 && TP_Open_Close_State[li][0]!=4) TP_Open_Close_State[li][0]=new_state;
										if(TP_Open_Close_State[li][1]!=3 && TP_Open_Close_State[li][1]!=4) TP_Open_Close_State[li][1]=new_state;
										//SB（两侧）—— 状态 3 坏 保留
										if(Startbox_Open_Close_State[li][0]!=3) Startbox_Open_Close_State[li][0]=new_state;
										if(Startbox_Open_Close_State[li][1]!=3) Startbox_Open_Close_State[li][1]=new_state;
										//MB（每边 3 个，两侧）—— 状态 3 坏 / 4 未安装 保留
										for(m=0;m<Max_MB_Num;m++)
										{
											if(MB_Open_Close_State[m][li]!=3 && MB_Open_Close_State[m][li]!=4) MB_Open_Close_State[m][li]=new_state;
											if(MB_Open_Close_State[m][li+10]!=3 && MB_Open_Close_State[m][li+10]!=4) MB_Open_Close_State[m][li+10]=new_state;
										}
										//—— 立刻按新 state 重绘本道 TP/SB/MB 图标 ——
										//左 TP
										if(TP_Open_Close_State[li][0]==3) Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][0]==4) Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][0]==1) Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,Open_TP_Color);
										else Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,Close_Color);
										//右 TP
										if(TP_Open_Close_State[li][1]==3) Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][1]==4) Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][1]==1) Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,Open_TP_Color);
										else Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,Close_Color);
										//左 SB
										if(Startbox_Open_Close_State[li][0]==3) Display_Startbox_State(Startboxsx[0],Startboxsy[0]+jj*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][0]==1) Display_Startbox_State(Startboxsx[0],Startboxsy[0]+jj*btnhy+8,24,24,Open_SB_Color);
										else Display_Startbox_State(Startboxsx[0],Startboxsy[0]+jj*btnhy+8,24,24,Close_Color);
										//右 SB
										if(Startbox_Open_Close_State[li][1]==3) Display_Startbox_State(Startboxsx[1],Startboxsy[1]+jj*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][1]==1) Display_Startbox_State(Startboxsx[1],Startboxsy[1]+jj*btnhy+8,24,24,Open_SB_Color);
										else Display_Startbox_State(Startboxsx[1],Startboxsy[1]+jj*btnhy+8,24,24,Close_Color);
										//左 MB（用第 0 个 MB 状态显示，与现有 Reset_Timer 一致）
										sprintf((char*)lcd_Dis,"L%d",(li));
										if(MB_Open_Close_State[0][li]==3) Display_MB_StateGroup(0,jj-1,Bad_Color,lcd_Dis);
										else if(MB_Open_Close_State[0][li]==1) Display_MB_StateGroup(0,jj-1,Open_MB_Color,lcd_Dis);
										else Display_MB_StateGroup(0,jj-1,Close_Color,lcd_Dis);
										//右 MB
										sprintf((char*)lcd_Dis,"R%d",(li));
										if(MB_Open_Close_State[0][li+10]==3) Display_MB_StateGroup(1,jj-1,Bad_Color,lcd_Dis);
										else if(MB_Open_Close_State[0][li+10]==1) Display_MB_StateGroup(1,jj-1,Open_MB_Color,lcd_Dis);
										else Display_MB_StateGroup(1,jj-1,Close_Color,lcd_Dis);
									}
								}
								break;

							//2026-05-13(2) 旧的私有 Set_MBDelayTime (raw 0x3C / wire 0x3C) 已被官方 0x4C
							//             Set_ForceAllOpen 占用 (raw 0x3C / wire 0x4C)，该 case 已删除。
							//             "盲表代替成绩延迟时间" 改用以下两种 PC→HW 路径任一：
							//               · 0x41 Set_ArmDelay_Time 的 d5  (已支持，见上面 case)
							//               · 0x42 SetCommand 子码 0x09 d4 = 0.1s 单位 (下面 case 0x32)
							//             (注: 上面的 case Set_LaneDeviceFullOpen 已切到新 raw 0x3C，
							//              所以该位置 wire 0x4C 由该 case 接管。)

							//2026-05-13(3) 0x42 SetCommand 单参数读写命令（按 通讯协议变更说明_v2026.05.13.pdf B3）
							//   d3 = 子码（参数 ID），d4 = 参数值。
							//   既有子码 0x01..0x08（LaneCloseTime / StartBlockCloseDelay / ... / BlindWatchCount），
							//   新增子码 0x09 BlindReplaceDelay (d4 = 0.1秒单位，例如 50 = 5.0s)。
							//   PC 端 TimingBridge.cs 已支持 TimingCommandType.SetCommand 接收方向；
							//   未来若硬件需要"参数变了主动回报 PC"，从这里发 0x42 即可。
							//   ?? raw 0x32 对应 wire 0x42。
							case 0x32:
								{
									u8 sub  = RXD_Data_Buffer[3];
									u8 val  = RXD_Data_Buffer[4];
									switch(sub)
									{
										case SetCmd_Sub_FalseStartThreshold:	//2026-05-26 0x04 抢跳判定阈值
											//   PC 端 FalseStartThreshold 单位 0.01s, 直接接收 val (10 = 0.1s)
											FalseStartThreshold = (u16)val;
											OnWriteMatchData();
											break;
										case SetCmd_Sub_BlindReplaceDelay:	//0x09  盲表代替成绩延迟时间
											//2026-05-18 val 为整秒 (PC BlindReplaceDelay)，与其它字段统一；硬件 *10 转 0.1s 单位
											MBdelay_Time = (u16)val * 10;
											OnWriteMatchData();				//持久化到板上 NAND Flash (2:/), 下次开机生效
											break;
										//0x01..0x08：原本通过 0x41 帧聚合下发，单字段变更也允许由此路径进入。
										//为最小变更面，此处只实现新增的 0x09。如需补齐 0x01..0x08，
										//可参照 case Set_ArmDelay_Time 拆分赋值即可。
										default:
											break;
									}
								}
								break;


							//2026-05-14 0x47 Set_LaneOpenClose (raw 0x37) —— 动态启用/禁用泳道
							//   d3 = 0xFF 全部，或 0-9 单道；d4 = 1 启用 / 0 屏蔽。
							//   被禁用的道任何 TP/SB/MB 信号都不上报、不计反应时。
							//
							//   实现机理：复用已有的 CloseLaneState[i] 门控（许多 TP/SB 处理函数
							//   都检查 CloseLaneState[i]==2 才放行），把 0x47 的 active=0 映射到
							//   CloseLaneState=3 即可——不需要往各信号路径里再插新条件。
							//   同时维护 LaneEnabled[] 作为协议层面"显式禁用"标记，可被未来的
							//   优先级逻辑（"被 0x47 关闭的道无视 0x4C 全开"）使用。
							//
							//   视觉反馈：CloseLanebtn 颜色/文字 + cmdLbtn/cmdRbtn 颜色 同步更新。
							case Set_LaneOpenClose:
							{
								//2026-05-17 重写: 0x47 在 PC 端语义是"全部打开/全部关闭 TP/SB/MB"，
								//   原硬件实现却动了 CloseLaneState/CloseLanebtn/cmdLbtn/cmdRbtn（道次按钮）
								//   不动 TP/SB/MB，与 PC 端语义反了。这里改成只同步 TP/SB/MB（与本地
								//   net_test 内 OpenCloseTPbtn 处理对齐），不动道次按钮/手动触板按钮。
								//   D3=0xFF 全部道；D3=0..9 单道；D4=1 打开/0 关闭。
								u8 target = RXD_Data_Buffer[3];
								u8 active = (RXD_Data_Buffer[4] != 0) ? 1 : 0;
								u8 a, b, li, jj, k;
								u8 new_st = active;
								u16 col_tp = active ? Open_TP_Color : Close_Color;
								u16 col_sb = active ? Open_SB_Color : Close_Color;
								u8 _mb_label[8];
								if(target == 0xFF) { a = 0; b = 10; Open_State = active; }
								else if(target < 10) { a = target; b = (u8)(target + 1); }
								else break;
								for(li = a; li < b; li++)
								{
									jj = li + 1;
									//---- TP 左 ----
									if(TP_Open_Close_State[li][0] != 3 && TP_Open_Close_State[li][0] != 4){
										TP_Open_Close_State[li][0] = new_st;
										Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, col_tp);
									}
									//---- TP 右 ----
									if(TP_Open_Close_State[li][1] != 3 && TP_Open_Close_State[li][1] != 4){
										TP_Open_Close_State[li][1] = new_st;
										Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, col_tp);
									}
									//---- SB 左 ----
									if(Startbox_Open_Close_State[li][0] != 3 && Startbox_Open_Close_State[li][0] != 4){
										Startbox_Open_Close_State[li][0] = new_st;
										Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, col_sb);
									}
									//---- SB 右 ----
									if(Startbox_Open_Close_State[li][1] != 3 && Startbox_Open_Close_State[li][1] != 4){
										Startbox_Open_Close_State[li][1] = new_st;
										Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, col_sb);
									}
									//---- MB 左 (3 个盲表) ----
									for(k = 0; k < 3; k++){
										if(MB_Open_Close_State[k][li] != 3 && MB_Open_Close_State[k][li] != 4)
											if(MB_Open_Close_State[k][li]!=3 && MB_Open_Close_State[k][li]!=4) MB_Open_Close_State[k][li]=new_st;
									}
									sprintf((char*)_mb_label, "L%d", li);
									{
										u8 _mbs = MB_Open_Close_State[0][li];
										u16 _mbc;
										if(_mbs == 4)      _mbc = UnInstall_Color;
										else if(_mbs == 3) _mbc = Bad_Color;
										else if(_mbs == 0) _mbc = Close_Color;
										else               _mbc = Open_MB_Color;
										Display_MB_StateGroup(0, jj-1, _mbc, _mb_label);
									}
									//---- MB 右 (3 个盲表) ----
									for(k = 0; k < 3; k++){
										if(MB_Open_Close_State[k][li+10] != 3 && MB_Open_Close_State[k][li+10] != 4)
											if(MB_Open_Close_State[k][li+10]!=3 && MB_Open_Close_State[k][li+10]!=4) MB_Open_Close_State[k][li+10]=new_st;
									}
									sprintf((char*)_mb_label, "R%d", li);
									{
										u8 _mbs = MB_Open_Close_State[0][li+10];
										u16 _mbc;
										if(_mbs == 4)      _mbc = UnInstall_Color;
										else if(_mbs == 3) _mbc = Bad_Color;
										else if(_mbs == 0) _mbc = Close_Color;
										else               _mbc = Open_MB_Color;
										Display_MB_StateGroup(1, jj-1, _mbc, _mb_label);
									}
								}
							}
								OnWriteDeviceData();	//2026-05-26 道次开关改了 TP/SB/MB 状态, 同步持久化
							break;
							case Set_LaneEnableDisable:
								{
									u8 target = RXD_Data_Buffer[3];
									u8 active = (RXD_Data_Buffer[4] != 0) ? 1 : 0;
									u8 a, b, li;
									if(target >= 10) break;
									a = target; b = (u8)(target + 1);
									
									for(li = a; li < b; li++)
									{
										LaneEnabled[li] = active;
										CloseLaneState[li] = active ? 2 : 3;	//复用 CloseLaneState 门控

										//—— 同步底层 CloseLanebtn ——
										if(CloseLanebtn[li])
										{
											if(active == 0)
											{	//关闭：暗色
												CloseLanebtn[li]->bkctbl[0]=0X3186;
												CloseLanebtn[li]->bkctbl[1]=0X2A0F;
												CloseLanebtn[li]->bkctbl[2]=0X2A0F;
												CloseLanebtn[li]->bkctbl[3]=0X10A2;
												CloseLanebtn[li]->bcfucolor=GRAY;
												CloseLanebtn[li]->caption="关闭";
											}
											else
											{	//打开：恢复正常蓝色
												CloseLanebtn[li]->bkctbl[0]=0X6BF6;
												CloseLanebtn[li]->bkctbl[1]=0X545E;
												CloseLanebtn[li]->bkctbl[2]=0X5C7E;
												CloseLanebtn[li]->bkctbl[3]=0X2ADC;
												CloseLanebtn[li]->bcfucolor=WHITE;
												CloseLanebtn[li]->caption="打开";
											}
											btn_draw(CloseLanebtn[li]);
										}
										//—— 同步 道次按钮 cmdLbtn / cmdRbtn 颜色 ——
										if(cmdLbtn[li])
										{
											if(active == 0)
											{
												cmdLbtn[li]->bkctbl[0]=0X3186;
												cmdLbtn[li]->bkctbl[1]=0X2A0F;
												cmdLbtn[li]->bkctbl[2]=0X2A0F;
												cmdLbtn[li]->bkctbl[3]=0X10A2;
												cmdLbtn[li]->bcfucolor=GRAY;
											}
											else Setup_LaneBtn_LightYellow(cmdLbtn[li]);
											btn_draw(cmdLbtn[li]);
										}
										if(cmdRbtn[li])
										{
											if(active == 0)
											{
												cmdRbtn[li]->bkctbl[0]=0X3186;
												cmdRbtn[li]->bkctbl[1]=0X2A0F;
												cmdRbtn[li]->bkctbl[2]=0X2A0F;
												cmdRbtn[li]->bkctbl[3]=0X10A2;
												cmdRbtn[li]->bcfucolor=GRAY;
											}
											else Setup_LaneBtn_LightYellow(cmdRbtn[li]);
											btn_draw(cmdRbtn[li]);
										}
										//—— 方向指示同步 ——
										display_swim_dir(dir_posx, li, CloseLaneState[li], 0);
									}
								}
								break;
							//2026-05-17 0x61 Set_LapRemaining (raw 0x51) 命令 同步某道某侧剩余圈数
							//2026-05-29 容错版 D 扩展 d6 / d7: 比赛特殊情况设备打开 (漏触/误触补救)
							//   d3=道次 0..9, d4=侧 (0左/1右), d5=剩余圈数
							//   d6=1 漏触补救 → 关 _side TP+MB, 开 1-_side TP+MB
							//   d6=2 误触回退 → 开 _side TP+MB, 关 1-_side TP+MB
							//   d7=1 开左 SB 关右; d7=2 开右 SB 关左; d7=0 不动 SB
							//   不动手动按键 T (用户要求), 翻方向, 清相关计时
							case Set_LapRemaining:
							{
								u8 _lane = RXD_Data_Buffer[3];
								u8 _side = RXD_Data_Buffer[4];
								u8 _val  = RXD_Data_Buffer[5];
								u8 _open = RXD_Data_Buffer[6];
								u8 _sb_side = RXD_Data_Buffer[7];
								u8 _open_side, _close_side, _mb_num, _close_mb_num, _mb_idx, _close_mb_idx, _k;
								u16 _jj;
								if(_lane >= 10 || _side >= 2) break;
								//2026-05-30 fix #2: Lap_Place 名位回退 (跟物理触板 Display_Laps_Place_Direct 的 ++ 逻辑同步)
								//   若_val < _old_val (剩余少 = 漏触补救增加圈完成): Lap_Place[new_total]++ (= 该 lane 拿新名次)
								//   若_val > _old_val (剩余多 = 误触回退撤销圈完成): Lap_Place[old_total]-- (= 撤销之前名次)
								//   若_val == _old_val (PC 只刷新): 不动 Lap_Place
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
									// d6=0: 只更新 laps + LED, 跳过设备处理 (但若 d7 != 0 仍处理 SB)
									goto sb_only_label;
								}
								_open_side    = (_open == 1) ? (u8)(1-_side) : _side;
								_close_side   = (u8)(1 - _open_side);
								_mb_num       = (_open_side == 0)  ? Left_MB_Num  : Right_MB_Num;
								_close_mb_num = (_close_side == 0) ? Left_MB_Num  : Right_MB_Num;
								_mb_idx       = (_open_side == 0)  ? _lane : (u8)(_lane + 10);
								_close_mb_idx = (_close_side == 0) ? _lane : (u8)(_lane + 10);
								_jj           = (u16)(_lane + 1);
								// 开 _open_side 的 TP (跳过坏/未装)
								if(TP_Open_Close_State[_lane][_open_side] != 3
								   && TP_Open_Close_State[_lane][_open_side] != 4) {
									TP_Open_Close_State[_lane][_open_side] = 1;
									Display_TP_State(TPsx[_open_side], TPsy[_open_side]+_jj*btnhy, 8, btnh, Open_TP_Color);
								}
								// 关 _close_side 的 TP
								if(TP_Open_Close_State[_lane][_close_side] != 3
								   && TP_Open_Close_State[_lane][_close_side] != 4) {
									TP_Open_Close_State[_lane][_close_side] = 0;
									Display_TP_State(TPsx[_close_side], TPsy[_close_side]+_jj*btnhy, 8, btnh, Close_Color);
								}
								// 开 _open_side 的 MB (按 _mb_num 数量, 跳过坏/未装)
								for(_k = 0; _k < _mb_num; _k++) {
									if(MB_Open_Close_State[_k][_mb_idx] != 3
									   && MB_Open_Close_State[_k][_mb_idx] != 4) {
										if(MB_Open_Close_State[_k][_mb_idx]!=3 && MB_Open_Close_State[_k][_mb_idx]!=4) MB_Open_Close_State[_k][_mb_idx]=1;
										sprintf((char*)lcd_Dis, (_open_side==0)?"L%d":"R%d", _lane);
										Display_MB_StateGroup(_open_side, _lane, Open_MB_Color, lcd_Dis);
									}
								}
								// 关 _close_side 的 MB
								for(_k = 0; _k < _close_mb_num; _k++) {
									if(MB_Open_Close_State[_k][_close_mb_idx] != 3
									   && MB_Open_Close_State[_k][_close_mb_idx] != 4) {
										if(MB_Open_Close_State[_k][_close_mb_idx]!=3 && MB_Open_Close_State[_k][_close_mb_idx]!=4) MB_Open_Close_State[_k][_close_mb_idx]=0;
										sprintf((char*)lcd_Dis, (_close_side==0)?"L%d":"R%d", _lane);
										Display_MB_StateGroup(_close_side, _lane, Close_Color, lcd_Dis);
									}
								}
								// 清等待状态 + bitmap + MB_Result
								Lane_TP_MB_State[_lane][_open_side] = 0;
								Lane_TP_MB_State[_lane][_close_side] = 0;
								Lane_TP_MB_Time_Difference[_lane] = 0;
								//2026-05-29 fix v2: 模拟物理触板 — Lane_Display_State[close_side]=1 让主循环重新画 >>>/<<< 方向
								//   close_side 是刚触板那侧 (跟物理触板 4366 行同源); MSecond=0 让倒计时从 0 重数
								//   状态机自然汇合: 等 MSecond 累到 Close_Time 时, 因 TP 已被 PC force open, 主循环 if(==0) 不成立 不会重复开
								Lane_Display_State[_lane][_close_side] = 1;
								Lane_Display_State[_lane][_open_side]  = 0;
								//2026-05-29 fix v3: MSecond=Display_Dir_Max_Time 让屏立即画满 10 个箭头, 之后保持不变 (等下次物理触板转向)
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
								// 翻方向箭头 (运动员朝 _open_side 端游)
								display_swim_dir(dir_posx, _lane, _close_side, Display_Dir_Max_len);  //2026-05-30 fix v4: xy=close_side 跟活代码 Process_Display_SiwmDir 5396/5478 同源 (5236-5371 是 /**/ 死代码, 不参考)  //2026-05-29 fix v3: 立即画满 10 个箭头 跟 Process_Display_SiwmDir 5263/5324 同源
							sb_only_label:
								// SB 处理 (d7 决定, 与 d6 解耦; 不动手动按键 T)
								if(_sb_side == 1 || _sb_side == 2) {
									u8 _sb_open_idx = (_sb_side == 1) ? 0 : 1;
									u8 _sb_close_idx = (u8)(1 - _sb_open_idx);
									u16 _jj2 = (u16)(_lane + 1);
									if(Startbox_Open_Close_State[_lane][_sb_open_idx] != 3
									   && Startbox_Open_Close_State[_lane][_sb_open_idx] != 4) {
										Startbox_Open_Close_State[_lane][_sb_open_idx] = 1;
										Display_Startbox_State(Startboxsx[_sb_open_idx], Startboxsy[_sb_open_idx]+_jj2*btnhy+8, 24, 24, Open_SB_Color);
									}
									if(Startbox_Open_Close_State[_lane][_sb_close_idx] != 3
									   && Startbox_Open_Close_State[_lane][_sb_close_idx] != 4) {
										Startbox_Open_Close_State[_lane][_sb_close_idx] = 0;
										Display_Startbox_State(Startboxsx[_sb_close_idx], Startboxsy[_sb_close_idx]+_jj2*btnhy+8, 24, 24, Close_Color);
									}
								}
								else {
									// d7=0: 两端 SB 都关 (跟 TP+MB 同步, 防残留 bug)
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
							break;
							//2026-05-18 0x65 Set_RefreshDisplay (raw 0x55) ── PC 改完参数后让硬件刷一次主控
							//   等同硬件本地 Setupbtn 路径: 保存 CloseLaneState/laps → SwimControl_init → 恢复+重画
							//   PC 端 SendRefreshDisplay() 在参数对话框确定后下发
							case Set_RefreshDisplay:
							{
								//2026-05-25 修复 #1 (按 PDF 硬件待改动需求 v2026.05.25)：
								// 0x65 收到后只做"局部 UI 重画"，不再调 SwimControl_init，
								// 避免重置 race distance / All_Lap / LAll_Lap / RAll_Lap / RelayBit /
								// Pool50mOr25mbit / PoolSingleOrDoubleTPbit / SwimmingPool_Arrage 等业务字段
								u16 _rsi;
								//—— 重画"参数显示文字"(比赛距离) ——
								if(Pool50mOr25mbit==0) sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);
								else                    sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);
								LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);
								//—— 重画"泳道关闭时间" ——
								display_closetime();
								//—— 重画各道：道次按钮颜色/方向箭头/左右剩余圈数 ——
								for(_rsi=0; _rsi<10; _rsi++){
									if(CloseLanebtn[_rsi]){
										if(CloseLaneState[_rsi]==3){
											CloseLanebtn[_rsi]->bkctbl[0]=0X3186;
											CloseLanebtn[_rsi]->bkctbl[1]=0X2A0F;
											CloseLanebtn[_rsi]->bkctbl[2]=0X2A0F;
											CloseLanebtn[_rsi]->bkctbl[3]=0X10A2;
											CloseLanebtn[_rsi]->bcfucolor=GRAY;
										}else{
											CloseLanebtn[_rsi]->bkctbl[0]=0X6BF6;
											CloseLanebtn[_rsi]->bkctbl[1]=0X545E;
											CloseLanebtn[_rsi]->bkctbl[2]=0X5C7E;
											CloseLanebtn[_rsi]->bkctbl[3]=0X2ADC;
											CloseLanebtn[_rsi]->bcfucolor=WHITE;
										}
										btn_draw(CloseLanebtn[_rsi]);
									}
									display_swim_dir(dir_posx, _rsi, CloseLaneState[_rsi], 0);
									LLaps_diaplay(_rsi);
									RLaps_diaplay(_rsi);
								}
							}
								//2026-05-26 (问题 2): 补 idle 态占位 UI 重画 (与 SwimControl_init 末尾对齐)
								//   原局部重画段漏画左右成绩占位/名次占位/滚动时间圆点, 导致 PC 发参数后显示不到位
								if(timer_bit==0 && Ready_timer_bit==0){
									u16 _jj, _jr;
									gui_fill_circle(RunningTime_x0-32, RunningTime_y0+cr, cr, Invalid_Color);
									display_rollingtime();
									for(_jj=0; _jj<10; _jj++){
										_jr = _jj+1;
										sprintf((char*)lcd_Dis,"          ");
										LCD_ShowString(Timer_posx[0], Timer_posy[0]+_jr*line_height1, 180, 32, 32, lcd_Dis);
										LCD_ShowString(Timer_posx[1], Timer_posy[1]+_jr*line_height1, 180, 32, 32, lcd_Dis);
										LCD_ShowString(Placex, Final_timer_posy+_jr*line_height1, 200, 32, 32, (u8*)"  ");
										LLaps_diaplay(_jj);
										RLaps_diaplay(_jj);
									}
								}
							break;

							//2026-05-12 控制计算机设置 泳池单/两端安装触板
							//2026-05-25 修复 (按 PDF 硬件待改动需求 v2026.05.25, 同 0x65 修复 #1)：
							//   原代码末尾调 SwimControl_init() 重置整屏 UI, 与 PC 端 0x43 紧邻下发时,
							//   会导致比赛距离 / 总圈数等显示在 init 路径中被覆盖回开机默认。
							//   改为只重画 TP/SB 状态图标, 保留 All_Lap / LAll_Lap / RAll_Lap / RelayBit /
							//   Pool50mOr25mbit / SwimmingPool_Arrage 等所有业务字段。
							case Set_PoolSingleOrDoubleTP:
								PoolSingleOrDoubleTPbit=(RXD_Data_Buffer[3]!=0)?1:0;
								OnWriteMatchData();		//持久化到板上 NAND Flash (2:/), 下次开机仍生效
								//2026-05-13 同步刷新主界面 TP_Open_Close_State 并立即重画触板/出发台/盲表图标
								//2026-05-14 Fix #3: 单边安装时, 未安装端 不仅是触板没有, 连同 出发台 也应标记为 "未安装"(=4)
								{
									u8 li;
									if(PoolSingleOrDoubleTPbit==1)
									{	//单边安装: 未安装端的触板 + 出发台 都标记为 "未安装"
										for(li=0;li<10;li++)
										{
											TP_Open_Close_State[li][1-FinalPlace]=4;	//未安装端 TP 未装
											TP_Open_Close_State[li][FinalPlace]=0;		//终点端 TP 正常
											Startbox_Open_Close_State[li][1-FinalPlace]=4;
											if(Startbox_Open_Close_State[li][FinalPlace]==4)
												Startbox_Open_Close_State[li][FinalPlace]=0;
										}
									}
									else
									{	//两端安装: 两边都"已安装": TP+SB 同步恢复正常状态(损坏/正常按事件流转)
										for(li=0;li<10;li++)
										{
											if(TP_Open_Close_State[li][1-FinalPlace]==4)
												TP_Open_Close_State[li][1-FinalPlace]=0;
											if(TP_Open_Close_State[li][FinalPlace]==4)
												TP_Open_Close_State[li][FinalPlace]=0;
											if(Startbox_Open_Close_State[li][1-FinalPlace]==4)
												Startbox_Open_Close_State[li][1-FinalPlace]=0;
											if(Startbox_Open_Close_State[li][FinalPlace]==4)
												Startbox_Open_Close_State[li][FinalPlace]=0;
										}
									}
									//—— 局部重画各道 TP / SB 状态图标 (不调 SwimControl_init) ——
									for(li=0;li<10;li++)
									{
										//左端 TP
										if(TP_Open_Close_State[li][0]==4)        Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][0]==3)   Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][0]==0)   Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,Close_Color);
										else                                      Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,Open_TP_Color);
										//右端 TP
										if(TP_Open_Close_State[li][1]==4)        Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][1]==3)   Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][1]==0)   Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,Close_Color);
										else                                      Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,Open_TP_Color);
										//左端 SB
										if(Startbox_Open_Close_State[li][0]==4)        Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,UnInstall_Color);
										else if(Startbox_Open_Close_State[li][0]==3)   Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][0]==0)   Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,Close_Color);
										else                                            Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,Open_SB_Color);
										//右端 SB
										if(Startbox_Open_Close_State[li][1]==4)        Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,UnInstall_Color);
										else if(Startbox_Open_Close_State[li][1]==3)   Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][1]==0)   Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,Close_Color);
										else                                            Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,Open_SB_Color);
									}
								}
									//2026-05-26 (问题 2): 补 idle 态占位 UI 重画 (与 SwimControl_init 末尾对齐)
								//   原局部重画段漏画左右成绩占位/名次占位/滚动时间圆点, 导致 PC 发参数后显示不到位
								if(timer_bit==0 && Ready_timer_bit==0){
									u16 _jj, _jr;
									gui_fill_circle(RunningTime_x0-32, RunningTime_y0+cr, cr, Invalid_Color);
									display_rollingtime();
									for(_jj=0; _jj<10; _jj++){
										_jr = _jj+1;
										sprintf((char*)lcd_Dis,"          ");
										LCD_ShowString(Timer_posx[0], Timer_posy[0]+_jr*line_height1, 180, 32, 32, lcd_Dis);
										LCD_ShowString(Timer_posx[1], Timer_posy[1]+_jr*line_height1, 180, 32, 32, lcd_Dis);
										LCD_ShowString(Placex, Final_timer_posy+_jr*line_height1, 200, 32, 32, (u8*)"  ");
										LLaps_diaplay(_jj);
										RLaps_diaplay(_jj);
									}
								}
								OnWriteDeviceData();	//2026-05-26 单/两端切换改 TP+SB 未安装标记, 同步持久化
							break;

							default:

								break;
						}
				}
	}
}



void display_time(void)
{
	// 2026-06-03 always-open 模式跳过时间显示 (= PC 显示成绩)
	if (HardwareAlwaysOpenBit) return;
/*
	//显示到1/1000秒  2023-9-16
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"     %2d.%03d",second,msecond);//将LCD ID打印到lcd_Dis数组。				 	
		else 	sprintf((char*)lcd_Dis,"  %2d:%02d.%03d",minute,second,msecond);//将LCD ID打印到lcd_Dis数组。				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%03d",hour,minute,second,msecond);//将LCD ID打印到lcd_Dis数组。				 	
*/
	//显示到1/100秒  2023-11-3
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"     %2d.%02d",second,msecond/10);//将LCD ID打印到lcd_Dis数组。				 	
		else 	sprintf((char*)lcd_Dis,"  %2d:%02d.%02d",minute,second,msecond/10);//将LCD ID打印到lcd_Dis数组。				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02d",hour,minute,second,msecond/10);//将LCD ID打印到lcd_Dis数组。				 	
}

							
void display_closetime(void)
{
	sprintf((char*)lcd_Dis,"CT:%3ds",Close_Time/10);
	LCD_ShowString(connbtn_ux-500,2,200,btnh1,32,lcd_Dis);		//显示泳道触板封闭时间  2024-11-24
}


void 	Display_MB(void)			//显示正常盲表成绩		2023-11-7
{
	// 2026-06-03 always-open 模式跳过 MB 时间显示
	if (HardwareAlwaysOpenBit) return;
	//显示MB到1/100秒  2023-11-7
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"    %2d.%02dM",second,msecond/10);			//将LCD ID打印到lcd_Dis数组。				 	
		else 	sprintf((char*)lcd_Dis," %2d:%02d.%02dM",minute,second,msecond/10);				//将LCD ID打印到lcd_Dis数组。				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02dM",hour,minute,second,msecond/10);	//将LCD ID打印到lcd_Dis数组。				 	
}

void 	Display_SB(void)			//显示正常出发台成绩		2024-12-3
{
	// 2026-06-03 always-open 模式跳过 SB 反应时显示
	if (HardwareAlwaysOpenBit) return;
	//显示SB到1/100秒  2023-11-7
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"    %2d.%02dB",second,msecond/10);			//将LCD ID打印到lcd_Dis数组。				 	
		else 	sprintf((char*)lcd_Dis," %2d:%02d.%02dB",minute,second,msecond/10);				//将LCD ID打印到lcd_Dis数组。				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02dB",hour,minute,second,msecond/10);	//将LCD ID打印到lcd_Dis数组。				 	
}



void 	Display_MB_Time(u16 hour,u16 minute,u16 second,u16 msecond)			//显示盲表成绩		2023-11-7	
{
	// 2026-06-03 always-open 模式跳过 MB 时间显示
	if (HardwareAlwaysOpenBit) return;
	//显示MB到1/100秒  2023-11-7
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"    %2d.%02d*",second,msecond/10);			//将LCD ID打印到lcd_Dis数组。				 	
		else 	sprintf((char*)lcd_Dis," %2d:%02d.%02d*",minute,second,msecond/10);				//将LCD ID打印到lcd_Dis数组。				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02d*",hour,minute,second,msecond/10);	//将LCD ID打印到lcd_Dis数组。				 	
}



//2026-05-27 按游泳比赛规则计算盲表替补触板的最终成绩
//  规则 (受 Left_MB_Num / Right_MB_Num 约束实际盲表数 1/2/3):
//   1 块: 用唯一一块, 原始成绩, 不截千分位
//   2 块: 平均, 截千分位 (末位归 0, 不四舍五入)
//   3 块全有: 双相同→用相同; 全不同→中位 (按数值排序的中间值)
//   3 块仅 2 块工作 (1 块坏): 平均, 截千分位
//  参数: lane (0-9), side (0=左 1=右), result[4] 输出 hour/min/sec/msec
//  返回: 1=有有效成绩, 0=无 (valid_count==0, 调用方不发送)
u8 CalculateMBFinalTime(u8 lane, u8 side, u16 result[4])
{
	u8 mb_num, idx, bitmap, k, valid_count;
	u8 valid[3];
	u32 ms[3], final_ms, sum, m0, m1, m2;

	mb_num = (side==0) ? Left_MB_Num : Right_MB_Num;
	idx    = (side==0) ? lane : (u8)(lane+10);
	if(mb_num<1 || mb_num>3) return 0;

	bitmap = MB_Pressed_Bitmap[idx];
	valid[0]=0; valid[1]=0; valid[2]=0;
	ms[0]=0;    ms[1]=0;    ms[2]=0;
	valid_count = 0;
	final_ms = 0;

	for(k=0; k<mb_num; k++) {
		// 已按下 (bitmap bit) + 该块未坏 (MB_Open_Close_State != 3)
		if( ((bitmap>>k) & 1) && (MB_Open_Close_State[k][idx] != 3) ) {
			valid[k] = 1;
			ms[k] = (u32)MB_Result[idx][k][0]*3600000UL
			      + (u32)MB_Result[idx][k][1]*60000UL
			      + (u32)MB_Result[idx][k][2]*1000UL
			      + (u32)MB_Result[idx][k][3];
			valid_count++;
		}
	}

	if(valid_count == 0) return 0;     // 一块都没按 / 都坏 → 不发

	if(valid_count == 1) {
		// 单块: 原始, 不截千分位
		for(k=0; k<mb_num; k++) if(valid[k]) { final_ms = ms[k]; break; }
	}
	else if(valid_count == 2) {
		// 平均, 截千分位 (个位 ms 归 0)
		sum = 0;
		for(k=0; k<mb_num; k++) if(valid[k]) sum += ms[k];
		final_ms = sum / 2;
		final_ms = (final_ms / 10) * 10;
	}
	else {
		// valid_count==3, 必然 mb_num==3
		m0 = ms[0]; m1 = ms[1]; m2 = ms[2];
		if(m0 == m1)      final_ms = m0;
		else if(m0 == m2) final_ms = m0;
		else if(m1 == m2) final_ms = m1;
		else {
			// 全不同, 取中位
			if((m0>=m1 && m0<=m2) || (m0<=m1 && m0>=m2)) final_ms = m0;
			else if((m1>=m0 && m1<=m2) || (m1<=m0 && m1>=m2)) final_ms = m1;
			else final_ms = m2;
		}
	}

	result[0] = (u16)(final_ms / 3600000UL);
	final_ms %= 3600000UL;
	result[1] = (u16)(final_ms / 60000UL);
	final_ms %= 60000UL;
	result[2] = (u16)(final_ms / 1000UL);
	final_ms %= 1000UL;
	result[3] = (u16)final_ms;
	return 1;
}

void 	Process_TP_MB(void)				//处理没有触板成绩时用盲表成绩补充  2023-11-4
{
	u8 i;
	for (i=0;i<10;i++)
	{																					
																					//=0：左，=1：右
		if(Lane_TP_MB_State[i][1]==2)				//=1：右		//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
		{
			Lane_TP_MB_Time_Difference[i]++;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
			if(Lane_TP_MB_Time_Difference[i]>MBdelay_Time)
			{
				Lane_TP_MB_Time_Difference[i]=0;
				Lane_TP_MB_State[i][1]=0;
				//2026-05-27 按比赛规则计算右侧盲表最终成绩 (受 Right_MB_Num 1/2/3 块约束); _has=0 跳过显示与上报
				{
					u16 mb_final[4];
					u8 _has = CalculateMBFinalTime(i, 1, mb_final);
					if(_has) {
						// 2026-06-03 直通模式硬件 LCD 不显示成绩 (= PC 接管显示)
						if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
							Display_Laps_Place_Direct(i,1);
							Display_MB_Time(mb_final[0],mb_final[1],mb_final[2],mb_final[3]);
							Lane_Display_State[i][0]=0;
							Lane_Display_State[i][1]=1;
							Lane_Display_MSecond[i][1]=MBdelay_Time;
							LCD_ShowString(Middle_timer_posx,Middle_timer_posy+(i+1)*line_height1,180,32,32,lcd_Dis);
							Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);
						}
						//发送盲表成绩到 PC (按规则计算后的最终成绩, D4=i+10 表示右侧)
						OnSendSWCommand_Data(Touchpad_Command+0x10,Pushbutton_Result,i+10,mb_final[1],mb_final[2],mb_final[3]/10,mb_final[0]*16+mb_final[3]%10,mb_final[0],0);
						Send_Bit=2+1;
					}
				}
			}
		}
		
		if(Lane_TP_MB_State[i][0]==2)				//=0：左	//每道运动员触板和裁判按盲表状态：=0：无动作；=1：运动员触板；=2：裁判按盲表；=5：触板坏；=6：盲表坏
		{
			Lane_TP_MB_Time_Difference[i]++;	//每道运动员触板和裁判按盲表的时间差   2023-11-5
			if(Lane_TP_MB_Time_Difference[i] > MBdelay_Time)
			{
				Lane_TP_MB_Time_Difference[i]=0;
				Lane_TP_MB_State[i][0]=0;
				//2026-05-27 按比赛规则计算左侧盲表最终成绩 (受 Left_MB_Num 1/2/3 块约束); _has=0 跳过显示与上报
				{
					u16 mb_final[4];
					u8 _has = CalculateMBFinalTime(i, 0, mb_final);
					if(_has) {
						// 2026-06-03 直通模式硬件 LCD 不显示成绩 (= PC 接管显示)
						if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
							Display_Laps_Place_Direct(i,0);
							Display_MB_Time(mb_final[0],mb_final[1],mb_final[2],mb_final[3]);
							Lane_Display_State[i][0]=1;
							Lane_Display_State[i][1]=0;
							Lane_Display_MSecond[i][0]=MBdelay_Time;
							LCD_ShowString(Final_timer_posx,Final_timer_posy+(i+1)*line_height1,180,32,32,lcd_Dis);
							Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);
						}
						//发送盲表成绩到 PC (按规则计算后的最终成绩, D4=i 表示左侧)
						OnSendSWCommand_Data(Touchpad_Command+0x10,Pushbutton_Result,i,mb_final[1],mb_final[2],mb_final[3]/10,mb_final[0]*16+mb_final[3]%10,mb_final[0],0);
						Send_Bit=2+1;
					}
				}

			}
		}
		// 2026-05-31 ==7 cleanup: tp recorded this lap, after MBdelay clear back to 0 for next lap
		if(Lane_TP_MB_State[i][0]==7 || Lane_TP_MB_State[i][1]==7) {
			Lane_TP_MB_Time_Difference[i]++;
			if(Lane_TP_MB_Time_Difference[i] > MBdelay_Time) {
				Lane_TP_MB_Time_Difference[i]=0;
				if(Lane_TP_MB_State[i][0]==7) Lane_TP_MB_State[i][0]=0;
				if(Lane_TP_MB_State[i][1]==7) Lane_TP_MB_State[i][1]=0;
			}
		}
	}
}


void	Process_TP_DelayClose(void)   //处理触板TP延迟时间 2024-12-12
{
	u16 i,j;
	
	for(j=0;j<2;j++)
	{
		for(i=0;i<10;i++)
		{
			if((TP_Open_Close_State[i][j]==2))										//左边第i个触板TP是初次打开 2024-12-12，蹬出发台有效
			{
				TP_DelayClose_Time[i]++;															//接力比赛运动员触板TP信号关闭延迟时间+1 2024-12-12
				if(TP_DelayClose_Time[i]>=TP_DelayCloseValue)   //延迟时间大于预设数值 2024-11-25
				{
					TP_DelayClose_Time[i]=0;
					TP_Open_Close_State[i][j]=0;																			//触板TP关闭
					Display_TP_State(TPsx[j],TPsy[j]+(i+1)*btnhy,8,btnh,Close_Color);//左边触板TP显示 2024-12-12 
					// 2026-05-31 sync MB close with TP delay-close (3 blocks together)
					if(MB_Open_Close_State[0][(j==0)?i:(i+10)]!=3 && MB_Open_Close_State[0][(j==0)?i:(i+10)]!=4) MB_Open_Close_State[0][(j==0)?i:(i+10)]=0;
					if(MB_Open_Close_State[1][(j==0)?i:(i+10)]!=3 && MB_Open_Close_State[1][(j==0)?i:(i+10)]!=4) MB_Open_Close_State[1][(j==0)?i:(i+10)]=0;
					if(MB_Open_Close_State[2][(j==0)?i:(i+10)]!=3 && MB_Open_Close_State[2][(j==0)?i:(i+10)]!=4) MB_Open_Close_State[2][(j==0)?i:(i+10)]=0;
					Display_MB_StateGroup((u8)j, (u8)i, Close_Color, lcd_Dis);
				}
			}
		}
	}
}



void	Process_StartBox_DelayClose(void)   //处理出发台延迟时间 2025-11-25
{
	u16 i,j;
	u8 _sb_side;
//	u16	Relay_SB_DelayClose_Time[10];				//接力比赛运动员跳台出发信号关闭延迟时间 2024-11-25
//	u8	Relay_SB_DelayCloseBit[10];			//接力比赛运动员跳台出发信号关闭延迟时间位 =1：接力比赛运动员出发后开始延迟计时   =0：还不延迟计时 2024-11-25
	
	for(i=0;i<10;i++)
	{
		// 2026-06-09 扩两侧扫描: 4×50m StartPos=左 棒2 SB 在右侧 (1-Start_Dir), 原只扫 Start_Dir 永远不关. 改成哪侧 ==2 就给哪侧延迟关.
		_sb_side = 255;
		if(Startbox_Open_Close_State[i][0]==2) _sb_side = 0;
		else if(Startbox_Open_Close_State[i][1]==2) _sb_side = 1;
		if(_sb_side != 255)
		{
			Relay_SB_DelayClose_Time[i]++;							//接力比赛运动员跳台出发信号关闭延迟时间+1 2024-11-25
			if(Relay_SB_DelayClose_Time[i]>=Relay_SB_DelayCloseValue)   //延迟时间大于预设数值 2024-11-25
			{
				Relay_SB_DelayClose_Time[i]=0;
				Startbox_Open_Close_State[i][_sb_side]=0;																			//出发台关闭
				j=i+1;
				Display_Startbox_State(Startboxsx[_sb_side],Startboxsy[_sb_side]+(i+1)*btnhy+8,24,24,Close_Color);// 2026-06-09 按 _sb_side 关
			}	
		}
	}
}

//2026-05-30 SB 状态变化扫描+上报 (硬件→PC, cmd=0x1B Startbox_StateChange_Command+0x10)
//   每主循环 tick 比较 Startbox_Open_Close_State[10][2] 跟上次快照, 有变化立即上报
//   D4=Lane_NoTbl[i+side*10] (= 包含 lane+终点/远端), D5=newState (0关/1开/2延时/3坏/4未装)
void Process_StartboxStateChange(void)
{
	u8 i, j;
	// 2026-06-02 "硬件设备一直打开" 模式下不上报状态变化 cmd 0x50, PC 有自己的开关流程
	if (HardwareAlwaysOpenBit) return;
	for(i = 0; i < 10; i++) {
		for(j = 0; j < 2; j++) {
			if(Startbox_Open_Close_State[i][j] != prev_Startbox_State[i][j]) {
				//2026-05-30 D3=side(0/1) + D4=lane(0-9) 独立, 不再用 Lane_NoTbl[i+side*10] 编码
				u8 _newState = Startbox_Open_Close_State[i][j];
				OnSendSWCommand_Data(Startbox_StateChange_Command + 0x10, (u8)j,
					(u8)i, _newState, 0, 0, 0, 0, 0);
				Send_Bit = 2;
				prev_Startbox_State[i][j] = _newState;
			}
		}
	}
}

//2026-05-30 TP 状态变化扫描+上报 (硬件→PC, cmd=0x1E)
//   每主循环 tick 比较 TP_Open_Close_State[10][2] 跟 prev 快照, 有变化立即上报
void Process_TPStateChange(void)
{
	u8 i, j;
	// 2026-06-02 "硬件设备一直打开" 模式下不上报状态变化 cmd 0x51, PC 有自己的开关流程
	if (HardwareAlwaysOpenBit) return;
	for(i = 0; i < 10; i++) {
		for(j = 0; j < 2; j++) {
			if(TP_Open_Close_State[i][j] != prev_TP_State[i][j]) {
				//2026-05-30 D3=side + D4=lane 独立
				u8 _newState = TP_Open_Close_State[i][j];
				OnSendSWCommand_Data(TP_StateChange_Command + 0x10, (u8)j,
					(u8)i, _newState, 0, 0, 0, 0, 0);
				Send_Bit = 2;
				prev_TP_State[i][j] = _newState;
			}
		}
	}
}

//2026-05-30 MB 状态变化扫描+上报 (硬件→PC, cmd=0x1F, 3 块盲表 × 10 道 × 2 端)
//   MB_Open_Close_State[mb_idx][lane_side_pos], lane_side_pos = lane (0-9 终点端) or lane+10 (10-19 远端)
void Process_MBStateChange(void)
{
	u8 m, p;
	// 2026-06-02 "硬件设备一直打开" 模式下不上报状态变化 cmd 0x52, PC 有自己的开关流程
	if (HardwareAlwaysOpenBit) return;
	for(m = 0; m < 3; m++) {
		for(p = 0; p < 20; p++) {
			if(MB_Open_Close_State[m][p] != prev_MB_State[m][p]) {
				//2026-05-30 D3=side + D4=lane 独立; p = lane + side*10 拆分: lane=p%10, side=p/10
				u8 _lane = (u8)(p % 10);
				u8 _side = (u8)(p / 10);
				u8 _newState = MB_Open_Close_State[m][p];
				OnSendSWCommand_Data(MB_StateChange_Command + 0x10, _side,
					_lane, _newState, m, 0, 0, 0, 0);
				Send_Bit = 2;
				prev_MB_State[m][p] = _newState;
			}
		}
	}
}
					
//2026-05-14 Fix #2: 右"S"标的相对偏移从 +600 → +300。
//   原右"S"位于 StartFinalPlace_x0(250)+600 = 850，落进新滚动时间背景 (655..935) 之内，
//   会被覆盖。改为 +300 → 实际 X=550，位于工作状态圆点(627)之左，安全。
//   左"S"保持 StartFinalPlace_x0=250 不变。
#define	StartFinalPlace_RightOffset	300
void Display_StartFinalPlace(u16 StartFinalPlace)  //显示发令位置 2024-6-17
{
	if(((StartFinalPlace&0x03)==0x01)||((StartFinalPlace&0x03)==0x02))
	{
						//终点在泳池右边
						gui_fill_circle(StartFinalPlace_x0,StartFinalPlace_y0+cr,1.3*cr,ControlArea_Color);
						gui_fill_circle(StartFinalPlace_x0+StartFinalPlace_RightOffset,StartFinalPlace_y0+cr,1.3*cr,YELLOW);
						LCD_ShowString(StartFinalPlace_x0+StartFinalPlace_RightOffset-0.5*cr,StartFinalPlace_y0-0*cr,32,32,32,"S");		//显示str
						Start_Dir=1;							//游泳箭头指示的方向 =0：left<-right  =1：right->left； 2024-6-9
	}
	else {
						//终点在泳池左边
						gui_fill_circle(StartFinalPlace_x0,StartFinalPlace_y0+cr,1.3*cr,YELLOW);
						LCD_ShowString(StartFinalPlace_x0-0.5*cr,StartFinalPlace_y0-0*cr,32,32,32,"S");		//显示str
						gui_fill_circle(StartFinalPlace_x0+StartFinalPlace_RightOffset,StartFinalPlace_y0+cr,1.3*cr,ControlArea_Color);
						Start_Dir=0;							//游泳箭头指示的方向  =1：right->left；=0：left<-right  2024-6-9
	}
}

void		SwimmingPool_ArrageSubject(u8 SwimmingPool_Arrage)  //泳池 泳道号排列顺序	2024-6-13
{
		u16 i;		
					
		for(i=0;i<10;i++)
		{
						cmdLbtn[i]=btn_creat(carea_x0,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 改为带色按钮
						//2024-6-8
						if(SwimmingPool_Arrage==0) 
						{
							cmdLbtn[i]->caption=Hcmd_Lbtncaption_tbl[i];			//画左边按钮 正向显示道次
							Lane_NoTbl[i]=i;
						}
						else 
						{
							cmdLbtn[i]->caption=Hcmd_Inv_Lbtncaption_tbl[i];			//画左边按钮 反向显示道次
							Lane_NoTbl[i]=9-i;
						}
						Setup_LaneBtn_LightYellow(cmdLbtn[i]);
						cmdLbtn[i]->font=btnfsize;
						btn_draw(cmdLbtn[i]);		//画左边按钮

						cmdRbtn[i]=btn_creat(btndsx,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 改为带色按钮	
						//2024-6-8
						if(SwimmingPool_Arrage==0) 	
						{
							cmdRbtn[i]->caption=Hcmd_btncaption_tbl[i];			//画右边按钮 正向显示道次
							Lane_NoTbl[i+10]=10+i;
						}
						else 
						{
							cmdRbtn[i]->caption=Hcmd_Inv_btncaption_tbl[i];			//画右边按钮 反向显示道次
							Lane_NoTbl[i+10]=10+9-i;
						}
						Setup_LaneBtn_LightYellow(cmdRbtn[i]);
						cmdRbtn[i]->font=btnfsize;
						btn_draw(cmdRbtn[i]);		//画右边按钮
	}
}


void	SwimControl_init(void)			//初始化游泳控制界面  2024-10-23
{
	u16 i;
/*	
	LCD_Clear(BLUE);//LGRAY);
	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//下分界线	 
	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[22][gui_phy.language]);//显示标题  
	system_task_return=0;
*/
	POINT_COLOR=WHITE;//RED;	 
	LCD_Clear(BLUE);//GREEN);	
		
	BACK_COLOR=GRAYBLUE;//LIGHTBLUE;// DARKBLUE;//背景颜色  2024-12-2;

	//填充比赛控制区域背景  2023-11-8
	gui_fill_rectangle(carea_x0-10,carea_y0,carea_x0-10+1045,carea_y0+595,ControlArea_Color);	

	calendar_get_date(&calendar);	//更新日期		
	sprintf((char*)lcd_id,"%4d-%02d-%02d",calendar.w_year,calendar.w_month,calendar.w_date);//将LCD ID打印到lcd_Dis数组。	
	LCD_ShowString(lcddev.width-300,1,240,32*2,32,lcd_id);		//显示LCD ID	  2024-11-10    					 
	
	if(Startbtn&&Resetbtn)
	{
//			LCD_Clear(LGRAY);
	//	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//下分界线	 
	//	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[23][gui_phy.language]);//显示标题  
 	
		//2026-05-12(2nd) 6个主控按钮整体着色，与 swim_play 中保持一致
		//—— 开始计时 GREEN ——
		Startbtn->caption=Hds0_btncaption_tbl[0][gui_phy.language];
		Startbtn->font=btnfsize;
		Startbtn->bkctbl[0]=0X0420;
		Startbtn->bkctbl[1]=0X07E0;
		Startbtn->bkctbl[2]=0X07E0;
		Startbtn->bkctbl[3]=0X0500;
		Startbtn->bcfucolor=WHITE;	Startbtn->bcfdcolor=BLACK;

		//—— 复位 RED（与"退出/关机"同款。2026-05-12(3rd)）——
		Resetbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		Resetbtn->font=btnfsize;
		Resetbtn->bkctbl[0]=0X9000;
		Resetbtn->bkctbl[1]=0XF800;
		Resetbtn->bkctbl[2]=0XF800;
		Resetbtn->bkctbl[3]=0X9000;
		Resetbtn->bcfucolor=WHITE;	Resetbtn->bcfdcolor=BLACK;

		btn_draw(Startbtn);		//画按钮
		btn_draw(Resetbtn);		//画按钮


		Startbtn->caption=Hds0_btncaption_tbl[1][gui_phy.language];


		//—— 参数设置 CYAN（浅色背景 + 黑色文字） ——
		Setupbtn->caption="参数设置";
		Setupbtn->font=btnfsize;
		Setupbtn->bkctbl[0]=0X041F;
		Setupbtn->bkctbl[1]=0X07FF;
		Setupbtn->bkctbl[2]=0X07FF;
		Setupbtn->bkctbl[3]=0X0410;
		Setupbtn->bcfucolor=BLACK;	Setupbtn->bcfdcolor=WHITE;
		btn_draw(Setupbtn);		//画发送发令时刻 按钮  2024-10-23

		//—— 退出/关机 RED ——
		if(ExitShutdownbtn)
		{
			ExitShutdownbtn->bkctbl[0]=0X9000;
			ExitShutdownbtn->bkctbl[1]=0XF800;
			ExitShutdownbtn->bkctbl[2]=0XF800;
			ExitShutdownbtn->bkctbl[3]=0X9000;
			ExitShutdownbtn->bcfucolor=WHITE;
			ExitShutdownbtn->bcfdcolor=BLACK;
			ExitShutdownbtn->caption="退出/关机";
			ExitShutdownbtn->font=24;
			btn_draw(ExitShutdownbtn);
		}

		//2026-05-14 Fix #1: 从参数设置(net_test)返回后 SwimControl_init 重画主界面，
		//   但原来漏画了 NetConnbtn —— 导致按钮"消失"。这里补上即可。
		//   颜色与 swim_play() 初始化时完全一致；caption 按当前 connstatus 动态切换。
		if(NetConnbtn)
		{
			//2026-05-18(5) "网络连接"按钮配色：未连接=红色醒目；已连接(显示"网络断开")=灰红
			if(connstatus==1){	//已连接：灰红
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0X6800;
				NetConnbtn->bkctbl[2]=0X6800;	NetConnbtn->bkctbl[3]=0X4000;
			}else{	//未连接：醒目红
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0XF800;
				NetConnbtn->bkctbl[2]=0XF800;	NetConnbtn->bkctbl[3]=0X8000;
			}
			NetConnbtn->bcfucolor=WHITE;
			NetConnbtn->bcfdcolor=BLACK;
			NetConnbtn->caption=(connstatus==1)?"网络断开":"网络连接";
			NetConnbtn->font=24;
			btn_draw(NetConnbtn);
		}

		//—— 发令时刻 MAGENTA ——
		SendStartTimerbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		SendStartTimerbtn->caption="发令时刻";	//"发送发令时刻";
		SendStartTimerbtn->font=btnfsize;
		SendStartTimerbtn->bkctbl[0]=0X9010;
		SendStartTimerbtn->bkctbl[1]=0XF81F;
		SendStartTimerbtn->bkctbl[2]=0XF81F;
		SendStartTimerbtn->bkctbl[3]=0XA014;
		SendStartTimerbtn->bcfucolor=WHITE;	SendStartTimerbtn->bcfdcolor=BLACK;
		btn_draw(SendStartTimerbtn);		//画发送发令时刻 按钮  2024-9-1


	//画+1按钮
		Distance_Addbtn->caption="+1";
		Distance_Addbtn->font=btnfsize;
		btn_draw(Distance_Addbtn);		//画+1按钮

	//画-1按钮
		Distance_Decbtn->caption="-1";
		Distance_Decbtn->font=btnfsize;
		btn_draw(Distance_Decbtn);		//画-1按钮
		

//测试按钮 Test		
		Testbtn->caption=Test_btncaption_tbl[0][gui_phy.language];
		Testbtn->font=btnfsize;
		btn_draw(Testbtn);		//画按钮

//接力按钮 Relay	2024-11-21
		Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language];
		Relaybtn->font=btnfsize;
		btn_draw(Relaybtn);		//画按钮


/*  //2024-11-3
//道次反向按钮 LaneInv		2024-6-8
		LaneInvbtn->caption=Lane_Inv_btncaption_tbl[0][gui_phy.language];
		LaneInvbtn->font=btnfsize;
		btn_draw(LaneInvbtn);		//画按钮

//发令位置按钮 StartFinalPlace		2024-6-8
		StartFinalPlacebtn->caption=StartFinalPlace_btncaption_tbl[0][gui_phy.language];
		StartFinalPlacebtn->font=btnfsize;
		btn_draw(StartFinalPlacebtn);		//画按钮
*/


//		Testbtn->caption=Test_btncaption_tbl[1][gui_phy.language];
		
//准备就绪按钮 Ready —— YELLOW 背景（浅色），文字 BLACK 高对比
		Readybtn->caption=Ready_btncaption_tbl[0][gui_phy.language];
		Readybtn->font=btnfsize;
		Readybtn->bkctbl[0]=0XA500;	//暗黄边框
		Readybtn->bkctbl[1]=0XFFE0;	//亮黄顶线
		Readybtn->bkctbl[2]=0XFFE0;	//上半 亮黄
		Readybtn->bkctbl[3]=0XC600;	//下半 暗黄
		Readybtn->bcfucolor=BLACK;	Readybtn->bcfdcolor=WHITE;	//2026-05-12(2nd) 黄底黑字
		btn_draw(Readybtn);		//画按钮
		
//		Readybtn->caption=Ready_btncaption_tbl[1][gui_phy.language];
	
	
	for(i=0;i<10;i++)
	{
		CloseLanebtn[i]=btn_creat(dir_posx,carea_y0+(i+1)*btnhy,CloseLanebtn_width,btnh,0,BTN_TYPE_ANG);

		CloseLanebtn[i]->bkctbl[0]=0X6BF6;	//边框颜色
		CloseLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.第一行的颜色				
		CloseLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,上半部分颜色
		CloseLanebtn[i]->bkctbl[3]=0X2ADC;	//下半部分颜色	 
		CloseLanebtn[i]->bcfucolor=WHITE;	//松开时为白色
		CloseLanebtn[i]->bcfdcolor=BLACK;	//按下时为黑色 
//		CloseLanebtn[i]->caption=netplay_btncaption_tbl[4][gui_phy.language];
//		CloseLanebtn[i]->font=sbtnfsize;



		CloseLaneState[i]=2 ;					//关闭道次状态=2：打开；=3：关闭
		CloseLanebtn[i]->caption="打开";	//Hcmd_Lbtncaption_tbl[i];
		CloseLanebtn[i]->font=btnfsize;
		btn_draw(CloseLanebtn[i]);		//画打开/关闭道次按钮
	}	

	//取消 不要读盲表成绩  2024-10-15
/*
	for(i=0;i<10;i++)
	{
		RMBLanebtn[i]=btn_creat(RMBbtn_posx,RMBbtn_posy+(i+1)*btnhy,80,btnh,0,BTN_TYPE_ANG);

		RMBLanebtn[i]->bkctbl[0]=0X6BF6;	//边框颜色
		RMBLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.第一行的颜色				
		RMBLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,上半部分颜色
		RMBLanebtn[i]->bkctbl[3]=0X2ADC;	//下半部分颜色	 
		RMBLanebtn[i]->bcfucolor=WHITE;	//松开时为白色
		RMBLanebtn[i]->bcfdcolor=BLACK;	//按下时为黑色 

		RMBLanebtn[i]->caption="读MB";	//Hcmd_Lbtncaption_tbl[i];
		RMBLanebtn[i]->font=btnfsize;
		btn_draw(RMBLanebtn[i]);		//画打开/关闭道次按钮
	}	
*/

	
	for(i=0;i<10;i++)
	{
		
		cmdLbtn[i]=btn_creat(carea_x0,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 改为带色按钮
		//2024-6-8
		if(SwimmingPool_Arrage==0) 
		{
			cmdLbtn[i]->caption=Hcmd_Lbtncaption_tbl[i];			//画左边按钮 正向显示道次
			Lane_NoTbl[i]=i;
		}
		else 
		{
			cmdLbtn[i]->caption=Hcmd_Inv_Lbtncaption_tbl[i];			//画左边按钮 反向显示道次
			Lane_NoTbl[i]=9-i;
		}
			
		Setup_LaneBtn_LightYellow(cmdLbtn[i]);
		cmdLbtn[i]->font=btnfsize;
		btn_draw(cmdLbtn[i]);		//画左边按钮

		sprintf((char*)lcd_Dis,"L%d",(i));
		//2026-05-17 MB 左重画按 MB_Open_Close_State[0][i] 状态选色
		{
			u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i];
			if(_mbs==4)      _mbc=UnInstall_Color;
			else if(_mbs==3) _mbc=Bad_Color;
			else if(_mbs==0) _mbc=Close_Color;
			else             _mbc=Open_MB_Color;
			Display_MB_StateGroup(0,i,_mbc,lcd_Dis);
		}

		//2026-05-14 Fix #3: 重画时按 Startbox_/TP_Open_Close_State 决定颜色，
		//     原始无条件画 GREEN/YELLOW 会把 "未安装(4)" / "坏(3)" 状态覆盖掉。
		if(Startbox_Open_Close_State[i][0]==4)
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,UnInstall_Color);
		else if(Startbox_Open_Close_State[i][0]==3)
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Bad_Color);
		else if(Startbox_Open_Close_State[i][0]==0)
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Close_Color);
		else
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);

		if(TP_Open_Close_State[i][0]==4)
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,UnInstall_Color);
		else if(TP_Open_Close_State[i][0]==3)
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);
		else if(TP_Open_Close_State[i][0]==0)
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 封闭=灰
		else
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 打开=Open_TP_Color

		if(Startbox_Open_Close_State[i][1]==4)
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,UnInstall_Color);
		else if(Startbox_Open_Close_State[i][1]==3)
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Bad_Color);
		else if(Startbox_Open_Close_State[i][1]==0)
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Close_Color);
		else
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);

		if(TP_Open_Close_State[i][1]==4)
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,UnInstall_Color);
		else if(TP_Open_Close_State[i][1]==3)
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);
		else if(TP_Open_Close_State[i][1]==0)
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 封闭=灰
		else
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 打开=Open_TP_Color


			sprintf((char*)lcd_Dis,"R%d",(i));
			//2026-05-17 MB 右重画按 MB_Open_Close_State[0][i+10] 状态选色
			{
				u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i+10];
				if(_mbs==4)      _mbc=UnInstall_Color;
				else if(_mbs==3) _mbc=Bad_Color;
				else if(_mbs==0) _mbc=Close_Color;
				else             _mbc=Open_MB_Color;
				Display_MB_StateGroup(1,i,_mbc,lcd_Dis);
			}

			cmdRbtn[i]=btn_creat(btndsx,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 改为带色按钮

			//2024-6-8
			if(SwimmingPool_Arrage==0)
			{
				cmdRbtn[i]->caption=Hcmd_btncaption_tbl[i];			//画右边按钮 正向显示道次
				Lane_NoTbl[i+10]=10+i;
			}
			else
			{
				cmdRbtn[i]->caption=Hcmd_Inv_btncaption_tbl[i];			//画右边按钮 反向显示道次
				Lane_NoTbl[i+10]=10+9-i;
			}
			//2026-05-13 右道次按钮配色：淡黄色背景 + 黑字
			cmdRbtn[i]->bkctbl[0]=0XC600;	//暗黄边框
			cmdRbtn[i]->bkctbl[1]=0XFFF8;	//淡黄顶线
			cmdRbtn[i]->bkctbl[2]=0XFFF8;	//上半淡黄
			cmdRbtn[i]->bkctbl[3]=0XEFE0;	//下半略暗淡黄
			cmdRbtn[i]->bcfucolor=BLACK;
			cmdRbtn[i]->bcfdcolor=WHITE;
/*	
	u8 type;						//按钮类型
									//[7]:0,模式A,按下是一种状态,松开是一种状态.
									//	  1,模式B,每按下一次,状态改变一次.按一下按下,再按一下弹起.
									//[6:4]:保留
									//[3:0]:0,标准按钮;1,图片按钮;2,边角按钮;3,文字按钮(背景透明),4,文字按钮(背景单一)
	u8 sta;							//按钮状态
									//[7]:坐标状态 0,松开.1,按下.(并不是实际的TP状态)
									//[6]:0,此次按键无效;1,此次按键有效.(根据实际的TP状态决定)
									//[5:2]:保留
									//[1:0]:0,激活的(松开);1,按下;2,未被激活的
	u8 *caption;					//按钮名字
	u8 font;						//caption文字字体
	u8 arcbtnr;						//圆角按钮时圆角的半径										
	u16 bcfucolor; 				  	//button caption font up color
	u16 bcfdcolor; 				  	//button caption font down color

	u16 *bkctbl;					//对于文字按钮:
									//背景色表(按钮为文字按钮的时候使用)
									//a,当为文字按钮(背景透明时),用于存储背景色
									//b,当为文字按钮(背景单一是),bkctbl[0]:存放松开时的背景色;bkctbl[1]:存放按下时的背景色.
									//对于边角按钮:
									//bkctbl[0]:圆角按钮边框的颜色
									//bkctbl[1]:圆角按钮第一行的颜色
									//bkctbl[2]:圆角按钮上半部分的颜色
									//bkctbl[3]:圆角按钮下半部分的颜色	

	u8 *picbtnpathu;				//图片按钮松开时的图片路径
	u8 *picbtnpathd;		 		//图片按钮按下时的图片路径
*/

			Setup_LaneBtn_LightYellow(cmdRbtn[i]);
			cmdRbtn[i]->font=btnfsize;
			btn_draw(cmdRbtn[i]);		//画右边按钮
		}

		system_task_return=0;
	}								
			
	if(Pool50mOr25mbit==0)
	{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
	}
	else
	{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
	}		
		
										
	//2026-05-18(2) SwimControl_init 末尾不再"按 Open_State 强制重置 TP/SB/MB 数组"：
	//   原实现会把 PC 之前精细化设置的某道状态(如关单道 5)覆盖。
	//   PC 接收路径 (case Set_LaneOpenClose / Set_PoolSingleOrDoubleTP / Set_ArmDelay_Time 等) 已同步数组；
	//   net_test 内 OpenCloseTPbtn / PoolSingleOrDoubleTPbtn 切换也同步数组。
	//   此处只按"当前数组状态"重画，颜色按实际值 0/1/2/3/4 决定，不再用 Open_State 推算 col。
{
	u8 li, jj;
	u8 _mb_lbl[8];
	for (li = 0; li < 10; li++) {
		jj = li + 1;
		//TP 左
		if (TP_Open_Close_State[li][0] == 4)      Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, UnInstall_Color);
		else if (TP_Open_Close_State[li][0] == 3) Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, Bad_Color);
		else if (TP_Open_Close_State[li][0] == 0) Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, Close_Color);
		else                                       Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, Open_TP_Color);
		//TP 右
		if (TP_Open_Close_State[li][1] == 4)      Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, UnInstall_Color);
		else if (TP_Open_Close_State[li][1] == 3) Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, Bad_Color);
		else if (TP_Open_Close_State[li][1] == 0) Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, Close_Color);
		else                                       Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, Open_TP_Color);
		//SB 左
		if (Startbox_Open_Close_State[li][0] == 4)      Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, UnInstall_Color);
		else if (Startbox_Open_Close_State[li][0] == 3) Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, Bad_Color);
		else if (Startbox_Open_Close_State[li][0] == 0) Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, Close_Color);
		else                                             Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, Open_SB_Color);
		//SB 右
		if (Startbox_Open_Close_State[li][1] == 4)      Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, UnInstall_Color);
		else if (Startbox_Open_Close_State[li][1] == 3) Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, Bad_Color);
		else if (Startbox_Open_Close_State[li][1] == 0) Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, Close_Color);
		else                                             Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, Open_SB_Color);
		//MB 左/右 (按第 0 个盲表选色)
		{
			u8 _mbsL = MB_Open_Close_State[0][li];
			u8 _mbsR = MB_Open_Close_State[0][li+10];
			u16 _mbcL, _mbcR;
			if (_mbsL == 4) _mbcL = UnInstall_Color;
			else if (_mbsL == 3) _mbcL = Bad_Color;
			else if (_mbsL == 0) _mbcL = Close_Color;
			else                 _mbcL = Open_MB_Color;
			sprintf((char*)_mb_lbl, "L%d", li);
			Display_MB_StateGroup(0, jj-1, _mbcL, _mb_lbl);
			if (_mbsR == 4) _mbcR = UnInstall_Color;
			else if (_mbsR == 3) _mbcR = Bad_Color;
			else if (_mbsR == 0) _mbcR = Close_Color;
			else                 _mbcR = Open_MB_Color;
			sprintf((char*)_mb_lbl, "R%d", li);
			Display_MB_StateGroup(1, jj-1, _mbcR, _mb_lbl);
		}
	}
}

	Start_Dir=FinalPlace;		//2024-12-1
										
				if((LAll_Lap+RAll_Lap)==1)
				{
					if((StartFinalPlace&0x03)==0x02)	//  50m, 发令点在 右边 ，终点：左边 2024-11-27
					{
						LAll_Lap=1;			//2024-11-27
						RAll_Lap=0;			//2024-11-27
						Start_Dir=1;		//2024-12-1
					}
					if((StartFinalPlace&0x03)==0x03)	//  50m， 发令点在左边 ，终点：右边 2024-12-1
					{
						LAll_Lap=0;			//2024-11-27
						RAll_Lap=1;			//2024-11-27
						Start_Dir=0;		//2024-12-1
					}
				}

		if(Pool50mOr25mbit==0)	sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,标准泳池50m  2025-1-2
		else sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);														//=1,短池 25m  		2025-1-2
		LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//显示比赛距离  2026-05-12 右移140
			
		gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color); 

	Exchange_StartFinalPlace();    //交换发令点  2024-11-27	
	

		
		SwimmingPool_ArrageSubject(SwimmingPool_Arrage);   //2024-11-3

		gui_show_string("网络连接：",lcddev.width-630,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//显示“网络连接 ”  2024-10-27

			if(connstatus==0)//连接断开了,强制断开连接?
						gui_fill_circle(cds0x,1+cr,cr,Close_Color); 			//网络连接指示灯 红：连接  灰：不连接
			else 	gui_fill_circle(cds0x,1+cr,cr,Valid_Color); //连接上 ，红色 
			
		display_closetime();	//显示泳道触板关闭时间  2023-10-17
	
		LCD_ShowString(Voltage_x0,Voltage_y0,200,16,2*16,"BatVol:00.00V");//先在固定位置显示小数点  	
								
		//2026-05-18(2) 去掉 SwimControl_init 末尾的 Reset_Timer 自动调用：
		//   该自动调用会在每次从 net_test 返回/PC 发刷新命令时把 TP/SB/MB 等状态重置；
		//   按用户'该改的改、不该改的坚决不改'原则，Reset_Timer 应只由用户主动按 Resetbtn 或
		//   PC 发 Timer_Reset_Command 时触发，不应在普通重画路径上自动跑。
		//if(timer_bit==0 && Ready_timer_bit==0) Reset_Timer();
		
		//2026-05-18(4) 返回主界面/PC 刷新时画"空白占位框"：左/右成绩、名次、滚动时间、工作圆点。
		//   仅在 idle 态画 (timer_bit==0 && Ready_timer_bit==0)，比赛中不覆盖实时数据。
		if(timer_bit==0 && Ready_timer_bit==0){
			u16 _jj, _jr;
			gui_fill_circle(RunningTime_x0-32, RunningTime_y0+cr, cr, Invalid_Color); //工作状态圆点(未启动色)
			display_rollingtime();        //滚动时间(idle 态显示 0.0)
			for(_jj=0; _jj<10; _jj++){
				_jr = _jj+1;
				sprintf((char*)lcd_Dis,"          ");
				LCD_ShowString(Timer_posx[0], Timer_posy[0]+_jr*line_height1, 180, 32, 32, lcd_Dis); //左侧成绩占位
				LCD_ShowString(Timer_posx[1], Timer_posy[1]+_jr*line_height1, 180, 32, 32, lcd_Dis); //右侧成绩占位
				LCD_ShowString(Placex, Final_timer_posy+_jr*line_height1, 200, 32, 32, (u8*)"  "); //名次占位
				LLaps_diaplay(_jj);  //2026-05-19 左侧剩余圈数占位 (按当前 laps[_jj][0] 值)
				RLaps_diaplay(_jj);  //2026-05-19 右侧剩余圈数占位 (按当前 laps[_jj][1] 值)
			}
		}
}

void	Exchange_StartFinalPlace(void)    //交换发令点  2024-11-27
{			
		//终点在泳池左边
		Middle_MBsx=MBsx[1];							//泳池右边MB,SB,TP,时间显示的X方向的位置
		Middle_Startboxsx=Startboxsx[1];
		Middle_TPsx=TPsx[1];
		Middle_timer_posx=Timer_posx[1];	
		Middle_lapsx=Lapsx[1];
		
		Final_MBsx=MBsx[0];							//泳池左边MB,SB,TP,时间显示的X方向的位置  2024-6-9
		Final_Startboxsx=Startboxsx[0];
		Final_TPsx=TPsx[0];
		Final_timer_posx=Timer_posx[0];	
		Final_lapsx=Lapsx[0];
/*
	if((FinalPlace==0x00)) //非50m比赛项目  2024-11-27
	{
		//终点在泳池左边
		Middle_MBsx=MBsx[1];							//泳池右边MB,SB,TP,时间显示的X方向的位置
		Middle_Startboxsx=Startboxsx[1];
		Middle_TPsx=TPsx[1];
		Middle_timer_posx=Timer_posx[1];	
		Middle_lapsx=Lapsx[1];
		
		Final_MBsx=MBsx[0];							//泳池左边MB,SB,TP,时间显示的X方向的位置  2024-6-9
		Final_Startboxsx=Startboxsx[0];
		Final_TPsx=TPsx[0];
		Final_timer_posx=Timer_posx[0];	
		Final_lapsx=Lapsx[0];
	}
	else {
		//终点在泳池右边
		Middle_MBsx=MBsx[0];							//泳池左边MB,SB,TP,时间显示的X方向的位置
		Middle_Startboxsx=Startboxsx[0];
		Middle_TPsx=TPsx[0];
		Middle_timer_posx=Timer_posx[0];	
		Middle_lapsx=Lapsx[0];

		Final_MBsx=MBsx[1];							//泳池右边MB,SB,TP,时间显示的X方向的位置  2024-6-9
		Final_Startboxsx=Startboxsx[1];
		Final_TPsx=TPsx[1];
		Final_timer_posx=Timer_posx[1];	
		Final_lapsx=Lapsx[1];
	}
	*/
	if((StartPlace==0x01)) //  50m比赛项目 2024-11-27
	{
		if((FinalPlace==0x00)) //在终点在泳池左边  2024-11-27
		{
			//发令点在泳池右边
//			Display_StartFinalPlace(StartFinalPlace);			//2024-6-17

			//泳池左边SB，时间显示的X方向的位置
//			Middle_Startboxsx=Startboxsx[0];
//			Middle_timer_posx=Timer_posx[0];	

			//泳池右边SB,时间显示的X方向的位置  2024-6-9
			Final_Startboxsx=Startboxsx[1];
	//		Final_timer_posx=Timer_posx[1];	
		}
		else {
					//发令在泳池左边
//			Display_StartFinalPlace(StartFinalPlace);			//2024-11-27
//			gui_fill_circle(StartFinalPlace_x0,StartFinalPlace_y0+cr,1.3*cr,YELLOW); 					
//			LCD_ShowString(StartFinalPlace_x0-0.5*cr,StartFinalPlace_y0-0*cr,32,32,32,"S");		//显示str	      					 

	//		Middle_Startboxsx=Startboxsx[1];
	//		Middle_timer_posx=Timer_posx[1];	

			//泳池左边SB,时间显示的X方向的位置  2024-6-9
			Final_Startboxsx=Startboxsx[1];
	//		Final_timer_posx=Timer_posx[0];	
		}
	}
				
	Display_StartFinalPlace(StartFinalPlace);			//2024-11-27

}	

//#define SD_CARD 0 //SD卡,卷标为0
//#define EX_FLASH 1 //外部spi flash,卷标为 1
//#define EX_NAND 2 //外部 nand flash,卷标为 2

void OnReadMatchData()   	//读比赛数据  2025-1-26
{
 	FIL* fp=0;		//存储文件	
	u8 res;
	u8 rval=0;
//	u8 *pname=0; 
	u8 *pdatabuf;
	u8* databuf;	
	
	const char *name="2:/swimtime.cfg";

  fp=(FIL *)gui_memin_malloc(sizeof(FIL));			//开辟FIL字节的内存区域  
//	pname=gui_memin_malloc(120);							//申请60个字节内存,类似"0:RECORDER/REC20120321210633.wav" 

//	if(!fp||!pname) rval=1;
	if(!fp) rval=1;
 	else
	{
		res=f_open(fp,(const TCHAR*)name,FA_READ);//打开文件夹  读文件
		if(res==FR_OK)
		{
			databuf=(u8*)gui_memex_malloc(fp->obj.objsize);	//为数据开辟缓存地址
			if(databuf==0) 
			{
				res=f_open(fp,(const TCHAR*)name,FA_CREATE_ALWAYS|FA_WRITE);//打开文件夹 写文件
				
				//2026-05-26 (需求 17): 扩充存盘字段, 让板上 NAND Flash (2:/, FatFs 卷 2, 不是 SD 卡) 也保存比赛距离/趟数/接力标志/设备状态等。
		//   旧 15 字段保持顺序不变(向后兼容旧 cfg), 新增字段追加在末尾:
		//   All_Lap / LAll_Lap / RAll_Lap = 比赛距离总趟数 + 左右触板次数 (核心)
		//   RelayBit                       = 接力项目标志
		//   Open_State                     = 全部TP/SB/MB开关状态(0=按事件流转, 1=全开)
		//   Laps_No                        = 比赛距离索引(供本地+1/-1按钮)
		sprintf((char*)databuf,"%d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d \n\0",
			StartFinalPlace,StartPlace,FinalPlace,Close_Time,SwimmingPool_Arrage,tport,
			Result_Display_Time,TP_DelayCloseValue,Relay_SB_DelayCloseValue,MBdelay_Time,
			Pool50mOr25mbit,PoolSingleOrDoubleTPbit,All_Close_Time,Left_MB_Num,Right_MB_Num,
			All_Lap,LAll_Lap,RAll_Lap,RelayBit,Open_State,Laps_No,StartBox_Edge_Bit,FalseStartThreshold);
				
				res=f_write(fp,databuf,strlen((char*)databuf),(UINT*)&bw);//写入文件
				
				if(res)
				{
					printf("write error:%d\r\n",res);
				}
				f_close(fp);

			}	
			else 
			{
				res=f_read(fp,databuf,fp->obj.objsize,(UINT*)&br);	//一次读取整个文件
				//2026-05-26 (需求 17 配套): 读取扩充字段, 向后兼容旧 cfg(15 字段) — sscanf 不到的字段保留全局默认
				//2026-05-26 修 #181-D 类型不匹配 bug: sscanf %d 期望 int*, 但全局变量是 u8/u16,
				//   直接传 &u16 会让 sscanf 写 4 字节, 破坏相邻内存。改用 int 临时数组接收, 再 cast 赋回。
				{
					int t[23] = {0};
					sscanf((char*)databuf,"%d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d ",
						&t[0],&t[1],&t[2],&t[3],&t[4],&t[5],&t[6],&t[7],&t[8],&t[9],
						&t[10],&t[11],&t[12],&t[13],&t[14],&t[15],&t[16],&t[17],&t[18],&t[19],
						&t[20],&t[21],&t[22]);
					StartFinalPlace          = (u8) t[0];
					StartPlace               = (u8) t[1];
					FinalPlace               = (u8) t[2];
					Close_Time               = (u16)t[3];
					SwimmingPool_Arrage      = (u8) t[4];
					tport                    = (u16)t[5];
					Result_Display_Time      = (u16)t[6];
					TP_DelayCloseValue       = (u16)t[7];
					Relay_SB_DelayCloseValue = (u16)t[8];
					MBdelay_Time             = (u16)t[9];
					Pool50mOr25mbit          = (u8) t[10];
					PoolSingleOrDoubleTPbit  = (u8) t[11];
					All_Close_Time           = (u16)t[12];
					Left_MB_Num              = (u8) t[13];
					Right_MB_Num             = (u8) t[14];
					All_Lap                  = (u16)t[15];
					LAll_Lap                 = (u16)t[16];
					RAll_Lap                 = (u16)t[17];
					RelayBit                 = (u8) t[18];
					Open_State               = (u8) t[19];
					Laps_No                  = (u8) t[20];
					StartBox_Edge_Bit        = (u8) t[21];
					FalseStartThreshold      = (u16)t[22];
				}
				//2026-05-26 合理性检查 (旧 cfg 异常或字段缺失时, 用合理默认值兜底, 避免开机后字段为 0):
				if(Close_Time            == 0) Close_Time            = SW_50M_Close_Time;
				if(All_Close_Time        == 0) All_Close_Time        = 400;
				if(Result_Display_Time   == 0) Result_Display_Time   = Result_Display_Time_Value;
				if(TP_DelayCloseValue    == 0) TP_DelayCloseValue    = 40;
				if(Relay_SB_DelayCloseValue == 0) Relay_SB_DelayCloseValue = 30;
				if(MBdelay_Time          == 0) MBdelay_Time          = 50;
				if(Left_MB_Num           == 0) Left_MB_Num           = 2;
				if(Right_MB_Num          == 0) Right_MB_Num          = 1;
				if(tport                 == 0) tport                 = 8088;
				if(All_Lap               == 0) All_Lap               = laps_No_tbl[Laps_No];
				if(LAll_Lap + RAll_Lap == 0){ LAll_Lap = Llaps_No_tbl[Laps_No]; RAll_Lap = Rlaps_No_tbl[Laps_No]; }
				if(FalseStartThreshold   == 0) FalseStartThreshold   = 10;                        //抢跳阈值默认 0.1s
				f_close(fp);
			}
		}
	}
	//释放内存
 	gui_memin_free(fp);
//	gui_memin_free(pname);  
	gui_memex_free(databuf);
//	databuf=0;				//清零
	
}


void OnWriteMatchData()   	//存储比赛数据  2025-1-26
{
	FIL* fp=0;		//存储文件	

	u8 res;
	u8 rval=0;
//	u8 *pname=0; 
	u8 *pdatabuf;
	u8* databuf;	//
	const char *name="2:/swimtime.cfg";

  	
	fp=(FIL *)gui_memin_malloc(sizeof(FIL));			//开辟FIL字节的内存区域  
//	pname=gui_memin_malloc(120);							//申请120个字节内存,类似"0:RECORDER/REC20120321210633.wav" 
	
//	if(!fp||!pname)rval=1;
	if(!fp)rval=1;
 	else
	{
		res=f_open(fp,(const TCHAR*)name,FA_CREATE_ALWAYS|FA_WRITE);//打开文件夹 写文件
					
		if(res)//文件创建失败
		{
			rval=0XFE;//提示是否存在SD卡
		}
		else 
		{
			databuf=(u8*)gui_memex_malloc(fp->obj.objsize);	//为数据开辟缓存地址
			//2026-05-26 (需求 17): 扩充存盘字段, 让板上 NAND Flash (2:/, FatFs 卷 2, 不是 SD 卡) 也保存比赛距离/趟数/接力标志/设备状态等。
		//   旧 15 字段保持顺序不变(向后兼容旧 cfg), 新增字段追加在末尾:
		//   All_Lap / LAll_Lap / RAll_Lap = 比赛距离总趟数 + 左右触板次数 (核心)
		//   RelayBit                       = 接力项目标志
		//   Open_State                     = 全部TP/SB/MB开关状态(0=按事件流转, 1=全开)
		//   Laps_No                        = 比赛距离索引(供本地+1/-1按钮)
		sprintf((char*)databuf,"%d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d \n\0",
			StartFinalPlace,StartPlace,FinalPlace,Close_Time,SwimmingPool_Arrage,tport,
			Result_Display_Time,TP_DelayCloseValue,Relay_SB_DelayCloseValue,MBdelay_Time,
			Pool50mOr25mbit,PoolSingleOrDoubleTPbit,All_Close_Time,Left_MB_Num,Right_MB_Num,
			All_Lap,LAll_Lap,RAll_Lap,RelayBit,Open_State,Laps_No,StartBox_Edge_Bit,FalseStartThreshold);
				
 			res=f_write(fp,databuf,strlen((char*)databuf),(UINT*)&bw);//写入文件
				
			if(res)
			{
					printf("write error:%d\r\n",res);
			}
			f_close(fp);
		} 
	}
	//释放内存
 	gui_memin_free(fp);
//	gui_memin_free(pname);  
	gui_memex_free(databuf);
//	databuf=0;				//清零
}

//2026-05-26 用户要求: 设备状态数组 (TP/SB/MB) 持久化到独立文件 2:/swimdev.cfg
//   保存 100 字段: TP_Open_Close_State[10][2] + Startbox_Open_Close_State[10][2] + MB_Open_Close_State[3][20]
//   状态值: 0=关闭(正常), 1=打开(正常), 3=损坏, 4=未安装。"全好" = !=3 && !=4
//   读取: swim_play 启动时若 swimdev.cfg 不存在, 字段保留全局默认 (通常 0 = 关闭/正常 = "全好")
void OnReadDeviceData(void)
{
	FIL* fp=0;
	u8 res;
	u8* databuf=0;
	const char *name="2:/swimdev.cfg";

	fp=(FIL *)gui_memin_malloc(sizeof(FIL));
	if(!fp) return;

	res=f_open(fp,(const TCHAR*)name,FA_READ);
	if(res==FR_OK)
	{
		if(fp->obj.objsize > 0)
		{
			databuf=(u8*)gui_memex_malloc(fp->obj.objsize + 1);
			if(databuf)
			{
				res=f_read(fp,databuf,fp->obj.objsize,(UINT*)&br);
				databuf[fp->obj.objsize]=0;	//确保 null 结尾
				{
					char *_p = (char*)databuf;
					u8 _ti, _tj;
					//—— 解析 TP_Open_Close_State[10][2] ——
					for(_ti=0; _ti<10; _ti++) for(_tj=0; _tj<2; _tj++){
						while(*_p == ' ' || *_p == '\t' || *_p == '\r' || *_p == '\n') _p++;
						if(!*_p) goto _eod;
						TP_Open_Close_State[_ti][_tj] = (u8)strtol(_p, &_p, 10);
					}
					//—— 解析 Startbox_Open_Close_State[10][2] ——
					for(_ti=0; _ti<10; _ti++) for(_tj=0; _tj<2; _tj++){
						while(*_p == ' ' || *_p == '\t' || *_p == '\r' || *_p == '\n') _p++;
						if(!*_p) goto _eod;
						Startbox_Open_Close_State[_ti][_tj] = (u8)strtol(_p, &_p, 10);
					}
					//—— 解析 MB_Open_Close_State[3][20] ——
					for(_ti=0; _ti<3;  _ti++) for(_tj=0; _tj<20; _tj++){
						while(*_p == ' ' || *_p == '\t' || *_p == '\r' || *_p == '\n') _p++;
						if(!*_p) goto _eod;
						MB_Open_Close_State[_ti][_tj] = (u8)strtol(_p, &_p, 10);
					}
					_eod: ;
				}
				gui_memex_free(databuf);
			}
		}
		f_close(fp);
	}
	gui_memin_free(fp);
}

//2026-05-26 用户要求: 把 TP/SB/MB 设备状态数组写入 2:/swimdev.cfg
//   触发点: case Set_TPSBMB_State (0x46) / case Set_PoolSingleOrDoubleTP (0x3A) 等修改设备状态的处理之后
void OnWriteDeviceData(void)
{
	FIL* fp=0;
	u8 res;
	u8* databuf=0;
	const char *name="2:/swimdev.cfg";

	fp=(FIL *)gui_memin_malloc(sizeof(FIL));
	if(!fp) return;

	res=f_open(fp,(const TCHAR*)name,FA_CREATE_ALWAYS|FA_WRITE);
	if(res==FR_OK)
	{
		//100 字段 × ~4 字符 ≈ 400 字节, 分配 1KB 余量
		databuf=(u8*)gui_memex_malloc(1024);
		if(databuf)
		{
			char *_p = (char*)databuf;
			u8 _ti, _tj;
			for(_ti=0; _ti<10; _ti++) for(_tj=0; _tj<2; _tj++) _p += sprintf(_p, "%d ", TP_Open_Close_State[_ti][_tj]);
			for(_ti=0; _ti<10; _ti++) for(_tj=0; _tj<2; _tj++) _p += sprintf(_p, "%d ", Startbox_Open_Close_State[_ti][_tj]);
			for(_ti=0; _ti<3;  _ti++) for(_tj=0; _tj<20; _tj++) _p += sprintf(_p, "%d ", MB_Open_Close_State[_ti][_tj]);
			*_p++ = '\n'; *_p = '\0';
			res=f_write(fp, databuf, strlen((char*)databuf), (UINT*)&bw);
			gui_memex_free(databuf);
		}
		f_close(fp);
	}
	gui_memin_free(fp);
}
