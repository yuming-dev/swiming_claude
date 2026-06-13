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
//SWIM STM32H7���Ŀ���
//��ʱ�� ��������	   
//��������:2023/10/24
//�汾��V1.3
//��Ȩ���У�����ؾ���
//Copyright(C) �������ڴ�����Ϣ�����ɷ����޹�˾�����ֹ�˾ 2024-2029
//All rights reserved									  
//********************************************************************************
//�޸�˵��
//20231107
//ɾ����������ʾ���� ������������ʾ����
//20231108
//ɾ����������ʾ����
////////////////////////////////////////////////////////////////////////////////// 	 
//********************************************************************************
//�޸�˵��
//20240201
//���ӳ���̨�����ؼ�⹦�ܣ�����Ӧ���ֳ���̨��һ�֣��½�����Ч����һ�֣���������Ч����Ҫ��
//����StartBox_Edge_Bit :=1:�½�����Ч��=0����������Ч
//2024-03-28
//�޸ĳ��� ��û�д���ɼ���ä���ɼ�����ʱ���м���ʾ�ɼ�������������������ʾ��
//���Ӵ���ɼ���ʾλ������ʾ����ɼ��󣬾Ͳ�����ʾä���ɼ�TP_Display_State[10][2],=0:û��ʾ��=1��������ʾTP�ɼ�
//2024-06-8��9
//�޸ĳ��� ���Ӹı䷢���͵��κ�˳���ܰ���
//2024-06-10
//���� ���κŲ��ұ����飬�����κ�˳��ı�ʱ���������ݸ��Ÿı�

//2024-6-12
//��ʼ������IP��ַ�� 	:192.168.1.100
//Ŀ������IP��ַ��	:192.168.1.108
//2024-6-13
//�����ͨ������������Ƹı����˳��źͷ����λ��
//2024-7-15
//����������׼����������������յ��������׼������������󣬿��������͡�׼������������������

	//2024-10-15  ȡ����ä����ť0-9��
	//2024-10-17  ���粻��ʾ���պͷ������ݵ�����
	//2024-11-10  ������ʾ���ڡ�ʱ����Ϣ  
	//2024-11-10  �������ô�����ʱ��༭����
		//					�����÷���λ�ð������ܣ��������ƴ��ڸĵ����������Ӵ���
	//2024-11-21	�޸ģ��ֱ���ʾ��������ʣ��Ȧ����x0  2024-11-21
	//						���ӽ�������λ=0���ǽ���������=1����������
	// ���ӽ����ڼ�����  ���� Baton_No[10];
		
	//2024-11-24	�޸����Ҵ����˶�Ա��赴���
	//						��д������������̨��״̬���� ��������̨���

//2024-11-25  ����̨�źż�����⣺
//		1.����ʱ������ǹ����ӳ�һ��ʱ�����5�룩������̨�رգ����ٽ����ź�
//		2.������Ŀ���Ե�2��3��4�������˶�Ա������ӳ�һ��ʱ�����5�룩������̨�رգ����ٽ����ź�
//		3.����̨��ΪGreen,��һ�γ����ź����󣬳���̨��ΪYellow���ӳ�ʱ��󣬱�ΪBlack;

//2024-11-27  0����Left��  ��  1���ң�Right)
//Startbox_Open_Close_State[10][2];


//#define SD_CARD 0 //SD��,����Ϊ0
//#define EX_FLASH 1 //�ⲿspi flash,����Ϊ 1
//#define EX_NAND 2 //�ⲿ nand flash,����Ϊ 2

//2024-12-7 ��д�洢�Ͷ�������ĳ��� OnReadMatchData();	OnWriteMatchData();
//2024-12-8 ��д ������ص�ѹ������PA5�ڣ���ص�ѹ�������ѹ�����뵽PA5,��A/Dת���󣬼������Ӧ�ĵ�ѹֵ������10.5V����ʾ���
//2024-12-9 ��д�������Ƿ�رմ����ʱ״̬λOpen_State��=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴��塣

//2024-12-12  ���Ӵ��������ӳ�ʱ����� �Է��󴥷���û���������˶�Ա�����ź�
//2024-12-14  ��������"�ɼ���ʾʱ��(S):","������ӳ�ʱ��(S):","����̨���ӳ�ʱ��(S):","ä������ɼ��ӳ�ʱ��(S):"
//2024-12-15   ������ʾ"����״̬��"

//2024-12-17 ��TP_DelayCloseValue=0��Relay_SB_DelayCloseValue=0ʱ��ֱ�ӹر��ӳٹ���
//						��Result_Display_Time����0ʱ����Ϊ10����ֵ����Ϊ0����СΪ10
//2024-12-22 ���Բ��Բ��ֳ��򣺲���ʾ��Ӿ�����ڳ���̨�������У�����һ�δ�����ɾ��

////////////////////////////////////////////////////////////////////////////////// 	 
//2025-1-5  ��������"50m/25mӾ��","��/���˰�װ����"����

//2025-1-26  �޸Ĵ洢�Ͷ�ȡ��ʱ�����ļ������Ӵ洢�Ͷ�ȡLeft_MB_Num,Right_MB_Num����

////////////////////////////////////////////////////////////////////////////////// 	 


u8 StartBox_Edge_Bit=1;  //2024-2-1
u8	StartBox_Bit=0;  			//��������̨��־λ  2024-11-25
u8	TP_Bit=0;  			//���������־λ  2024-12-12

u8 Pool50mOr25mbit=0;		//Ӿ���Ǳ�׼50m=0; ;�̳�25m=1;   2025-1-2
u8 PoolSingleOrDoubleTPbit=0;	//Ӿ�ذ�װ������һ��=1; ����=0  2025-1-2



u8 led0sta=1,led1sta=1;
u8  Check_State_Bit=0;

//TCP IP Server
u16 		recLength;
u8		FLAG;

u8		TCPIP_CommandBuf[RX_DATA_MaxLEN];						//�����������  2023-8-11
u16 	TCPIP_send_len,TCPIP_recv_len;
u8		TCPIP_send_buff[UART4_MAX_SEND_LEN];							//�����������  2023-8-11
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

u8 RXD_Data_Buffer[4*TxRx_Data_Length];		//���յ����������   2023-10-24

u16 TCPIP_Rec_Char_Ptr;


u8 SW_Command0,SW_Command1;
u8 SW_Start_Num,SW_StartingBlock_Num;
u8 Control_Port_Num;

u8 Left_MB_Num=2;				//��� ä������  �������
u8 Right_MB_Num=1;				//�ұ� ä������  �������
u8 L_MB_State_Line[3];		//��� ä����״̬���ӻ��ǲ�����
u8 R_MB_State_Line[3];		//�ұ� ä����״̬���ӻ��ǲ�����

u8	Open_State=0;						//�����Ƿ�رմ����ʱ״̬λ��=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴��塣2024-12-9
u8	g_in_net_test=0;					//2026-05-16 ��ǵ�ǰ�Ƿ��� net_test(��������)�ӽ��棺=1 ʱ���� Display_TP/SB/MB_State ��ͼ��������Ⱦ�ý���

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
//netplay��ʾ��Ϣ
/*
u8*const netplay_remindmsg_tbl[5][GUI_LANGUAGE_NUM]=
{
{"���������!���ڳ�ʼ������...","Ո����W��!���ڳ�ʼ���W��...","Pls insert cable!Ethernet Initing..",}, 
{"��ʼ��ʧ��!��������!","��ʼ��ʧ��!Ո�z��W��!","Init failed!Check the cable!",},  
{"����DHCP��ȡIP...","����DHCP�@ȡIP...","DHCP IP configing...",},  
{"DHCP��ȡIP�ɹ�!","DHCP�@ȡIP�ɹ�!","DHCP IP config OK!",},  
{"DHCP��ȡIPʧ��,ʹ��Ĭ��IP!","DHCP�@ȡIPʧ��,ʹ��Ĭ�JIP!","DHCP IP config fail!Use default IP",},  
};

//netplay IP��Ϣ

u8*const netplay_ipmsg[5][GUI_LANGUAGE_NUM]=
{
{"����MAC��ַ:","���CMAC��ַ:","Local MAC Addr:",}, 
{" Զ��IP��ַ:"," �h��IP��ַ:","Remote IP Addr:",}, 
{" ����IP��ַ:"," ����IP��ַ:"," Local IP Addr:",}, 
{"   ��������:","   �ӾW�ڴa:","   Subnet MASK:",},
{"       ����:","       �W�P:","       Gateway:",},  
}; 
//������ʾ 
//u8*const netplay_netspdmsg[GUI_LANGUAGE_NUM]={"   �����ٶ�:","   �W�j�ٶ�:","Ethernet Speed:"};
//netplay ������ʾ��Ϣ
u8*const netplay_testmsg_tbl[3][GUI_LANGUAGE_NUM]=
{
{"�ɼ������״̬.","�əz���B�Ӡ�B.","to check the connection.",}, 
{"2,�����������:","2,�ڞg�[��ݔ��:","2,Input:",}, 	
{"�ɵ�¼web���档","�ɵ��web���档","in browser,you can log on to website.",}, 	
};
//netplay memo��ʾ��Ϣ
u8*const netplay_memoremind_tb[2][GUI_LANGUAGE_NUM]=
{
{"������:","���Յ^:","Receive:",},
{"������:","�l�ͅ^:","Send:",},
};
//netplay ���԰�ť����
u8*const netplay_tbtncaption_tb[GUI_LANGUAGE_NUM]={"��ʼ����","�_ʼ�yԇ","Start Test",};
//netplay Э�����
u8*const netplay_protcaption_tb[GUI_LANGUAGE_NUM]={"Э��","�f�h","PROT",};
//netplay Э������
u8*const netplay_protname_tb[3]={"TCP Server","TCP Client","UDP",};
//netplay �˿ڱ���
u8*const netplay_portcaption_tb[GUI_LANGUAGE_NUM]={"�˿�:","�˿�:","Port:",};
//netplay IP��ַ����
u8*const netplay_ipcaption_tb[2][GUI_LANGUAGE_NUM]=
{
{"Ŀ��IP:","Ŀ��IP:","Target IP:",},
{"����IP:","���CIP:"," Local IP:",},
};
//netplay ��ť����
u8*const netplay_btncaption_tbl[5][GUI_LANGUAGE_NUM]=
{
{"Э��ѡ��","�f�h�x��","PROT SEL",},
{"����","�B��","Conn",},
{"�Ͽ�","���_","Dis Conn",},
{"�������","�������","Clear",},
{"����","�l��","Send",},
};
//����ģʽѡ��
u8*const netplay_mode_tbl[3]={"TCP Server","TCP Client","UDP"};
//����������ʾ��Ϣ
u8*const netplay_connmsg_tbl[4][GUI_LANGUAGE_NUM]=
{
{"��������...","�����B��...","Connecting...",},
{"����ʧ��!","�B��ʧ��!","Connect fail!",},
{"���ӳɹ�!","�B�ӳɹ�!","Connect OK!",},
{"LwIP����!","LwIP�e�`!","LwIP Error!",}, 
};
*/


u16 hour,minute,second;
u16 msecond;

u16 Start_hour,Start_minute,Start_second,Start_msecond;   //2024-8-31 �����Ӧʱ�̵�ʱ��

//2026-05-11 ����������������ʱ������/��/���룬10ms �ֱ��ʣ�
//             ��"׼������"���º�ʼ��������һֱ�ۼӣ�����ǰ�����ˣ���
//             ���Լ�¼��������̨�źų���ʱ�̵Ķ������Ա��ڷ����"����ʱ��"
//             ���������ÿ����Է����ʱ�䣨�ɸ���������/���棩��
u16 PreStart_minute=0,PreStart_second=0,PreStart_msecond=0;

//2026-05-11 ����ǹ��ʱ����������ʱ���еĶ�����=����˲��� PreStart_*��
u16 Gun_minute=0,Gun_second=0,Gun_msecond=0;

//2026-05-11 ÿ���˶�Ա����̨������ʱ�䣨��������ʱ���еĶ�����  [i][0]:�� [i][1]:��
//          ��δ������������ǣ����һ����Ч������������ʾ/����
u16 LaneStart_minute[10][2]={{0}},LaneStart_second[10][2]={{0}},LaneStart_msecond[10][2]={{0}};
u8  LaneStart_Valid[10][2]={{0}};     //=1:�Ѽ�¼������̨�ź�; =0:�����ݣ���ʱ��ʾ"--"
u8  LaneStart_Computed[10][2]={{0}};
//2026-05-31 relay reaction time computation (hardware side): TouchPad_Process stores last touch time per lane, StartBox_RecordSignal in relay leg reads delta as reaction
u16 LastTouchTime_minute[10]={0}, LastTouchTime_second[10]={0}, LastTouchTime_msecond[10]={0};
u8  LastTouchTime_Valid[10]={0};  //=1:���ڴ��ڽ�������㲢�㲥�����ʱ�䣬=0:������

//2026-05-11 ��������̨���Ŵ��ڣ�Ĭ�� 3s = 300��10ms���ļ�ʱ��/����λ
u16 PostGun_OpenWait_Time=0;          //������ѵȴ�ʱ�䣨10ms Ϊ��λ��
u8  GunFired_PostOpenDoneBit=0;       //=0:����δ���� =1:���ڽ�������ѭ����ʼ���㲢�㲥ÿ�����

u16	Final_timer_posx=220,Final_timer_posy=40;	
u16	Middle_timer_posx=480,Middle_timer_posy=40;	

//u16 Result[50][10][2][5][4];   //�ɼ� �ڼ���  ����ɼ� ���� ��0/��1 ����/����/ä��1/ä��2/ä��3 ʱ/��/��/ǧ��֮һ��
//u8 Result[10][10][2][5][4];   //�ɼ� �ڼ���  ����ɼ� ���� ��0/��1 ����/����/ä��1/ä��2/ä��3 ʱ/��/��/ǧ��֮һ��

//u8 Result_TP[10][10][6];		//�ɼ� �ڼ���  �ڼ��� �ڼ���  ����ɼ� ���� ��0/��1 ����/����/ä��1/ä��2/ä��3 ʱ/��/��/ǧ��֮һ��


u8  Race_No[10][2],Lane;		//
u16	All_Lap;			//�ε���������50�ף�  2023-10-16
u16	LAll_Lap;			//�ε������������50�ף�  2024-11-24
u16	RAll_Lap;			//�ε��ұ���������50�ף�  2024-11-24

u8 Dir_Dis[16];				//���LCD ID�ַ���

u8 scmd_buf[TxRx_Data_Length+16];				//��ŷ����������

u8 lcd_id[32*2];				//���LCD ID�ַ���
u8	timer_bit;				//��ʱλ=0������ʱ��=1����ʼ��ʱ
u8	Ready_timer_bit;				//׼��������ʱ����ʼ��ʱ����ʱλ=0������ʱ��=1����ʼ��ʱ 2024-8-31


//[i][0]:��� ��[i][1]:�ұ� ��  2024-11-27
u16 Lane_Display_MSecond[10][2];		//ÿ���ɼ�����ʾʱ��3000����
u8 	Lane_Display_State[10][2];				//ÿ���ɼ�����ʾ״̬=0������ʾ��=1����ʾ
u8	TP_Display_State[10][2];				//=1��TP�ɼ�������ʾ��=0��TP�ɼ�û����ʾ  2024-3-28

u8 	Lane_TP_MB_State[10][2];							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
u16 Lane_TP_MB_Time_Difference[10];	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5

u8	TP_MB_Bit;  //���������ä��֮���ϵ��־λ  2023-11-5

u16	dir_posx,dir_posy;
u16	Start_Dir;								//������Ӧ��Ӿ����   2024-6-9
u16	RMBbtn_posx,RMBbtn_posy;

u16 Open_Color=GUI_COLOR_WHITE;			//TP ��

u16 Open_TP_Color=YELLOW;			//TP �򿪣��˶�Ա���Դ��壺������гɼ�
u16 Open_SB_Color=GREEN;			//SB �򿪣� �˶�Ա���Գ������г�����Ӧʱ��
u16 Delay_Color=0XD000;	//GUI_COLOR_WHITE;			//TP SB �ӳ��ڣ��˶�Ա���Գ������г�����Ӧʱ��  2024-11-25
u16 Open_MB_Color=YELLOW;			//MB �򿪣����п��԰�ä������ä���ɼ� 
u16 Close_Color=GRAY;				//TP �رգ��˶�Ա�����û�ɼ���������Ч
u16 Valid_Color=RED;				//TP �������гɼ�����ɫ��ʾ
u16 Invalid_Color=GRAYBLUE;		//TP δ��������ɫ��ʾ
u16 Bad_Color=BLACK;					//TP ������ɫ��ʾ
u16 UnInstall_Color=GUI_COLOR_BLACK;					//û�а�װ����ɫ��ʾ  2025-1-6

u16 ControlArea_Color=0X2ACC;	//0X2ADC;			//����������ɫ	 2023-11-8

u8 TP_Wait_Open_Time[10];		//10�� ���屻������һ�ߴ���ȴ�ʱ�� ��Ŵ�
u8 TP_Open_Close_State[10][2];			//10�� ����״̬���򿪻�ر� =0���رգ�=1���򿪡�=2���ӳٹرգ���״̬��=3����  =4��û�а�װ�����߰�װ��  =0����=1���ң�   2025-1-6
u8 Startbox_Open_Close_State[10][2];		//10�� ����̨��=0:��:0-9; =1:��:0-9  ״̬���򿪻�رգ�=0���رգ�=1���򿪣�=2���ӳٹرգ���״̬���� =3����  =4��û�а�װ�����߰�װ��  2025-1-6
u8 prev_Startbox_State[10][2] = {{0}};	//2026-05-30 SB ״̬�ϴο���, ����ǰ�ȶԴ����ϱ�
u8 prev_TP_State[10][2] = {{0}};	//2026-05-30 TP ״̬�ϴο���
u8 prev_MB_State[3][20] = {{0}};	//2026-05-30 MB ״̬�ϴο��� [mb_idx][lane*2+side]
u8 MB_Open_Close_State[3][20];			//10�� ä������:0-9����:10-19    ״̬���򿪻�رգ�=0���رգ�=1���򿪣� =3����  =4��û�а�װ�����߰�װ�� 2025-1-6


//2026-05-27 �����������: ֧��ÿ�� 3 ��ä��������ɼ�, �� CalculateMBFinalTime ������������ճɼ�
//  MB_Result[20][3][4]: [��(��0-9 ��10-19)][�ڼ��� 0/1/2][hour/min/sec/msec]
//  MB_Pressed_Bitmap[20]: [��] bit0/1/2 = �� 1/2/3 ���Ƿ��Ѱ���
u16 MB_Result[20][3][4];
u8  MB_Pressed_Bitmap[20];

u16	Lane_NoTbl[20];						//���κŲ��ұ�  2024-6-10
u8 KeyState[20];

u8 key;
u16	KeyValue,keyline,keycol;

	u16 MBsx[2];							//[0]:Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��  [1]:Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ�� 2024-11-28
	u16 Startboxsx[2];
	u16 TPsx[2];
	u16	Timer_posx[2];
	u16 MBsy[2];							//[0]:Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��  [1]:Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ�� 2024-11-28
	u16 Startboxsy[2];
	u16 TPsy[2];
	u16	Timer_posy[2];	
		
	u16 Lapsx[2];						//[0]:��ʾ����ε�������λ�� [1]:��ʾ�ұ��ε�������λ�� 2024-11-28


	u16 MB_CR; 										//ä����ʾ�뾶
	u16	LaneStep_y;								//���μ���ʾ�ĵ���
	
	u16 Final_MBsx,Final_MBsy;
	u16 Final_Startboxsx,Final_Startboxsy;
	u16 Final_TPsx,Final_TPsy;
	u16 Final_lapsx;						//��ʾ�յ��ε�������λ��
	
	u16 Middle_MBsx,Middle_MBsy;
	u16 Middle_Startboxsx,Middle_Startboxsy;
	u16 Middle_TPsx,Middle_TPsy;
	u16 Middle_lapsx;						//��ʾ�м��ε�������λ��
	
	u8 fsize=0;				//key�����С
	
	u8 keyold=0XFF;			//������֮ǰ�İ���ֵ
	u8 key_oldstate[5][20];			//������֮ǰ�İ���ֵ

	u16  scanline=0;		//ɨ��������
	u16  TouchPadscanline=0;		//����ɨ��������
	u16 readcol=0;		//����������

	u8	Place[20];		//�˶�Ա�ı������Σ�
	u8	Lap_Place[40*2];		//ÿȦ��Ӧ�����Σ�
	
	u8 ds0sta=1,ds1sta=1;		//����LED״̬
	
	u8	RelayBit=0;					//��������λ =1������������ =0���ǽ������� 2024-11-21
	//2026-06-02 PC �� "Ӳ���豸 һֱ��" ���� (Set_MatchEvent 0x43 d9): =1 ���� *_Open_Close_State==0 �ر��ж�, ֻ�� ==3 ��/==4 δװ. =0 ԭ��������
	u8	HardwareAlwaysOpenBit=0;
	u8 	Baton_No[10];				//�����ڼ��� 2024-11-21
	u8 	RelayLaps=0;					//�������루Ȧ����2024-11-24
	u16	Relay_SB_DelayClose_Time[10];				//���������˶�Ա��̨�����źŹر��ӳ�ʱ�� 2024-11-25
	u8	Relay_SB_DelayCloseBit[10];			//���������˶�Ա��̨�����źŹر��ӳ�ʱ��λ =1�����������˶�Ա������ʼ�ӳټ�ʱ   =0�������ӳټ�ʱ 2024-11-25

	u16	TP_DelayClose_Time[10];				//�˶�Ա����TP�źŹر��ӳ�ʱ�� 2024-12-12
	u8	TP_DelayCloseBit[10];			//�˶�Ա����TP�źŹر��ӳ�ʱ��λ =1:�˶�Ա����TP��ʼ�ӳټ�ʱ   =0�������ӳټ�ʱ 2024-12-12


	u8	CloseLaneState[10];					//�رյ���״̬=2���򿪣�=3���ر�

	//2026-05-14 0x47 Set_LaneOpenClose �ã�������������/���ñ�־
	//   =1���õ��������������/����̨/ä��������״̬����Ӧ��Ĭ�ϣ�
	//   =0���õ���ȫ���Σ�Ӳ�����������κ��źţ���ʹ 0x4C ȫ��Ҳ���ӣ�
	//   �� CloseLaneState ����CloseLaneState �� 0x43 Set_MatchEvent ������"����յ�λͼ"��
	//   LaneEnabled �� 0x47 ������"��̬����/����"�����߾�Ϊ 0 ʱ�õ����Ρ�
	u8	LaneEnabled[10] = {1,1,1,1,1,1,1,1,1,1};	//Ĭ��ȫ������

	u32 tx_overflow_cnt = 0;	//2026-05-30 ���� 2: Send_Data_buf ring buffer ������� (������)
	u8	Testing_bit;							//���ڽ��в���λ =1�����ڲ��ԣ� =0��ֹͣ����   2023-8-5

	u16 btnw,btnh;				//��ť����
	u16 btnw1,btnh1;				//��ť����
	u16 CMD_btnw,CMD_btnh;				//Command��ť���ȣ��߶Ȳ���
	u16 resultw,resulth;					//�ɼ���ʾ����Ŀ��Ⱥ͸߶Ȳ���

	u16 btnds0x,btnds0y,btnds1x,btnds1y;	//��ť�������
	u16 carea_x0,carea_y0,Lbtnwx,Lbtnhy;	//��߰�ť�������
	u16 btndsx,btnwx,btnhy;	//�ұ߰�ť�������
	  
	u16 cds0x,cr; 		//Բ�������
	u16 Inf_area_x0,Inf_area_y0; 		//��Ϣ��ʾ������������
	u16 RunningTime_x0,RunningTime_y0; 		//����ʱ����ʾ������������
	u16 StartFinalPlace_x0,StartFinalPlace_y0;			//����λ�ñ�־��ʾλ��x,y�������
	
	u8 btnfsize;				//��ť�����С   
	u8	laps[10][2];					//[i][0]����50����ߵ����� ; [i][1]:����50���ұߵ�����  2024-11-27

	u16 Placex;						//��ʾ���ε�λ��

	
	u16	Rec_send_num;											//��¼���������ݵĴ���  2023-7-11
	u8	Send_Data_buf[TxRx_Data_Length*Rec_Loop];		//	�����������ݻ�����  2023-8-14
	u8	Send_buf[TxRx_Data_Length*Rec_Loop];				//	���ڷ������ݻ�����  2023-8-14

	u8	Procee_SwimDir_Bit;		//������Ӿ�˶�Ա�εķ���͹���ʱ�� 2023-7-6 
 
u8 lcd_Dis[32*2];				//���LCD ID�ַ���
u8 line_height1=64;//34;//26;//28;		//�м��
									
u8 dir_len;		//�˶�Ա�ε�ʱ������ʾ��  2023-7-27
			
u8 	RS_TX_Bit;							//���ڷ���״̬λ
u16 RS_TX_No,RS_TX_len;
u16 RS_TX_Ptr;							//���ڷ�������ָ��   2023-10-25
u8  UART4_TX_BUF[UART4_MAX_SEND_LEN]; 		//���ͻ���,���UART4_MAX_SEND_LEN�ֽ�
u8 	UART4_RX_BUF[UART4_MAX_RECV_LEN]; 		//���ջ���,���UART4_MAX_RECV_LEN���ֽ�.
//[15]:0,û�н��յ�����;1,���յ���һ������.
//[14:0]:���յ������ݳ���
vu16 UART4_RX_STA=0;   	 
u16		UART4_RX_PTR=0;

//2024-6-8 
u8 SwimmingPool_Arrage;	//=0:�������ã����δ��ϵ���0-9���� =1:�������ã����δ��ϵ���9-0���� 
u8 StartFinalPlace;	//=0:�������յ������  2024-11-27
u8 StartPlace;	//���������λ�� =0:��� �� =1:�ұ�
u8 FinalPlace;	//�յ�λ�� �����յ�λ�� =0:�յ�λ������Ļ��ߣ� =1���յ�λ������Ļ�ұߡ�
	
u16	Close_Time=60;//200;	2024-11-24				//Ӿ���ر�ʱ�� ��Ӿ�����˰�װ����  2025-1-2
u16	All_Close_Time=400;			//ȫӾ���ر�ʱ��    ��Ӿ�ص��߰�װ���� 2025-1-2
u16 Result_Display_Time=Result_Display_Time_Value;
u16 FalseStartThreshold=10;	//2026-05-26 �����ж���ֵ, ��λ 0.01s, Ĭ�� 10 = 0.1s				//������1�룬ÿ���ɼ�����ʾͣ��ʱ��3000����
u16	TP_DelayCloseValue=50;		//�˶�Ա����TP�źŹر��ӳ�ʱ���ʼ����5�� 2024-12-12
u16	Relay_SB_DelayCloseValue=50;		//������1�룬���������˶�Ա��̨�����źŹر��ӳ�ʱ���ʼ����5�� 2024-11-25
u16	MBdelay_Time=40;				//������2�룬��û��TP�ɼ�������£�����ä���ɼ����ȴ�MBdelay_Timeʱ�����Ȼû��TP�ɼ�������ä���ɼ�����˵��ɼ� 2023-11-3
//////////////////////////////////////////////////////////////////////////////////	 
//ALIENTEK STM32������
//��������:2023/10/24
//�汾��V1.0
//��Ȩ���У�����ؾ���
//Copyright(C) �������ڴ�����Ϣ�����ɷ����޹�˾ 2023-2029
//All rights reserved									  
//*******************************************************************************
//�޸���Ϣ
//��
////////////////////////////////////////////////////////////////////////////////// 	   

//#define TP_PRES_DOWN 0x80 //????? 
//#define TP_CATH_PRES 0x40 //??????
 //Cmd��ť����
u8*const Hcmd_btncaption_tbl[10]= 
{"����0","����1","����2","����3","����4","����5","����6","����7","����8","����9"};

u8*const Hcmd_Lbtncaption_tbl[10]= 
{"����0","����1","����2","����3","����4","����5","����6","����7","����8","����9"};

//2024-6-8 ��������ʾ����
u8*const Hcmd_Inv_btncaption_tbl[10]= 
{"����9","����8","����7","����6","����5","����4","����3","����2","����1","����0"};

u8*const Hcmd_Inv_Lbtncaption_tbl[10]= 
{"����9","����8","����7","����6","����5","����4","����3","����2","����1","����0"};


u8	Distance_Max=6;
u8 	laps_No_tbl[6]={1,2,4,8,16,30};
u8 	Llaps_No_tbl[6]={1,1,2,4,8,15};		//2024-11-24
u8 	Rlaps_No_tbl[6]={0,1,2,4,8,15};		//2024-11-24
u8	Laps_No=1;

//�̳�25m ��Ӿ�˶�Ա ÿ�ߴ��ڵĴ��� 2025-1-4
u8 	laps25m_No_tbl[6]={2,4,8,16,32,60};
u8 	Llaps25m_No_tbl[6]={1,2,4,8,16,30};		//2025-1-4
u8 	Rlaps25m_No_tbl[6]={1,2,4,8,16,30};		//2025-1-4

//DS0��ť����
u8*const Hds0_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"M.����","M.Start","Timer ON",},
{"M.Start","Pause","Timer OFF",},  
};
//DS1��ť����
u8*const Hds1_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"��ʱ��λ","DS1��","DS1 ON",},
{"RESET","DS1��","DS1 OFF",},  
};

//Relay��ť����  2024-11-21
u8*const Relay_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"�ǽ���","�ǽ���","No Relay",},  
{"����","����","Relay",},
};

//Test��ť����
u8*const Test_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"����","DS1��","DS1 ON",},
{"���ڲ���","DS1��","DS1 OFF",},  
};

//Lane����ť����  2024-6-8
u8*const Lane_Inv_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"����˳��","DS1��","DS1 ON",},
{"LaneInv","DS1��","DS1 OFF",},  
};



//Ready��ť����
u8*const Ready_btncaption_tbl[2][GUI_LANGUAGE_NUM]=
{ 
{"׼������","DS1��","DS1 ON",},
{"Ready","DS1��","DS1 OFF",},  
};


extern vu8 ledplay_ds0_sta;		//ledplay����,DS0�Ŀ���״̬
extern u8 net_test(void);				//2024-10-23 ����������ڣ������Ӱ�ť��
extern void net_toggle_connect(void);	//2026-05-13 �����涥���������Ӱ�ťר�ã�ֱ���л� connstatus

u8 lcd_btnDis[32];				//���LCD ID�ַ��� 2024-11-24

//2024-10-23
/*
extern u8 connstatus;//0,δ����,1,������
extern struct netbuf *recvbuf;//���ջ�����
extern struct netbuf *sendbuf;//���ͻ�����	
extern struct netbuf *sendcmdbuf;//����command������	
extern struct netconn *netconnnew;//��TCP/UDP�������ӽṹ��ָ��(ֻ��TCP Server���õ����)
extern struct netconn *netconncom;//ͨ��TCP/UDP�������ӽṹ��ָ��(TCP Server/TCP Client/UDPͨ��)
*/
u8 connstatus;//0,δ����,1,������
 struct netbuf *recvbuf;//���ջ�����
 struct netbuf *sendbuf;//���ͻ�����	
 struct netbuf *sendcmdbuf;//����command������	
 struct netconn *netconnnew;//��TCP/UDP�������ӽṹ��ָ��(ֻ��TCP Server���õ����)
 struct netconn *netconncom;//ͨ��TCP/UDP�������ӽṹ��ָ��(TCP Server/TCP Client/UDPͨ��)

	u8 editflag=0;	//0,�༭����smemo
					//1,�༭����eip
					//2,�༭����eport
	u8 *p,*ptemp; 
	u32 rxcnt=0;
	u32 txcnt=0;
	u8 protocol=0;	//Ĭ��TCP ServerЭ��
					//0,TCP ServerЭ��
					//1,TCP ClientЭ��
					//2,UDPЭ��
	u8 oldconnstatus=0;//�ϵ�״̬
	u8 tcpconn=0;	//TCP�����Ƿ���:0,δ����;1,������
	u32 oldaddr=0;	//���һ���������Ե�ip��ַ
	u16 oldport=0;	//���һ���������Ե�port

	ip_addr_t tipaddr;	//����IP��ַ   2024-11-1
	u16	tport=8088;		//�����˿ں�,(Ҫ���ӵĶ˿ں�)Ĭ��Ϊ8088;		 


	ip_addr_t Remote_tipaddr;	//Զ�̡���ʱIP��ַ
	u16	Remote_tport=8088;		//Զ�̡���ʱ�˿ں�,(Ҫ���ӵĶ˿ں�)Ĭ��Ϊ8088;		2024-11-1 


u8	Display_RollingTime_Bit;
u8	Send_Bit;
u16	Prot_Sx,IP_Sx,Port_Sx,TX_Sx,RX_Sx;
u16 connbtn_ux,protbtn_ux;

/*
//Prot_Sx=5,IP_Sx=220-20,Port_Sx=500-20,TX_Sx=900+20,RX_Sx=1030+20;

//��ʾ��ʾ��Ϣ
//y:y����,x����㶨��0��ʼ
//height:����߶�
//fsize:�����С
//tx:�����ֽ���
//rx:�����ֽ���
//prot:Э������
//     0:TCP Server 
//     1:TCP Client
//     2:UDP
//flag:���±��,������������
//bit0,1,����tx����,0,������
//bit1,1,����rx����,0,������
//bit2,1,����port����,0,������
void net_msg_show(u16 y,u16 height,u8 fsize,u32 tx,u32 rx,u8 prot,u8 flag)
{
	u8 *pbuf;
	pbuf=gui_memin_malloc(100);
	if(pbuf==NULL)return;//�ڴ�����ʧ��
	if(prot>2)prot=2;
//	xdis=(lcddev.width-(35*fsize/2))/3;//��϶
	
//	BACK_COLOR=GRAYBLUE;//LIGHTBLUE;// DARKBLUE;//2023-5-12 NET_MSG_BACK_COLOR;
		y=5;		
		fsize=24;
	
	//2024-10-17  ���粻��ʾ���պͷ�������
	   
	if(flag&1<<0)//����TX����
	{
		//		xdis=1100;
		gui_fill_rectangle(TX_Sx,y+(height-fsize)/2,10*fsize/2,fsize,NET_MSG_BACK_COLOR);//���֮ǰ����ʾ
		sprintf((char*)pbuf,"TX:%d",tx);
		gui_show_string(pbuf,TX_Sx,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//TX�ֽ�����ʾ
	}
	if(flag&1<<1)//����RX����
	{ 
//		xdis=10;
		gui_fill_rectangle(RX_Sx,y+(height-fsize)/2,10*fsize/2,fsize,NET_MSG_BACK_COLOR);//���֮ǰ����ʾ
		sprintf((char*)pbuf,"RX:%d",rx);
		gui_show_string(pbuf,RX_Sx,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//RX�ֽ�����ʾ
	}
	
	if(flag&1<<2)//����prot����
	{
//		xdis=10;
//		gui_fill_rectangle(xdis/2+20*fsize/2+xdis*2,y+(height-fsize)/2,15*fsize/2,fsize,NET_MSG_BACK_COLOR);//���֮ǰ����ʾ
//		sprintf((char*)pbuf,"%s:%s",netplay_protcaption_tb[gui_phy.language],netplay_protname_tb[prot]);//Э��
//		gui_show_string(pbuf,xdis/2+20*fsize/2+xdis*2,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//��ʾЭ��
		gui_fill_rectangle(Prot_Sx,y+(height-fsize)/2,15*fsize/2,fsize,NET_IP_BACK_COLOR);//NET_MSG_BACK_COLOR);//���֮ǰ����ʾ
		sprintf((char*)pbuf,"%s:%s",netplay_protcaption_tb[gui_phy.language],netplay_protname_tb[prot]);//Э��
		gui_show_string(pbuf,Prot_Sx,y+(height-fsize)/2,lcddev.width,fsize,fsize,WHITE);//NET_MSG_FONT_COLOR);//��ʾЭ��
//		gui_show_string(pbuf,xdis,y+(height-fsize)/2,lcddev.width,fsize,fsize,NET_MSG_FONT_COLOR);//��ʾЭ��
	}
	gui_memin_free(pbuf);//�ͷ��ڴ�
	
}
//���ñ༭����ɫ
//ipx:ip�༭��
//portx:port�༭��
//prot:Э��
//connsta:����״̬
void net_edit_colorset(_edit_obj *ipx,_edit_obj *portx,u8 prot,u8 connsta)
{
	if(connsta==1)//���ӳɹ�?û��˵,���ǲ��ɱ༭
	{
		ipx->textcolor=WHITE;
		portx->textcolor=WHITE;
	}else//������״̬
	{
		switch(prot)
		{
			case 0://TCP ServerЭ��
				portx->textcolor=GREEN;	//��ɫ,��ʾ���Ա༭
				ipx->textcolor=WHITE;	//��ɫ,�̶�����
				break;
			case 1://TCP ClientЭ��
			case 2://UDPЭ��
				portx->textcolor=GREEN;	//��ɫ,��ʾ���Ա༭
				ipx->textcolor=GREEN;	//��ɫ,��ʾ���Ա༭ 
				break;
		}		
	}
	edit_draw(ipx);		//���༭��
	edit_draw(portx);	//���༭��
} 
//���ַ�����ʽ��portת��Ϊ������ʽ��port
//str:�ַ�����ʽ��port��
//����ֵ:ת����������ʽ��port��
u16 net_get_port(u8 *str)
{
	u16 port;
	port=atoi((char*)str);
	return port;
}
//���ַ�����ʽ��ip��ַת��Ϊ������ʽ��ip
//����ֵ:0,�����IP,����,��ȷ��IP.
u32 net_get_ip(u8 *str)
{
	u8 *p1,*p2,*ipstr;
	struct ip_addr ipx;
	u8 ip[4];
	ipstr=gui_memin_malloc(30);
	if(ipstr==NULL)return 0;
	strcpy((char*)ipstr,(char*)str);//�����ַ���
	p1=ipstr;p2=(u8*)strstr((const char*)p1,".");
	if(p2==NULL){gui_memin_free(ipstr);return 0;}//IP����
	p2[0]=0;ip[0]=atoi((char*)p1);//�õ���һ��ֵ
	p1=p2+1;p2=(u8*)strstr((const char*)p1,".");
	if(p2==NULL){gui_memin_free(ipstr);return 0;}//IP����
	p2[0]=0;ip[1]=atoi((char*)p1);//�õ��ڶ���ֵ 
	p1=p2+1;p2=(u8*)strstr((const char*)p1,".");
	if(p2==NULL){gui_memin_free(ipstr);return 0;}//IP����
	p2[0]=0;ip[2]=atoi((char*)p1);//�õ�������ֵ 
	p1=p2+1;ip[3]=atoi((char*)p1);//�õ����ĸ�ֵ 
	IP4_ADDR(&ipx,ip[0],ip[1],ip[2],ip[3]);
	gui_memin_free(ipstr);
	return ipx.addr;//���صõ���IP��ַ
}
extern void tcp_pcb_purge(struct tcp_pcb *pcb);	//�� tcp.c���� 
extern struct tcp_pcb *tcp_active_pcbs;			//�� tcp.c���� 
extern struct tcp_pcb *tcp_tw_pcbs;				//�� tcp.c����  
//ǿ��ɾ��TCP Server�����Ͽ�ʱ��time wait
void net_tcpserver_remove_timewait(void)
{
	struct tcp_pcb *pcb,*pcb2; 
	while(tcp_active_pcbs!=NULL)delay_ms(10);//�ȴ�tcp_active_pcbsΪ�� 
	pcb=tcp_tw_pcbs;
	while(pcb!=NULL)//����еȴ�״̬��pcbs
	{
		tcp_pcb_purge(pcb); 
		tcp_tw_pcbs=pcb->next;
		pcb2=pcb;
		pcb=pcb->next;
		memp_free(MEMP_TCP_PCB,pcb2);	
	}
}
//�Ͽ�����
//netconn1:�������ӽṹ��1
//netconn2:�������ӽṹ��2
void net_disconnect(struct netconn *netconn1,struct netconn *netconn2)
{
	if(netconn1!=NULL)//���ӽṹ����Ч?
	{
		if(netconn1->type==NETCONN_TCP)netconn_close(netconn1);//�ر�TCP netconn1����
		else if(netconn1->type==NETCONN_UDP)netconn_disconnect(netconn1);//�ر�UDP netconn1������
		netconn_delete(netconn1);  //ɾ��netconn1����
	}
	if(netconn2!=NULL)//���ӽṹ����Ч?
	{
		if(netconn2->type==NETCONN_TCP)netconn_close(netconn2);//�ر�TCP netconn2����
		else if(netconn2->type==NETCONN_UDP)netconn_disconnect(netconn2);//�ر�UDP netconn2������
		netconn_delete(netconn2);  //ɾ��netconn2����
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

	//2026-05-13(3) ��ʽ���� D10��SW_Back1_Pos�������� scmd_buf ��ȫ�ֹ������壬��һ��
	//   OnSendSWCommand_Data д��� sign=1��������־�����������һ֡�����������/ä��/
	//   ��ͨ����̨����֡��"Ī��"���� D10=1��PC �˰� D10��0 ����������Ӧʱ������ȡ����
	//   ���ڱ����������� sign ����ļ��׷��ͣ���D10 ��ԶӦ���� 0��
	scmd_buf[SW_Back1_Pos]=0;

	scmd_buf[SW_End_Code_Pos]=SW_End_Code;
	
	scmd_buf[TxRx_Data_Length-1]=SW_End_Code;
	
	if(Rec_send_num>=Rec_Loop) { tx_overflow_cnt++; Rec_send_num=0; }   //2026-05-30 ���� 2: ���� ring buffer �������
	for(u16 i=0;i<TxRx_Data_Length;i++)
	{
		Send_Data_buf[Rec_send_num*TxRx_Data_Length+i]=scmd_buf[i];
	}
	Rec_send_num++;
					
	RS_TX_No++;				//��Ҫ�������ݴ���  2023-10-26

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
	
	if(Rec_send_num>=Rec_Loop) { tx_overflow_cnt++; Rec_send_num=0; }   //2026-05-30 ���� 2: ���� ring buffer �������
	for(u16 i=0;i<TxRx_Data_Length;i++)
	{
		Send_Data_buf[Rec_send_num*TxRx_Data_Length+i]=scmd_buf[i];
	}
	Rec_send_num++;
	RS_TX_No++;				//��Ҫ�������ݴ���  2023-10-26
	
}

 
//��ʾԲ����ʾ��Ϣ
//x,y:Ҫ��ʾ��Բ��������
//r:�뾶
//fsize:�����С
//color:Բ����ɫ
//str:��ʾ���ַ���
void key_show_circle(u16 x,u16 y,u16 r,u8 fsize,u16 color,u8 *str)
{ 
	gui_fill_circle(x,y,r,color);
	gui_show_strmid(x-r,y-fsize/2,2*r,fsize,BLUE,fsize,str);//��ʾ����  
}

	_btn_obj* CloseLanebtn[10];			//���ƹرյ��ΰ�ť
	u16 	CloseLanebtn_width=160+40;		//2024-11-24  ���ƹرյ��ΰ�ť�Ŀ��ȣ�

	_btn_obj* RMBLanebtn[10];			//��ä���ɼ�������TP�ɼ��Ķ�Ӧ���ΰ�ť 2023-11-3
	
 	_btn_obj* cmdRbtn[10];			//�����ұ߰�ť
 	_btn_obj* cmdLbtn[10];			//������߰�ť

 	_btn_obj* Startbtn=0;			//���ư�ť
 	_btn_obj* Resetbtn=0;			//���ư�ť
	_btn_obj* Readybtn=0;			//Ready׼���������ư�ť
	_btn_obj* Testbtn=0;			//Test���Կ��ư�ť

	_btn_obj* Relaybtn=0;			//�����������ư�ť  2024-11-21

 	_btn_obj* SendStartTimerbtn=0;			//���ͷ���ʱ�� ���ư�ť   2024-9-1
	
//	_btn_obj* LaneInvbtn=0;			//LaneInv���η�����ư�ť  2024-11-3
 
	_btn_obj* Distance_Addbtn=0;			//��������+1���ư�ť
	_btn_obj* Distance_Decbtn=0;			//��������-1���ư�ť

 	_btn_obj* Setupbtn=0;			//���ò��� ���ư�ť   2024-10-23

	_btn_obj* ExitShutdownbtn=0;	//�˳�/�ػ� ���ư�ť   2026-05-11

	_btn_obj* NetConnbtn=0;			//��������/�Ͽ� ���ư�ť�������涥����   2026-05-13

//2026-05-13(2nd) ���֣�����/�ҵ��ΰ�ť��BTN_TYPE_ANG��Ϳ"����"�ĵ���ɫ����+����
static void Setup_LaneBtn_LightYellow(_btn_obj *btn)
{
	if(!btn || !btn->bkctbl) return;
	btn->bkctbl[0]=0XFFE0;	//�߿� ���ƣ�������ʶ�ȣ�
	btn->bkctbl[1]=0XFFFC;	//���� �����׵�ǳ��
	btn->bkctbl[2]=0XFFFC;	//�ϰ� �����׵�ǳ��
	btn->bkctbl[3]=0XFFF4;	//�°� �Դ��ƵĽ���
	btn->bcfucolor=BLACK;
	btn->bcfdcolor=WHITE;
}

	u8 ip_height,ip_fsize;			//IP/PORT����߶Ⱥ������С

	//2024-11-10
	u8 Csecond=0;
	short temperate=0;	//�¶�ֵ		   
	u8 t=0;
	u8 tempdate=0;
	
	u16	Voltage_x0=400,Voltage_y0=1;   //��ѹ��ʾx,y����



//SWIM����
//�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T
// 2026-05-18(3) STM32 H7 Backup SRAM (4KB @0x38800000, VBAT ���ݳ־�) �־�״̬
//   ��Ų��� + TP/SB/MB ״̬ + ������������ + ��ǰ��ʱ����+��ʱλ
//   ���� swim_play ��ʼ��ʱ�Զ����� (Load)����ѭ��ÿ���Զ����� (Save)
//   BKPSRAM д����ģ���Ƶ��д (������ NAND Flash �෴, NAND д�����������)
//�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T�T
//2026-05-20 BKPSRAM �־û��û��Ĺ��ı��� IP ��Ҫ������
#include "lwip/netif.h"
extern struct netif lwip_netif;   //lwip_comm.c �ж���
u8 bkp_local_ip_valid = 0;        //net_test �� IP ʱ�� 1, Save ʱд�� BKPSRAM

#define SWIM_STATE_MAGIC  0x53574D31u    // "SWM1" ��ʶ BKPSRAM ���ѱ�������ռ��
#define SWIM_STATE_VERSION 2u            // ���ݲ��ְ汾�� (v2: +local_ip[4], 2026-05-20)

typedef struct {
	u32 magic;          // SWIM_STATE_MAGIC
	u32 version;        // SWIM_STATE_VERSION
	u32 checksum;       // �����ֽ� XOR
	u32 _pad0;          // ����ռλ
	//---- ���� ----
	u8  StartFinalPlace, StartPlace, FinalPlace, SwimmingPool_Arrage;
	u8  Pool50mOr25mbit, PoolSingleOrDoubleTPbit, Left_MB_Num, Right_MB_Num;
	u8  Open_State;
	u8  _pad1[3];
	u16 Close_Time, All_Close_Time;
	u16 Result_Display_Time, TP_DelayCloseValue;
	u16 Relay_SB_DelayCloseValue, MBdelay_Time;
	u16 tport;
	u16 _pad2;
	//---- TP/SB/MB ״̬���� ----
	u8 TP_Open_Close_State[10][2];
	u8 Startbox_Open_Close_State[10][2];
	u8 MB_Open_Close_State[3][20];
	//---- ������������ ----
	u8 CloseLaneState[10];
	u8 laps[10][2];
	u8 LAll_Lap, RAll_Lap, All_Lap, RelayBit;
	u16 RelayLaps;
	u16 _pad3;
	//---- ��ǰ��ʱ���� + ��ʱλ ----
	u16 hour, minute, second, msecond;
	u16 Start_hour, Start_minute, Start_second, Start_msecond;
	u8  timer_bit, Ready_timer_bit, _pad4, _pad5;
	//---- 2026-05-20 �û��޸ĺ�ı��� IP (lwip_comm_default Ĭ�� 192.168.1.30, �û��ĺ󸲸�) ----
	u8  local_ip[4];
	u8  local_ip_valid;     // 1=��Ч(�� local_ip ����Ĭ��), 0=δ�Ĺ�, ��Ĭ��
	u8  _pad6[3];
} SwimMatchState_t;

//���� BKPSRAM ʱ�� + Backup ����� + ��������ȷ�� VBAT ������Ч
void BkpSRAM_Init(void)
{
	//2026-05-18(3) ֱ�ӼĴ������������� HAL ��ʽ��������
	RCC->AHB4ENR |= RCC_AHB4ENR_BKPRAMEN;   //�� BKPSRAM ʱ��
	PWR->CR1     |= PWR_CR1_DBP;             //����д Backup ��
	PWR->CR2     |= (1u << 0);               //BREN: �� Backup ������ (VBAT/standby ���� BKPSRAM)
	while(!(PWR->CR2 & (1u << 16)));         //BRRDY: �ȵ���������
}

//���� checksum��magic/version/checksum ֮��������ֽ� XOR��
static u8 _SwimState_CalcChecksum(SwimMatchState_t *st)
{
	u8 *p = (u8*)st;
	u32 i;
	u8 cs = 0;
	// ���� magic(4) + version(4) + checksum(4) + _pad0(4) = 16 �ֽ�
	for(i=16; i<sizeof(SwimMatchState_t); i++) cs ^= p[i];
	return cs;
}

//��ȫ�ֱ�������� BKPSRAM
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
	//2026-05-20 �����û��޸ĺ�ı��� IP (�������־û�)
	st.local_ip[0] = lwipdev.ip[0]; st.local_ip[1] = lwipdev.ip[1];
	st.local_ip[2] = lwipdev.ip[2]; st.local_ip[3] = lwipdev.ip[3];
	st.local_ip_valid = bkp_local_ip_valid;  //�����û����� net_test �Ĺ� IP ���� 1
	st._pad6[0]=0; st._pad6[1]=0; st._pad6[2]=0;
	st.checksum = _SwimState_CalcChecksum(&st);
	memcpy((void*)bkp, &st, sizeof(st));
}

//�� BKPSRAM ��ȡ���ָ�ȫ�ֱ��������� 1=���سɹ�, 0=BKPSRAM ��Ч/�״�����
u8 Load_State_From_BkpSRAM(void)
{
	SwimMatchState_t *bkp = (SwimMatchState_t*)D3_BKPSRAM_BASE;
	SwimMatchState_t st;
	u16 i, k;
	memcpy(&st, (const void*)bkp, sizeof(st));
	if(st.magic != SWIM_STATE_MAGIC) return 0;     //�״����� / VBAT �ϵ�
	if(st.version != SWIM_STATE_VERSION) return 0; //�汾������
	if(_SwimState_CalcChecksum(&st) != st.checksum) return 0; //������
	//У��ͨ�����ָ�ȫ�ֱ���
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
	//2026-05-20 �û��Ĺ��ı��� IP �ָ� (�� netif_set_ipaddr ʵʱӦ��)
	if(st.local_ip_valid==1 && (st.local_ip[0]|st.local_ip[1]|st.local_ip[2]|st.local_ip[3])!=0){
		struct ip_addr _bkp_ip;
		lwipdev.ip[0]=st.local_ip[0]; lwipdev.ip[1]=st.local_ip[1];
		lwipdev.ip[2]=st.local_ip[2]; lwipdev.ip[3]=st.local_ip[3];
		IP4_ADDR(&_bkp_ip, lwipdev.ip[0], lwipdev.ip[1], lwipdev.ip[2], lwipdev.ip[3]);
		netif_set_ipaddr(&lwip_netif, &_bkp_ip);
		bkp_local_ip_valid = 1;
		printf("BKPSRAM �ָ����� IP -> %d.%d.%d.%d\r\n", lwipdev.ip[0], lwipdev.ip[1], lwipdev.ip[2], lwipdev.ip[3]);
	}
	return 1;
}


void swim_play(void)
{
//	_edit_obj* eip=0;	//IP�༭��
//	_edit_obj* eport=0; //�˿ڱ༭��
//  _btn_obj* protbtn=0;//Э��ѡ��ť
 // _btn_obj* sendbtn=0;//���Ͱ�ť
//  _btn_obj* connbtn=0;//���Ӱ�ť
//  _btn_obj* clrbtn=0;	//�����ť
//	_memo_obj * rmemo=0;//,* smemo=0;	//memo�ؼ� 
//	_t9_obj * t9=0;					//���뷨  
			
	u8 	Lkey_state=1,Rkey_state=1;
	u8  tmp[128];
	u16	USART4_RX_len;
	u8	buff[RX_DATA_MaxLEN];
	u16 	i,j;
	u16	SendLength;					//�������ݳ���  2023-7-11
	
	u8 msg_height;					//��Ϣ����߶Ⱥ������С
	u16 memo_width,btn_width;		//memo�ؼ�����,��ť�Ŀ���
	u16 rmemo_height,smemo_height;	//����memo�ͷ���memo�ĸ߶�
	u16 rbtn_height;				//��������ť�ĸ߶�
	u8 m_offx,sm_offy,rm_offy; 		//memo x�����ƫ��;smemo��rmemo y����ƫ��  
	u8 fsize,sbtnfsize;				//�����С,�ͷ��Ͱ�ť�����С
//	u16 t9height; 					//���뷨�ĸ߶�
	u16 tempx,tempy;

	
	gui_phy.language=0;
	u8 *ipcaption=netplay_ipcaption_tb[1][gui_phy.language];//Ĭ����TCP Serverģʽ,��ʾ����IP
	
	u16 res; 
	u8 rval=0;
	/////////////////////////////////
	err_t err; 			//�����־ 
	u16 *bkcolor;

	BACK_COLOR=GRAYBLUE;//LIGHTBLUE;// DARKBLUE;//2023-5-12 NET_MSG_BACK_COLOR;


	Timer_posx[0]=220;	
	Timer_posx[1]=480;	

	Left_MB_Num=2;				//��� ä������  �������  2025-1-26
	Right_MB_Num=1;				//�ұ� ä������  �������

	StartFinalPlace=0;			//=0:�������յ�����Ļ��ߣ� =1���������յ�����Ļ�ұߡ�  2024-11-27
	StartPlace=0;						//=0:���������Ļ��ߣ� =1�����������Ļ�ұߡ�
	FinalPlace=0;						//=0:�յ�����Ļ��ߣ� =1���յ�����Ļ�ұߡ�			

	All_Close_Time=400;			//ȫӾ���ر�ʱ��    ��Ӿ�ص��߰�װ���� 2025-1-17
	
	MBdelay_Time=30;
	
	SwimmingPool_Arrage=0;	//=0:�������ã����δ��ϵ���0-9���� =1:�������ã����δ��ϵ���9-0����   2024-6-8

	tport=8088;		//�����˿ں�,(Ҫ���ӵĶ˿ں�)Ĭ��Ϊ8088;						

	TP_DelayCloseValue=40;		//�˶�Ա����TP�źŹر��ӳ�ʱ���ʼ����5�� 2024-12-12

	OnReadDeviceData();			//2026-05-26 ���������豸״̬���� (TP/SB/MB) from 2:/swimdev.cfg
	OnReadMatchData();			//���洢���� 2024-12-7

	
	for(i=0;i<20;i++)
	{
			Lane_NoTbl[i]=i;
	}


	Beep(1);			// ���������1 û�������  2023-8-2
	
	delay_init(180);		//��ʼ����ʱ���� 

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
	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//�·ֽ���	 
	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[22][gui_phy.language]);//��ʾ����  
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
	if(audiodev.status&(1<<7))//��ǰ�ڷŸ�??
	{
		audio_stop_req(&audiodev);	//ֹͣ��Ƶ����
		audio_task_delete();		//ɾ�����ֲ�������.
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
  	res=lwip_comm_init();	//lwip��ʼ�� LwIP_Initһ��Ҫ��OSInit֮�������LWIP�̴߳���֮ǰ��ʼ��!!!!!!!!
	//if(res==0)				//������ʼ���ɹ�
	{
		
		lwip_comm_dhcp_creat();	//����DHCP���� 
		//��ʾ����DHCP��ȡIP
		window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[2][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);
		while(lwipdev.dhcpstatus==0||lwipdev.dhcpstatus==1)//�ȴ�DHCP����ɹ�
		{
			delay_ms(10);//�ȴ�.
		}
		if(lwipdev.dhcpstatus==2)window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[3][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//DHCP�ɹ�
		else window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[4][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//DHCPʧ��

		//2026-05-11 �޸�bug��ע�͵��������� lwipdev.ip[3]=100;
		//��Ϊ����ǿ�Ƹ���DHCP/��̬��õ���ʵ����IP���һ�Σ�
		//����TCP Serverҳ����ʾ�̶��� .100 ����ʵ��IP��
		//lwipdev.ip[3]=100;

		delay_ms(100);
//		net_load_ui_init();	//����������UI
		httpd_init(); 	//��ʼ��http
 //2023-9-28



		
//	LCD_Clear(NET_MEMO_BACK_COLOR);//���� 

		btnw=100;
		btnh=40;
	
		btnw1=80;		//��ť1�Ŀ���
		btnh1=32;			//��ť1�ĸ߶�
	
		CMD_btnw=140-10;		//command��ť�Ŀ���
		CMD_btnh=40+10+10;			//command��ť�ĸ߶�
		
	resultw=200;			//�ɼ���ʾ����Ŀ��Ⱥ͸߶Ȳ���
	resulth=32;	
		
	btnds0x=lcddev.width-150;
	btnds0y=lcddev.height-150;
	
	btnds1x=btnds0x;
	btnds1y=24+20;

	Inf_area_x0=40;	
	Inf_area_y0=48;



//	carea_x0=10;			//��߰�����x0
	carea_x0=10*4;			//��߰�����x0  2023-11-2
//	carea_y0=20+10;
	carea_y0=20+10+12*5;

	//2026-05-14 Fix #2: ����ʱ����ʾ���ұ���"����0"�Ҳ���ƽ��
	//   "����0"�� X ��� = TPsx[1] = Middle_TPsx = 927 (Display_TP_State �� 8 px �� ���� 935)��
	//   ������ 280 px �� ���� 655 �� RunningTime_x0 (=���������) = 659��
	//   ����״̬Բ��(RunningTime_x0-32 = 627) �Զ����浽��������⡣
	RunningTime_x0=carea_x0+781;	//2026-05-16 ��������+162�����ض���"����0"��ť����(=btndsx+btnw)
	RunningTime_y0=carea_y0+10; 		//����ʱ����ʾ������������

	StartFinalPlace_x0=200+50+162;							//2024-6-8
	StartFinalPlace_y0=carea_y0+10; 		//����ʱ����ʾ������������

	btnwx=100;
	btnhy=64;
	
//	MB_CR=btnh/2;	//	2023-11-2 	//20;//28;	
	MB_CR=btnh/4;//		2023-11-2 	//20;//28;	
	Final_MBsx=carea_x0+130-14;//2023-11-3				//���԰���ֶ���ť��x0
//	Final_MBsy=carea_y0+MB_CR;//32+20;
	Final_MBsy=carea_y0+2*MB_CR;//  2023-11-2
	LaneStep_y=btnhy;			//64;
			
//	Final_Startboxsx=Final_MBsx+30;			//��߳���̨��ť��x0
	Final_Startboxsx=Final_MBsx+15+1;			//��߳���̨��ť��x0  2023-11-3
	Final_Startboxsy=carea_y0;
 
	Final_TPsx=Final_Startboxsx+30;			//��ߴ��尴ť��x0
	Final_TPsy=carea_y0;
 
 	Placex=Final_TPsx+15;
 
  Final_timer_posx=Placex+10+10+20;			//�����ʾ�ɼ���x0
	Final_timer_posy=carea_y0;

	Lapsx[0]=Final_timer_posx+170+15;    		//�����ʾʣ��Ȧ����x0  2024-11-21

	dir_posx=Lapsx[0]+40;    		//�����ʾ�˶������x0
	dir_posy=carea_y0;

	Start_Dir=0;							//��Ӿ��ͷָʾ�ķ���  =0��left->right��=1��left<-right  2024-12-1

	MBsx[0]=Final_MBsx;							//Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��  2024-6-9
	Startboxsx[0]=Final_Startboxsx;
	TPsx[0]=Final_TPsx;
	Timer_posx[0]=Final_timer_posx;	
	MBsy[0]=Final_MBsy;							//Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��  2024-6-10
	Startboxsy[0]=Final_Startboxsy;
	TPsy[0]=Final_TPsy;
	Timer_posy[0]=Final_timer_posy;	



	RMBbtn_posx=dir_posx+170;    		//�����ʾ��ä���ɼ������޴���ɼ�������x0  2023-11-3
	RMBbtn_posy=carea_y0;

	Lapsx[1]=dir_posx+170+40;    		//�ұ���ʾʣ��Ȧ����x0  2024-11-21


  Middle_timer_posx=RMBbtn_posx+90;					//�ұ���ʾ�ɼ���x0
	Middle_timer_posy=Final_timer_posy;	
	
	Middle_TPsx=Middle_timer_posx+180+5;			//�ұߴ��尴ť��x0
	Middle_TPsy=carea_y0;

//	Middle_Startboxsx=Middle_TPsx+20;			//�ұ߳���̨��ť��x0
	Middle_Startboxsx=Middle_TPsx+20-5-1;			//�ұ߳���̨��ť��x0
	Middle_Startboxsy=carea_y0;
	
 //	Middle_MBsx=Middle_Startboxsx+50;			//�ұ�ä����ť��x0
	Middle_MBsx=Middle_Startboxsx+50-10;			//�ұ�ä����ť��x0  2023-11-3
	Middle_MBsy=carea_y0+2*MB_CR;						//  2024-6-10

	MBsx[1]=Middle_MBsx;							//Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ��  2024-6-9
	Startboxsx[1]=Middle_Startboxsx;
	TPsx[1]=Middle_TPsx;
	Timer_posx[1]=Middle_timer_posx;	
	MBsy[1]=Middle_MBsy;							//Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ��
	Startboxsy[1]=Middle_Startboxsy;
	TPsy[1]=Middle_TPsy;
	Timer_posy[1]=Middle_timer_posy;	



//	btndsx=Middle_MBsx+30;					//lcddev.width-400;//660;
	btndsx=Middle_MBsx+30-14;					//2023-11-3

	cr=12+4;//16;
//	cds0x=Port_Sx+140;  2024-10-27
	cds0x=lcddev.width-486;  //2026-05-19 �Ƶ� NetConnbtn(lcddev.width-460,1)��� 10px ��, cr=16

	Relaybtn=btn_creat(Inf_area_x0+500,Inf_area_y0,CMD_btnw,btnh1,0,0);	//2024-11-21
	
//	Testbtn=btn_creat(Inf_area_x0+500,Inf_area_y0,CMD_btnw,CMD_btnh,0,0);
	Testbtn=btn_creat(Inf_area_x0+500+150,Inf_area_y0,CMD_btnw,btnh1,0,0);

	
//2024-6-8

	//2026-05-12(2nd) 6�����ذ�ťȫ���� BTN_TYPE_ANG (=2) ���ͣ��ñ����������ɫ����Ĭ�ϻ�ɫ��
	//                ͬʱ������ɫ���뱳���Ա�ǿ�ҵ�ɫ��ȷ�����塣

	//�˳�/�ػ���ť�ŵ���Ļ���Ͻ�(�ܿ��Ҳ����ذ�ť����ֹ����)��
	//ԭ"�������� +1/-1"��ť��"��������"��ʾ�ַ�������ƽ��140�����ó�λ�á�
	ExitShutdownbtn=btn_creat(5,1,CMD_btnw,CMD_btnh*3/4,0,BTN_TYPE_ANG);  //2026-05-19 �Ƶ���Ļ���Ͻ�

	//�Ҳ����ذ�ť��(�����£���������/����ʱ��/��λ/...׼��/��ʼ��ʱ)
	Setupbtn=btn_creat(btnds0x,Inf_area_y0-00,CMD_btnw,CMD_btnh*3/4,0,BTN_TYPE_ANG);		//2024-10-23  ���ò������ư�ť

	SendStartTimerbtn=btn_creat(btnds0x,Inf_area_y0+70,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);		//2024-9-1  ���ͷ���ʱ�̰�ť

	Resetbtn=btn_creat(btnds0x,Inf_area_y0+150,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);

	Readybtn=btn_creat(btnds0x,btnds0y-250,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);

	Startbtn=btn_creat(btnds0x,btnds0y,CMD_btnw,CMD_btnh,0,BTN_TYPE_ANG);

	//2026-05-12 �������� "+1"/"-1" ��ť����140���أ��������Ͻǵ�"�˳�/�ػ�"��ť
	Distance_Addbtn=btn_creat(Inf_area_x0+140,Inf_area_y0,btnw1,btnh1,0,0);
	Distance_Decbtn=btn_creat(Inf_area_x0+240,Inf_area_y0,btnw1,btnh1,0,0);

	//2026-05-13 �����涥��"��������"��ť ���� �����á�����Э������� ͬ���ܣ����ⷴ��������
	//�����Ҳ� Setupbtn ��ߣ�������"��ʼ/��λ/����/����"�ȴ�ť�ص�
	NetConnbtn=btn_creat(lcddev.width-460,1,150,btnh1,0,BTN_TYPE_ANG);  //2026-05-19 �Ƶ��������(���ڻ��� lcddev.width-300,1����Լ160����10px���)

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
		L_MB_State_Line[i]=0;		//��� ä����״̬���ӻ��ǲ�����
		R_MB_State_Line[i]=0;		//�ұ� ä����״̬���ӻ��ǲ�����
	}
	for	(i=0;i<Left_MB_Num;i++)
	{
		L_MB_State_Line[i]=1;		//���LINE ä����״̬���ӻ��ǲ�����
	}
	for	(i=0;i<Right_MB_Num;i++)
	{
		R_MB_State_Line[i]=1;		//�ұ�LINE ä����״̬���ӻ��ǲ�����
	}
	
	
	timer_bit=0;				//��ʱλ=0������ʱ��
	
// 	TIM3_Int_Init(100-1,2*8400-1);	//��ʱ��ʱ��84M����Ƶϵ��8400������84M/8400=10Khz�ļ���Ƶ�ʣ�����5000��Ϊ500ms     
 //	TIM3_Int_Init(100-1,9600-1);	//10Khz�ļ���Ƶ�ʣ�����5K��Ϊ500ms     
 //    TIM3_Int_Init(100-1,4000-1);       //��ʱ��3��ʼ������ʱ��ʱ��Ϊ90M����Ƶϵ��Ϊ9000-1��
 	
//	BEEP_Init();        //��ʼ���������˿�
	EXTIX_Init();       //��ʼ���ⲿ�ж����� 


	POINT_COLOR=WHITE;//RED;	 
	LCD_Clear(BLUE);//GREEN);	
	//����LCD��ʾ����
	//dir:0,������1,����
	LCD_Display_Dir(1);		//2023-3-17

	Init_Key_Pin();			//2023-5-11

	//�������������򱳾�  2023-11-8
	gui_fill_rectangle(carea_x0-10,carea_y0,carea_x0-10+1045,carea_y0+595,ControlArea_Color);	

	if(Startbtn&&Resetbtn)
	{
//			LCD_Clear(LGRAY);
	//	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//�·ֽ���	 
	//	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[23][gui_phy.language]);//��ʾ����  
 	
		//2026-05-12(2nd) 6�����ذ�ť������ɫ����ť����=��ɫ������=�߶Ա�ɫ����
		//   ��ʼ��ʱ  ����GREEN(0x07E0)    ����WHITE       ���� "GO" ����
		//   ׼������  ����YELLOW(0xFFE0)   ����BLACK       ���� ǳɫ���������ú���
		//   ��λ      ����BLUE(0x001F)     ����WHITE
		//   ����ʱ��  ����MAGENTA(0xF81F)  ����WHITE
		//   ��������  ����CYAN(0x07FF)     ����BLACK       ���� ǳɫ���������ú���
		//   �˳�/�ػ� ����RED(0xF800)      ����WHITE

		//���� ��ʼ��ʱ GREEN ����
		Startbtn->caption=Hds0_btncaption_tbl[0][gui_phy.language];
		Startbtn->font=btnfsize;
		Startbtn->bkctbl[0]=0X0420;	//���̱߿�
		Startbtn->bkctbl[1]=0X07E0;	//���̶���
		Startbtn->bkctbl[2]=0X07E0;	//�ϰ� ����
		Startbtn->bkctbl[3]=0X0500;	//�°� ����
		Startbtn->bcfucolor=WHITE;
		Startbtn->bcfdcolor=BLACK;

		//���� ��λ RED����"�˳�/�ػ�"ͬ�����������Ļ��ɫ�����ص������塣2026-05-12(3rd)������
		Resetbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		Resetbtn->font=btnfsize;
		Resetbtn->bkctbl[0]=0X9000;
		Resetbtn->bkctbl[1]=0XF800;
		Resetbtn->bkctbl[2]=0XF800;
		Resetbtn->bkctbl[3]=0X9000;
		Resetbtn->bcfucolor=WHITE;
		Resetbtn->bcfdcolor=BLACK;

		btn_draw(Startbtn);		//����ť
		btn_draw(Resetbtn);		//����ť


		Startbtn->caption=Hds0_btncaption_tbl[1][gui_phy.language];

		//���� �������� CYAN��ǳ�ף������� BLACK ���֣� ����
		Setupbtn->caption="��������";
		Setupbtn->font=btnfsize;
		Setupbtn->bkctbl[0]=0X041F;
		Setupbtn->bkctbl[1]=0X07FF;
		Setupbtn->bkctbl[2]=0X07FF;
		Setupbtn->bkctbl[3]=0X0410;
		Setupbtn->bcfucolor=BLACK;
		Setupbtn->bcfdcolor=WHITE;
		btn_draw(Setupbtn);		//�����ͷ���ʱ�� ��ť  2024-10-23

		//���� �˳�/�ػ� RED��������ȷ�ϣ�����Сһ��ȷ�� 5 ��ȫ��ʾ�� ����
		if(ExitShutdownbtn)
		{
			ExitShutdownbtn->bkctbl[0]=0X9000;
			ExitShutdownbtn->bkctbl[1]=0XF800;
			ExitShutdownbtn->bkctbl[2]=0XF800;
			ExitShutdownbtn->bkctbl[3]=0X9000;
			ExitShutdownbtn->bcfucolor=WHITE;
			ExitShutdownbtn->bcfdcolor=BLACK;
			ExitShutdownbtn->caption="�˳�/�ػ�";
			ExitShutdownbtn->font=24;
			btn_draw(ExitShutdownbtn);
		}

		//���� �������� GREEN���Ͽ�ʱ��ʾ"��������"�����ӳɹ����л�Ϊ"����Ͽ�"���䰵������
		if(NetConnbtn)
		{
			//2026-05-18(5) "��������"��ť��ɫ��δ����=��ɫ��Ŀ��������(��ʾ"����Ͽ�")=�Һ�
			if(connstatus==1){	//�����ӣ��Һ�
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0X6800;
				NetConnbtn->bkctbl[2]=0X6800;	NetConnbtn->bkctbl[3]=0X4000;
			}else{	//δ���ӣ���Ŀ��
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0XF800;
				NetConnbtn->bkctbl[2]=0XF800;	NetConnbtn->bkctbl[3]=0X8000;
			}
			NetConnbtn->bcfucolor=WHITE;
			NetConnbtn->bcfdcolor=BLACK;
			NetConnbtn->caption=(connstatus==1)?"����Ͽ�":"��������";
			NetConnbtn->font=24;
			btn_draw(NetConnbtn);
		}

		//���� ����ʱ�� MAGENTA ����
		SendStartTimerbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		SendStartTimerbtn->caption="����ʱ��";	//"���ͷ���ʱ��";
		SendStartTimerbtn->font=btnfsize;
		SendStartTimerbtn->bkctbl[0]=0X9010;
		SendStartTimerbtn->bkctbl[1]=0XF81F;
		SendStartTimerbtn->bkctbl[2]=0XF81F;
		SendStartTimerbtn->bkctbl[3]=0XA014;
		SendStartTimerbtn->bcfucolor=WHITE;
		SendStartTimerbtn->bcfdcolor=BLACK;
		btn_draw(SendStartTimerbtn);		//�����ͷ���ʱ�� ��ť  2024-9-1

		
	//��+1��ť
		Distance_Addbtn->caption="+1";
		Distance_Addbtn->font=btnfsize;
		btn_draw(Distance_Addbtn);		//��+1��ť

	//��-1��ť
		Distance_Decbtn->caption="-1";
		Distance_Decbtn->font=btnfsize;
		btn_draw(Distance_Decbtn);		//��-1��ť
										
				if(Pool50mOr25mbit==0)
				{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
					sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,��׼Ӿ��50m  2025-1-2
				}
				else
				{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
					sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);					//=1,�̳� 25m  		2025-1-2
				}		
				LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//��ʾ��������  2026-05-12 ����140
			
		gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color); 

//������ť Relay	2024-11-21
		Relaybtn->caption=Relay_btncaption_tbl[0][gui_phy.language];
		Relaybtn->font=btnfsize;
		btn_draw(Relaybtn);		//����ť


//���԰�ť Test		
		Testbtn->caption=Test_btncaption_tbl[0][gui_phy.language];
		Testbtn->font=btnfsize;
		btn_draw(Testbtn);		//����ť


//		Testbtn->caption=Test_btncaption_tbl[1][gui_phy.language];
		
//׼��������ť Ready ���� YELLOW ������ǳɫ�������� BLACK �߶Ա�
		Readybtn->caption=Ready_btncaption_tbl[0][gui_phy.language];
		Readybtn->font=btnfsize;
		Readybtn->bkctbl[0]=0XA500;	//���Ʊ߿�
		Readybtn->bkctbl[1]=0XFFE0;	//���ƶ���
		Readybtn->bkctbl[2]=0XFFE0;	//�ϰ� ����
		Readybtn->bkctbl[3]=0XC600;	//�°� ����
		Readybtn->bcfucolor=BLACK;	Readybtn->bcfdcolor=WHITE;	//2026-05-12(2nd) �Ƶ׺���
		btn_draw(Readybtn);		//����ť
		
//		Readybtn->caption=Ready_btncaption_tbl[1][gui_phy.language];
		
		system_task_return=0;
		
  }

	Exchange_StartFinalPlace();    //���������  2024-11-27	
	
	
	for(i=0;i<10;i++)
	{
		CloseLanebtn[i]=btn_creat(dir_posx,carea_y0+(i+1)*btnhy,CloseLanebtn_width,btnh,0,BTN_TYPE_ANG);

		CloseLanebtn[i]->bkctbl[0]=0X6BF6;	//�߿���ɫ
		CloseLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.��һ�е���ɫ				
		CloseLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,�ϰ벿����ɫ
		CloseLanebtn[i]->bkctbl[3]=0X2ADC;	//�°벿����ɫ	 
		CloseLanebtn[i]->bcfucolor=WHITE;	//�ɿ�ʱΪ��ɫ
		CloseLanebtn[i]->bcfdcolor=BLACK;	//����ʱΪ��ɫ 
//		CloseLanebtn[i]->caption=netplay_btncaption_tbl[4][gui_phy.language];
//		CloseLanebtn[i]->font=sbtnfsize;



		CloseLaneState[i]=2 ;					//�رյ���״̬=2���򿪣�=3���ر�
		CloseLanebtn[i]->caption="��";	//Hcmd_Lbtncaption_tbl[i];
		CloseLanebtn[i]->font=btnfsize;
		btn_draw(CloseLanebtn[i]);		//����/�رյ��ΰ�ť
	}	

	//ȡ�� ��Ҫ��ä���ɼ�  2024-10-15
/*
	for(i=0;i<10;i++)
	{
		RMBLanebtn[i]=btn_creat(RMBbtn_posx,RMBbtn_posy+(i+1)*btnhy,80,btnh,0,BTN_TYPE_ANG);

		RMBLanebtn[i]->bkctbl[0]=0X6BF6;	//�߿���ɫ
		RMBLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.��һ�е���ɫ				
		RMBLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,�ϰ벿����ɫ
		RMBLanebtn[i]->bkctbl[3]=0X2ADC;	//�°벿����ɫ	 
		RMBLanebtn[i]->bcfucolor=WHITE;	//�ɿ�ʱΪ��ɫ
		RMBLanebtn[i]->bcfdcolor=BLACK;	//����ʱΪ��ɫ 

		RMBLanebtn[i]->caption="��MB";	//Hcmd_Lbtncaption_tbl[i];
		RMBLanebtn[i]->font=btnfsize;
		btn_draw(RMBLanebtn[i]);		//����/�رյ��ΰ�ť
	}	
*/

	
	for(i=0;i<10;i++)
	{
		cmdLbtn[i]=btn_creat(carea_x0,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 ��Ϊ��ɫ��ť
		//2024-6-8
		if(SwimmingPool_Arrage==0) 
		{
			cmdLbtn[i]->caption=Hcmd_Lbtncaption_tbl[i];			//����߰�ť ������ʾ����
			Lane_NoTbl[i]=i;
		}
		else 
		{
			cmdLbtn[i]->caption=Hcmd_Inv_Lbtncaption_tbl[i];			//����߰�ť ������ʾ����
			Lane_NoTbl[i]=9-i;
		}
			
		Setup_LaneBtn_LightYellow(cmdLbtn[i]);
		cmdLbtn[i]->font=btnfsize;
		btn_draw(cmdLbtn[i]);		//����߰�ť

		sprintf((char*)lcd_Dis,"L%d",(i));
		//2026-05-17 MB ���ػ��� MB_Open_Close_State[0][i] ״̬ѡɫ
		{
			u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i];
			if(_mbs==4)      _mbc=UnInstall_Color;
			else if(_mbs==3) _mbc=Bad_Color;
			else if(_mbs==0) _mbc=Close_Color;
			else             _mbc=Open_MB_Color;
			Display_MB_StateGroup(0,i,_mbc,lcd_Dis);
		}

		//2026-05-14 Fix #3: �ػ�ʱ�� Startbox_/TP_Open_Close_State ������ɫ��
		//     ԭʼ�������� GREEN/YELLOW ��� "δ��װ(4)" / "��(3)" ״̬���ǵ���
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
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 ���=��
		else
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 ��=Open_TP_Color

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
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 ���=��
		else
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 ��=Open_TP_Color


			sprintf((char*)lcd_Dis,"R%d",(i));
			//2026-05-17 MB ���ػ��� MB_Open_Close_State[0][i+10] ״̬ѡɫ
			{
				u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i+10];
				if(_mbs==4)      _mbc=UnInstall_Color;
				else if(_mbs==3) _mbc=Bad_Color;
				else if(_mbs==0) _mbc=Close_Color;
				else             _mbc=Open_MB_Color;
				Display_MB_StateGroup(1,i,_mbc,lcd_Dis);
			}

			cmdRbtn[i]=btn_creat(btndsx,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 ��Ϊ��ɫ��ť

			//2024-6-8
			if(SwimmingPool_Arrage==0)
			{
				cmdRbtn[i]->caption=Hcmd_btncaption_tbl[i];			//���ұ߰�ť ������ʾ����
				Lane_NoTbl[i+10]=10+i;
			}
			else
			{
				cmdRbtn[i]->caption=Hcmd_Inv_btncaption_tbl[i];			//���ұ߰�ť ������ʾ����
				Lane_NoTbl[i+10]=10+9-i;
			}
			//2026-05-13 �ҵ��ΰ�ť��ɫ������ɫ���� + ����
			cmdRbtn[i]->bkctbl[0]=0XC600;	//���Ʊ߿�
			cmdRbtn[i]->bkctbl[1]=0XFFF8;	//���ƶ���
			cmdRbtn[i]->bkctbl[2]=0XFFF8;	//�ϰ뵭��
			cmdRbtn[i]->bkctbl[3]=0XEFE0;	//�°��԰�����
			cmdRbtn[i]->bcfucolor=BLACK;
			cmdRbtn[i]->bcfdcolor=WHITE;
/*	
	u8 type;						//��ť����
									//[7]:0,ģʽA,������һ��״̬,�ɿ���һ��״̬.
									//	  1,ģʽB,ÿ����һ��,״̬�ı�һ��.��һ�°���,�ٰ�һ�µ���.
									//[6:4]:����
									//[3:0]:0,��׼��ť;1,ͼƬ��ť;2,�߽ǰ�ť;3,���ְ�ť(����͸��),4,���ְ�ť(������һ)
	u8 sta;							//��ť״̬
									//[7]:����״̬ 0,�ɿ�.1,����.(������ʵ�ʵ�TP״̬)
									//[6]:0,�˴ΰ�����Ч;1,�˴ΰ�����Ч.(����ʵ�ʵ�TP״̬����)
									//[5:2]:����
									//[1:0]:0,�����(�ɿ�);1,����;2,δ�������
	u8 *caption;					//��ť����
	u8 font;						//caption��������
	u8 arcbtnr;						//Բ�ǰ�ťʱԲ�ǵİ뾶										
	u16 bcfucolor; 				  	//button caption font up color
	u16 bcfdcolor; 				  	//button caption font down color

	u16 *bkctbl;					//�������ְ�ť:
									//����ɫ��(��ťΪ���ְ�ť��ʱ��ʹ��)
									//a,��Ϊ���ְ�ť(����͸��ʱ),���ڴ洢����ɫ
									//b,��Ϊ���ְ�ť(������һ��),bkctbl[0]:����ɿ�ʱ�ı���ɫ;bkctbl[1]:��Ű���ʱ�ı���ɫ.
									//���ڱ߽ǰ�ť:
									//bkctbl[0]:Բ�ǰ�ť�߿����ɫ
									//bkctbl[1]:Բ�ǰ�ť��һ�е���ɫ
									//bkctbl[2]:Բ�ǰ�ť�ϰ벿�ֵ���ɫ
									//bkctbl[3]:Բ�ǰ�ť�°벿�ֵ���ɫ	

	u8 *picbtnpathu;				//ͼƬ��ť�ɿ�ʱ��ͼƬ·��
	u8 *picbtnpathd;		 		//ͼƬ��ť����ʱ��ͼƬ·��
*/

			Setup_LaneBtn_LightYellow(cmdRbtn[i]);
			cmdRbtn[i]->font=btnfsize;
			btn_draw(cmdRbtn[i]);		//���ұ߰�ť
		}

	
		GT9271_Init(); 
		tp_dev.scan=GT9271_Scan;		//ɨ�躯��ָ��GT271������ɨ��		
		tp_dev.touchtype|=0X80;			//������ 
	//	tp_dev.touchtype|=lcddev.dir&0X01;//������������ 
			tp_dev.touchtype|=lcddev.dir&0X00;//������������ 

		
//	LCD_Clear(NET_MEMO_BACK_COLOR);//���� 
	
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

//	2026-05-19 ȡ�������������: ��ԭ���� "�������ӣ�"��ǩ�ı���, �����ס��/������ y<36 �İ�ť.
//	gui_fill_rectangle(0,0,lcddev.width,ip_height,NET_IP_BACK_COLOR);			//���IP��ַ���򱳾�
//	gui_fill_rectangle(0,ip_height,lcddev.width,msg_height,NET_MSG_BACK_COLOR);	//��Ϣ���򱳾�
//	gui_draw_hline(0,ip_height+msg_height-1+25,lcddev.width,NET_COM_RIM_COLOR);	//�ָ���  2023-11-9
//	tempy=ip_height+msg_height+rmemo_height+fsize+2*rm_offy; 
	tempy=720; 
 //	gui_draw_hline(0,tempy,lcddev.width,NET_COM_RIM_COLOR);	//�ָ���  2023-11-9
//��ʾIP ����
//	tempx=(lcddev.width-35*ip_fsize/2)/3-50;
		gui_show_string("�������ӣ�",lcddev.width-630,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//��ʾ���������� ��  2024-10-27

//2024-10-27	gui_show_string(ipcaption,IP_Sx,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//����IP/Ŀ��IP
//	tempx=lcddev.width-tempx-10*ip_fsize/2-50;
//2024-10-27	gui_show_string(netplay_portcaption_tb[gui_phy.language],Port_Sx,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//�˿�:

	tempy=800-60;//ip_height+msg_height+rm_offy+fsize; 
//	gui_show_string(netplay_memoremind_tb[0][gui_phy.language],m_offx,tempy-fsize-rm_offy/3,lcddev.width,fsize,fsize,WHITE);//NET_MSG_FONT_COLOR);//��ʾ������
//	rmemo=memo_creat(m_offx,tempy,memo_width,rmemo_height,0,0,fsize,NET_RMEMO_MAXLEN);//����memo�ؼ�,���NET_RMEMO_MAXLEN���ַ�	
	
	
//	tempy=ip_height+msg_height+rm_offy*2+rmemo_height+fsize*2+sm_offy; 
//	gui_show_string(netplay_memoremind_tb[1][gui_phy.language],m_offx+lcddev.width/3,tempy-fsize-sm_offy/3,lcddev.width,fsize,fsize,WHITE);//NET_MSG_FONT_COLOR);//��ʾ������
//	smemo=memo_creat(m_offx+memo_width+50,tempy,memo_width,smemo_height,0,1,fsize,NET_SMEMO_MAXLEN);//���NET_SMEMO_MAXLEN���ַ�	

	//2023-5-15
//	tempx=lcddev.width-tempx-10*ip_fsize/2;	
//	eip=edit_creat(strlen((char*)ipcaption)*ip_fsize/2+IP_Sx,(ip_height-ip_fsize-6)/2,15*ip_fsize/2+6,ip_fsize+6,0,4,ip_fsize);//����ip�༭��

// 	tempx=lcddev.width-tempx-10*ip_fsize/2;	
// 	eport=edit_creat(Port_Sx+5*ip_fsize/2,(ip_height-ip_fsize-6)/2,5*ip_fsize/2+6,ip_fsize+6,0,4,ip_fsize);//����eport�༭��

//	tempy=ip_height+msg_height+rm_offy*2+rmemo_height+fsize*2+sm_offy*2+smemo_height; 
//	t9=t9_creat((lcddev.width%5)/2,tempy,lcddev.width-(lcddev.width%5),t9height,0);//t9�Ŀ��ȱ�����5�ı���	
	tempy=ip_height+msg_height+rm_offy+fsize; 
 
	memo_width=(rmemo_height-3*rbtn_height)/2;//����һ��memo_width.
	if(memo_width>rbtn_height/2)memo_width=rbtn_height/2;

// 	protbtn=btn_creat(protbtn_ux,2,btn_width,rbtn_height,0,0);
//	connbtn=btn_creat(connbtn_ux,2,btn_width,rbtn_height,0,0);	
//	clrbtn=btn_creat(RX_Sx+150-20,2,btn_width,rbtn_height,0,0);	

//	sendbtn=btn_creat(btnds1x,btnds1y+2*rbtn_height,btn_width,48,0,2);	//�����߽ǰ�ť
// 	sendbtn=btn_creat(Inf_area_x0+500+200,Inf_area_y0,CMD_btnw,btnh1,0,2);	//�����߽ǰ�ť  2023-11-9

//  p=gui_memin_malloc(1024);	//����1500�ֽ��ڴ�
// 	ptemp=gui_memin_malloc(1024);//����100�ֽ��ڴ�
  p=gui_memin_malloc(2048);	//����1500�ֽ��ڴ�
 	ptemp=gui_memin_malloc(1024);//(100);//����100�ֽ��ڴ�   2024-10-26
//	if(!rmemo||!eip||!eport||!smemo||!t9||!protbtn||!connbtn||!clrbtn||!sendbtn||!p||!ptemp)rval=1;//����ʧ��. 
//	if(!rmemo||!eip||!eport||!smemo||!protbtn||!connbtn||!clrbtn||!sendbtn||!p||!ptemp)rval=1;//����ʧ��. 
//	if(!eip||!eport||!protbtn||!connbtn||!clrbtn||!p||!ptemp)rval=1;//����ʧ��. 
	if(!p||!ptemp)rval=1;//����ʧ��. 
//	if(!rmemo||!eip||!eport||!smemo||!connbtn||!clrbtn||!sendbtn||!p||!ptemp)rval=1;//����ʧ��. 
		    

	if(rval==0)//�����ɹ�
	{ 
//		protbtn->caption=netplay_btncaption_tbl[0][gui_phy.language];
//		protbtn->font=fsize;
	//	connbtn->caption=netplay_btncaption_tbl[1][gui_phy.language];
//		connbtn->font=fsize;
	//	clrbtn->caption=netplay_btncaption_tbl[3][gui_phy.language];  2024-10-17
	//	clrbtn->font=fsize;
/*
		sendbtn->bkctbl[0]=0X6BF6;	//�߿���ɫ
		sendbtn->bkctbl[1]=0X545E;	//0X8C3F.��һ�е���ɫ				
		sendbtn->bkctbl[2]=0X5C7E;	//0X545E,�ϰ벿����ɫ
		sendbtn->bkctbl[3]=0X2ADC;	//�°벿����ɫ	 
		sendbtn->bcfucolor=WHITE;	//�ɿ�ʱΪ��ɫ
		sendbtn->bcfdcolor=BLACK;	//����ʱΪ��ɫ 
		sendbtn->caption=netplay_btncaption_tbl[4][gui_phy.language];
		sendbtn->font=sbtnfsize;
*/
/*
		eip->textbkcolor=NET_IP_BACK_COLOR;
		eip->textcolor=WHITE;
		eport->textbkcolor=NET_IP_BACK_COLOR;
		eport->textcolor=GREEN;//GREEN,��ʾ���Ա༭
*/
//		rmemo->textbkcolor=WHITE;
//		rmemo->textcolor=BLACK;
//		smemo->textbkcolor=WHITE;
//		smemo->textcolor=BLACK; 
/*
sprintf((char*)ptemp,"%d.%d.%d.%d",lwipdev.ip[0],lwipdev.ip[1],lwipdev.ip[2],lwipdev.ip[3]);
 		strcpy((char*)eip->text,(const char *)ptemp);	//����IP��ַ
		sprintf((char*)ptemp,"%d",tport);
		strcpy((char*)eport->text,(const char *)ptemp);	//�����˿ں�
 		edit_draw(eip);			//���༭��
 		edit_draw(eport);		//���༭��
*/
//		memo_draw_memo(smemo,0);//��memo�ؼ�
//		memo_draw_memo(rmemo,0);//��memo�ؼ�
//		btn_draw(protbtn);
	//	btn_draw(connbtn);
//		btn_draw(clrbtn);   2024-10-17
//		btn_draw(sendbtn); 
//		t9_draw(t9);	
//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,0X07);//��ʾ��Ϣ 2024-10-27
	}
						
		sendcmdbuf=netbuf_new();
		netbuf_alloc(sendcmdbuf,32*TxRx_Data_Length);

		gui_fill_circle(cds0x,1+cr,cr,Close_Color); 			//��������ָʾ�� �죺����  �ң�������
								
		display_closetime();	//��ʾӾ������ر�ʱ��  2023-10-17

				RS_TX_No=0;
	
		RS_TX_Ptr=0;
		RS_TX_Bit=0;
		RS_TX_len=0;		//�������ݳ���   2023-10-23

//arr���Զ���װֵ��
//psc��ʱ��Ԥ��Ƶ��
//��ʱ�����ʱ����㷽��:Tout=((arr+1)*(psc+1))/Ft us.
//Ft=��ʱ������Ƶ��,��λ:Mhz
	//	u16 Osc4Mhz_arr=10-1,Osc4Mhz_psc=400-1;			
	//	u16 Osc4Mhz_arr=3999-1,Osc4Mhz_psc=1-1;				//2023-7-14
	//	u16 Osc4Mhz_arr=4000-1,Osc4Mhz_psc=1-1;				//2023-7-14
		u16 Osc4Mhz_arr=4000-1,Osc4Mhz_psc=10-1;				//2023-7-28   10�����ж�һ��
//		u16 Osc4Mhz_arr=400-1,Osc4Mhz_psc=10-1;				//2023-9-25   10�����ж�һ��
		TIM8_Int_Init_ETR(Osc4Mhz_arr,Osc4Mhz_psc);       //��ʱ��8��ʼ������ʱ��ʱ��Ϊ4M����Ƶϵ��Ϊ4000-1��


	
	Voltage_x0=400;		//2024-12-8
	Voltage_y0=1;

	Adc_Init();						//��ʼ��ADC
	//	LCD_ShowString(Voltage_x0+250,Voltage_y0,200,16,16,"ADC1_CH19_VAL:");	      
//	LCD_ShowString(Voltage_x0,Voltage_y0+20,200,16,2*16,"ADC1_CH19_VOL:0.000V");//���ڹ̶�λ����ʾС����  	
	LCD_ShowString(Voltage_x0,Voltage_y0,200,16,2*16,"BatVol:00.00V");//���ڹ̶�λ����ʾС����  	
//	LCD_ShowString(Voltage_x0,Voltage_y0,200,16,2*16,"������ѹ:00.00V");//���ڹ̶�λ����ʾС����  	

//	calendar_display();
 	u8 led0sta=1;  
 	u16 adcx;
	float temp;

										
	if(PoolSingleOrDoubleTPbit==1)   //Ӿ�ذ�װ������һ��=1; ����=0  2025-1-6
	{
			for(i=0;i<10;i++)			
			{
				TP_Open_Close_State[i][1-FinalPlace]=4;			//����û�а�װ =4; // FinalPlace=0:�յ�����Ļ��ߣ� =1���յ�����Ļ�ұߡ�
				TP_Open_Close_State[i][FinalPlace]=0;			//���尲װ =0; // FinalPlace=0:�յ�����Ļ��ߣ� =1���յ�����Ļ�ұߡ�
			}
	}
	else 
	{
			for(i=0;i<10;i++)			
			{
				TP_Open_Close_State[i][1-FinalPlace]=0;			//���尲װ =0; // FinalPlace=0:�յ�����Ļ��ߣ� =1���յ�����Ļ�ұߡ�
				TP_Open_Close_State[i][FinalPlace]=0;			//���尲װ =0; // FinalPlace=0:�յ�����Ļ��ߣ� =1���յ�����Ļ�ұߡ�
			}
	}

	Reset_Timer();

	//2026-05-18(3) ���� STM32 H7 Backup SRAM (VBAT �־�) �����Լ����ϴα����״̬
	//   �� BKPSRAM У��ͨ����magic+version+checksum�������ǲ���/TP/SB/MB/��������/��ʱλ��
	//   ��У��ʧ�ܣ��״�����/VBAT �ϵ磩������ Reset_Timer ���Ĭ��ֵ
	BkpSRAM_Init();
	Load_State_From_BkpSRAM();
				
//	UART4->CR3|=1<<28;				// λ28 RXFTIE:RXFIFO ��ֵ�ж�ʹ�� (RXFIFO threshold interrupt enable)
														//1:������ FIFO �ﵽ RXFTCFG �б�̵���ֵʱ,���� USART �ж�

//		UART4->CR3|=1<<23;				// λ23 TXFTIE:TXFIFO ��ֵ�ж�ʹ�� (TXFIFO threshold interrupt enable)
														//1:��TXFIFO �ﵽ TXFTCFG �б�̵���ֵʱ,���� USART �ж�
//		UART4->ICR|=(1<<5);    			//TXFECF:TXFIFO Ϊ�������־ �����λд��1ʱ,USART_ISR �Ĵ����е� TXFE ��־������
//	UART4->CR1|=1<<30;	 			
		//λ 30 TXFEIE:TXFIFO Ϊ��ʱ�ж�ʹ�� (TXFIFO empty interrupt enable)
		//��λ�������� 1 ������
		//0:��ֹ�ж�
		//1:��USART_ISR �Ĵ����е� TXFE=1,���� USART �ж�
//	UART4->ISR|=1<<23;  				//λ 23 TXFE:TXFIFO Ϊ�� (TXFIFO Empty)
		//��� USART_CR1 �Ĵ����е� TXFEIE λ =1��λ 30),�������ж�
		//0:TXFIFO ��Ϊ��
		//1:TXFIFO Ϊ��	
		
//	UART4->CR3|=1<<23;				// λ23 TXFTIE:TXFIFO ��ֵ�ж�ʹ�� (TXFIFO threshold interrupt enable)
														//1:��TXFIFO �ﵽ TXFTCFG �б�̵���ֵʱ,���� USART �ж�
		
//	UART4->ICR|=(1<<6);    			//TCCF=1
//	UART4->CR1|=1<<6;  			//���ڷ����ж�ʹ��   CR1 �е� TCIE=1���������ж�
	UART4->CR1|=1<<3;  			//���ڷ���ʹ��  Transmitter enable
	
	while(1)
	{ 
		calendar_get_time(&calendar);	//����ʱ��    2024-11-10 
//		if(system_task_return)break;	//��Ҫ����	  
 		if(Csecond!=calendar.sec)//���Ӹı���
		{ 	
  			Csecond=calendar.sec;  
			//2026-05-26 (���� B): Ӳ��δ�� VBAT ���ݵ��, BKPSRAM �ϵ�Ͷ�, ��������д��
			//   ԭ "ÿ 10 �� Save_State_To_BkpSRAM()" �ѽ��á����г־û����е��¼�������
			//   ���� OnWriteMatchData() д���� NAND Flash (2:/swimtime.cfg, �ϵ籣��)��
			//   ������: case Set_MatchEvent / Set_ArmDelay_Time / Set_PoolConfiguration_Com1
			//          / Set_MB_Num / Set_PoolSingleOrDoubleTP / ���� +1/-1 ���밴ť �ȡ�
			sprintf((char*)lcd_id,"%2d:%02d:%02d",calendar.hour,calendar.min,calendar.sec);//��LCD ID��ӡ��lcd_Dis���顣	
			LCD_ShowString(lcddev.width-128,1,240,32*2,32,lcd_id);		//��ʾLCD ID	  2024-11-10    					 
		
			calendar_get_date(&calendar);	//��������		
			if(calendar.w_date!=tempdate)
			{
				tempdate=calendar.w_date;	//�޸�tempdate����ֹ�ظ�����
				sprintf((char*)lcd_id,"%4d-%02d-%02d",calendar.w_year,calendar.w_month,calendar.w_date);//��LCD ID��ӡ��lcd_Dis���顣	
				LCD_ShowString(lcddev.width-300,1,240,32*2,32,lcd_id);		//��ʾLCD ID	  2024-11-10    					 
			}
		
			
//		if(Ready_timer_bit==0)  		//�ڲ���ʱʱ�ż���ص�ѹ����׼��������ʱ����ʼ��ʱʱ������ص�ѹ 2024-12-9
			if((t%5)==0)
		{
//	  adcx=Get_Adc_Average(ADC1_CH19,20);		//��ȡͨ��5��ת��ֵ��20��ȡƽ��
			adcx=Get_Adc_Average(ADC1_CH19,1);		//��ȡͨ��5��ת��ֵ��1��ȡƽ��

	//	LCD_ShowxNum(Voltage_x0-30+142+250,Voltage_y0,adcx,5,16,0);		//��ʾADCC�������ԭʼֵ
//		temp=(float)adcx*(3.3/65536);			//��ȡ�����Ĵ�С����ʵ�ʵ�ѹֵ������3.1111
			temp=(float)adcx*(8.45/65536)*(8.3/7.8);			//��ȡ�����Ĵ�С����ʵ�ʵ�ѹֵ������3.1111    //2025-1-3 ����ϵ��*(8.3/7.8)
			adcx=temp;								//��ֵ�������ָ�adcx��������ΪadcxΪu16����
			LCD_ShowxNum(Voltage_x0-30+142,Voltage_y0,adcx,2,2*16,0);		//��ʾ��ѹֵ���������֣�3.1111�Ļ������������ʾ3
			temp-=adcx;								//���Ѿ���ʾ����������ȥ��������С�����֣�����3.1111-3=0.1111
			temp*=100;								//С�����ֳ���100�����磺0.1111��ת��Ϊ11.11���൱�ڱ�����λС����
			LCD_ShowxNum(Voltage_x0-30+158+16+16,Voltage_y0,temp,2,2*16,0X80);	//��ʾС�����֣�ǰ��ת��Ϊ��������ʾ����������ʾ�ľ���111.
//			temp*=10;								//С�����ֳ���100�����磺0.1111��ת��Ϊ11.11���൱�ڱ�����λС����
//			LCD_ShowxNum(Voltage_x0-30+158+16+16,Voltage_y0,temp,1,2*16,0X80);	//��ʾС�����֣�ǰ��ת��Ϊ��������ʾ����������ʾ�ľ���111.

			//2026-05-13(2) �ѵ�ǰ��ص�ѹ�ϱ������Ƽ������raw 0x4B���� PC �� BatteryVoltage ������룩��
			//   ��λ������ mV  (e.g. 12.34V -> 12340)
			//   v_mV = �������֡�1000 + С�����֡�10  ����ʱ temp �Ѿ�Ϊ decimal��100��
			//   �� �� ͨѶЭ����˵��_v2026.05.13.pdf ����Ϊ BIG-ENDIAN��
			//        d3 = v_mV ���ֽ�, d4 = v_mV ���ֽ�
			{
				u16 v_mV = (u16)((u32)adcx*1000 + (u32)temp*10);
				OnSendSWCommand_Data(Report_Voltage_RAW, (u8)((v_mV>>8) & 0xFF), (u8)(v_mV & 0xFF), 0, 0, 0, 0, 0, 0);
				Send_Bit=2;
			}
		}
			t++;

	 	} 
	
			tp_dev.scan(0);    
			tp_dev.touchtype|=lcddev.dir&0X00;//������������ 
			in_obj.get_key(&tp_dev,IN_TYPE_TOUCH);	//�õ�������ֵ   
			res=btn_check(Startbtn,&in_obj);   
			if(res&&((Startbtn->sta&(1<<7))==0)&&(Startbtn->sta&(1<<6)))//������,�а���M.Start�������ɿ�,����TP�ɿ���
			{
				StartTiming();
			}

		//2026-05-17 �������ð�ť������������(timer_bit==1)��׼������̬(Ready_timer_bit==1)ʱ
		//   �ûҽ��ã�ȷ�������в��Ĳ�������λ��(�� bit ��Ϊ 0)�ָ���ɫ���á�
		{
			static u8 _setup_btn_last = 255;
			u8 _setup_btn_busy = (timer_bit || Ready_timer_bit) ? 1 : 0;
			if(_setup_btn_busy != _setup_btn_last && Setupbtn){
				if(_setup_btn_busy){
					//���ã����ұ��� + ����
					Setupbtn->bkctbl[0]=0X2104;
					Setupbtn->bkctbl[1]=0X4208;
					Setupbtn->bkctbl[2]=0X4208;
					Setupbtn->bkctbl[3]=0X2104;
					Setupbtn->bcfucolor=0X8410;
				}else{
					//���ã��ָ�ԭ��ɫ + ����
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
		//2024-10-23  ��������
			res=btn_check(Setupbtn,&in_obj);   
			//2026-05-17 ��ʱ�����л�׼������̬ʱ����Ӧ
			if(timer_bit==0 && Ready_timer_bit==0 && res&&((Setupbtn->sta&(1<<7))==0)&&(Setupbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{
		//		net_play();				//�������

				//2026-05-16 �����������ǰ�ȵ�ȷ�Ͽ򣬱�����
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ�Ͻ���������ã�",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);
				delay_ms(800);
				if(res==OkbtnValue)
				{
					//2026-05-18 �����������ǰ����"�ǲ���"״̬��ȱ�� CloseLaneState / ʣ��Ȧ�� laps��
					//   �������ý��治�����Щ���� SwimControl_init ĩβ�� Reset_Timer ����������
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
					
					//2026-05-18 �ָ������"�ǲ���"״̬ + �ػ���� UI
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
					//2026-05-16 �����޸ķ�������ʱ���� Open_State ͨ�� 0x47 �ϱ��� PC��ʹ PC ��"ȫ����/ȫ���ر�"��ť״̬��Ӳ��һ��
					//   on-the-wire: D2=0x47 D3=0xFF(ȫ����) D4=Open_State(1=ȫ��/0=ȫ��) D5..D10=0
					OnSendSWCommand_Data(Set_LaneOpenClose+0x10, 0xFF, (Open_State==1)?1:0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-16 ͬ��Ӿ�ش��尲װ��ʽ: 0x3A D3=PoolSingleOrDoubleTPbit (0=���� / 1=����)
					OnSendSWCommand_Data(Set_PoolSingleOrDoubleTP+0x10, PoolSingleOrDoubleTPbit, 0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-16 ͬ��Ӿ�س���: 0x44 D3=Ӿ����(Ĭ��10) D4=Ӿ�س���(50/25)
					OnSendSWCommand_Data(Set_PoolConfiguration_Com1+0x10, 10, (Pool50mOr25mbit==0)?50:25, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-17 ͬ������˳��: 0x62 D3=SwimmingPool_Arrage (0=���� / 1=����)
					OnSendSWCommand_Data(Set_LaneOrder+0x10, SwimmingPool_Arrage, 0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-17 ͬ���յ�λ��: 0x63 D3=FinalPlace (0=�յ���� / 1=�յ��Ҷ�)
					OnSendSWCommand_Data(Set_FinishPosition+0x10, (u8)(FinalPlace&0x01), 0, 0, 0, 0, 0, 0, 0);
					Send_Bit=2;
					//2026-05-17 ͬ�� 5 ��ʱ������: 0x64 (��λ��Ϊ��, Ӳ���ڲ� 0.1s ��λ�� /10 ��ԭ)
					//   D3=Close_Time/10 D4=TP_DelayCloseValue/10 D5=Relay_SB_DelayCloseValue/10 D6=MBdelay_Time/10 D7=Result_Display_Time/10
					OnSendSWCommand_Data(Set_TimingsBundle+0x10,
						(u8)(Close_Time/10), (u8)(TP_DelayCloseValue/10), (u8)(Relay_SB_DelayCloseValue/10),
						(u8)(MBdelay_Time/10), (u8)(Result_Display_Time/10), 0, 0, 0);
					Send_Bit=2;
					delay_ms(800);				//2026-05-16 �� SwimControl_init �ػ��� TP ��ɫ���� Open_State ����仯��ͣ���ɼ���������ѭ������ʱ���Ƚӹ�
				}
			}
			
		//2024-9-1
			res=btn_check(SendStartTimerbtn,&in_obj);   
			if(res&&((SendStartTimerbtn->sta&(1<<7))==0)&&(SendStartTimerbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{
				//2024-9-1
				OnSendSWCommand_Data(Start_Command+0x10,0,0,Start_minute,Start_second,Start_msecond/10,Start_hour*16+Start_msecond%10,Start_hour,0);
				Send_Bit=2;														//�÷��ͱ�־
			}

			
			res=btn_check(Resetbtn,&in_obj);   
			if(res&&((Resetbtn->sta&(1<<7))==0)&&(Resetbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{    
//���ƶ�λ����ʾһ��msg box
//x,y,width,height:����ߴ�
//str:�ַ���
//caption:��Ϣ��������
//font:�����С
//color:��ɫ
//mode:
//[7]:0,û�йرհ�ť.1,�йرհ�ť			   
//[6]:0,����ȡ����ɫ.1,��ȡ����ɫ.					 
//[5]:0,���⿿��.1,�������.					 
//[4:2]:����
//[1]:0,����ʾȡ������;1,��ʾȡ������.
//[0]:0,����ʾOK����;1,��ʾOK����.
//time:��ʱʱ��,��λ:ms(����û�а�������Ҫ��ȡ����ɫ��ʱ��,��Ч,���65535)
//����ֵ:
//0,û���κΰ�������/�����˴���.
//1,ȷ�ϰ�ť������.
//2,ȡ����ť������.	   
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ�ϼ�ʱ��λ��",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//��ʾ��ʱ��λ����ʾ��Ϣ
	//			delay_ms(800);//��ʱ�ȴ���ʾ
				if(res==OkbtnValue)
				{
					timer_bit=0;
					gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color);
		//		LCD_ShowString(Middle_timer_posx,Final_timer_posy+5*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID

//				ds1sta=!ds1sta;
//				Resetbtn->caption=Hds1_btncaption_tbl[ds1sta][gui_phy.language];

					Reset_Timer();
				}
			}

			//2026-05-11 �˳�/�ػ���ť������ȷ�ϣ��������ݺ���������ʾ�رյ�Դ��ͣ�� WFI ѭ��
			if(ExitShutdownbtn)
			{
				//2026-05-26 busy-state visual disable: ������(timer_bit||Ready_timer_bit) ��ť��ʾ���ҽ�����ʽ,
				//   �Ǳ���̬�ָ�ԭ��ɫ, �Ӿ���ʾ�û���ǰ�ܷ���������״̬�仯ʱ btn_draw, ����ÿ����ѭ���ػ���
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
				//2026-05-26 ������(��ʱ̬/׼��̬)������Ч, ��ֹ��ػ����ɼ�
				if(res&&((ExitShutdownbtn->sta&(1<<7))==0)&&(ExitShutdownbtn->sta&(1<<6))&&timer_bit==0&&Ready_timer_bit==0)
				{
					res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ���˳�/�ػ���",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);
					if(res==OkbtnValue)
					{
						OnWriteMatchData();		//����������������� NAND Flash (2:/, FatFs �� 2)
						timer_bit=0;
						Ready_timer_bit=0;
						Timer_Reset(1-0);
						//2026-05-13 �ػ����棺��ף�ʹ�� gui_show_string��֧�����ģ����������
						//"��رյ�Դ��"��С��"�����ѱ���"��������ʾ��LCD_ShowString ��֧�����ġ�
						LCD_Clear(RED);
						BACK_COLOR=RED;
						{
							u8 *msg_big   = (u8*)"��رյ�Դ��";
							u8 *msg_small = (u8*)"�����ѱ���";
							//2026-05-13 ����Ϣ����ˮƽ���У�
							//"��رյ�Դ" 5 ������ + "��" ȫ��(ռ 1 �����Ŀ�) = 6 char �� 32 px = 192 px
							u16 big_w = 6 * 32;	//ʵ����Ⱦ����
							u16 big_x = (lcddev.width  - big_w) / 2;
							u16 big_y = (lcddev.height - 32  ) / 2;
							//font=32 + ƫ�� 1 px �ػ�һ���γɼӴ�Ч��
							gui_show_string(msg_big, big_x,   big_y,   big_w+8, 64, 32, WHITE);
							gui_show_string(msg_big, big_x+1, big_y+1, big_w+8, 64, 32, WHITE);
							//����Ϣ��font=24, 5 ������ = 120 px
							u16 small_w = 5 * 24;
							u16 small_x = (lcddev.width  - small_w) / 2;
							u16 small_y = big_y - 48;
							gui_show_string(msg_small, small_x, small_y, small_w+8, 32, 24, YELLOW);
						}
						//��������Ӳ���ػ���ͣ�� WFI ѭ���ȴ��û��ֶ��ϵ�
						while(1)
						{
							delay_ms(500);
							__WFI();
						}
					}
				}
			}

			//2026-05-13 �����涥��"��������/����Ͽ�"��ť������ net_toggle_connect()
			//���ܵ�ͬ���á�����Э��ѡ������ӣ�������������ý��棻���º���� connstatus
			//���°�ť���⡣protbtn ��"����"�� net_test �˳������Ч������������ͨ����
			//��� NetConnbtn ������ connstatus �л�"��������/����Ͽ�"��
			if(NetConnbtn)
			{
				//2026-05-18(5) "��������/�Ͽ�"��ť��״̬(connstatus) + ����̬(timer_bit/Ready_timer_bit) ������ɫ
				//   δ���� idle �� �� (��Ŀ, ��������)
				//   ������ idle �� �Һ� (�͵�, ���ѿɶϿ�)
				//   ������/׼��̬ �� ���ҽ��� (���²���Ӧ, ͬ Setupbtn)
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
					NetConnbtn->caption=(connstatus==1)?"����Ͽ�":"��������";
					btn_draw(NetConnbtn);
					prev_netbtn_busy = _net_busy;
					prev_connstatus_net = connstatus;
				}
				//���£��� idle ̬��Ӧ (������/׼��̬��ֹ�л���������״̬)
				res=btn_check(NetConnbtn,&in_obj);
				if(_net_busy==0 && res&&((NetConnbtn->sta&(1<<7))==0)&&(NetConnbtn->sta&(1<<6)))
				{
					net_toggle_connect();
					//��ɫ+caption ����һ֡״̬������Զ����� (�� connstatus ����)
				}
			}

			//2024-11-21
			res=btn_check(Relaybtn,&in_obj);
			if(res&&((Relaybtn->sta&(1<<7))==0)&&(Relaybtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{  
				if((All_Lap%8)==0)  //������400m,800m
				{
					res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ�Ͻ��н���������",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//��ʾ���н�����������ʾ��Ϣ				
	//			delay_ms(800);//��ʱ�ȴ���ʾ
					if(res==OkbtnValue)
					{
						RelayBit=1;  //�ý�����־λ=1 2024-11-24
						RelayLaps=All_Lap/8;  //�������� 2024-11-24
						Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
						btn_draw(Relaybtn);		//����ť
					}
				}
				else {
					RelayBit=0;  //�������־λ=0 2024-11-24
					RelayLaps=0;  //���ǽ������� 2024-11-24
					Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
					btn_draw(Relaybtn);		//����ť
				}
			}	
	
			res=btn_check(Testbtn,&in_obj);   
			if(res&&((Testbtn->sta&(1<<7))==0)&&(Testbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{    
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ�Ͻ��в��ԣ�",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//��ʾ��ʱ��λ����ʾ��Ϣ				
	//			delay_ms(800);//��ʱ�ȴ���ʾ
				if(res==OkbtnValue)
				{
					Test_Button();
	//				ds1sta=!ds1sta;

				}
			}	
	
			//2024-11-3
	/*
			res=btn_check(LaneInvbtn,&in_obj);   
			if(res&&((LaneInvbtn->sta&(1<<7))==0)&&(LaneInvbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{    
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ�Ͻ��е��α任��",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//��ʾ��ʱ��λ����ʾ��Ϣ				
	//			delay_ms(800);//��ʱ�ȴ���ʾ
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
			if(res&&((Readybtn->sta&(1<<7))==0)&&(Readybtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
			{    
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ��׼��������",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//��ʾ��ʱ��λ����ʾ��Ϣ				
		//		delay_ms(800);//��ʱ�ȴ���ʾ
				if(res==OkbtnValue)
				{
	//				timer_bit=0;
	//				gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN); 
		//		LCD_ShowString(Middle_timer_posx,Final_timer_posy+5*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	      					 
		
//				ds1sta=!ds1sta;
//				Readybtn->caption=Ready_btncaption_tbl[ds1sta][gui_phy.language]; 

					TP_Ready_Init();
		//			display_rollingtime();		//��ʾ����ʱ��		2023-7-11
				}
			}	
	
			key=KEY_Scan(1);					//����ɨ��
			if(key==WKUP_PRES)					//WKUP����������
			{
				res=window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,300,180,"ȷ��׼��������",(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],24,0,0xE3,0);//��ʾ��ʱ��λ����ʾ��Ϣ				
		//		delay_ms(800);//��ʱ�ȴ���ʾ
				if(res==OkbtnValue)
				{
			//		timer_bit=0;
			//		gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN); 

					TP_Ready_Init();
		//			display_rollingtime();		//��ʾ����ʱ��		2023-7-11
				}
			}	
		
			
			
			res=btn_check(Distance_Addbtn,&in_obj);   
			if(res&&((Distance_Addbtn->sta&(1<<7))==0)&&(Distance_Addbtn->sta&(1<<6)))//������,�а����������ɿ�,����+1�ɿ���
			{ 
				if(RelayBit==1) //֮ǰ�ǽ������� ��� 2024-11-24
				{
					RelayBit=0;  //�������־λ=0 2024-11-24
					RelayLaps=0;  //���ǽ������� 2024-11-24
					Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
					btn_draw(Relaybtn);		//����ť
				}
					
				Laps_No++;
				if(Laps_No>=Distance_Max) Laps_No=0;
				
				if(Pool50mOr25mbit==0)
				{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
					sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,��׼Ӿ��50m  2025-1-2
				}
				else
				{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
					sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);					//=1,�̳� 25m  		2025-1-2
				}		
				LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//��ʾ��������  2026-05-12 ����140
			
				if((LAll_Lap+RAll_Lap)==1)	//2024-11-27
				{
					StartPlace=0x01;			//=50M,�����ı�  2024-11-27					
					StartFinalPlace=StartFinalPlace|0x02;			//=50M,�����ı�  2024-11-27					
					Display_StartFinalPlace(StartFinalPlace);   //�ı䷢���  2024-6-10
				}
				else 		//2024-11-27
				{
					StartPlace=0x00;			//!=50M,����㲻��  2024-11-27					
					StartFinalPlace=StartFinalPlace&0xFD;			//>50M,����㲻��  2024-6-17		
					Display_StartFinalPlace(StartFinalPlace);   //�ı䷢���  2024-6-10
				}
				
									
				if((LAll_Lap+RAll_Lap)==1)
				{
					if((StartFinalPlace&0x03)==0x02)	//  50m ���� �ұ� ���յ㣺��� 2024-11-27
					{
						LAll_Lap=1;			//2024-11-27
						RAll_Lap=0;			//2024-11-27
					}
					if((StartFinalPlace&0x03)==0x03)	//  50m ���� �ұ� ���յ㣺��� 2024-11-27
					{
						LAll_Lap=0;			//2024-11-27
						RAll_Lap=1;			//2024-11-27
					}
				}
				
				Exchange_StartFinalPlace();    //���������  2024-11-27	

				for(i=0;i<10;i++)
				{
					laps[i][0]=LAll_Lap;
					laps[i][1]=RAll_Lap;					//2024-11-24
					LLaps_diaplay(i);
					RLaps_diaplay(i);					//2024-11-21
				}	
			}

			res=btn_check(Distance_Decbtn,&in_obj);   
			if(res&&((Distance_Decbtn->sta&(1<<7))==0)&&(Distance_Decbtn->sta&(1<<6)))//������,�а����������ɿ�,����+1�ɿ���
			{ 
				if(RelayBit==1) //֮ǰ�ǽ������� ��� 2024-11-24
				{
					RelayBit=0;  //�������־λ=0 2024-11-24
					RelayLaps=0;  //���ǽ������� 2024-11-24
					Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language]; 
					btn_draw(Relaybtn);		//����ť
				}
				
				if(Laps_No==0) Laps_No=Distance_Max-1;
				else Laps_No--;
				if(Pool50mOr25mbit==0)
				{
					All_Lap=laps_No_tbl[Laps_No];
					LAll_Lap=Llaps_No_tbl[Laps_No];			//2024-11-24
					RAll_Lap=Rlaps_No_tbl[Laps_No];			//2024-11-24
					sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,��׼Ӿ��50m  2025-1-2
				}
				else
				{
					All_Lap=laps25m_No_tbl[Laps_No];
					LAll_Lap=Llaps25m_No_tbl[Laps_No];			//2025-1-4
					RAll_Lap=Rlaps25m_No_tbl[Laps_No];			//2025-1-4
					sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);					//=1,�̳� 25m  		2025-1-2
				}		
				LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//��ʾ��������  2026-05-12 ����140
			
				if((LAll_Lap+RAll_Lap)==1)	//2024-11-27
				{
					StartPlace=0x01;			//=50M,�����ı�  2024-11-27					
					StartFinalPlace=StartFinalPlace|0x02;			//=50M,�����ı�  2024-11-27					
					Display_StartFinalPlace(StartFinalPlace);   //�ı䷢���  2024-6-10
				}
				else 		//2024-11-27
				{
					StartPlace=0x00;			//!=50M,����㲻��  2024-11-27					
					StartFinalPlace=StartFinalPlace&0xFD;			//>50M,����㲻��  2024-6-17		
					Display_StartFinalPlace(StartFinalPlace);   //�ı䷢���  2024-6-10
				}
				
									
				if((LAll_Lap+RAll_Lap)==1)
				{
					if((StartFinalPlace&0x03)==0x02)	//  50m ���� �ұ� ���յ㣺��� 2024-11-27
					{
						LAll_Lap=1;			//2024-11-27
						RAll_Lap=0;			//2024-11-27
					}
					if((StartFinalPlace&0x03)==0x03)	//  50m ���� �ұ� ���յ㣺��� 2024-11-27
					{
						LAll_Lap=0;			//2024-11-27
						RAll_Lap=1;			//2024-11-27
					}
				}
				
				
				Exchange_StartFinalPlace();    //���������  2024-11-27	
		
				for(i=0;i<10;i++)
				{
					laps[i][0]=LAll_Lap;
					laps[i][1]=RAll_Lap;					//2024-11-24
					LLaps_diaplay(i);
					RLaps_diaplay(i);					//2024-11-21
				}	
			}	
			
						
			if(Check_State_Bit==1)				//�ü�鴥�塢ä��������̨״̬  2023-8-15
			{
				Check_State_Bit=0;
	//			TouchPadSignalKey_Process();		
	//			SignalKey_Process();
	//			SignalKey_Process();
	//			SignalKey_Process();
	//			SignalKey_Process();
			}
			
			if(Procee_SwimDir_Bit) 							//������ʾ����͹���ʱ��  2023-7-11
			{
				if(Testing_bit==0)								//���ǲ���״̬�ڴ���  2024-12-22
				{		
					if(PoolSingleOrDoubleTPbit==0)	//Ӿ�ذ�װ������һ��=1; ����=0  2025-1-16
							Process_Display_SiwmDir();		//Ӿ�����߰�װ���壬������Ӿ����  2025-1-16 
					else Single_Process_Display_SiwmDir();		//Ӿ�ص��߰�װ���壬������Ӿ����  2025-1-16 
					Procee_SwimDir_Bit=0;
				}
			}

			//Ӿ����/�رմ�������
			for(i=0;i<10;i++)
			{
				res=btn_check(CloseLanebtn[i],&in_obj);
				if(res&&((CloseLanebtn[i]->sta&(1<<7))==0)&&(CloseLanebtn[i]->sta&(1<<6)))//������,�а����������ɿ�,�����ɿ���
				{
					if(CloseLaneState[i]==2) CloseLaneState[i]=3 ;					//�رյ���״̬=2���򿪣�=3���ر�
					else CloseLaneState[i]=2 ;
					//2026-05-12 ���º�����ˢ�°�ť��ɫ����=����ԭɫ���ر�=�䰵
					if(CloseLaneState[i]==3)
					{	//��ɫ�������Ա�ʾ�õ��ѹر�
						CloseLanebtn[i]->bkctbl[0]=0X3186;	//���߿�
						CloseLanebtn[i]->bkctbl[1]=0X2A0F;	//����һ��
						CloseLanebtn[i]->bkctbl[2]=0X2A0F;	//���ϰ�
						CloseLanebtn[i]->bkctbl[3]=0X10A2;	//���°�
						CloseLanebtn[i]->bcfucolor=GRAY;	//��ɫ����
					}
					else
					{	//ԭ��ɫ
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
			
			
				//ȡ�� ��Ҫ��ä���ɼ�  2024-10-15
/*
			//����Ƿ��ж�ä������ɼ��İ�������   2023-11-3
			for(i=0;i<10;i++)
			{
				res=btn_check(RMBLanebtn[i],&in_obj);  
				if(res&&((RMBLanebtn[i]->sta&(1<<7))==0)&&(RMBLanebtn[i]->sta&(1<<6)))//������,�а����������ɿ�,�����ɿ���
				{  
					//		display_time();		//��LCD ID��ӡ��lcd_Dis���顣
					//		LCD_ShowString(Final_timer_posx,Final_timer_posy+(i+1)*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  

					Display_Laps_Place_Direct(i,1);				//�ڹ涨ʱ���ڣ�ֻ��ä���ɼ����޴���ɼ�����ȡä���ɼ�������ʽ�ɼ�   2023-11-5

					Display_MB_Time(MB_Result[i][0],MB_Result[i][1],MB_Result[i][2],MB_Result[i][3]);		//��ʾMB�ɼ�  2023-11-7
					LCD_ShowString(Final_timer_posx,Final_timer_posy+(i+1)*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
					//���ʹ˵��� ä���ɼ�   Сʱ	��	��	1/1000��
	//				OnSendSWCommand_Data(Pushbutton1_Command+(0)+0x10,SW_Command1+1,i,MB_Result[i][1],MB_Result[i][2],MB_Result[i][3]/10,MB_Result[i][0]*16+MB_Result[i][3]%10,MB_Result[i][0],0);
					OnSendSWCommand_Data(Touchpad_Command+0x10,Pushbutton_Result,i,MB_Result[i][1],MB_Result[i][2],MB_Result[i][3]/10,MB_Result[i][0]*16+MB_Result[i][3]%10,MB_Result[i][0],0);
					Send_Bit=2+1;					//�÷���ä���ɼ����津��ɼ���־
				}
			}	
		*/	
			for(i=0;i<10;i++)
			{
			 if(CloseLaneState[i]==2)		//�رյ���״̬  =2���򿪣�=3���ر�
			 {													//�������˶�Ա����������Ч  2023-11-17
				res=btn_check(cmdLbtn[i],&in_obj);   
				if(((cmdLbtn[i]->sta&(1<<7))==0)&&(cmdLbtn[i]->sta&(1<<6))) {
					Lkey_state=1;//0;//������,�а����������ɿ�,����TP�ɿ���
					cmdLbtn[i]->sta&=~(1<<6); //  ??????//b6:0,û�а�������;1,�а�������
				}
			  if(res&&(cmdLbtn[i]->sta&(1<<6))&&(Lkey_state==1))//������,�а����������ɿ�,����TP�ɿ���
				{  
					if(((cmdLbtn[i]->sta&&TP_PRES_DOWN)==1))	
					{	
						if((TP_Open_Close_State[i][0]==1)||(MB_Open_Close_State[0][i]==1)
							||((TP_Open_Close_State[i][0]==3)&&(MB_Open_Close_State[0][i]==3)&&(Lane_Display_MSecond[i][1-0]>=Close_Time)))			//��ߴ���򿪻���ä���򿪣�������Ч
						{
							OnSendSWData(Touchpad_Command+0x10,TouchButton_Result,Lane_NoTbl[i]);			//���ʹ��������ɼ����津��ɼ�������
							Send_Bit=2+2;					//�÷��ʹ��������ɼ����津��ɼ���־

							display_time();		//��LCD ID��ӡ��lcd_Dis���顣
			
							Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
							Lane_Display_State[i][1-0]=0;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
							Lane_Display_MSecond[i][1-0]=0;								//��ʾʱ������
				
							LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  
							Lkey_state=0;
					
						 if(Testing_bit==0)						//2024-12-23
						 {
							display_swim_dir(dir_posx,i,0,1);	
		
							if(TP_Open_Close_State[i][0]==3)														//���廵 =3����;
							{
								Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);			//��ߴ��廵��ɫ��ʾ
							}
							else {																											//�����
								TP_Open_Close_State[i][0]=0;									//��ߴ���ر�
								Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);						//��ߴ���رգ��˶�Ա������޳ɼ�
							}
						
							Display_Laps_Place_Direct(i,0);
							
							if(MB_Open_Close_State[0][i]==3)														//��0�����ä���� =3����;
							{
								sprintf((char*)lcd_Dis,"L%d",(i));
								Display_MB_StateGroup(0,i,Bad_Color,lcd_Dis);		//���ä������ɫ��ʾ
							}
							else {																											//ä����
								if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;									//��0�����ä���ر�
								if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=0;									//��1�����ä���ر�
								if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=0;									//��2�����ä���ر�
								sprintf((char*)lcd_Dis,"L%d",(i));
								Display_MB_StateGroup(0,i,Close_Color,lcd_Dis);		
							}
	
						}
					}
				 }
			  }
				res=btn_check(cmdRbtn[i],&in_obj);   
				if(((cmdRbtn[i]->sta&(1<<7))==0)&&(cmdRbtn[i]->sta&(1<<6))) {
						Rkey_state=1;	//������,�а����������ɿ�,����TP�ɿ���
						cmdRbtn[i]->sta&=~(1<<6); //  ??????//b6:0,û�а�������;1,�а�������
				}
			
			  if(res&&(cmdRbtn[i]->sta&(1<<6))&&(Rkey_state==1))//������,�а����������ɿ�,����TP�ɿ���
				{  
					if(((cmdRbtn[i]->sta&&TP_PRES_DOWN)==1))	
					{	
						if((TP_Open_Close_State[i][1]==1)||(MB_Open_Close_State[0][i+10]==1)
							||((TP_Open_Close_State[i][1]==3)&&(MB_Open_Close_State[0][i+10]==3)&&(Lane_Display_MSecond[i][0]>=Close_Time)))				//�ұߴ���򿪻���ä���򿪣�������Ч
						{
							OnSendSWData(Touchpad_Command+0x10,TouchButton_Result,Lane_NoTbl[i+10]);			//���ʹ��������ɼ����津��ɼ�������
							Send_Bit=2+2;					//�÷��ʹ��������ɼ����津��ɼ���־

							display_time();		//��LCD ID��ӡ��lcd_Dis���顣
							Lane_Display_State[i][0]=0;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ0;
							Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
							Lane_Display_MSecond[i][0]=0;								//��ʾʱ������
							LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i+1)*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  
							Rkey_state=0;
													 
						 if(Testing_bit==0)					//2024-12-23
						 {
							display_swim_dir(dir_posx,i,1,1);	
		
							if(TP_Open_Close_State[i][1]==3)														//���廵 =3����;
							{
								Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);		//�ұߴ��廵��ɫ��ʾ
							}
							else {																											//�����
								TP_Open_Close_State[i][1]=0;															//�ұߴ���رգ��˶�Ա������Ч
								Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);	//�ұߴ������ɫ��ʾ
							}
					
							Display_Laps_Place_Direct(i,1);
							
							if(MB_Open_Close_State[0][i+10]==3)														//ä���� =3����;
							{
								sprintf((char*)lcd_Dis,"R%d",(i));
								Display_MB_StateGroup(1,i,Bad_Color,lcd_Dis);			//�ұ�ä������ɫ��ʾ
							}
							else {																											//ä����
								if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=0;								//��0���ұ�ä���ر�
								if(MB_Open_Close_State[1][i+10]!=3 && MB_Open_Close_State[1][i+10]!=4) MB_Open_Close_State[1][i+10]=0;								//��1���ұ�ä���ر�
								if(MB_Open_Close_State[2][i+10]!=3 && MB_Open_Close_State[2][i+10]!=4) MB_Open_Close_State[2][i+10]=0;								//��2���ұ�ä���ر�
								sprintf((char*)lcd_Dis,"R%d",(i));
								Display_MB_StateGroup(1,i,Close_Color,lcd_Dis);		
							}
						}
					 }
				 }
				}
			 }				
		}
		
		if(connstatus==0)//��������δ������ʱ��,�����л����봰��
		{
		/*
			if(smemo->top<in_obj.y&&in_obj.y<(smemo->top+smemo->height)&&(smemo->left<in_obj.x)&&in_obj.x<(smemo->left+smemo->width))//��smemo�ڲ� 
			{ 
				editflag=0;			//�༭����smemo
				edit_show_cursor(eip,0);	//�ر�edit�Ĺ��
				edit_show_cursor(eport,0);	//�ر�eport�Ĺ��
				eip->type=0X04;		//eip��겻��˸ 
				eport->type=0X04;	//eport��겻��˸ 
				smemo->type=0X01;	//memo�ɱ༭,��˸���  
			}
		*/
/*			
			if(eip->top<in_obj.y&&in_obj.y<(eip->top+eip->height)&&(eip->left<in_obj.x)&&in_obj.x<(eip->left+eip->width))//��eip���ڲ� 
			{
				if(protocol==0)continue;//tcp serverЭ���ʱ��,����Ҫ����IP��ַ
				editflag=1;			//�༭����eip
		//		memo_show_cursor(smemo,0);	//�ر�smemo�Ĺ��
				edit_show_cursor(eport,0);	//�ر�eport�Ĺ��
				eip->type=0X06;		//eip�����˸ 
				eport->type=0X04;	//eport��겻��˸ 
		//		smemo->type=0X00;	//smemo���ɱ༭,��겻��˸
			}
			if(eport->top<in_obj.y&&in_obj.y<(eport->top+eport->height)&&(eport->left<in_obj.x)&&in_obj.x<(eport->left+eport->width))//��eport���ڲ� 
			{
				editflag=2;			//�༭����eport
		//		memo_show_cursor(smemo,0);	//�ر�smemo�Ĺ��
				edit_show_cursor(eip,0);	//�ر�eip�Ĺ��
				eport->type=0X06;	//eport�����˸ 
				eip->type=0X04;		//eip��겻��˸ 
		//		smemo->type=0X00;	//smemo���ɱ༭,��겻��˸
			}
			*/
		}
	//	edit_check(eip,&in_obj);
	//	edit_check(eport,&in_obj);
	//	t9_check(t9,&in_obj);		   
	//	memo_check(smemo,&in_obj);
//		memo_check(rmemo,&in_obj);//���rmemo
	/*
		if(t9->outstr[0]!=NULL)//�����ַ�
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
			t9->outstr[0]=NULL;//�������ַ� 
		}
	*/	
	/*  2024-10-26 ȡ�������ƽ����е�Э��ѡ����
		res=btn_check(protbtn,&in_obj);   
		if(res&&((protbtn->sta&(1<<7))==0)&&(protbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
		{  
			//��ѡ��ģʽ    
			tempx=protocol;
			app_items_sel((lcddev.width-180)/2,(lcddev.height-192)/2,180,72+40*3,(u8**)netplay_mode_tbl,3,(u8*)&tempx,0XD0,(u8*)netplay_btncaption_tbl[0][gui_phy.language]);//3��ѡ��
		if(tempx!=protocol)
			{
				protocol=tempx;  
				if(protocol!=0)
				{
					lwipdev.ip[3]=108;					//2023-5-22

					sprintf((char*)ptemp,"%d.%d.%d.%d",lwipdev.ip[0],lwipdev.ip[1],lwipdev.ip[2],lwipdev.ip[3]);
		//			strcpy((char*)eip->text,(const char *)ptemp);	//�ָ�Ĭ��IP��ַ 
					ipcaption=netplay_ipcaption_tb[0][gui_phy.language];//TCP Client/UDPģʽ,��ʾĿ��IP
				}
				else 
				{ 
					lwipdev.ip[3]=100;					//2023-5-22

					sprintf((char*)ptemp,"%d.%d.%d.%d",lwipdev.ip[0],lwipdev.ip[1],lwipdev.ip[2],lwipdev.ip[3]);
		//			strcpy((char*)eip->text,(const char *)ptemp);	//�ָ�Ĭ��IP��ַ 
					ipcaption=netplay_ipcaption_tb[1][gui_phy.language];//Ĭ����TCP Server/UDPģʽ,��ʾ����IP  
				}
		//		tempx=(lcddev.width-35*ip_fsize/2)/3-50;
				gui_fill_rectangle(IP_Sx,(ip_height-ip_fsize)/2,ip_fsize*strlen((char*)ipcaption)/2,ip_fsize,NET_IP_BACK_COLOR);//���ԭ������ʾ
				gui_show_string(ipcaption,IP_Sx,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//����IP/Ŀ��IP
		//		net_edit_colorset(eip,eport,protocol,connstatus);//�ػ�edit�� 
				net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<2);//����prot��Ϣ 
			}
		} 
*/
//   2024-10-26
/*		res=btn_check(connbtn,&in_obj);   
		if(res&&((connbtn->sta&(1<<7))==0)&&(connbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
		{   
			connstatus=!connstatus;
			tcpconn=0;				//���TCP����δ����
			if(connstatus==1)//��������
			{
				bkcolor=gui_memex_malloc(200*80*2);//�����ڴ�
				if(bkcolor==NULL)//��ȡ����ɫʧ����,ֱ�Ӽ�������,��ִ�к�������
				{
					connstatus=0;
					printf("netplay ex outof memory\r\n");
					continue;
				}
				app_read_bkcolor((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,bkcolor);//��ȡ����ɫ
				window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,(u8*)netplay_connmsg_tbl[0][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//��ʾ��������	
		//	2024-10-26			
				tipaddr.addr=net_get_ip(eip->text);
						if(tipaddr.addr!=0)
						{
							netconncom=netconn_new(NETCONN_UDP);  	//����һ��UDP����
							netconncom->recv_timeout=10;  			//���ճ�ʱ����
							tport=net_get_port(eport->text); 
							err=netconn_bind(netconncom,IP_ADDR_ANY,tport);	//��UDP_PORT�˿�
  							if(err==ERR_OK)err=netconn_connect(netconncom,&tipaddr,tport);//���ӵ�Զ�������˿�
							if(err!=ERR_OK)//����ʧ�� 
							{ 
								connstatus=0;//����ʧ��
								net_disconnect(netconncom,NULL);//�ر�����
							} 
						}
	//

				switch(protocol)
				{
					case 0://TCP ServerЭ�� 
			//			tport=net_get_port(eport->text);		//�õ�port��
						netconnnew=netconn_new(NETCONN_TCP);  	//����һ��TCP����
						netconnnew->recv_timeout=10;  			//��ֹ�����߳�
						err=netconn_bind(netconnnew,IP_ADDR_ANY,tport);//�󶨶˿�
						if(err==ERR_OK)err=netconn_listen(netconnnew);  //�������ģʽ
						else
						{
							connstatus=0;//����ʧ��
							net_disconnect(netconnnew,NULL);//�ر����� 
						}
						break;
					case 1://TCP ClientЭ�� 
			//			tipaddr.addr=net_get_ip(eip->text);
						if(tipaddr.addr!=0)
						{
							netconncom=netconn_new(NETCONN_TCP); //����һ��TCP����
							netconncom->recv_timeout=10;
					//		tport=net_get_port(eport->text); 
 							err=netconn_connect(netconncom,&tipaddr,tport);//���ӷ����� 
							if(err==ERR_OK)tcpconn=1;//���ӳɹ� 
							else
							{
								connstatus=0;//����ʧ��
								net_disconnect(netconncom,NULL);//�ر�����
							}
						} 
						break;
					case 2://UDPЭ��  
			//			tipaddr.addr=net_get_ip(eip->text);
						if(tipaddr.addr!=0)
						{
							netconncom=netconn_new(NETCONN_UDP);  	//����һ��UDP����
							netconncom->recv_timeout=10;  			//���ճ�ʱ����
			//				tport=net_get_port(eport->text); 
							err=netconn_bind(netconncom,IP_ADDR_ANY,tport);	//��UDP_PORT�˿�
  							if(err==ERR_OK)err=netconn_connect(netconncom,&tipaddr,tport);//���ӵ�Զ�������˿�
							if(err!=ERR_OK)//����ʧ�� 
							{ 
								connstatus=0;//����ʧ��
								net_disconnect(netconncom,NULL);//�ر�����
							} 
						}
						break;
				}
				
				if(err==ERR_OK)window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,(u8*)netplay_connmsg_tbl[2][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//��ʾ���ӳɹ�
				else window_msg_box((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,(u8*)netplay_connmsg_tbl[1][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);//��ʾ����ʧ��
				delay_ms(800);//��ʱ�ȴ���ʾ
				app_recover_bkcolor((lcddev.width-200)/2,(lcddev.height-80)/2,200,80,bkcolor);//�ָ�����ɫ
				gui_memex_free(bkcolor);//�ͷ��ڴ�
			}				
 		} 
	*/	
//	 2024-10-17 ȡ����  ������� ������
	/*
		res=btn_check(clrbtn,&in_obj);   
		if(res&&((clrbtn->sta&(1<<7))==0)&&(clrbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
		{   
			rxcnt=0;//������������
			txcnt=0;//������������
//			rmemo->text[0]=0;//���rmemo,��ͷ��ʼ
//			memo_draw_memo(rmemo,1);//�ػ�rmemo 		 
			net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,0X07);//����������Ϣ 
		} 
	*/
		
		//��ʾ����ʱ��  2023-7-27
		if(Display_RollingTime_Bit==1)	
		{
				display_rollingtime();
				Display_RollingTime_Bit=0;
		}

		//���������ä��֮���ϵ����  2023-11-5
		if(TP_MB_Bit==1)	
		{
				Process_TP_MB();
				TP_MB_Bit=0;
		}
	
		//��������̨�ӳ�ʱ�����  2024-11-25
		if(StartBox_Bit==1)
		{
				Process_StartBox_DelayClose();   //��������̨�ӳ�ʱ�� 2025-11-25
				Process_StartboxStateChange();   //2026-05-30 SB ״̬�仯ɨ��+�ϱ�
				Process_TPStateChange();         //2026-05-30 TP ״̬�仯ɨ��+�ϱ�
				Process_MBStateChange();         //2026-05-30 MB ״̬�仯ɨ��+�ϱ�
				StartBox_Bit=0;
		}

		//2026-05-11 �����"����ʱ��"������ʱ�̣����㲢�㲥/��ʾÿ���˶�Ա��Է���ĳ���ʱ��
		if(GunFired_PostOpenDoneBit==1)
		{
			Process_StartBox_LaneTime();
		}
	
		//���������ӳ�ʱ�����  2024-12-12
		if(TP_Bit==1)	
		{
				Process_TP_DelayClose();   //���������ӳ�ʱ�� 2025-12-12
				TP_Bit=0;
		}
		
		//���͹���ʱ��  2023-7-27
		if((Send_Bit!=0))//������,������OK
		{
			Send_Bit=0;
			/*
			if(Send_RollingTime_Bit==1)	
			{
				if((Send_Bit==0))
				{
		//			OnSendSWData(0x7f,SW_Command1,Control_Port_Num);  //���͹���ʱ��
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

		if(connstatus==1)//������״̬ʱ��������  2024-10-15
		{	
			if(tcpconn==1&&protocol!=2)//TCP Client/TCP Server��������
			{ 
					err=netconn_write(netconncom ,Send_buf,SendLength,NETCONN_COPY);//����smemo->text�е����� 
					if(err==ERR_OK)//���ͳɹ�
					{
				//		txcnt+=Rec_send_num*TxRx_Data_Length;//�ܷ��ͳ������� 	 
				//				txcnt+=SendLength;//�ܷ��ͳ������� 	 
				//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//����TX��Ϣ  2024-10-27
					}
			}else
				
		//		if(connstatus==1)
				{
					netbuf_alloc(sendcmdbuf,SendLength);
					sendcmdbuf->p->payload=Send_buf;//�������ݵ�sendbuf����

					err=netconn_send(netconncom,sendcmdbuf);//��netbuf�е����ݷ��ͳ�ȥ
		//			if(err!=ERR_OK)printf("netconn_send fail\r\n"); 
		//			err=netconn_send(netconncom,sendbuf);//��netbuf�е����ݷ��ͳ�ȥ
				if(err==ERR_OK)		//2023-7-26
				{
				//		txcnt+=strlen((char*)sendbuf);//�ܷ��ͳ������� 	
				//		txcnt+=SendLength;//�ܷ��ͳ������� 	 
				//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//����TX��Ϣ  2024-10-27
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
				RS_TX_len=RS_TX_len+SendLength;		//�������ݳ���
				SendLength=0;	
			}
			*/
	//		if((RS_TX_Bit==0)&&(RS_TX_len>0))		 //������Ҫ����
//			if((RS_TX_Bit==0)&&(RS_TX_len>=TxRx_Data_Length))		 //������Ҫ����
	/*
			if((RS_TX_Bit==0)&&(RS_TX_No>0))		 //������Ҫ����  2023-10-26
			{
				{
		//			RS_TX_No=0;
		//			UART4->TDR=UART4_TX_BUF[RS_TX_No]; //��������
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
					UART4->CR1|=1<<30;	 	//λ 30 TXFEIE:TXFIFO Ϊ��ʱ�ж�ʹ�� (TXFIFO empty interrupt enable)
					
				}		
			}
			*/
		u4_SendData(Send_buf,SendLength);	
							

		}


		/*
		res=btn_check(sendbtn,&in_obj);   
		if(res&&((sendbtn->sta&(1<<7))==0)&&(sendbtn->sta&(1<<6)))//������,�а����������ɿ�,����TP�ɿ���
		{  
			memo_add_text(smemo,"hong65");
			tempx=strlen((char*)smemo->text);//���������ݲŷ���
			if(connstatus==1&&tempx)//������,������OK
			{
				if(tcpconn==1&&protocol!=2)//TCP Client/TCP Server��������
				{ 
					err=netconn_write(netconncom ,smemo->text,tempx,NETCONN_COPY);//����smemo->text�е����� 
					if(err==ERR_OK)//���ͳɹ�
					{
						txcnt+=strlen((char*)smemo->text);//�ܷ��ͳ������� 	 
						net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//����TX��Ϣ 
					}
				}else
				{
	//		notice_len++;
	//		sprintf((char*)lcd_Dis,"S%d",notice_len);	 		
	//		LCD_ShowString(1050,500,300,32,24,lcd_Dis);		//��ʾLCD ID	  
					sendbuf=netbuf_new();
					netbuf_alloc(sendbuf,strlen((char *)smemo->text));
					strcpy(sendbuf->p->payload,(void*)smemo->text);//�������ݵ�sendbuf����
					err=netconn_send(netconncom,sendbuf);//��netbuf�е����ݷ��ͳ�ȥ
					if(err!=ERR_OK)printf("netconn_send fail\r\n"); 
					else 
					{
						txcnt+=strlen((char*)smemo->text);//�ܷ��ͳ������� 	
						net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<0);//����TX��Ϣ 
					}
					netbuf_delete(sendbuf);  //ɾ��buf									
				}	
			}
		} 
			*/
		
		if(connstatus==1)//����״̬
		{
			if(tcpconn==0&&protocol==0)//TCP Serverģʽ��,���ӻ�δ����,���TCP����
			{
				err=netconn_accept(netconnnew,&netconncom);//������������
				if(err==ERR_OK)//�ɹ���⵽����
				{ 
					netconncom->recv_timeout=10; 
   					tcpconn=1;
				}
			}else
			{			
				//�������հ�
				err=netconn_recv(netconncom,&recvbuf);//�鿴�Ƿ���յ�����
				if(err==ERR_OK)  //���յ�����
				{		 
	//				notice_len++;
	//		sprintf((char*)lcd_Dis,"R%d",notice_len);	 		
//			LCD_ShowString(1050,500,300,32,24,lcd_Dis);		//��ʾLCD ID	  
					
					netconn_getaddr(netconncom,&Remote_tipaddr,&Remote_tport,0); //��ȡԶ��IP��ַ�Ͷ˿ں�  2024-11-1
					if(Remote_tipaddr.addr!=oldaddr||Remote_tport!=oldport)//�µ�ַ/�˿ں�  2024-11-1
					{
						oldaddr=Remote_tipaddr.addr;  //2024-11-1
						oldport=Remote_tport;					  //2024-11-1
						sprintf((char*)ptemp,"[From:%d.%d.%d.%d:%d]:\r\n",oldaddr&0XFF,(oldaddr>>8)&0XFF,(oldaddr>>16)&0XFF,(oldaddr>>24)&0XFF,oldport); 
//						tempx=strlen((char*)rmemo->text)+strlen((char*)ptemp);//�õ��µ��ܳ���
//						if(tempx>=NET_RMEMO_MAXLEN)rmemo->text[0]=0;//���rmemo,��ͷ��ʼ
//						strcat(((char*)rmemo->text),(char*)ptemp);//�����յ�������	 
					}

					memcpy(buff,recvbuf->p->payload,recvbuf->p->tot_len);
//						tempx=strlen((char*)rmemo->text);//�õ��µ��ܳ���
		
					rxcnt+=recvbuf->p->tot_len;//strlen((char*)p);//�ܽ��ճ�������

					for(i=0;i<recvbuf->p->tot_len;i++)
					{
						TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=buff[i];			//2023-7-17
						TCPIP_Rec_Char_Ptr++;
						if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
						tempx=tempx+3;//�õ��µ��ܳ���
	//					if(tempx>=NET_RMEMO_MAXLEN)rmemo->text[0]=0;//���rmemo,��ͷ��ʼ
	//					sprintf((char*)tmp,_T("%02x,"), buff[i]);										// 
	//					strcat(((char*)rmemo->text),(char*)tmp);//�����յ�������	 
			
					}
					netbuf_delete(recvbuf); // 2023-8-15
							
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15

					
	//		memcpy(p,recvbuf->p->payload,recvbuf->p->tot_len);
	//		p[recvbuf->p->tot_len]=0;	//ĩβ���������  
	//				tempx=strlen((char*)rmemo->text)+strlen((char*)p);//�õ��µ��ܳ���
	//				if(tempx>NET_RMEMO_MAXLEN)rmemo->text[0]=0;//���rmemo,��ͷ��ʼ
	//				strcat(((char*)rmemo->text),(char*)p);//�����յ�������		
	//		rxcnt+=strlen((char*)p);//�ܽ��ճ�������
		//			rxcnt+=recvbuf->p->tot_len;//�ܽ��ճ�������
	//		memo_draw_memo(rmemo,1);//�ػ�rmemo 		 
	//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//����RX��Ϣ  2024-10-27
			//		netbuf_delete(recvbuf); // 2023-8-15
		}else if(err==ERR_CLSD)
			{
					if(protocol==0)tcpconn=0;//�������ӶϿ�״̬
					else connstatus=0;
					net_disconnect(netconncom,NULL);//�Ͽ�netconncom����  
				} 
			}				
		}
		if(oldconnstatus!=connstatus)//����״̬�ı���
		{		
			oldconnstatus=connstatus;
			if(connstatus==0)//���ӶϿ���?ǿ�ƶϿ�����?
			{
				net_disconnect(netconnnew,netconncom);//�Ͽ����� 
				netconncom=NULL;
				netconnnew=NULL; 
				if(protocol==0)net_tcpserver_remove_timewait();//TCP Server,ɾ���ȴ�״̬
	//			protbtn->sta=0;//Э��ѡ��ť���뼤��״̬
	//			connbtn->caption=netplay_btncaption_tbl[1][gui_phy.language];  			
				gui_fill_circle(cds0x,1+cr,cr,Close_Color);  //û�����ϣ���ɫ
			}else//���ӳɹ�
			{
	//			protbtn->sta=2;//Э��ѡ��ť����Ǽ���״̬
	//			connbtn->caption=netplay_btncaption_tbl[2][gui_phy.language]; 
	//			editflag=0;			//ֻ�����༭smemo
	//			edit_show_cursor(eip,0);	//�ر�edit�Ĺ��
	//			edit_show_cursor(eport,0);	//�ر�eport�Ĺ��
	//			eip->type=0X04;		//eip��겻��˸ 
	//			eport->type=0X04;	//eport��겻��˸ 
		//		smemo->type=0X01;	//memo�ɱ༭,��˸���  
				gui_fill_circle(cds0x,1+cr,cr,Valid_Color); //������ ����ɫ 
			}
	//		btn_draw(protbtn);//�ػ���ť
	//		btn_draw(connbtn);
		//	net_edit_colorset(eip,eport,protocol,connstatus);//�ػ�edit��
					
		}
					
/*
		if(USART_RX_STA&0x8000)			//����1���յ����ݷ�
		{					   
			USART1_RX_len=USART_RX_STA&0x3fff;//�õ��˴ν��յ������ݳ���
			for(i=0;i<USART1_RX_len;i++)
			{
				TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=USART_RX_BUF[i];			//2023-7-17
				TCPIP_Rec_Char_Ptr++;
				if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
				sprintf((char*)tmp,_T("%02x,"), USART_RX_BUF[i]);										
				strcat(((char*)rmemo->text),(char*)tmp);//�����յ�������	 
			}
			USART_RX_STA=0;
		
			rxcnt+=USART1_RX_len;//�ܽ��ճ�������
			memo_draw_memo(rmemo,1);//�ػ�rmemo 		2023-8-11		 
			net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//����RX��Ϣ 
		
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15
			
		}
		*/
/*	
		if(USART2_RX_STA&0x8000)			//����1���յ����ݷ�
		{					   
			USART1_RX_len=USART2_RX_STA&0x3fff;//�õ��˴ν��յ������ݳ���
			for(i=0;i<USART1_RX_len;i++)
			{
				TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=USART2_RX_BUF[i];			//2023-7-17
				TCPIP_Rec_Char_Ptr++;
				if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
				sprintf((char*)tmp,_T("%02x,"), USART2_RX_BUF[i]);									
				strcat(((char*)rmemo->text),(char*)tmp);//�����յ�������	 
			}
			USART2_RX_STA=0;
		
			rxcnt+=USART1_RX_len;//�ܽ��ճ�������
			memo_draw_memo(rmemo,1);//�ػ�rmemo 		2023-8-11		 
			net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//����RX��Ϣ 
		
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15
			
		}
*/

		if(UART4_RX_STA&0x8000)			//����4���յ����ݷ�
		{					   
			UART4->CR1&=~(1<<0);  			//����ʹ��
			USART4_RX_len=UART4_RX_PTR;		//UART4_RX_STA&0x3fff;//�õ��˴ν��յ������ݳ���
			for(u8 i=0;i<USART4_RX_len;i++)
			{
				if(TCPIP_Rec_Char_Ptr>=RX_DATA_MaxLEN) TCPIP_Rec_Char_Ptr=0;
				TCPIP_CommandBuf[TCPIP_Rec_Char_Ptr]=UART4_RX_BUF[i];			//2023-7-17
				TCPIP_Rec_Char_Ptr++;
		//		sprintf((char*)tmp,_T("%02x,"), UART4_RX_BUF[i]);										/* ?????????			*/
		//		strcat(((char*)rmemo->text),(char*)tmp);//�����յ�������	 
			}
			UART4_RX_PTR=0;
			UART4_RX_STA=0;
			UART4->CR1|=1<<0;  			//����ʹ��
		
			rxcnt+=USART4_RX_len;//�ܽ��ճ�������
	//		memo_draw_memo(rmemo,1);//�ػ�rmemo 		2023-8-11		 
	//		net_msg_show(ip_height,msg_height,fsize,txcnt,rxcnt,protocol,1<<1);//����RX��Ϣ  2024-10-27
		
			if(TCPIP_Rec_Char_Ptr>=TxRx_Data_Length)
					OnTCP_RS232_Receive_Data_Proc();	//2023-8-15
			
		}
		system_task_return=0;
//		if(system_task_return)break;		//TPAD����  
//		delay_ms(10);

	}
	ledplay_ds0_sta=0;
	Timer_State_LED(1);
	LED1(1);		//�ر�LED
	btn_delete(Startbtn);	//ɾ����ʼ��ʱ��ť
	btn_delete(Resetbtn);	//ɾ����λ��ť 
	btn_delete(Readybtn);	//ɾ��׼��������ť 
	btn_delete(Relaybtn);	//ɾ��������ť 		//2024-11-24
	btn_delete(Testbtn);	//ɾ�����԰�ť
	btn_delete(Distance_Addbtn);		//ɾ��+1��ť 
	btn_delete(Distance_Decbtn);		//ɾ��-1��ť 
	btn_delete(Setupbtn);						//ɾ���������ð�ť 
	btn_delete(SendStartTimerbtn);	//ɾ������ʱ�̰�ť 

	if(connstatus)//����״̬�˳�?�Ͽ�����!
	{
		net_disconnect(netconnnew,netconncom);//�Ͽ�����  
		if(protocol==0)net_tcpserver_remove_timewait();//TCP Server,ɾ���ȴ�״̬
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
	netbuf_delete(sendcmdbuf);  //ɾ��buf									
	
	system_task_return=0;	
		
		
	}
	/*
	else//��ʾ������ʼ��ʧ��!
	{
		window_msg_box((lcddev.width-220)/2,(lcddev.height-100)/2,220,100,(u8*)netplay_remindmsg_tbl[1][gui_phy.language],(u8*)APP_REMIND_CAPTION_TBL[gui_phy.language],12,0,0,0);
 		delay_ms(2000);
	} 
	*/
	system_task_return=0;
	lwip_comm_destroy(); 
	PCF8574_WriteBit(ETH_RESET_IO,1);//���ָ�λLAN8720,���͹���
}

void StartTiming(void)
{
	u8 i;

	//2024-8-31
	Start_hour=hour;					//����ʱ���Ӧʱ������ Сʱ
	Start_minute=minute;			//����ʱ���Ӧʱ������ 	��
	Start_second=second;			//����ʱ���Ӧʱ������ 	��
	Start_msecond=msecond;		//����ʱ���Ӧʱ������ 	1/1000��

	//2026-05-11 ����"����ǹ��ʱ��"��������ʱ���еĶ�����Gun_*����
	//             ÿ���˶�Աʵ�ʳ���ʱ�� = LaneStart_* - Gun_*���ɸ�������������
	Gun_minute  = PreStart_minute;
	Gun_second  = PreStart_second;
	Gun_msecond = PreStart_msecond;

	//2026-05-11 ���ó���̨���Ŵ��ڵļ�ʱ����һ���Դ���λ
	PostGun_OpenWait_Time   = 0;
	GunFired_PostOpenDoneBit= 0;

	//2024-9-1
	OnSendSWData(Start_Command+0x10,0,0);			//���Ϳ�ʼ��ʱ����	2023-7-18
	Send_Bit=2;														//�÷��ͱ�־

	if(timer_bit!=1)
	{
	//	if(laps_No_tbl[Laps_No]!=1)   //2024-6-18
		{
					for(i=0;i<10;i++)
					{
						if(CloseLaneState[i]==2)		//�رյ���״̬=2���򿪣�=3���ر�
						{
							Lane_Display_State[i][Start_Dir] = 1;					//2026-06-09 ��1���: ģ���������Ѵ����� Process_Display_SiwmDir ������ʱ, ���㿪��1�յ�� (= ��2 SB+TP+MB), ͬ PC L6464
							Lane_Display_State[i][1-Start_Dir] = 0;
							Lane_Display_MSecond[i][Start_Dir] = 0;					//����ʱ����
							display_swim_dir(dir_posx,i,Start_Dir,1);			//�ı䷢��㣬�˶�Ա��Ӿ������֮�仯 2024-6-9
						
				//			laps[i][0]=LAll_Lap;
				//			laps[i][1]=RAll_Lap;					//2024-12-1
						}
					}
		}
		  //2024-6-18
/*		else {
					for(i=0;i<10;i++)
					{
						if(CloseLaneState[i]==2)		//�رյ���״̬=2���򿪣�=3���ر�
						{
							Lane_Display_State[i][0]=1-Start_Dir;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1-Start_Dir;
							Lane_Display_State[i][1]=Start_Dir;																				//�˵���ʾ�ɼ�����ʾ״̬ΪStart_Dir;
							Lane_Display_MSecond[i][0]=0;								//��ʾʱ������
							display_swim_dir(dir_posx,i,1-Start_Dir,1);			//�ı䷢��㣬�˶�Ա��Ӿ������֮�仯 2024-6-9	
							laps[i][0]=All_Lap;
						}
					}
		}
		*/
		for(i=0;i<10;i++)		//2024-11-24
		{
			if((Startbox_Open_Close_State[i][Start_Dir]==1))									//��ߵ�i������̨�ǳ��δ� 2024-11-24���ų���̨��Ч
			{
				Relay_SB_DelayClose_Time[i]=0;
				Startbox_Open_Close_State[i][Start_Dir]=2;																			//����̨���ӳ�
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
									
	Timer_Reset((1-timer_bit));			//��ʱ����ʼ��ʱ    2024-1-25
			
}



void display_swim_dir(u16 posx,u8 Lane,u8 xy,u8 xy_length)
{
	// 2026-06-03 ֱͨģʽ����ʾ�����ͷ (xy=0/1)
	if (HardwareAlwaysOpenBit && (xy==0 || xy==1)) return;
	if(xy==0)  //left->right
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"          ");//����->��		 	
		if(xy_length==1) 		sprintf((char*)Dir_Dis,">         ");//����->��		 	
		if(xy_length==2) 		sprintf((char*)Dir_Dis,">>        ");//����->��		 	
		if(xy_length==3) 		sprintf((char*)Dir_Dis,">>>       ");//����->��		 	
		if(xy_length==4) 		sprintf((char*)Dir_Dis,">>>>      ");//����->��		 	
		if(xy_length==5) 		sprintf((char*)Dir_Dis,">>>>>     ");//����->��		 	
		if(xy_length==6) 		sprintf((char*)Dir_Dis,">>>>>>    ");//����->��		 	
		if(xy_length==7) 		sprintf((char*)Dir_Dis,">>>>>>>   ");//����->��		 	
		if(xy_length==8) 		sprintf((char*)Dir_Dis,">>>>>>>>  ");//����->��		 	
		if(xy_length==9) 		sprintf((char*)Dir_Dis,">>>>>>>>> ");//����->��		 	
		if(xy_length>9) 		sprintf((char*)Dir_Dis,">>>>>>>>>>");//����->��		 	
	}
	if(xy==1)  //left<-right
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"          ");//����->��		 	
		if(xy_length==1) 		sprintf((char*)Dir_Dis,"         <");//����->��		 	
		if(xy_length==2) 		sprintf((char*)Dir_Dis,"        <<");//����->��		 	
		if(xy_length==3) 		sprintf((char*)Dir_Dis,"       <<<");//����->��		 	 	
		if(xy_length==4) 		sprintf((char*)Dir_Dis,"      <<<<");//����->��		 			 	
		if(xy_length==5) 		sprintf((char*)Dir_Dis,"     <<<<<");//����->��		 	
		if(xy_length==6) 		sprintf((char*)Dir_Dis,"    <<<<<<");//����->��		 	
		if(xy_length==7) 		sprintf((char*)Dir_Dis,"   <<<<<<<");//����->��		 	 	
		if(xy_length==8) 		sprintf((char*)Dir_Dis,"  <<<<<<<<");//����->��		 			 	
		if(xy_length==9) 		sprintf((char*)Dir_Dis," <<<<<<<<<");//����->��		 	 	
		if(xy_length>9) 		sprintf((char*)Dir_Dis,"<<<<<<<<<<");//����->��		 			 	
	}
	if(xy==2)  //open
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"   ��   ");
	}		
	if(xy==3)  //Close
	{
		if(xy_length==0) 		sprintf((char*)Dir_Dis,"   �ر�   ");
	}		
//	LCD_ShowString(posx,dir_posy+(Lane+1)*line_height1,200,btnh,32,Dir_Dis);		//��ʾLCD ID	      					 
		
	CloseLanebtn[Lane]->caption=Dir_Dis;	//Hcmd_Lbtncaption_tbl[i];
	btn_draw(CloseLanebtn[Lane]);		//����/�رյ��ΰ�ť

}


void Display_Startbox_State(u16 posx,u16 posy,u8 width,u8 height,u16 color)
{
		if(g_in_net_test) return;	//2026-05-16 net_test �ӽ����ڼ��������ػ�ͼ
		gui_fill_rectangle(posx,posy,width,height,color);//����̨��ʾ
}

void Display_TP_State(u16 posx,u16 posy,u8 width,u8 height,u16 color)
{
		if(g_in_net_test) return;	//2026-05-16 net_test �ӽ����ڼ��������ػ�ͼ
		gui_fill_rectangle(posx,posy,width,height,color);//������ʾ
}


#define MB_CR_Sub      6
#define MB_Y_Offset    18

void Display_MB_State(u16 posx,u16 posy,u16 MB_CR,u8 fsize,u16 color,u8 Dis[20])
{
	if(g_in_net_test) return;	//2026-05-16 net_test �ӽ����ڼ��������ػ�ͼ
	gui_fill_circle(posx,posy,MB_CR,color);		//2023-11-17
//	gui_show_strmid(x-r,y-fsize/2,2*r,fsize,BLUE,fsize,str);//��ʾ����  
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
//GPIO����ר�ú궨��
#define GPIO_MODE_IN    	0		//��ͨ����ģʽ
#define GPIO_MODE_OUT		1		//��ͨ���ģʽ
#define GPIO_MODE_AF		2		//AF����ģʽ
#define GPIO_MODE_AIN		3		//ģ������ģʽ

#define GPIO_SPEED_2M		0		//GPIO�ٶ�2Mhz
#define GPIO_SPEED_HIGH		1		//GPIO�ٶ�25Mhz
#define GPIO_SPEED_50M		2		//GPIO�ٶ�50Mhz
#define GPIO_SPEED_100M		3		//GPIO�ٶ�100Mhz
#define GPIO_PUPD_NONE		0		//����������
#define GPIO_PUPD_PU		1		//����
#define GPIO_PUPD_PD		2		//����
#define GPIO_PUPD_RES		3		//���� 

#define GPIO_OTYPE_PP		0		//�������
#define GPIO_OTYPE_OD		1		//��©��� 
*/

//������ʼ������

void Init_Key_Pin(void)
{
	RCC->AHB1ENR|=1<<0;     //ʹ��PORTAʱ�� 
	RCC->AHB1ENR|=1<<1;     //ʹ��PORTBʱ�� 
	RCC->AHB1ENR|=1<<2;     //ʹ��PORTCʱ�� 
	RCC->AHB1ENR|=1<<3;     //ʹ��PORTDʱ��
	RCC->AHB1ENR|=1<<4;     //ʹ��PORTEʱ��
	RCC->AHB1ENR|=1<<6;     //ʹ��PORTGʱ�� 
	RCC->AHB1ENR|=1<<7;     //ʹ��PORTHʱ�� 
	GPIO_Set(GPIOA,PIN4|PIN6,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																				//PA PIN4,6������������,��Ϊ���̵��������ź�
//	GPIO_Set(GPIOB,PIN6|PIN7|PIN8|PIN9|PIN12|PIN13|PIN14|PIN15,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PB PIN6,7,8,9,1,13,14,15������������,��Ϊ���̵��������ź�
	GPIO_Set(GPIOB,PIN7|PIN8|PIN9|PIN12|PIN13|PIN14|PIN15,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PB PIN6,7,8,9,1,13,14,15������������,��Ϊ���̵��������ź�
	GPIO_Set(GPIOC,PIN6|PIN7|PIN8|PIN9|PIN10|PIN11|PIN13,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);				//PC PIN6,7,8,9,10,11,13������������,��Ϊ���̵��������ź�
	GPIO_Set(GPIOD,PIN2|PIN3,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																		//PD PIN2,3������������,��Ϊ���̵��������ź�
	GPIO_Set(GPIOG,PIN10|PIN12,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																	//PG PIN10,12������������,��Ϊ���̵��������ź�
	GPIO_Set(GPIOH,PIN8,GPIO_MODE_IN,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);																				//PH PIN8������������,��Ϊ���̵��������ź�

	GPIO_Set(GPIOE,PIN2|PIN3|PIN4|PIN5|PIN6,GPIO_MODE_OUT,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PE2 PIN3 PIN4 PIN5 PIN6�������	��Ϊ���̵���ɨ����ź�

	GPIO_Set(GPIOH,PIN3,GPIO_MODE_OUT,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PH PIN3�������	��Ϊ ��ʱ��λ�źŵ����  2024-1-25
	
//	GPIO_Set(GPIOC,PIN12,GPIO_MODE_AF,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PC12�������	��Ϊ����������������ź�
//	GPIO_Set(GPIOC,PIN12,GPIO_MODE_OUT,GPIO_OTYPE_PP,GPIO_SPEED_HIGH,GPIO_PUPD_PU);	//PC12�������	��Ϊ����������������ź�

			Line0(1);
			Line1(1);
			Line2(1);
			Line3(1);
			Line4(1);
}

//������������
//���ذ���ֵ
//mode:0,��֧��������;1,֧��������;
//0��û���κΰ�������
//1��KEY0����
//2��KEY1����
//3��KEY2���� 
//4��KEY_UP���� ��WK_UP
//ע��˺�������Ӧ���ȼ�,KEY0>KEY1>KEY2>KEY_UP!!

void Display_Button_State(u16 line)
{
	/*
	u8 lcd_Dis1[20];				//���LCD ID�ַ���
	sprintf((char*)lcd_Dis1,"%d %d %d %d %d %d %d %d %d %d",KeyState[0],KeyState[1],KeyState[2],KeyState[3],KeyState[4],KeyState[5],KeyState[6],KeyState[7],KeyState[8],KeyState[9]);	 		
	LCD_ShowString(1050,100+32*(line+1),300,32,24,lcd_Dis1);		//��ʾLCD ID	  
*/
}


void Key_Process(u8 mode)
{
/*	if(key_up&&(KEY0==0||KEY1==0||KEY2==0||WK_UP==1))
	{
//		delay_ms(10);//ȥ���� 
		key_up=0;
		if(KEY0==0)return 1;
		else if(KEY1==0)return 2;
		else if(KEY2==0)return 3;
		else if(WK_UP==1)return 4;
	}else if(KEY0==1&&KEY1==1&&KEY2==1&&WK_UP==0)key_up=1; 	    
 //	return 0;// �ް�������
*/	
	scanline++;
	
	if(scanline>10) scanline=0;
	keyline=scanline;//+1;
	switch(scanline)
	{
		case 1:
			Line0(0);
				
			delay_ms(1);	//����
			Read_ColKey();		

			Line0(1);
	
			TouchPad_Process(0);

			Line0(1);

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
		
			break;

		case 2:
			Line1(0);
				
			delay_ms(1);	//����
			Read_ColKey();		

			Line1(1);
	
			ManualBut_Process(1,L_MB_State_Line[0],R_MB_State_Line[0]);

			Line1(1);

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
		
		break;
		
		case 3:
			Line2(0);
			delay_ms(1);	//����
			Read_ColKey();		
			Line2(1);
	
			ManualBut_Process(2,L_MB_State_Line[1],R_MB_State_Line[1]);

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
	
			break;

		case 4:
			Line3(0);
		
			delay_ms(1);	//����
			Read_ColKey();		
			Line3(1);
	

			ManualBut_Process(3,L_MB_State_Line[2],R_MB_State_Line[2]);

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
	
			break;

		case 5:
			Line4(0);
		
			delay_ms(1);	//����
			Read_ColKey();		
			Line4(1);
	
			StartBox_Process(4);

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28

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
	
			if(Testing_bit==0) 			StartBox_Process(scanline);		//=0����ʱ����
			else 	Test_StartBox_Process(scanline);  										//=1:���Դ���

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
		
		break;
		
		case 2:
			Line2(0);
			Read_ColKey();		
			Line2(1);
	
			if(Testing_bit==0) 			ManualBut_Process(scanline,L_MB_State_Line[0],R_MB_State_Line[0]);		//=0����ʱ����
			else 	Test_ManualBut_Process(scanline,L_MB_State_Line[0],R_MB_State_Line[0]);  										//=1:���Դ���

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
	
			break;

		case 3:
			Line3(0);
			Read_ColKey();		
			Line3(1);

			if(Testing_bit==0) 			ManualBut_Process(scanline,L_MB_State_Line[1],R_MB_State_Line[1]);		//=0����ʱ����
			else 	Test_ManualBut_Process(scanline,L_MB_State_Line[1],R_MB_State_Line[1]);  										//=1:���Դ���

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
	
			break;

		case 4:
			Line4(0);
			Read_ColKey();		
			Line4(1);
	
			if(Testing_bit==0) 			ManualBut_Process(scanline,L_MB_State_Line[2],R_MB_State_Line[2]);		//=0����ʱ����
			else 	Test_ManualBut_Process(scanline,L_MB_State_Line[2],R_MB_State_Line[2]);  										//=1:���Դ���

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28

		break;

		
		case 5:
			Line0(0);
			Read_ColKey();		
			Line0(1);
	
			if(Testing_bit==0) TouchPad_Process(0);								//=0����ʱ����
			else Test_TouchPad_Process(0);  //=1:���Դ���

			Display_Button_State(scanline);		//��ʾ���壬����̨��ä���İ���/�ſ���״̬��Ϣ  2023-6-28
		
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


void TouchPadSignalKey_Process(void)			//ֻ���������ź�   2023-7-6
{
	TouchPadscanline++;
	
	if(TouchPadscanline>1) TouchPadscanline=1;
	switch(TouchPadscanline)
	{
		case 1:
			Line0(0);
			Read_ColKey();		
			Line0(1);
	
			if(Testing_bit==0) TouchPad_Process(0);								//=0����ʱ����
			else Test_TouchPad_Process(0);  //=1:���Դ���
		
			break;


		default :
			Line0(1);
		
			break;
	}
}



void			Delay_us(u16 usecond)	//�ӳ�΢����ֵ
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
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (MB_Open_Close_State[line-2][i]!=3 && MB_Open_Close_State[line-2][i]!=4) : (MB_Open_Close_State[line-2][i]==1)))									//��ߵ�line����i��ä���򿪣�����ä����Ч
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				sprintf((char*)lcd_Dis,"L%d",i);
				Display_MB_State_Sub(0,i,line-2,Valid_Color,lcd_Dis);		
		//		display_time();																										//��ʱ�������lcd_Dis���顣
				Display_MB();									//��ʾMB�����ɼ�		2023-11-7
				Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_State[i][1]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
								
				if(TP_Display_State[i][0]==0 && !HardwareAlwaysOpenBit && CloseLaneState[i]==2)																			//�˵�û����ʾTP�ɼ�����ʾ״̬Ϊ0��������ʾä���ɼ�;  2024-3-28
					LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);			//����ä���ɼ�������
				Send_Bit=2;					//�÷���ä���ɼ���־
								
					
				//���ָõ��εĵ�ǰä���ɼ�
					//2026-05-27 ��ά: [��][��line-2��][�ֶ�] + �� bitmap
					MB_Result[i][line-2][0]=hour;
					MB_Result[i][line-2][1]=minute;
					MB_Result[i][line-2][2]=second;
					MB_Result[i][line-2][3]=msecond;
					MB_Pressed_Bitmap[i] |= (1<<(line-2));
				if(Lane_TP_MB_State[i][0]==1) 
				{
					//���幤����������Ҫä���ɼ�
					Lane_TP_MB_State[i][0]=7;					//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				else if(Lane_TP_MB_State[i][0]==5){
					//���廵��ֱ����ä���ɼ�����  2023-11-6
					Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Bad_Color);						//��ߴ��廵����ɫ��ʾ  2023-11-7
					Display_MB_Time(hour,minute,second,msecond);		//��ʾMB�ɼ�  2023-11-7
					Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
					Lane_Display_State[i][1]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
					Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
					// 2026-06-03 ֱͨģʽ OR �ر�Ӿ�� ����ʾ�ɼ�
					if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
						LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
					}
	//				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1+1,Lane_NoTbl[i]);			//����ä���ɼ����津��ɼ�������
					OnSendSWData(Touchpad_Command+0x10,Pushbutton_Result,Lane_NoTbl[i]);			//����ä���ɼ����津��ɼ�������
					Send_Bit=2+1;					//�÷���ä���ɼ����津��ɼ���־
				}
				else if(Lane_TP_MB_State[i][0]==0) {  //2026-05-31 ==0 guard, ==7 already-recorded skips
					//����ɼ���û�����߹������������ȼ�¼ä���ɼ����ȵ��涨ʱ��󣬻�û�д���ɼ�������ä���ɼ�����  2023-11-6
					Lane_TP_MB_State[i][0]=2;					//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
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
		if(CloseLaneState[i-10]==2 && (HardwareAlwaysOpenBit ? (MB_Open_Close_State[line-2][i]!=3 && MB_Open_Close_State[line-2][i]!=4) : (MB_Open_Close_State[line-2][i]==1)))									//��ߵ�line����i��ä���򿪣�����ä����Ч
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				sprintf((char*)lcd_Dis,"R%d",i);
				Display_MB_State_Sub(1,i-10,line-2,Valid_Color,lcd_Dis);		
		//		display_time();																										//��ʱ�������lcd_Dis���顣
				Display_MB();									//��ʾMB�����ɼ�		2023-11-7
				Lane_Display_State[i-10][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_State[i-10][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_MSecond[i-10][1]=0;																			//��ʾʱ������
				
				if(TP_Display_State[i-10][1]==0 && !HardwareAlwaysOpenBit && CloseLaneState[i-10]==2)																			//�˵�û����ʾTP�ɼ�����ʾ״̬Ϊ0��������ʾä���ɼ�;  2024-3-28
					LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);			//����ä���ɼ�������
				Send_Bit=2;					//�÷���ä���ɼ���־
					
				//���ָõ��εĵ�ǰä���ɼ�
					//2026-05-27 ��ά: i ���� 10-19 (�ҵ�����), д�� MB_Result[i][line-2][..] + �� bitmap
					MB_Result[i][line-2][0]=hour;
					MB_Result[i][line-2][1]=minute;
					MB_Result[i][line-2][2]=second;
					MB_Result[i][line-2][3]=msecond;
					MB_Pressed_Bitmap[i] |= (1<<(line-2));

				if(Lane_TP_MB_State[i-10][1]==1) 
				{
					//���幤����������Ҫä���ɼ�
					Lane_TP_MB_State[i-10][1]=7;					//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i-10]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				else if(Lane_TP_MB_State[i-10][1]==5){
					//���廵��ֱ����ä���ɼ�����  2023-11-6
					Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Bad_Color);						//��ߴ��廵����ɫ��ʾ  2023-11-7
					Display_MB_Time(hour,minute,second,msecond);		//��ʾMB�ɼ�  2023-11-7
					Lane_Display_State[i-10][0]=0;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
					Lane_Display_State[i-10][1]=1;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
					Lane_Display_MSecond[i-10][0]=0;																			//��ʾʱ������
					// 2026-06-03 ֱͨģʽ OR �ر�Ӿ�� ����ʾ�ɼ�
					if (!HardwareAlwaysOpenBit && CloseLaneState[i-10]==2) {
						LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i-10+1)*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
					}
	//				OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1+1,Lane_NoTbl[i]);			//����ä���ɼ����津��ɼ�������
					OnSendSWData(Touchpad_Command+0x10,Pushbutton_Result,Lane_NoTbl[i]);			//����ä���ɼ����津��ɼ�������
					Send_Bit=2+1;					//�÷���ä���ɼ����津��ɼ���־
				}
				else if(Lane_TP_MB_State[i-10][1]==0) {  //2026-05-31 same as left
					//����ɼ���û�����߹������������ȼ�¼ä���ɼ����ȵ��涨ʱ��󣬻�û�д���ɼ�������ä���ɼ�����  2023-11-6
					Lane_TP_MB_State[i-10][1]=2;					//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i-10]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
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
		//display_time();																										//��ʱ�������lcd_Dis���顣
			Display_MB();									//��ʾMB�����ɼ�		2023-11-7
			Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
			Lane_Display_State[i][1]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
			Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

			OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);							//����ä���ɼ�������
			Send_Bit=2;					//�÷���ä���ɼ���־
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
		//display_time();																										//��ʱ�������lcd_Dis���顣
			Display_MB();									//��ʾMB�����ɼ�		2023-11-7
			Lane_Display_State[i-10][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
			Lane_Display_State[i-10][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
			Lane_Display_MSecond[i-10][1]=0;																			//��ʾʱ������
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  2024-9-1

			OnSendSWData(Pushbutton1_Command+(line-2)+0x10,SW_Command1,Lane_NoTbl[i]);					//����ä���ɼ�������
			Send_Bit=2;					//�÷���ä���ɼ���־
		}
		if((MB_Open_Close_State[line-2][i]!=3)&&(MB_Open_Close_State[line-2][i]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
			sprintf((char*)lcd_Dis,"R%d",i);
			Display_MB_StateGroup(1,j-1,Open_MB_Color,lcd_Dis);		
		}
		key_oldstate[line][i]=KeyState[i];
	}
 }
}

//2026-05-11 ����̨�źŻ���/���͸���
//   ��"׼������"~"����� StartingBlock_Open_Time ��"�����ڣ�
//     ����������̨�źŵ�ʱ��"����������ʱ��"�������浽 LaneStart_*[i][side]��
//     ��κ��źŰ��������ǣ���"ʱ�������ݸ��Ǿ�����"����"����ʾ��������"��
//   ��������ԭ�е�������ʾ/������Ϊ��
//   side: 0=�� 1=��
static void StartBox_RecordSignal(u8 i,u8 j,u8 side)
{
	// 2026-06-09 ����/����: ��ֱͨģʽ�� SB Closed (==0) ʱ������Ӧʱ���� (4054/4081 ������˫����)
	if (!HardwareAlwaysOpenBit && Startbox_Open_Close_State[i][side] == 0) return;
	if(Ready_timer_bit==1 && GunFired_PostOpenDoneBit==0)
	{
		//�ڴ����ڣ������棬����ʾ/������
		LaneStart_minute[i][side]  = PreStart_minute;
		LaneStart_second[i][side]  = PreStart_second;
		LaneStart_msecond[i][side] = PreStart_msecond;
		LaneStart_Valid[i][side]   = 1;
		LaneStart_Computed[i][side]= 0;
	}
	else
	{
		//�����⣺����ԭ��������ʾ/������Ϊ
		// 2026-06-03 ֱͨģʽӲ�� LCD ����ʾ�ɼ� (= PC �ӹ���ʾ)
		// 2026-06-09 SB ���²������Է�Ӧʱ, �ı��� SB ʱ�̵� LaneStart_*, �� Process_StartBox_LaneTime �� SB �ӳٹ�(state=0)ʱ��
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

//����̨��������
//���������ش��� ���� 2024-2-1
void  StartBox_Process(u8 line)
{
	u8 i,j;

 if(StartBox_Edge_Bit==1)	  //�½��ش���  2024-2-1
 {
	for(i=0;i<10;i++)
	{
		j=i+1;
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][0]!=3 && Startbox_Open_Close_State[i][0]!=4) : ((Startbox_Open_Close_State[i][0]==1)||(Startbox_Open_Close_State[i][0]==2))))									//��ߵ�i������̨�򿪻��ӳٴ� 2024-11-24���ų���̨��Ч
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				//2026-05-11 ����½��أ�����̨��ɫ�仯��Ϊ������ʱ���¼/������ StartBox_RecordSignal ͳһ����
				Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,0);
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 ֱͨģʽ SB �����ɿ��ָ� Open_SB_Color (= ��), ����ҵ��ر��߼�
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][0]==1))									//��ߵ�i������̨�ǳ��δ� 2024-11-24���ų���̨��Ч
				{
					if(Relay_SB_DelayCloseValue==0)
					{
						Startbox_Open_Close_State[i][0]=0;																			//���ӳ٣���߳���̨�ر�  2024-12-17
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);//2024-11-24 Close_Color);//��߳���̨��ʾ
					}
					else {
						Startbox_Open_Close_State[i][0]=2;																			//��߳���̨�ر�
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//��߳���̨��ʾ
					}
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}

		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][1]!=3 && Startbox_Open_Close_State[i][1]!=4) : ((Startbox_Open_Close_State[i][1]==1)||(Startbox_Open_Close_State[i][1]==2))))								//�ұߵ�i������̨�򿪣��ų���̨��Ч
		{
			if((KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {
				//2026-05-11 �ұ��½��أ�����̨��ɫ�仯��Ϊ������ʱ���¼/������ StartBox_RecordSignal ͳһ����
				Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,1);
			}
			if((KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 ֱͨģʽ SB �����ɿ��ָ� Open_SB_Color (= ��), ����ҵ��ر��߼�
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][1]==1))									//�ұߵ�i������̨�ǳ��δ� 2024-11-24���ų���̨��Ч
				{
					if(Relay_SB_DelayCloseValue==0) 
					{
						Startbox_Open_Close_State[i][1]=0;																					//���ӳ٣��ұ߳���̨�ر�		2024-12-17
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//�ұ߳���̨��ʾ
					}
					else {
						Startbox_Open_Close_State[i][1]=2;																			//�ұ߳���̨�ر�
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//�ұ߳���̨��ʾ
					}
				}
			}
			key_oldstate[line][i+10]=KeyState[i+10];
		}
	}
 }
 else   //�����ش���  2024-2-1
 {    
	for(i=0;i<10;i++)
	{
		j=i+1;
	//	if(Startbox_Open_Close_State[i][0]==1)									//��ߵ�i������̨�򿪣��ų���̨��Ч
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][0]!=3 && Startbox_Open_Close_State[i][0]!=4) : ((Startbox_Open_Close_State[i][0]==1)||(Startbox_Open_Close_State[i][0]==2))))			//��ߵ�i������̨�򿪻��ӳٴ� 2024-11-24���ų���̨��Ч
		{
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {  //2024-2-1
				//2026-05-11 ��������أ��� StartBox_RecordSignal ͳһ����
				Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,0);
			}
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {    //2024-2-1
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 ֱͨģʽ SB �����ɿ��ָ� Open_SB_Color (= ��), ����ҵ��ر��߼�
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][0]==1))									//��ߵ�i������̨�ǳ��δ� 2024-11-24���ų���̨��Ч
				{
					if(Relay_SB_DelayCloseValue==0) 
					{
						Startbox_Open_Close_State[i][0]=0;																			//���ӳ٣���߳���̨�ر�  2024-12-17
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Close_Color);//2024-11-24 Close_Color);//��߳���̨��ʾ
					}
					else {
						Startbox_Open_Close_State[i][0]=2;																			//��߳���̨�ر�
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//��߳���̨��ʾ
					}
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
	//	if(Startbox_Open_Close_State[i][1]==1)												//�ұߵ�i������̨�򿪣��ų���̨��Ч
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (Startbox_Open_Close_State[i][1]!=3 && Startbox_Open_Close_State[i][1]!=4) : ((Startbox_Open_Close_State[i][1]==1)||(Startbox_Open_Close_State[i][1]==2))))									//��ߵ�i+10������̨�򿪻��ӳٴ� 2024-11-24���ų���̨��Ч
		{
			if((KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {  // 2024-2-1
				//2026-05-11 �ұ������أ��� StartBox_RecordSignal ͳһ����
				Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);
				StartBox_RecordSignal(i,j,1);
			}
			if((KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {   //2024-2-1
				if (HardwareAlwaysOpenBit) {
					// 2026-06-03 ֱͨģʽ SB �����ɿ��ָ� Open_SB_Color (= ��), ����ҵ��ر��߼�
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Close_Color);
				} else if((Startbox_Open_Close_State[i][1]==1))									//��ߵ�i+10������̨�ǳ��δ� 2024-11-24���ų���̨��Ч
				{
					if(Relay_SB_DelayCloseValue==0) 
					{
						Startbox_Open_Close_State[i][1]=0;																					//���ӳ٣��ұ߳���̨�ر�		2024-12-17
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//�ұ߳���̨��ʾ
					}
					else {
						Startbox_Open_Close_State[i][1]=2;																			//�ұ߳���̨�ر�
						Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);	//�ұ߳���̨��ʾ
					}
				}
			}
			key_oldstate[line][i+10]=KeyState[i+10];
		}
	}

 }
}


//2026-05-11 ��������̨ "����ʱ��" ������ʱ�̣�һ���Լ��㲢�㲥/��ʾÿ����Է���ĳ���ʱ�䡣
//   ÿ���˶�Աʵ��ʱ�� = LaneStart_* - Gun_*���ɸ���������/���棩��
//   ��ĳ���ڴ�������δ��������̨������ʾ "--"��
//   ͨ�� LaneStart_Computed ��Ƿ�ֹ�ظ�������
void  Process_StartBox_LaneTime(void)
{
	u8 i,side;
	long swim_10ms,gun_10ms,delta_10ms;
	u8 sign;        //=0:�˶�Ա���ڷ�������� =1:�˶�Ա���ڷ�����������棩
	u16 d_minute,d_second,d_msecond;

	if(GunFired_PostOpenDoneBit!=1) return;       //������δ������������

	for(i=0;i<10;i++)
	{
		for(side=0;side<2;side++)
		{
			if(LaneStart_Computed[i][side]) continue;   //�Ѵ�����������

			// 2026-06-09 SB �ӳ�̬ (state==2) ���Է�Ӧʱ, ���ӳٹص� (state==0) ����
			if (Startbox_Open_Close_State[i][side]==2) continue;
			//��������̨���봦��"��"��"�ӳٴ�"��"�ر�(0)"״̬��Ӳ����(3)/δ��װ(4) ������
			if((Startbox_Open_Close_State[i][side]!=1)
				&&(Startbox_Open_Close_State[i][side]!=2)
				&&(Startbox_Open_Close_State[i][side]!=0))
			{
				LaneStart_Computed[i][side]=1;        //����Ѵ�������ֹ�ظ���
				continue;
			}

			if(LaneStart_Valid[i][side])
			{
				//�� 10ms Ϊ��С��λ����ɴ����ŵĲ�ֵ
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
				d_msecond = (u16)((delta_10ms - (long)d_second*100L) * 10);   //��ԭ�� 0~990ms

				//��ʽ��Ϊ����������ʾ
				if(d_minute==0)
					sprintf((char*)lcd_Dis,"%s%2d.%02d ",(sign==1)?"-":"+",d_second,d_msecond/10);
				else
					sprintf((char*)lcd_Dis,"%s%2d:%02d.%02d",(sign==1)?"-":"+",d_minute,d_second,d_msecond/10);
				// 2026-06-03 ֱͨģʽ����ʾ SB ��Ӧʱ (= PC �ӹ���ʾ)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Timer_posx[side],Timer_posy[side]+(i+1)*line_height1,180,32,32,lcd_Dis);
					// 2026-06-09 ��Ӧʱ��ʾ��, Process_Display_SiwmDir �� Result_Display_Time ���Զ�����
					Lane_Display_State[i][side] = 1;
					Lane_Display_State[i][1-side] = 0;
					Lane_Display_MSecond[i][side] = 0;
				}

				//���ͣ��� OnSendSWCommand_Data Я����ֵ��para5 = sign��0:����1:������
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
				//�޳���̨�źţ���ʾ "--"
				sprintf((char*)lcd_Dis,"   --   ");
				// 2026-06-03 ֱͨģʽ����ʾ SB ��Ӧʱ (= PC �ӹ���ʾ)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Timer_posx[side],Timer_posy[side]+(i+1)*line_height1,180,32,32,lcd_Dis);
				}
			}

			LaneStart_Computed[i][side]=1;
		}
	}
}


//���Գ���̨
//���������ش��� ���� 2024-2-1
void  Test_StartBox_Process(u8 line)
{
	u8 i,j;
	
 if(StartBox_Edge_Bit==1)	  //�½��ش���  2024-2-1
 {
	for(i=0;i<10;i++)
	{
		j=i+1;
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);//��߳���̨��ʾ
			Display_SB();																										//��ʱ�������lcd_Dis���顣
			Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
			Lane_Display_State[i][1]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
			Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i]);			//���ͳ���̨����ʱ�䣬����
			Send_Bit=2;					//�÷��ͳ���̨������־
		}
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//��߳���̨��ʾ
		}
		key_oldstate[line][i]=KeyState[i];
		
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);//�ұ߳���̨��ʾ
			Display_SB();																										//��ʱ�������lcd_Dis���顣
			Lane_Display_State[i][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
			Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
			Lane_Display_MSecond[i][1]=0;																			//��ʾʱ������
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i+10]);			//���ͳ���̨����ʱ�䣬����
			Send_Bit=2;																											//�÷��ͳ���̨������־
		}
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//�ұ߳���̨��ʾ
		}
		key_oldstate[line][i+10]=KeyState[i+10];
	}
 }
 else   //�����ش���  2024-2-1
 {    
	for(i=0;i<10;i++)
	{
		j=i+1;
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {   //2024-2-1
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Valid_Color);//��߳���̨��ʾ
			Display_SB();																										//��ʱ�������lcd_Dis���顣
			Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
			Lane_Display_State[i][1]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
			Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i]);			//���ͳ���̨����ʱ�䣬����
			Send_Bit=2;					//�÷��ͳ���̨������־
		}
		if((Startbox_Open_Close_State[i][0]!=3)&&(Startbox_Open_Close_State[i][0]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {   //2024-2-1
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//��߳���̨��ʾ
		}
		key_oldstate[line][i]=KeyState[i];
		
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==1)&&(key_oldstate[line][i+10]==0)) {   //2024-2-1
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Valid_Color);//�ұ߳���̨��ʾ
			Display_SB();																										//��ʱ�������lcd_Dis���顣
			Lane_Display_State[i][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
			Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
			Lane_Display_MSecond[i][1]=0;																			//��ʾʱ������
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  

			OnSendSWData(Startingblock_Command+0x10,SW_Command1,Lane_NoTbl[i+10]);			//���ͳ���̨����ʱ�䣬����
			Send_Bit=2;																											//�÷��ͳ���̨������־
		}
		if((Startbox_Open_Close_State[i][1]!=3)&&(Startbox_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==0)&&(key_oldstate[line][i+10]==1)) {  //2024-2-1
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//�ұ߳���̨��ʾ
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
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][0]!=3 && TP_Open_Close_State[i][0]!=4) : (TP_Open_Close_State[i][0]==1)))									//��ߴ���򿪣��˶�Ա������Ч
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Valid_Color);						//��ߴ���ʾ��ͼ��ʾ
				display_time();																										//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_State[i][1]=0;																				//�˵���ʾ�ɼ�������ʾ״̬Ϊ0;
				Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
				TP_Display_State[i][0]=1;																				//�˵���ʾTP�ɼ�����ʾ״̬Ϊ1;  2024-3-28
				
				// 2026-06-03 ֱͨģʽӲ�� LCD ����ʾ�ɼ� (= PC �ӹ���ʾ)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Final_timer_posx,Final_timer_posy+j*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  
					
					Display_Laps_Place_Direct(i,1);
				}
					
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
				
				if(Lane_TP_MB_State[i][0]==2)
				{
					Lane_TP_MB_State[i][0]=0;							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				else if(Lane_TP_MB_State[i][0]==0)
				{
					Lane_TP_MB_State[i][0]=1;							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
							
	
				//��ߵ�i������̨�������ӳ� ���ڴ��ӳ�ʱ���ڵų���̨��Ч  2024-11-26
				if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
				{
					if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
					{
						Relay_SB_DelayClose_Time[i]=0;						//�ڽ��������У��˶�Ա����󣬳���̨���Դ��ӳ�һ��ʱ�� 2024-11-26
						Startbox_Open_Close_State[i][0]=2;																			//����̨���ӳ�
						Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//��߳���̨��ʾ
					}
				}

			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				TP_Open_Close_State[i][0]=0;																			//��ߴ���ر�
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);						//��ߴ���رգ��˶�Ա������޳ɼ�
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][1]!=3 && TP_Open_Close_State[i][1]!=4) : (TP_Open_Close_State[i][1]==1)))									//�ұߴ���򿪣��˶�Ա������Ч
		{
			if((KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//�ұߴ���ʾ��ͼ��ʾ
				display_time();		//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_MSecond[i][1]=0;																			//��ʾʱ������
				TP_Display_State[i][1]=1;																				//�˵���ʾTP�ɼ�����ʾ״̬Ϊ1;  2024-3-28

				// 2026-06-03 ֱͨģʽӲ�� LCD ����ʾ�ɼ� (= PC �ӹ���ʾ)
				if (!HardwareAlwaysOpenBit) {
					LCD_ShowString(Middle_timer_posx,Middle_timer_posy+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID
					
					Display_Laps_Place_Direct(i,0);
				}
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);											//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
				
				if(Lane_TP_MB_State[i][1]==2)
				{
					Lane_TP_MB_State[i][1]=0;							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				else if(Lane_TP_MB_State[i][1]==0)
				{
					Lane_TP_MB_State[i][1]=1;							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
			}
			if((KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				TP_Open_Close_State[i][1]=0;																			//�ұߴ���ر�
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Close_Color);						//�ұߴ���رգ��˶�Ա������޳ɼ�
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
	}
	*/
	for(i=0;i<10;i++)
	{
		j=i+1;
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][0]!=3 && TP_Open_Close_State[i][0]!=4) : (TP_Open_Close_State[i][0]==1)))									//��ߴ���򿪣��˶�Ա������Ч
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				Display_TP_State(TPsx[0],Timer_posy[0]+j*btnhy,8,btnh,Valid_Color);						//��ߴ���ʾ��ͼ��ʾ
				display_time();																										//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_State[i][1]=0;																				//�˵���ʾ�ɼ�������ʾ״̬Ϊ0;
				Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
				TP_Display_State[i][0]=1;																				//�˵���ʾTP�ɼ�����ʾ״̬Ϊ1;  2024-3-28
				
				// 2026-06-03 ֱͨģʽ OR �ر�Ӿ�� ����ʾ�ɼ� (= PC �ӹ�)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  
					
					Display_Laps_Place_Direct(i,0);
				}
					
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
				//2026-05-31 store LastTouchTime for relay reaction calc
				LastTouchTime_minute[i]=minute; LastTouchTime_second[i]=second; LastTouchTime_msecond[i]=msecond; LastTouchTime_Valid[i]=1;
				
				if(Lane_TP_MB_State[i][0]==2)
				{
					Lane_TP_MB_State[i][0]=7;  //2026-05-31 touchpad over BW, mark lap-recorded							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				else if(Lane_TP_MB_State[i][0]==0)
				{
					Lane_TP_MB_State[i][0]=7;  //2026-05-31 unified mark 7							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				
				if(!HardwareAlwaysOpenBit && (RelayBit==1)) //�յ������  2024-12-1
				{
					//��ߵ�i������̨�������ӳ� ���ڴ��ӳ�ʱ���ڵų���̨��Ч  2024-11-26
					if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
					{
						if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
						{
							Relay_SB_DelayClose_Time[i]=0;						//�ڽ��������У��˶�Ա����󣬳���̨���Դ��ӳ�һ��ʱ�� 2024-11-26
							Startbox_Open_Close_State[i][0]=2;	// 2026-06-09 ���ص� 5-29-1 ==2 �����ӳٹ� (= PC ͬ����)
							Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//��߳���̨��ʾ
						}
					}
				}
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-10
					if(TP_DelayCloseValue==0)	
					{
						TP_Open_Close_State[i][0]=0;			//���ӳ٣���ߴ���ر�  2024-12-17
						Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);						//��ߴ���رգ��˶�Ա������޳ɼ�
					}
					else {
						TP_Open_Close_State[i][0]=2;																			//��ߴ���ӳ��ر�
						Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Delay_Color);						//��ߴ���رգ��˶�Ա������޳ɼ�
					}
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}
		else if(TP_Open_Close_State[i][0]==2)									//��ߴ����ӳٴ򿪣��˶�Ա������Ч  2024-12-12
		{
			if((KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
				Display_TP_State(TPsx[0],Timer_posy[0]+j*btnhy,8,btnh,Valid_Color);						//��ߴ���ʾ��ͼ��ʾ
				display_time();																										//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_State[i][1]=0;																				//�˵���ʾ�ɼ�������ʾ״̬Ϊ0;
				Lane_Display_MSecond[i][0]=0;																			//��ʾʱ������
				TP_Display_State[i][0]=1;																				//�˵���ʾTP�ɼ�����ʾ״̬Ϊ1;  2024-3-28
				
				// 2026-06-03 ֱͨģʽ OR �ر�Ӿ�� ����ʾ�ɼ� (= PC �ӹ�)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  
				}
				
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
				
			}
			if((KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-10
		//			TP_Open_Close_State[i][0]=0;																			//��ߴ���ر�
					TP_Open_Close_State[i][0]=2;																			//��ߴ���ӳ��ر�
					Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);						//��ߴ���رգ��˶�Ա������޳ɼ�
				}
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
		if(CloseLaneState[i]==2 && (HardwareAlwaysOpenBit ? (TP_Open_Close_State[i][1]!=3 && TP_Open_Close_State[i][1]!=4) : (TP_Open_Close_State[i][1]==1)))									//�ұߴ���򿪣��˶�Ա������Ч
		{
			if((KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//�ұߴ���ʾ��ͼ��ʾ
				display_time();		//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_MSecond[i][1]=0;																			//��ʾʱ������
				TP_Display_State[i][1]=1;																				//�˵���ʾTP�ɼ�����ʾ״̬Ϊ1;  2024-3-28

				// 2026-06-03 ֱͨģʽ OR �ر�Ӿ�� ����ʾ�ɼ� (= PC �ӹ�)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID
					
					Display_Laps_Place_Direct(i,1);
				}
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);											//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
				//2026-05-31 store LastTouchTime for relay reaction calc
				LastTouchTime_minute[i]=minute; LastTouchTime_second[i]=second; LastTouchTime_msecond[i]=msecond; LastTouchTime_Valid[i]=1;
				
				if(Lane_TP_MB_State[i][1]==2)
				{
					Lane_TP_MB_State[i][1]=7;  //2026-05-31 same as left							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				else if(Lane_TP_MB_State[i][1]==0)
				{
					Lane_TP_MB_State[i][1]=7;  //2026-05-31 unified mark 7							//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
					Lane_TP_MB_Time_Difference[i]=0;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
				}
				
				if(!HardwareAlwaysOpenBit && (RelayBit==1)) //�յ����ұ�  2024-12-1
				{
					//�ұߵ�i������̨�������ӳ� ���ڴ��ӳ�ʱ���ڵų���̨��Ч  2024-11-26
					if((RelayBit==1)&&(Startbox_Open_Close_State[i][1]!=3)&&(laps[i][0]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
					{
						if(((RelayLaps==2)&&((laps[i][0]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
						{
							Relay_SB_DelayClose_Time[i]=0;						//�ڽ��������У��˶�Ա����󣬳���̨���Դ��ӳ�һ��ʱ�� 2024-11-26
							Startbox_Open_Close_State[i][1]=2;	// 2026-06-09 ���ص� 5-29-1 ==2 �����ӳٹ� (= PC ͬ����)
							Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//�ұ߳���̨��ʾ
						}
					}
				}
				
			}
			if((KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-10
					if(TP_DelayCloseValue==0)	
					{
						TP_Open_Close_State[i][1]=0;																			//���ӳ٣��ұߴ���ر�  2024-12-17
						Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Close_Color);						//�ұߴ����ӳٹرգ��˶�Ա������г�
					}
					else {
						TP_Open_Close_State[i][1]=2;																			//�ұߴ����ӳٹر�
						Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Delay_Color);
					}
				}
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
		else if(TP_Open_Close_State[i][1]==2)									//�ұߴ���򿪣��˶�Ա������Ч   2024-12-12 
		{
			if((KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//�ұߴ���ʾ��ͼ��ʾ
				display_time();		//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_MSecond[i][1]=0;																			//��ʾʱ������
				TP_Display_State[i][1]=1;																				//�˵���ʾTP�ɼ�����ʾ״̬Ϊ1;  2024-3-28

				// 2026-06-03 ֱͨģʽ OR �ر�Ӿ�� ����ʾ�ɼ� (= PC �ӹ�)
				if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
					LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID
				}
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);											//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
				
				
			}
			if((KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-10
		//			TP_Open_Close_State[i][1]=0;																			//�ұߴ���ر�
					TP_Open_Close_State[i][1]=2;																			//�ұߴ����ӳٹر�
					Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Delay_Color);
				}
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
	/*					
		//��ߵ�i������̨�������ӳ� ���ڴ��ӳ�ʱ���ڵų���̨��Ч  2024-11-26
				if((RelayBit==1)&&(Startbox_Open_Close_State[i][Start_Dir]!=3)&&(laps[i][1-Start_Dir]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
				{
					if(((RelayLaps==2)&&((laps[i][1-Start_Dir]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
					{
						Relay_SB_DelayClose_Time[i]=0;						//�ڽ��������У��˶�Ա����󣬳���̨���Դ��ӳ�һ��ʱ�� 2024-11-26
						Startbox_Open_Close_State[i][Start_Dir]=2;																			//����̨���ӳ�
						Display_Startbox_State(Startboxsx[Start_Dir],Startboxsy[Start_Dir]+j*btnhy+8,24,24,Delay_Color);//2024-11-24 Close_Color);//Start_Dir��߳���̨��ʾ
					}
				}
*/
	}

}

//���Դ����ӳ���  2023-8-6
void Test_TouchPad_Process(u8 line)
{
	u16 i,j;
	
	for(i=0;i<10;i++)
	{
		j=i+1;	
		//������ߴ���  2023-8-6
		{
			if((TP_Open_Close_State[i][0]!=3)&&(TP_Open_Close_State[i][0]!=4)&&(KeyState[i]==0)&&(key_oldstate[line][i]==1)) {
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Valid_Color);						//��ߴ���ʾ��ͼ��ʾ
				display_time();																										//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_State[i][1]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_MSecond[i][0]=0;								//��ʾʱ������
				LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID	  
					
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i]);			//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
			}
			if((TP_Open_Close_State[i][0]!=3)&&(TP_Open_Close_State[i][0]!=4)&&(KeyState[i]==1)&&(key_oldstate[line][i]==0)) {
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Open_TP_Color);						//��ߴ���򿪣�������гɼ�
			}
			key_oldstate[line][i]=KeyState[i];
		}
		
		//�����ұߴ���  2023-8-6
		{
			if((TP_Open_Close_State[i][1]!=3)&&(TP_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==0)&&(key_oldstate[Lane][i+10]==1)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Valid_Color);						//�ұߴ���ʾ��ͼ��ʾ
				display_time();		//��LCD ID��ӡ��lcd_Dis���顣
				Lane_Display_State[i][0]=0;																				//�˵�����ʾ�ɼ�����ʾ״̬Ϊ0;
				Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
				Lane_Display_MSecond[i][1]=0;								//��ʾʱ������
				LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,200,32,32,lcd_Dis);		//��ʾLCD ID
						
				OnSendSWData(Touchpad_Command+0x10,Touchpad_Result,Lane_NoTbl[i+10]);			//���ʹ�����μ��ɼ�
				Send_Bit=2;					//�÷��ʹ���ʱ���־
			}
			if((TP_Open_Close_State[i][1]!=3)&&(TP_Open_Close_State[i][1]!=4)&&(KeyState[i+10]==1)&&(key_oldstate[Lane][i+10]==0)) {
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Open_TP_Color);						//�ұߴ���򿪣�������гɼ�
			}
			key_oldstate[Lane][i+10]=KeyState[i+10];
		}
	}
}


void	Display_Laps_Place_Direct(u8 Lane,u8 Direct)
{
	if(Direct==0)  //Ӿ�����  2024-11-24
	{
		if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-11
			if(laps[Lane][0]>0)  	laps[Lane][0]--;
			LLaps_diaplay(Lane);
		}
			if(laps[Lane][0]>0)	display_swim_dir(dir_posx,Lane,0,1);	//display_swim_dir(dir_posx,Lane,Direct,1);	
			else 	display_swim_dir(dir_posx,Lane,0,0);	
	
	}
	else   //Ӿ���ұ�  2024-11-24
	{
		if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-11
			if(laps[Lane][1]>0)  	laps[Lane][1]--;
			RLaps_diaplay(Lane);
		}
		if(laps[Lane][1]>0)	display_swim_dir(dir_posx,Lane,1,1);	//display_swim_dir(dir_posx,Lane,Direct,1);	
		else 	display_swim_dir(dir_posx,Lane,0,0);	
	}
		
	if(Open_State==0){  //=1��ȫ���򿪴��壬����գ�=0����֮ǰԼ����ʽ�رա��򿪴���  2024-12-11
		Lap_Place[laps[Lane][0]+laps[Lane][1]]++;			//��Ӧ����+1��
		if(Lap_Place[laps[Lane][0]+laps[Lane][1]]>10) Lap_Place[laps[Lane][0]+laps[Lane][1]]=10;			//ʮ�������β�����10��
		Place_display(Lane);
	}
}


void Result_Process(u8 lane)
{
//	u8 i=lane-1;
	
	//Result[10][10][10][2][4];   //�ɼ� �ڼ���  �ڼ��� =0:�ڼ���  ����ɼ�  ����/����/ä��1/ä��2/ä��3 ʱ/��/��/ǧ��֮һ��
	/*
	Result_TP[lane][laps[i][0]][0]=Lap_Place[laps[i][0]];				//��������
	Result_TP[lane][laps[i][0]][1]=hour;				//����ɼ�  ʱ
	Result_TP[lane][laps[i][0]][2]=minute;				//����ɼ�  ��
	Result_TP[lane][laps[i][0]][3]=second;				//����ɼ�  ��
	Result_TP[lane][laps[i][0]][4]=msecond/10;				//����ɼ�  �ٷ�֮һ��
	Result_TP[lane][laps[i][0]][5]=msecond%10;				//����ɼ�  ǧ��֮һ��
	*/
}


void Read_ColKey(void)
{
//	Delay_us(100);				//�ӳ�200usȥ����  2023-7-28
	Delay_us(50);		//50		//�ӳ�200usȥ����  2023-8-18
	
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

	Ready_timer_bit=0;				//׼��������ʱ����ʼ��ʱ����ʱλ=0������ʱ��=1����ʼ��ʱ 2024-8-31

	timer_bit=0;								//��ʱλ=0������ʱ��
	
	Testing_bit=0;							//���ڽ��в���λ =1�����ڲ��ԣ� =0��ֹͣ����   2023-8-5
	Testbtn->bcfucolor=BLACK;		//��ɫ  2024-12-24
	Testbtn->caption=Test_btncaption_tbl[Testing_bit][gui_phy.language]; 		//��ʾ�����ԡ�
	btn_draw(Testbtn);		//����ť
	
	Timer_State_LED(0); 
						
	Timer_Reset((1-timer_bit));			//��ʱ����λ    2024-1-25
							
	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color);
	hour=0;
	minute=0;
	second=0;
	msecond=0;

	Start_hour=hour;   				//2024-8-31
	Start_minute=minute;
	Start_second=second;
	Start_msecond=msecond;

	//2026-05-11 "��λ"ͬ���������������ʱ���뷢��ʱ��/ÿ������̨����
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

  display_rollingtime();		//��ʾ����ʱ��		2023-7-11

	Send_Bit=1;

	OnSendSWData(0x7f,0,0);  //���͹���ʱ��

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


	//2026-05-27 ��ά [20��][3��][4�ֶ�] + �尴�� bitmap
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
									
		if(TP_Open_Close_State[i][0]==3)														//���廵 =3����;
		{
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Bad_Color);			//��ߴ��廵��ɫ��ʾ
		}
		else if(TP_Open_Close_State[i][0]==4)														//����û�а�װ =4 2025-1-6;																											//�����
		{
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//��ߴ���û�а�װ��ɫ��ʾ  2025-1-6
		}
		else {														//�����
			TP_Open_Close_State[i][0]=Open_State;		//2026-05-17 ���� Open_State��=1 ʱ���ִ�
			Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,(Open_State==1)?Open_TP_Color:Close_Color);
		}
		
		if(TP_Open_Close_State[i][1]==3)														//���廵 =3����;
		{
			Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Bad_Color);		//�ұߴ��廵��ɫ��ʾ
		}
		else if(TP_Open_Close_State[i][1]==4)														//����û�а�װ =4 2025-1-6;																											//�����
		{
			Display_TP_State(TPsx[1],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//�ұߴ���û�а�װ��ɫ��ʾ  2025-1-6
		}
		else {														//�����
			TP_Open_Close_State[i][1]=Open_State;		//2026-05-17 ���� Open_State
			Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,(Open_State==1)?Open_TP_Color:Close_Color);
		}
				

		if(Startbox_Open_Close_State[i][0]==3)														//����̨�� =3����;
		{
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Bad_Color);			//��߳���̨����ɫ��ʾ
		}
		else if(Startbox_Open_Close_State[i][0]==4)										//2026-05-17 δװ =4 ����
		{
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,UnInstall_Color);
		}
		else {														//����̨��
			Startbox_Open_Close_State[i][0]=Open_State;	//2026-05-17 ���� Open_State
			Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,(Open_State==1)?Open_SB_Color:Close_Color);
		}
		
		if(Startbox_Open_Close_State[i][1]==3)														//����̨�� =3����;
		{
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Bad_Color);		//�ұ߳���̨����ɫ��ʾ
		}
		else if(Startbox_Open_Close_State[i][1]==4)										//2026-05-17 δװ =4 ����
		{
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,UnInstall_Color);
		}
		else {														//����̨��
			Startbox_Open_Close_State[i][1]=Open_State;	//2026-05-17 ���� Open_State
			Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,(Open_State==1)?Open_SB_Color:Close_Color);
		}
		
		if(MB_Open_Close_State[0][i]==3)														//��0�����ä���� =3����;
		{
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateGroup(0,i,Bad_Color,lcd_Dis);		//���ä������ɫ��ʾ
		}
		else if(MB_Open_Close_State[0][i]==4)										//2026-05-17 δװ =4 ����
		{
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateGroup(0,i,UnInstall_Color,lcd_Dis);
		}
		else {														//ä����
			if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=Open_State;	//2026-05-17 ���� Open_State
			if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=Open_State;
			if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=Open_State;
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateGroup(0,i,(Open_State==1)?Open_MB_Color:Close_Color,lcd_Dis);
		}
		if(MB_Open_Close_State[0][i+10]==3)														//ä���� =3����;
		{
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateGroup(1,j-1,Bad_Color,lcd_Dis);			//�ұ�ä������ɫ��ʾ
		}
		else if(MB_Open_Close_State[0][i+10]==4)										//2026-05-17 δװ =4 ����
		{
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateGroup(1,j-1,UnInstall_Color,lcd_Dis);
		}
		else {														//ä����
			if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=Open_State;	//2026-05-17 ���� Open_State
			if(MB_Open_Close_State[1][i+10]!=3 && MB_Open_Close_State[1][i+10]!=4) MB_Open_Close_State[1][i+10]=Open_State;
			if(MB_Open_Close_State[2][i+10]!=3 && MB_Open_Close_State[2][i+10]!=4) MB_Open_Close_State[2][i+10]=Open_State;
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateGroup(1,j-1,(Open_State==1)?Open_MB_Color:Close_Color,lcd_Dis);
		}
		
		CloseLaneState[i]=2 ;					//�رյ���״̬=2���򿪣�=3���ر�
		display_swim_dir(dir_posx,i,CloseLaneState[i],0);			//open
		

		Lane_Display_MSecond[i][0]=0;
		Lane_Display_MSecond[i][1]=0;
		
		laps[i][0]=LAll_Lap;
		laps[i][1]=RAll_Lap;					//2024-11-24

		LLaps_diaplay(i);
					
		RLaps_diaplay(i);					//2024-11-21
		
//		sprintf((char*)lcd_Dis,"           ");
		sprintf((char*)lcd_Dis,"          ");			//��һ���ո�
//		LCD_ShowString(Final_timer_posx,Final_timer_posy+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
		LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
		LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
								
		LCD_ShowString(Placex,Final_timer_posy+j*line_height1,200,32,32,"  ");		//����ʾ���� 
	}
}

void TP_Ready_Init(void)		//׼���������ȴ�����
{
	u16 i,j,k;
	
	timer_bit=0;				//��ʱλ=0������ʱ��
	Testing_bit=0;							//���ڽ��в���λ =1�����ڲ��ԣ� =0��ֹͣ����   2023-8-5

	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN); 

	Timer_Reset((1-timer_bit));			//��ʱ����λ   2024-1-25

	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,GREEN);
	hour=0;
	minute=0;
	second=0;
	msecond=0;

	Start_hour=hour;   				//2024-8-31
	Start_minute=minute;
	Start_second=second;
	Start_msecond=msecond;

	//2026-05-11 ��λ"����������ʱ��"�뷢��ʱ�̡�ÿ���˶�Ա����̨��������
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
		if (CloseLaneState[i] != 2) continue;  // 2026-06-04 �ر�Ӿ������ Ready ���� (= ����״̬����)
		for(j=0;j<2;j++)
		{
			LaneStart_minute[i][j]=0;
			LaneStart_second[i][j]=0;
			LaneStart_msecond[i][j]=0;
			LaneStart_Valid[i][j]=0;
			LaneStart_Computed[i][j]=0;
		}
	}

	Ready_timer_bit=1;				//׼��������ʱ����ʼ��ʱ����ʱλ=0������ʱ��=1����ʼ��ʱ 2024-8-31

	display_rollingtime();		//��ʾ����ʱ��		2023-7-11
	
	OnSendSWData(Timer_Ready_Command+0x10,0,0);  //����׼�������������λ�����������  2024-7-15
	Send_Bit=1;
		
	Timer_State_LED(0); 
	
	Exchange_StartFinalPlace();    //���������  2024-11-27	

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

	
	//2026-05-27 ��ά [20��][3��][4�ֶ�] + �尴�� bitmap
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
		if(CloseLaneState[i]==2)		//�رյ���״̬  =2���򿪣�=3���ر�
		{
			j=i+1;
			if(TP_Open_Close_State[i][0]==3)														//���廵 =3����;
			{
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Bad_Color);			//��ߴ��廵��ɫ��ʾ
			}
			else if(TP_Open_Close_State[i][0]==4)														//����û�а�װ =4 2025-1-6;																											//�����
			{
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//��ߴ���û�а�װ��ɫ��ʾ  2025-1-6
			}
			else {																											//�����
				TP_Open_Close_State[i][0]=0;															//��ߴ���رգ��˶�Ա������Ч
				Display_TP_State(TPsx[0],TPsy[0]+j*btnhy,8,btnh,Close_Color);		//��ߴ������ɫ��ʾ
			}
			
			if(TP_Open_Close_State[i][1]==3)														//���廵 =3����;
			{
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Bad_Color);		//�ұߴ��廵��ɫ��ʾ
			}
			else if(TP_Open_Close_State[i][1]==4)														//����û�а�װ =4 2025-1-6;																											//�����
			{
				Display_TP_State(TPsx[1],TPsy[0]+j*btnhy,8,btnh,UnInstall_Color);			//�ұߴ���û�а�װ��ɫ��ʾ  2025-1-6
			}
			else {																											//�����
				TP_Open_Close_State[i][1]=0;															//�ұߴ���رգ��˶�Ա������Ч
				Display_TP_State(TPsx[1],TPsy[1]+j*btnhy,8,btnh,Close_Color);	//�ұߴ������ɫ��ʾ
			}
		
			//2026-05-31 ���� ==3 (Bad), ==4 (UnInstall), ����״̬���� 0 (Close); �� Auto helper ���� 3 Բ��ɫ
			if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;
			if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=0;
			if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=0;
			sprintf((char*)lcd_Dis,"L%d",(i));
			Display_MB_StateAuto(0, (u8)i, 0, lcd_Dis);
			Display_MB_StateAuto(0, (u8)i, 1, lcd_Dis);
			Display_MB_StateAuto(0, (u8)i, 2, lcd_Dis);
			
			//2026-05-31 ͬ���, ���� Bad/UnInstall, �������� 0
			if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=0;
			if(MB_Open_Close_State[1][i+10]!=3 && MB_Open_Close_State[1][i+10]!=4) MB_Open_Close_State[1][i+10]=0;
			if(MB_Open_Close_State[2][i+10]!=3 && MB_Open_Close_State[2][i+10]!=4) MB_Open_Close_State[2][i+10]=0;
			sprintf((char*)lcd_Dis,"R%d",(i));
			Display_MB_StateAuto(1, (u8)i, 0, lcd_Dis);
			Display_MB_StateAuto(1, (u8)i, 1, lcd_Dis);
			Display_MB_StateAuto(1, (u8)i, 2, lcd_Dis);

/*
			if(Startbox_Open_Close_State[i][0]==3)														//����̨�� =3����;
			{
				Display_Startbox_State(Startboxsx[0],Final_Startboxsy+j*btnhy+8,24,24,Bad_Color);			//��߳���̨����ɫ��ʾ
			}
			else {																											//����̨��
				if(((StartFinalPlace&0x03)==0x00)||((StartFinalPlace&0x03)==0x03))  //=0,3�����������  2024-6-18
				{
					Startbox_Open_Close_State[i][0]=1;																							//��߳���̨��  2023-8-6
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//��߳���̨��ʾ
				}
				else {		 //�������ұߣ���߳���̨�ر�
					Startbox_Open_Close_State[i][0]=0;								//
					Display_Startbox_State(Middle_Startboxsx,Middle_Startboxsy+j*btnhy+8,24,24,Close_Color);//��߳���̨�ر� 2024-11-27
				}
			}
	*/
/*			
		if(Startbox_Open_Close_State[i][1]==3)														//����̨�� =3����;
		{
			Display_Startbox_State(Startboxsx[1],Middle_Startboxsy+j*btnhy+8,24,24,Bad_Color);		//�ұ߳���̨����ɫ��ʾ
		}
		else {																											//����̨��
			if(((StartFinalPlace&0x03)==0x00)||((StartFinalPlace&0x03)==0x03))  //=0,3����������ߣ��ұ߳���̨�ر� 2024-6-19
			{
			 		Startbox_Open_Close_State[i][1]=0;						//�ұ߳���̨�ر�
			 		Display_Startbox_State(Middle_Startboxsx,Middle_Startboxsy+j*btnhy+8,24,24,Close_Color);//�ұ߳���̨��ʾ
			}
			else {		 //�������ұߣ��ұ߳���̨��  2023-8-19
					Startbox_Open_Close_State[i][1]=1;																							//�ұ߳���̨��  2023-8-9
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//Close_Color);		//�ұ߳���̨��ʾ��״̬
			}
		}
		*/
//		display_swim_dir(dir_posx,i,CloseLaneState[i],0);			//open
			
	//	if(((StartFinalPlace&0x03)==0x00)||((StartFinalPlace&0x03)==0x03))  //=0,3����������ߣ��ұ߳���̨�ر� 2024-6-19
		if(Start_Dir==0)  //=0,3����������ߣ��ұ߳���̨�ر� 2024-6-19
		{
//			Start_Dir=0;	//���������ߣ������� 2023-12-1
			display_swim_dir(dir_posx,i,Start_Dir,0);			//open  ������ 2024-12-1  
			if(Startbox_Open_Close_State[i][0]==3)														//����̨�� =3����;
			{
				Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Bad_Color);		//��߳���̨����ɫ��ʾ
			}
			else {																											//����̨��
					Lane_Display_State[i][0]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;  2024-12-3
					Lane_Display_State[i][1]=0;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
					Startbox_Open_Close_State[i][0]=1;										//��߳���̨��  2023-8-9
					Display_Startbox_State(Startboxsx[0],Startboxsy[0]+j*btnhy+8,24,24,Open_SB_Color);//Close_Color);		//��߳���̨��ʾ��״̬
			}
		}
		else {
	//		Start_Dir=1;	//��������ұߣ����ҵ��� 2023-12-1

			display_swim_dir(dir_posx,i,Start_Dir,0);			//open  ���ҵ��� 2024-12-1
			if(Startbox_Open_Close_State[i][1]==3)														//�ұ߳���̨�� =3����;
			{
				Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Bad_Color);		//�ұ߳���̨����ɫ��ʾ
			}
			else {																											//����̨��
					Lane_Display_State[i][0]=0;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;  2024-12-3
					Lane_Display_State[i][1]=1;																				//�˵���ʾ�ɼ�����ʾ״̬Ϊ1;
					Startbox_Open_Close_State[i][1]=1;										//�ұ߳���̨��  2023-8-9
					Display_Startbox_State(Startboxsx[1],Startboxsy[1]+j*btnhy+8,24,24,Open_SB_Color);//Close_Color);		//�ұ߳���̨��ʾ��״̬
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
			LCD_ShowString(Timer_posx[0],Timer_posy[0]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
			LCD_ShowString(Timer_posx[1],Timer_posy[1]+j*line_height1,180,32,32,lcd_Dis);		//��ʾLCD ID	  
								
			LCD_ShowString(Placex,Timer_posy[1]+j*line_height1,200,32,32,"  ");		//����ʾ���� 
		}
	}
	display_rollingtime();		//��ʾ����ʱ��		2023-7-11

}


void Test_Button(void)
{
	u8 i;
	
	timer_bit=0;
	Testing_bit=1;							//���ڽ��в���λ =1�����ڲ��ԣ� =0��ֹͣ����   2023-8-5
					
	Testbtn->bcfucolor=RED;		//��ɫ  2024-12-24
	Testbtn->caption=Test_btncaption_tbl[Testing_bit][gui_phy.language]; 		//��ʾ�����ڲ��ԡ�
	btn_draw(Testbtn);		//����ť
				
	gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,YELLOW); 

	hour=0;
	minute=0;
	second=0;
	msecond=0;

	Start_hour=hour;   				//2024-8-31
	Start_minute=minute;
	Start_second=second;
	Start_msecond=msecond;


	display_rollingtime();		//��ʾ����ʱ��		2023-7-11
				
	Send_Bit=1;
	
	for(i=0;i<10;i++)
	{
		sprintf((char*)lcd_Dis,"L%d",(i));
		TP_Open_Close_State[i][0]=1;									//��ߴ����
		TP_Open_Close_State[i][1]=1;									//�ұߴ����
		
								
		if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//ä����  2024-9-1
		if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//ä����
		if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//ä����
		if(MB_Open_Close_State[0][(1-0)*10+i]!=3 && MB_Open_Close_State[0][(1-0)*10+i]!=4) MB_Open_Close_State[0][(1-0)*10+i]=1;																			//ä����  2024-9-1
		if(MB_Open_Close_State[1][(1-0)*10+i]!=3 && MB_Open_Close_State[1][(1-0)*10+i]!=4) MB_Open_Close_State[1][(1-0)*10+i]=1;																			//ä����
		if(MB_Open_Close_State[2][(1-0)*10+i]!=3 && MB_Open_Close_State[2][(1-0)*10+i]!=4) MB_Open_Close_State[2][(1-0)*10+i]=1;																			//ä����
		
		Startbox_Open_Close_State[i][0]=1;																							//��߳���̨��  2024-9-1
		Startbox_Open_Close_State[i][1]=1;																							//�ұ߳���̨��  2024-9-1
		
		
		Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		

		Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//��߳���̨��ʾ
			
		Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//��ߴ���ʾ��ͼ��ʾ
		
		Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);//�ұ߳���̨��ʾ
			
		Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�ұߴ���ʾ��ͼ��ʾ
		
		sprintf((char*)lcd_Dis,"R%d",(i));
		Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);		
	}
	timer_bit=1;
	Timer_Reset((1-timer_bit));			//��ʱ����λ   2024-1-25
	Ready_timer_bit=1;				//׼��������ʱ����ʼ��ʱ����ʱλ=0������ʱ��=1����ʼ��ʱ 2024-9-1
	
}

void LLaps_diaplay(u8 Lane)		//��ʾ��ߵ�ʣ��Ȧ��  2024-11-21
{
	// 2026-06-03 always-open ģʽ���� LCD Ȧ�� (= �ÿո񸲸�, ����ʼ������)
	if (HardwareAlwaysOpenBit) { sprintf((char*)lcd_Dis,"  "); Lane=Lane+1; LCD_ShowString(Lapsx[0],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis); return; }
	if(CloseLaneState[Lane]==2)		//�رյ���״̬=2���򿪣�=3���ر�
	{
		sprintf((char*)lcd_Dis,"%2d",laps[Lane][0]);
	}
	else 		sprintf((char*)lcd_Dis,"  ");
	Lane=Lane+1;
	LCD_ShowString(Lapsx[0],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis);		//��ʾ����ε�����	2024-11-21  
}

void RLaps_diaplay(u8 Lane)			//��ʾ�ұߵ�ʣ��Ȧ��  2024-11-21
{
	// 2026-06-03 always-open ģʽ���� LCD Ȧ��
	if (HardwareAlwaysOpenBit) { sprintf((char*)lcd_Dis,"  "); Lane=Lane+1; LCD_ShowString(Lapsx[1],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis); return; }
	if(CloseLaneState[Lane]==2)		//�رյ���״̬=2���򿪣�=3���ر�
	{
		sprintf((char*)lcd_Dis,"%2d",laps[Lane][1]);
	}
	else 		sprintf((char*)lcd_Dis,"  ");
	Lane=Lane+1;
	LCD_ShowString(Lapsx[1],Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis);		//��ʾ�ұ��ε�����	2024-11-21  
}

void Place_display(u8 Lane)
{
	// 2026-06-03 always-open ģʽ��������
	if (HardwareAlwaysOpenBit) { sprintf((char*)lcd_Dis,"  "); Lane=Lane+1; LCD_ShowString(Placex,Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis); return; }
	sprintf((char*)lcd_Dis,"%2d",Lap_Place[laps[Lane][0]+laps[Lane][1]]);
	Lane=Lane+1;
	LCD_ShowString(Placex,Final_timer_posy+Lane*line_height1,200,32,32,lcd_Dis);		//��ʾ���� 
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



//Ӿ�����߰�װ���壬������Ӿ����  2024-11-28 
void  Process_Display_SiwmDir(void)
{
		u16 i,j;
		// 2026-06-03 ֱͨģʽ��ִ��������ʾˢ��
		if (HardwareAlwaysOpenBit) return;
/*
	for(i=0;i<10;i++)
		{
			if(CloseLaneState[i]==2)		//�رյ���״̬  =2���򿪣�=3���ر�
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
					//				LCD_ShowString(Final_timer_posx+0*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//����ʾ  
					//				LCD_ShowString(Final_timer_posx+FinalPlace*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Final_timer_posx+FinalPlace*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Placex,Final_timer_posy+(i+1)*line_height1,200,32,32,"  ");		//����ʾ���� 
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
									if(TP_Open_Close_State[i][1]==0)						//����û�� =3����;
									{
										TP_Open_Close_State[i][1]=1;									//����򿪣��˶�Ա���Դ�����Ч
											
										if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
										{
											if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
												Startbox_Open_Close_State[i][0]=1;								//����յ����̨�� =1
										}
										
										if(0==1)	
										{
											Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�յ㴥��ʾ��ͼ��ʾ
		
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
												Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//��� �յ����̨��״̬��ʾ  2024-11-24
											}
										}
										else Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�ұߴ���ʾ��ͼ��ʾ
									}
									
									if(MB_Open_Close_State[0][10+i]==0)			//ä��û�� =3����;
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=1;																			//ä����  2023-11-1
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=1;																			//ä����
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=1;																			//ä����
		
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
					//				LCD_ShowString(Final_timer_posx+1*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//����ʾ  
									LCD_ShowString(Final_timer_posx+(1)*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Placex,Final_timer_posy+(i+1)*line_height1,200,32,32,"  ");		//����ʾ���� 
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
									if(TP_Open_Close_State[i][0]==0)						//����û�� =3����;
									{
										TP_Open_Close_State[i][0]=1;									//����򿪣��˶�Ա���Դ�����Ч
											
										if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
										{
											if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
												Startbox_Open_Close_State[i][0]=1;								//����յ����̨�� =1
										}
										
										if(1==1)	
										{
											Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�յ㴥��ʾ��ͼ��ʾ
		
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2024-11-24
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
												Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//��� �յ����̨��״̬��ʾ  2024-11-24
											}
										}
										else Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�ұߴ���ʾ��ͼ��ʾ
									}
									
									if(MB_Open_Close_State[0][i]==0)			//ä��û�� =3����;
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//ä����  2023-10
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//ä����
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//ä����
		
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
			if(CloseLaneState[i]==2)		//�رյ���״̬  =2���򿪣�=3���ر�
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
					//				LCD_ShowString(Final_timer_posx+0*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//����ʾ  
									LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[0]+(i+1)*line_height1,200,32,32,"  ");		//����ʾ���� 
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
									if(TP_Open_Close_State[i][1]==0)						//����û�� =3����;
									{
										TP_Open_Close_State[i][1]=1;									//����򿪣��˶�Ա���Դ�����Ч
										Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�ұߴ���ʾ��ͼ��ʾ
																				
									//	if(0==1)	
					/*
										if(!HardwareAlwaysOpenBit && (RelayBit==1)&&(Start_Dir==0)) //�յ������  2024-12-1
										{
											Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�յ㴥��ʾ��ͼ��ʾ
		
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-11-24
												{
													Startbox_Open_Close_State[i][0]=1;								//����յ����̨�� =1
													Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//��� �յ����̨��״̬��ʾ  2024-11-24
												}	
										}
										*/
																				
										if(!HardwareAlwaysOpenBit && (RelayBit==1)) //�յ����ұ�  2024-12-1
										{
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][1]!=3)&&(laps[i][0]!=0))			//2026-06-09 ���ص��Բ�: �󴥰�·�� �� �� SB (= ��K+2 ����)
											{
												if(((RelayLaps==2)&&((laps[i][0]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-12-1
												{
													Startbox_Open_Close_State[i][1]=1;  // 2026-06-09 ���ضԲ�: �󴥰� �� �� SB (= ��K+2 ����, PC L6306 ͬ����)
													Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);//2026-06-09 ���ضԲ���

												}	
											}
										}

									}
									
									if(MB_Open_Close_State[0][10+i]==0)			//ä��û�� =3����;
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=1;																			//ä����  2023-11-1
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=1;																			//ä����
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=1;																			//ä����
		
										Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);		
									}	
									
									if(MB_Open_Close_State[0][i]==1)			//ä��û�� =3����;  ����һ��ä�����Ǵ򿪵ģ�����ر�
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;																			//ä���ر�  2024-12-17
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=0;																			//ä���ر�  2024-12-17
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=0;																			//ä���ر�  2024-12-17
		
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
					//				LCD_ShowString(Final_timer_posx+1*(Middle_timer_posx-Final_timer_posx),Final_timer_posy+(i+1)*line_height1,200,32,32,"           ");		//����ʾ  
									LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[1]+(i+1)*line_height1,200,32,32,"  ");		//����ʾ���� 
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
									if(TP_Open_Close_State[i][0]==0)						//����û�� =3����;
									{
										TP_Open_Close_State[i][0]=1;									//����򿪣��˶�Ա���Դ�����Ч
										Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�յ㴥��ʾ��ͼ��ʾ

										
	//									if(1==1)	
										if(!HardwareAlwaysOpenBit && (RelayBit==1)) //�յ������  2024-12-1
										{
											if((RelayBit==1)&&(Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//2026-06-09 ���ضԲ�: �Ҵ���·�� �� �� SB (= ��K+2 ����)
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-12-1
												{
													Startbox_Open_Close_State[i][0]=1;  // 2026-06-09 ���ضԲ�: �Ҵ��� �� �� SB (= ��K+2 ����, PC L6306 ͬ����)
													Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//2026-06-09 ���ضԲ���

												}	
											}
										}
		//								else Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�ұߴ���ʾ��ͼ��ʾ

						//				Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//��ߴ���ʾ��ͼ��ʾ
									}
									
									if(MB_Open_Close_State[0][i]==0)			//ä��û�� =3����;
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//ä����  2023-11-1
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//ä����
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//ä����
		
										Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		
									}	
								
									if(MB_Open_Close_State[0][10+i]==1)			//ä��û�� =3����;  ����һ��ä�����Ǵ򿪵ģ�����ر�
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=0;																			//ä���ر�  2024-12-17
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=0;																			//ä���ر�  2024-12-17
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=0;																			//ä���ر�  2024-12-17
		
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


//Ӿ�ص��߰�װ���壬������Ӿ����  2025-1-16 
void  Single_Process_Display_SiwmDir(void)
{
		u16 i,j;
				
		// 2026-06-03 ֱͨģʽ��ִ��������ʾˢ��
		if (HardwareAlwaysOpenBit) return;
		for(i=0;i<10;i++)
		{
			if(CloseLaneState[i]==2)		//�رյ���״̬  =2���򿪣�=3���ر�
			{
					if(FinalPlace==0)	//=0:�յ�����Ļ��ߣ� =1���յ�����Ļ�ұߡ�
					{
						//���尲װ����� 2025-1-16;
					 if((laps[i][0]!=0))
						if(Lane_Display_State[i][0]==1)
						{
							Lane_Display_MSecond[i][0]++;	
							if((Lane_Display_MSecond[i][0]==Result_Display_Time)||(Lane_Display_MSecond[i][0]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
									LCD_ShowString(Timer_posx[0],Timer_posy[0]+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[0]+(i+1)*line_height1,200,32,32,"  ");		//����ʾ���� 
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
								if(Lane_Display_MSecond[i][0]>=All_Close_Time)   //ȫӾ���ر�ʱ�� 2025-1-16
								{
									if(TP_Open_Close_State[i][1]==0)						//����û�� =3����;
									{
										TP_Open_Close_State[i][1]=1;									//����򿪣��˶�Ա���Դ�����Ч
										Display_TP_State(TPsx[1],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//��ߴ���ʾ��ͼ��ʾ
																				
																				
										if((RelayBit==1)) //�յ������  2024-12-1
										{
											if((Startbox_Open_Close_State[i][1]!=3)&&(laps[i][0]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2025-1-16
											{
												if(((RelayLaps==2)&&((laps[i][0]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2025-1-16
												{
													Startbox_Open_Close_State[i][1]=1;								//����յ����̨�� =1
													Display_Startbox_State(Startboxsx[1],Startboxsy[0]+(i+1)*btnhy+8,24,24,Open_SB_Color);//��� �յ����̨��״̬��ʾ  2025-1-16

												}	
											}
										}
									}
									if(MB_Open_Close_State[0][10+i]==0)			//ä��û�� =3����;
									{
										if(MB_Open_Close_State[0][10+i]!=3 && MB_Open_Close_State[0][10+i]!=4) MB_Open_Close_State[0][10+i]=1;																			//ä����  2025-1-16
										if(MB_Open_Close_State[1][10+i]!=3 && MB_Open_Close_State[1][10+i]!=4) MB_Open_Close_State[1][10+i]=1;																			//ä����
										if(MB_Open_Close_State[2][10+i]!=3 && MB_Open_Close_State[2][10+i]!=4) MB_Open_Close_State[2][10+i]=1;																			//ä����
		
										Display_MB_StateGroup(1,i,Open_MB_Color,lcd_Dis);		
									}	
								}
							}
						}
					}
					else {
						//���尲װ���ұ� 2025-1-16;
					if((laps[i][1]!=0))
						if(Lane_Display_State[i][1]==1)
						{
							Lane_Display_MSecond[i][1]++;	
							if((Lane_Display_MSecond[i][1]==Result_Display_Time)||(Lane_Display_MSecond[i][1]==(Result_Display_Time+MBdelay_Time)))   //2024-3-28
							{
									LCD_ShowString(Timer_posx[1],Timer_posy[1]+(i+1)*line_height1,200,32,32,"          ");		//����ʾ ��һ���ո� 2023-11-3 
									LCD_ShowString(Placex,Timer_posy[1]+(i+1)*line_height1,200,32,32,"  ");		//����ʾ���� 
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
								if(Lane_Display_MSecond[i][1]>=All_Close_Time)   //ȫӾ���ر�ʱ�� 2025-1-16
								{
									if(TP_Open_Close_State[i][0]==0)						//����û�� =3����;
									{
										TP_Open_Close_State[i][0]=1;									//����򿪣��˶�Ա���Դ�����Ч
										Display_TP_State(TPsx[0],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);						//�յ㴥��ʾ��ͼ��ʾ

										if((RelayBit==1)) //�յ����ұ�  2025-1-16
										{
											if((Startbox_Open_Close_State[i][0]!=3)&&(laps[i][1]!=0))			//������־λ=1 ���� ����̨���ǻ��ģ�=3������ �������һȦ 2025-1-16
											{
												if(((RelayLaps==2)&&((laps[i][1]%RelayLaps)==0))||(RelayLaps==1))			//4*100m���� ��4*200�׽��� ʱ ��������̨  2024-12-1
												{
													Startbox_Open_Close_State[i][0]=1;								//�ұ��յ����̨�� =1
													Display_Startbox_State(Startboxsx[0],Startboxsy[1]+(i+1)*btnhy+8,24,24,Open_SB_Color);//�ұ� �յ����̨��״̬��ʾ  2025-1-16

												}	
											}
										}
									}
									
									if(MB_Open_Close_State[0][i]==0)			//ä��û�� =3����;
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=1;																			//ä����  2025-1-16
										if(MB_Open_Close_State[1][i]!=3 && MB_Open_Close_State[1][i]!=4) MB_Open_Close_State[1][i]=1;																			//ä����
										if(MB_Open_Close_State[2][i]!=3 && MB_Open_Close_State[2][i]!=4) MB_Open_Close_State[2][i]=1;																			//ä����
		
										Display_MB_StateGroup(0,i,Open_MB_Color,lcd_Dis);		
									}	
								}
							}
						}
					}						
			}
		}
}



void OnTCP_RS232_Receive_Data_Proc()   //TCP �� RS232 ��������Ԥ��������   2023-7-17
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

void OnTCP_RS232_Receive_Command_Proc()   //TCP �� RS232 �������������
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
							case Start_Command:   //��ʱ����ʼ��ʱ  2023-7-17
								StartTiming();

						
							break;
							
							case Test_Command:   //��ʱ�����в���  2023-8-4
								Test_Button();
						
							break;

							case Timer_Ready_Command:   //��ʱ��׼������  2023-7-17

								TP_Ready_Init();

					
							break;


							case Timer_Reset_Command:   //��ʱ����λ  2023-7-17
								//2026-05-25 �޸� #2 (�� PDF Ӳ�����Ķ����� v2026.05.25)��
								//   �յ� 0x20 ����ֹͣ"���� 0x7F"����case ������� timer_bit/Ready_timer_bit �� RTC �ֶ�,
								//   ��������һ֡ 0x7F=0 �� PC, ��Ϊ"��λȷ��"��
								//   ԭ Reset_Timer ������ͬ����, �˴���ǰ����Ϊ�˷�ֹ 0x20 �� Reset_Timer ֮��
								//   100Hz �жϴ����Է����� 0x7F (���� PC �� _runningTime �� 1-2 ��þ�ֵ����)��
								timer_bit = 0;
								Ready_timer_bit = 0;
								hour = 0; minute = 0; second = 0; msecond = 0;
								OnSendSWData(0x7f, 0, 0);
								Send_Bit = 1;

								Reset_Timer();
								
								break;

							case Set_MatchEvent:   		//���ñ�����Ŀ�����������˶�Ա���κţ�ȱ����  2023-11-13
								//2026-05-13(2) �� ͨѶЭ����˵��_v2026.05.13.pdf ���룺
								//   d3 = ��Ȧ�� All_Lap
								//   d4 = �Ҷ������������ RAll_Lap
								//   d5 = �������������� LAll_Lap
								//   d6 = 0-4 ������λͼ   d7 = 5-9 ������λͼ   d8 = ������־(0/1)
								//
								// ע����ʵ�ְ� RelayBit ���� d3�����Ҵ������ d4/d5����Э�����Ȧ���� d3��
								//     ������ d8��Ϊ���ݾ� PC �ˣ����� (All_Lap == d4+d5) У�顣
								All_Lap = RXD_Data_Buffer[3];                 //��Ȧ��
								RAll_Lap = RXD_Data_Buffer[4];                //�Ҷ������������
								LAll_Lap = RXD_Data_Buffer[5];                //��������������
								// ���ݻ��ˣ��� d3==0���ɰ� PC δ����Ȧ�������� d4+d5 ����
								if(All_Lap == 0) All_Lap = LAll_Lap + RAll_Lap;

								// 2026-05-13(2) d8 = ������־���� PC ��ָ����������Ƿ������Ŀ
								//   d8=1 �� ����������Ӳ���� RelayLaps ������ʱ�ٴδ�����̨����ÿ����Ӧʱ
								//   d8=0 �� ������Ŀ��Ӳ��ֻ��һ��������Ӧʱ
								RelayBit = (RXD_Data_Buffer[8] != 0) ? 1 : 0;
								// 2026-06-02 d9 = HardwareAlwaysOpen flag (PC �˲������� "Ӳ���豸: һֱ��")
								HardwareAlwaysOpenBit = (RXD_Data_Buffer[9] != 0) ? 1 : 0;
								// 2026-06-03 always-open ģʽʱ�������� (= ����������ԭ��Ȧ��/������ʾ)
								if (HardwareAlwaysOpenBit) {
									u8 _ci;
									for (_ci = 0; _ci < 10; _ci++) {
										LLaps_diaplay(_ci);
										RLaps_diaplay(_ci);
										Place_display(_ci);
									}
						// 2026-06-03 ֱͨ����Ҳ�� Timer �ɼ��� (= ���ϴα���������ʾ��Ӧʱ/�ɼ�)
						sprintf((char*)lcd_Dis, "          ");
						LCD_ShowString(Timer_posx[0], Timer_posy[0] + (_ci+1) * line_height1, 200, 32, 32, lcd_Dis);
						LCD_ShowString(Timer_posx[1], Timer_posy[1] + (_ci+1) * line_height1, 200, 32, 32, lcd_Dis);
								}
								// RelayLaps �԰�����Լ����4��100m ʱ=1��4��200m ʱ=2���� All_Lap/8����
								// �ǽ�����Ŀ RelayLaps=0�������󴥷�����̨�ؿ��߼���
								RelayLaps = (RelayBit == 1) ? ((All_Lap == 4) ? 1 : (All_Lap / 8)) : 0;  // 2026-06-09 �� 4��50m (All_Lap=4) ���� �� RelayLaps=1, �� 4��100m ͬ��֧
								// ͬ����������"����/�ǽ���"��ť��ʾ
								if(Relaybtn)
								{
									Relaybtn->caption = Relay_btncaption_tbl[RelayBit][gui_phy.language];
									btn_draw(Relaybtn);
								}

								//2026-05-25 �޸� #3: ԭӲ���� 50*All_Lap, 25 �׳���Ŀ����������
								if(Pool50mOr25mbit==0) sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);
								else                    sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);
								LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//��ʾ��������  2026-05-12 ����140

								for(i=0;i<10;i++)
								{
									laps[i][0]=LAll_Lap;

									laps[i][1]=RAll_Lap;					//2024-11-21
							//		LLaps_diaplay(i);
								}

								//2023-11-13
								Receive_Command_buf=RXD_Data_Buffer[6];			//��ǰ�飬4-0���˶�Ա���������000XXXXX; X=0:�˵����˶�Ա��=1���˵����˶�Ա
								for(i=0;i<5;i++)
								{
									if((Receive_Command_buf&0x01)==1) CloseLaneState[i]=2;					//�رյ���״̬  =2���򿪣�=3���ر�
									else CloseLaneState[i]=3;
									//2026-05-13 ���� CloseLaneState ���̸��¹رհ�ť��ɫ���ر�=������=����
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
									Receive_Command_buf=Receive_Command_buf>>1;						//�����һ��
								}
								Receive_Command_buf=RXD_Data_Buffer[7];			//��ǰ�飬9-5���˶�Ա���������000XXXXX; X=0:�˵����˶�Ա��=1���˵����˶�Ա
								for(i=0;i<5;i++)
								{
									if((Receive_Command_buf&0x01)==1) CloseLaneState[i+5]=2;					//�رյ���״̬  =2���򿪣�=3���ر�
									else CloseLaneState[i+5]=3;
									//2026-05-13 ͬ�������̸��¹رհ�ť��ɫ
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
									Receive_Command_buf=Receive_Command_buf>>1;						//�����һ��
								}
					
								
								//2026-05-26 (���� B): �� All_Lap / LAll_Lap / RAll_Lap / RelayBit �ȹؼ���������
								//   �־û������� NAND Flash, Ӳ����������ʱ�Զ��ָ�, ������ VBAT ���ݵ��
								OnWriteMatchData();
								break;

							case Set_ArmDelay_Time:   		//���ô�����ʱ�� 2023-10-16
								All_Close_Time=RXD_Data_Buffer[3]*10;								//ȫӾ���ر�ʱ��  2024-12-27
								Close_Time=RXD_Data_Buffer[4]*10;								//�ر�ʱ��
								MBdelay_Time=RXD_Data_Buffer[5]*10;						//2026-05-18 d5 Ϊ����(PC �� BlindReplaceDelay)���������ֶ�ͳһ��Ӳ�� *10 ת 0.1s ��λ
								Result_Display_Time=RXD_Data_Buffer[6]*10;				//ÿ���ɼ�����ʾͣ��ʱ��3000����
								StartBox_Edge_Bit=RXD_Data_Buffer[7];							// 2024-4-21 receive Startbox Edge bit 

								StartFinalPlace=0x0f&RXD_Data_Buffer[8];									//������յ�λ��  2024-11-27
								TP_DelayCloseValue=RXD_Data_Buffer[9]*10;									//�˶�Ա����TP�źŹر��ӳ�ʱ���ʼ����5�� 2024-12-27
								Relay_SB_DelayCloseValue=RXD_Data_Buffer[10]*10;					//������1�룬���������˶�Ա��̨�����źŹر��ӳ�ʱ���ʼ����5�� 2024-12-27
							
							
							
								FinalPlace=0x01&StartFinalPlace;													//�յ�λ��  2024-11-27
								StartPlace=(0x02&StartFinalPlace)>>1;											//����λ��  2024-11-27
								Display_StartFinalPlace(StartFinalPlace);   									//��ʾ�����  2024-6-10
								SwimmingPool_Arrage=(0x0f0&RXD_Data_Buffer[8])>>4;		//�ı�Ӿ����˳��  2024-6-13
								SwimmingPool_ArrageSubject(SwimmingPool_Arrage);

								display_closetime();																//��ʾӾ������ر�ʱ��  2023-10-17
								//2026-05-26 (���� B): ���ӳ�/�ر�ʱ��Ȳ����־û������� NAND Flash
								OnWriteMatchData();
								break;

							case Set_MB_Num:   					//����ÿ��ä���� 2023-10-16

								Left_MB_Num=RXD_Data_Buffer[3];				//Final�� ä������  �������
								Right_MB_Num=RXD_Data_Buffer[4];				//Middle�� ä������  �������
	
								for	(i=0;i<Max_MB_Num;i++)
								{
									L_MB_State_Line[i]=0;		//��� ä����״̬���ӻ��ǲ�����
									R_MB_State_Line[i]=0;		//�ұ� ä����״̬���ӻ��ǲ�����
								}
								for	(i=0;i<Left_MB_Num;i++)
								{
									L_MB_State_Line[i]=1;		//���Lane ä����״̬���ӻ��ǲ�����
								}
								for	(i=0;i<Right_MB_Num;i++)
								{
									R_MB_State_Line[i]=1;		//�ұ�Lane ä����״̬���ӻ��ǲ�����
								}
								//2026-05-31 sync MB_Open_Close_State: unused MB �� UnInstall(=4), reinstall (4��0 Close), redraw LCD
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
								
								OnWriteMatchData();   	//�洢��������  20254-1-26
								
								break;
							//2026-05-17 0x44 Set_PoolConfiguration_Com1: PC ��Ӿ�س���/Ӿ����ʱ�·�
							//   d3 = Ӿ����(Ӳ���̶� 10������)  d4 = Ӿ�س��� 25 �� 50 ��
							case Set_PoolConfiguration_Com1:
							{
								if(RXD_Data_Buffer[4] == 25)      Pool50mOr25mbit = 1;
								else if(RXD_Data_Buffer[4] == 50) Pool50mOr25mbit = 0;
								OnWriteMatchData();		//�־û������� NAND Flash (2:/)
							}
							break;

							case Set_TPSBMB_State:   		//����TP,SB.MB�û�״̬			2023-11-15
								Command_buf=RXD_Data_Buffer[5];				//���յ�Ӿ��TP����������
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[4];
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[3];
								for(i=0;i<10;i++)			
								{
									if(((Command_buf&0x01)==0))
									{
										TP_Open_Close_State[i][0]=3;			//��ߴ��廵 =3����;
										Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);			//��ʾ��ߴ��廵��ԭɫ
									}
									else if((TP_Open_Close_State[i][0]==3))
									{
										TP_Open_Close_State[i][0]=0;			//��ߴ���� =0���� �ر�;
										Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);			//��ʾ��ߴ���ã��رգ���ԭɫ
									}
									Command_buf=Command_buf>>1;						//�����һλ			
								}
								for(i=0;i<10;i++)			
								{
									if((Command_buf&0x01)==0)
									{
										TP_Open_Close_State[i][1]=3;			//�ұߴ��廵 =3����;
										Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);			//��ʾ�ұߴ��廵��ԭɫ
									}
									else if(TP_Open_Close_State[i][1]==3)
									{
										TP_Open_Close_State[i][1]=0;			//�ұߴ���� =0���� �ر�;
										Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);			//��ʾ�ұߴ���ã��رգ���ԭɫ
									}
									Command_buf=Command_buf>>1;						//�����һλ			
								}
								
								Command_buf=RXD_Data_Buffer[10];				//���յ�Ӿ��SB����������
								Command_buf=((Command_buf&0xF0)<<4)|RXD_Data_Buffer[7];
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[6];
								for(i=0;i<10;i++)			
								{
									if(((Command_buf&0x01)==0))
									{
										Startbox_Open_Close_State[i][0]=3;								//��߳���̨�� =3����;
										Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Bad_Color);//��߳���̨��ʾ
									}
									else if((Startbox_Open_Close_State[i][0]==3))
									{
										Startbox_Open_Close_State[i][0]=0;								//��߳���̨�� =0���� �ر�;
										Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(i+1)*btnhy+8,24,24,Close_Color);			//��ʾ��ߴ���ã��رգ���ԭɫ
									}
									Command_buf=Command_buf>>1;						//�����һλ			
								}
								for(i=0;i<10;i++)			
								{
									if((Command_buf&0x01)==0)
									{
										Startbox_Open_Close_State[i][1]=3;						//�ұ߳���̨�� =3����;
										Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Bad_Color);			//��ʾ�ұ߳���̨����ԭɫ
									}
									else if(Startbox_Open_Close_State[i][1]==3)
									{
										Startbox_Open_Close_State[i][1]=0;						//�ұ߳���̨�� =0���� �ر�;
										Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(i+1)*btnhy+8,24,24,Close_Color);//�ұ߳���̨��ʾ�ã��رգ���ԭɫ
									}
									Command_buf=Command_buf>>1;						//�����һλ			
								}
								
								Command_buf=RXD_Data_Buffer[10];				//���յ�Ӿ��MB����������
								Command_buf=((Command_buf&0x0F)<<8)|RXD_Data_Buffer[9];
								Command_buf=(Command_buf<<8)|RXD_Data_Buffer[8];
								for(i=0;i<10;i++)			
								{
									if(((Command_buf&0x01)==0))
									{
										MB_Open_Close_State[0][i]=3;									//��0�����ä���� =3����;
										sprintf((char*)lcd_Dis,"L%d",(i));
										Display_MB_StateGroup(0,i,Bad_Color,lcd_Dis);				//��ʾ���ä������ԭɫ
									}
									else if((MB_Open_Close_State[0][i]==3))
									{
										if(MB_Open_Close_State[0][i]!=3 && MB_Open_Close_State[0][i]!=4) MB_Open_Close_State[0][i]=0;									//��0�����ä���� =0���� �ر�;
										sprintf((char*)lcd_Dis,"L%d",(i));
										Display_MB_StateGroup(0,i,Close_Color,lcd_Dis);		
									}
									Command_buf=Command_buf>>1;						//�����һλ			
								}
								for(i=0;i<10;i++)			
								{
									if((Command_buf&0x01)==0)
									{
										MB_Open_Close_State[0][i+10]=3;								//��0���ұ�ä���� =3����;
										sprintf((char*)lcd_Dis,"R%d",(i));
										Display_MB_StateGroup(1,i,Bad_Color,lcd_Dis);			//��ʾ�ұ�ä������ԭɫ
									}
									else if(MB_Open_Close_State[0][i+10]==3)
									{
										if(MB_Open_Close_State[0][i+10]!=3 && MB_Open_Close_State[0][i+10]!=4) MB_Open_Close_State[0][i+10]=0;								//��0���ұ�ä���� =0���� �ر�;
										sprintf((char*)lcd_Dis,"R%d",(i));
										Display_MB_StateGroup(1,i,Close_Color,lcd_Dis);		
									}
									Command_buf=Command_buf>>1;						//�����һλ			
								}
									
									OnWriteDeviceData();	//2026-05-26 PC ���� TP/SB/MB ״̬�־û��� swimdev.cfg
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

							//2026-05-12 ���Ƽ������������+ʱ�䣬Ӳ�� RTC �Զ�ͬ��
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

							//2026-05-13 ���Ƽ����ǿ�� ȫ�� / �ָ����� ������ĳ���������豸(TP/SB/MB)
							case Set_LaneDeviceFullOpen:
								{
									u8 target=RXD_Data_Buffer[3];	// 0xFF=ALL  0..9=����
									u8 mode  =RXD_Data_Buffer[4];	// 0=�ָ������ر����� 1=ȫ����ǿ�ƴ򿪣�
									u8 new_state=(mode==1)?1:0;
									u8 li,m,jj;
									u8 lane_a=0,lane_b=0;
									if(target==0xFF) { lane_a=0; lane_b=10; }
									else if(target<10) { lane_a=target; lane_b=target+1; }
									else break;

									for(li=lane_a; li<lane_b; li++)
									{
										jj=li+1;
										//TP�����ࣩ���� ״̬ 3 �� / 4 δ��װ ��������
										if(TP_Open_Close_State[li][0]!=3 && TP_Open_Close_State[li][0]!=4) TP_Open_Close_State[li][0]=new_state;
										if(TP_Open_Close_State[li][1]!=3 && TP_Open_Close_State[li][1]!=4) TP_Open_Close_State[li][1]=new_state;
										//SB�����ࣩ���� ״̬ 3 �� ����
										if(Startbox_Open_Close_State[li][0]!=3) Startbox_Open_Close_State[li][0]=new_state;
										if(Startbox_Open_Close_State[li][1]!=3) Startbox_Open_Close_State[li][1]=new_state;
										//MB��ÿ�� 3 �������ࣩ���� ״̬ 3 �� / 4 δ��װ ����
										for(m=0;m<Max_MB_Num;m++)
										{
											if(MB_Open_Close_State[m][li]!=3 && MB_Open_Close_State[m][li]!=4) MB_Open_Close_State[m][li]=new_state;
											if(MB_Open_Close_State[m][li+10]!=3 && MB_Open_Close_State[m][li+10]!=4) MB_Open_Close_State[m][li+10]=new_state;
										}
										//���� ���̰��� state �ػ汾�� TP/SB/MB ͼ�� ����
										//�� TP
										if(TP_Open_Close_State[li][0]==3) Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][0]==4) Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][0]==1) Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,Open_TP_Color);
										else Display_TP_State(TPsx[0],TPsy[0]+jj*btnhy,8,btnh,Close_Color);
										//�� TP
										if(TP_Open_Close_State[li][1]==3) Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][1]==4) Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][1]==1) Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,Open_TP_Color);
										else Display_TP_State(TPsx[1],TPsy[1]+jj*btnhy,8,btnh,Close_Color);
										//�� SB
										if(Startbox_Open_Close_State[li][0]==3) Display_Startbox_State(Startboxsx[0],Startboxsy[0]+jj*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][0]==1) Display_Startbox_State(Startboxsx[0],Startboxsy[0]+jj*btnhy+8,24,24,Open_SB_Color);
										else Display_Startbox_State(Startboxsx[0],Startboxsy[0]+jj*btnhy+8,24,24,Close_Color);
										//�� SB
										if(Startbox_Open_Close_State[li][1]==3) Display_Startbox_State(Startboxsx[1],Startboxsy[1]+jj*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][1]==1) Display_Startbox_State(Startboxsx[1],Startboxsy[1]+jj*btnhy+8,24,24,Open_SB_Color);
										else Display_Startbox_State(Startboxsx[1],Startboxsy[1]+jj*btnhy+8,24,24,Close_Color);
										//�� MB���õ� 0 �� MB ״̬��ʾ�������� Reset_Timer һ�£�
										sprintf((char*)lcd_Dis,"L%d",(li));
										if(MB_Open_Close_State[0][li]==3) Display_MB_StateGroup(0,jj-1,Bad_Color,lcd_Dis);
										else if(MB_Open_Close_State[0][li]==1) Display_MB_StateGroup(0,jj-1,Open_MB_Color,lcd_Dis);
										else Display_MB_StateGroup(0,jj-1,Close_Color,lcd_Dis);
										//�� MB
										sprintf((char*)lcd_Dis,"R%d",(li));
										if(MB_Open_Close_State[0][li+10]==3) Display_MB_StateGroup(1,jj-1,Bad_Color,lcd_Dis);
										else if(MB_Open_Close_State[0][li+10]==1) Display_MB_StateGroup(1,jj-1,Open_MB_Color,lcd_Dis);
										else Display_MB_StateGroup(1,jj-1,Close_Color,lcd_Dis);
									}
								}
								break;

							//2026-05-13(2) �ɵ�˽�� Set_MBDelayTime (raw 0x3C / wire 0x3C) �ѱ��ٷ� 0x4C
							//             Set_ForceAllOpen ռ�� (raw 0x3C / wire 0x4C)���� case ��ɾ����
							//             "ä������ɼ��ӳ�ʱ��" ������������ PC��HW ·����һ��
							//               �� 0x41 Set_ArmDelay_Time �� d5  (��֧�֣������� case)
							//               �� 0x42 SetCommand ���� 0x09 d4 = 0.1s ��λ (���� case 0x32)
							//             (ע: ����� case Set_LaneDeviceFullOpen ���е��� raw 0x3C��
							//              ���Ը�λ�� wire 0x4C �ɸ� case �ӹܡ�)

							//2026-05-13(3) 0x42 SetCommand ��������д����� ͨѶЭ����˵��_v2026.05.13.pdf B3��
							//   d3 = ���루���� ID����d4 = ����ֵ��
							//   �������� 0x01..0x08��LaneCloseTime / StartBlockCloseDelay / ... / BlindWatchCount����
							//   �������� 0x09 BlindReplaceDelay (d4 = 0.1�뵥λ������ 50 = 5.0s)��
							//   PC �� TimingBridge.cs ��֧�� TimingCommandType.SetCommand ���շ���
							//   δ����Ӳ����Ҫ"�������������ر� PC"�������﷢ 0x42 ���ɡ�
							//   ?? raw 0x32 ��Ӧ wire 0x42��
							case 0x32:
								{
									u8 sub  = RXD_Data_Buffer[3];
									u8 val  = RXD_Data_Buffer[4];
									switch(sub)
									{
										case SetCmd_Sub_FalseStartThreshold:	//2026-05-26 0x04 �����ж���ֵ
											//   PC �� FalseStartThreshold ��λ 0.01s, ֱ�ӽ��� val (10 = 0.1s)
											FalseStartThreshold = (u16)val;
											OnWriteMatchData();
											break;
										case SetCmd_Sub_BlindReplaceDelay:	//0x09  ä������ɼ��ӳ�ʱ��
											//2026-05-18 val Ϊ���� (PC BlindReplaceDelay)���������ֶ�ͳһ��Ӳ�� *10 ת 0.1s ��λ
											MBdelay_Time = (u16)val * 10;
											OnWriteMatchData();				//�־û������� NAND Flash (2:/), �´ο�����Ч
											break;
										//0x01..0x08��ԭ��ͨ�� 0x41 ֡�ۺ��·������ֶα��Ҳ�����ɴ�·�����롣
										//Ϊ��С����棬�˴�ֻʵ�������� 0x09�����貹�� 0x01..0x08��
										//�ɲ��� case Set_ArmDelay_Time ��ָ�ֵ���ɡ�
										default:
											break;
									}
								}
								break;


							//2026-05-14 0x47 Set_LaneOpenClose (raw 0x37) ���� ��̬����/����Ӿ��
							//   d3 = 0xFF ȫ������ 0-9 ������d4 = 1 ���� / 0 ���Ρ�
							//   �����õĵ��κ� TP/SB/MB �źŶ����ϱ������Ʒ�Ӧʱ��
							//
							//   ʵ�ֻ������������е� CloseLaneState[i] �ſأ����� TP/SB ��������
							//   ����� CloseLaneState[i]==2 �ŷ��У����� 0x47 �� active=0 ӳ�䵽
							//   CloseLaneState=3 ���ɡ�������Ҫ�����ź�·�����ٲ���������
							//   ͬʱά�� LaneEnabled[] ��ΪЭ�����"��ʽ����"��ǣ��ɱ�δ����
							//   ���ȼ��߼���"�� 0x47 �رյĵ����� 0x4C ȫ��"��ʹ�á�
							//
							//   �Ӿ�������CloseLanebtn ��ɫ/���� + cmdLbtn/cmdRbtn ��ɫ ͬ�����¡�
							case Set_LaneOpenClose:
							{
								//2026-05-17 ��д: 0x47 �� PC ��������"ȫ����/ȫ���ر� TP/SB/MB"��
								//   ԭӲ��ʵ��ȴ���� CloseLaneState/CloseLanebtn/cmdLbtn/cmdRbtn�����ΰ�ť��
								//   ���� TP/SB/MB���� PC �����巴�ˡ�����ĳ�ֻͬ�� TP/SB/MB���뱾��
								//   net_test �� OpenCloseTPbtn �������룩���������ΰ�ť/�ֶ����尴ť��
								//   D3=0xFF ȫ������D3=0..9 ������D4=1 ��/0 �رա�
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
									//---- TP �� ----
									if(TP_Open_Close_State[li][0] != 3 && TP_Open_Close_State[li][0] != 4){
										TP_Open_Close_State[li][0] = new_st;
										Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, col_tp);
									}
									//---- TP �� ----
									if(TP_Open_Close_State[li][1] != 3 && TP_Open_Close_State[li][1] != 4){
										TP_Open_Close_State[li][1] = new_st;
										Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, col_tp);
									}
									//---- SB �� ----
									if(Startbox_Open_Close_State[li][0] != 3 && Startbox_Open_Close_State[li][0] != 4){
										Startbox_Open_Close_State[li][0] = new_st;
										Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, col_sb);
									}
									//---- SB �� ----
									if(Startbox_Open_Close_State[li][1] != 3 && Startbox_Open_Close_State[li][1] != 4){
										Startbox_Open_Close_State[li][1] = new_st;
										Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, col_sb);
									}
									//---- MB �� (3 ��ä��) ----
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
									//---- MB �� (3 ��ä��) ----
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
								OnWriteDeviceData();	//2026-05-26 ���ο��ظ��� TP/SB/MB ״̬, ͬ���־û�
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
										CloseLaneState[li] = active ? 2 : 3;	//���� CloseLaneState �ſ�

										//���� ͬ���ײ� CloseLanebtn ����
										if(CloseLanebtn[li])
										{
											if(active == 0)
											{	//�رգ���ɫ
												CloseLanebtn[li]->bkctbl[0]=0X3186;
												CloseLanebtn[li]->bkctbl[1]=0X2A0F;
												CloseLanebtn[li]->bkctbl[2]=0X2A0F;
												CloseLanebtn[li]->bkctbl[3]=0X10A2;
												CloseLanebtn[li]->bcfucolor=GRAY;
												CloseLanebtn[li]->caption="�ر�";
											}
											else
											{	//�򿪣��ָ�������ɫ
												CloseLanebtn[li]->bkctbl[0]=0X6BF6;
												CloseLanebtn[li]->bkctbl[1]=0X545E;
												CloseLanebtn[li]->bkctbl[2]=0X5C7E;
												CloseLanebtn[li]->bkctbl[3]=0X2ADC;
												CloseLanebtn[li]->bcfucolor=WHITE;
												CloseLanebtn[li]->caption="��";
											}
											btn_draw(CloseLanebtn[li]);
										}
										//���� ͬ�� ���ΰ�ť cmdLbtn / cmdRbtn ��ɫ ����
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
										//���� ����ָʾͬ�� ����
										display_swim_dir(dir_posx, li, CloseLaneState[li], 0);
									}
								}
								break;
							//2026-05-17 0x61 Set_LapRemaining (raw 0x51) ���� ͬ��ĳ��ĳ��ʣ��Ȧ��
							//2026-05-29 �ݴ��� D ��չ d6 / d7: ������������豸�� (©��/�󴥲���)
							//   d3=���� 0..9, d4=�� (0��/1��), d5=ʣ��Ȧ��
							//   d6=1 ©������ �� �� _side TP+MB, �� 1-_side TP+MB
							//   d6=2 �󴥻��� �� �� _side TP+MB, �� 1-_side TP+MB
							//   d7=1 ���� SB ����; d7=2 ���� SB ����; d7=0 ���� SB
							//   �����ֶ����� T (�û�Ҫ��), ������, ����ؼ�ʱ
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
								//2026-05-30 fix #2: Lap_Place ��λ���� (���������� Display_Laps_Place_Direct �� ++ �߼�ͬ��)
								//   ��_val < _old_val (ʣ���� = ©����������Ȧ���): Lap_Place[new_total]++ (= �� lane ��������)
								//   ��_val > _old_val (ʣ��� = �󴥻��˳���Ȧ���): Lap_Place[old_total]-- (= ����֮ǰ����)
								//   ��_val == _old_val (PC ֻˢ��): ���� Lap_Place
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
									// d6=0: ֻ���� laps + LED, �����豸���� (���� d7 != 0 �Դ��� SB)
									goto sb_only_label;
								}
								_open_side    = (_open == 1) ? (u8)(1-_side) : _side;
								_close_side   = (u8)(1 - _open_side);
								_mb_num       = (_open_side == 0)  ? Left_MB_Num  : Right_MB_Num;
								_close_mb_num = (_close_side == 0) ? Left_MB_Num  : Right_MB_Num;
								_mb_idx       = (_open_side == 0)  ? _lane : (u8)(_lane + 10);
								_close_mb_idx = (_close_side == 0) ? _lane : (u8)(_lane + 10);
								_jj           = (u16)(_lane + 1);
								// �� _open_side �� TP (������/δװ)
								if(TP_Open_Close_State[_lane][_open_side] != 3
								   && TP_Open_Close_State[_lane][_open_side] != 4) {
									TP_Open_Close_State[_lane][_open_side] = 1;
									Display_TP_State(TPsx[_open_side], TPsy[_open_side]+_jj*btnhy, 8, btnh, Open_TP_Color);
								}
								// �� _close_side �� TP
								if(TP_Open_Close_State[_lane][_close_side] != 3
								   && TP_Open_Close_State[_lane][_close_side] != 4) {
									TP_Open_Close_State[_lane][_close_side] = 0;
									Display_TP_State(TPsx[_close_side], TPsy[_close_side]+_jj*btnhy, 8, btnh, Close_Color);
								}
								// �� _open_side �� MB (�� _mb_num ����, ������/δװ)
								for(_k = 0; _k < _mb_num; _k++) {
									if(MB_Open_Close_State[_k][_mb_idx] != 3
									   && MB_Open_Close_State[_k][_mb_idx] != 4) {
										if(MB_Open_Close_State[_k][_mb_idx]!=3 && MB_Open_Close_State[_k][_mb_idx]!=4) MB_Open_Close_State[_k][_mb_idx]=1;
										sprintf((char*)lcd_Dis, (_open_side==0)?"L%d":"R%d", _lane);
										Display_MB_StateGroup(_open_side, _lane, Open_MB_Color, lcd_Dis);
									}
								}
								// �� _close_side �� MB
								for(_k = 0; _k < _close_mb_num; _k++) {
									if(MB_Open_Close_State[_k][_close_mb_idx] != 3
									   && MB_Open_Close_State[_k][_close_mb_idx] != 4) {
										if(MB_Open_Close_State[_k][_close_mb_idx]!=3 && MB_Open_Close_State[_k][_close_mb_idx]!=4) MB_Open_Close_State[_k][_close_mb_idx]=0;
										sprintf((char*)lcd_Dis, (_close_side==0)?"L%d":"R%d", _lane);
										Display_MB_StateGroup(_close_side, _lane, Close_Color, lcd_Dis);
									}
								}
								// ��ȴ�״̬ + bitmap + MB_Result
								Lane_TP_MB_State[_lane][_open_side] = 0;
								Lane_TP_MB_State[_lane][_close_side] = 0;
								Lane_TP_MB_Time_Difference[_lane] = 0;
								//2026-05-29 fix v2: ģ���������� �� Lane_Display_State[close_side]=1 ����ѭ�����»� >>>/<<< ����
								//   close_side �Ǹմ����ǲ� (���������� 4366 ��ͬԴ); MSecond=0 �õ���ʱ�� 0 ����
								//   ״̬����Ȼ���: �� MSecond �۵� Close_Time ʱ, �� TP �ѱ� PC force open, ��ѭ�� if(==0) ������ �����ظ���
								Lane_Display_State[_lane][_close_side] = 1;
								Lane_Display_State[_lane][_open_side]  = 0;
								//2026-05-29 fix v3: MSecond=Display_Dir_Max_Time ������������ 10 ����ͷ, ֮�󱣳ֲ��� (���´���������ת��)
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
								// �������ͷ (�˶�Ա�� _open_side ����)
								display_swim_dir(dir_posx, _lane, _close_side, Display_Dir_Max_len);  //2026-05-30 fix v4: xy=close_side ������� Process_Display_SiwmDir 5396/5478 ͬԴ (5236-5371 �� /**/ ������, ���ο�)  //2026-05-29 fix v3: �������� 10 ����ͷ �� Process_Display_SiwmDir 5263/5324 ͬԴ
							sb_only_label:
								// SB ���� (d7 ����, �� d6 ����; �����ֶ����� T)
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
									// d7=0: ���� SB ���� (�� TP+MB ͬ��, ������ bug)
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
							//2026-05-18 0x65 Set_RefreshDisplay (raw 0x55) ���� PC �����������Ӳ��ˢһ������
							//   ��ͬӲ������ Setupbtn ·��: ���� CloseLaneState/laps �� SwimControl_init �� �ָ�+�ػ�
							//   PC �� SendRefreshDisplay() �ڲ����Ի���ȷ�����·�
							case Set_RefreshDisplay:
							{
								//2026-05-25 �޸� #1 (�� PDF Ӳ�����Ķ����� v2026.05.25)��
								// 0x65 �յ���ֻ��"�ֲ� UI �ػ�"�����ٵ� SwimControl_init��
								// �������� race distance / All_Lap / LAll_Lap / RAll_Lap / RelayBit /
								// Pool50mOr25mbit / PoolSingleOrDoubleTPbit / SwimmingPool_Arrage ��ҵ���ֶ�
								u16 _rsi;
								//���� �ػ�"������ʾ����"(��������) ����
								if(Pool50mOr25mbit==0) sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);
								else                    sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);
								LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);
								//���� �ػ�"Ӿ���ر�ʱ��" ����
								display_closetime();
								//���� �ػ����������ΰ�ť��ɫ/�����ͷ/����ʣ��Ȧ�� ����
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
								//2026-05-26 (���� 2): �� idle ̬ռλ UI �ػ� (�� SwimControl_init ĩβ����)
								//   ԭ�ֲ��ػ���©�����ҳɼ�ռλ/����ռλ/����ʱ��Բ��, ���� PC ����������ʾ����λ
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

							//2026-05-12 ���Ƽ�������� Ӿ�ص�/���˰�װ����
							//2026-05-25 �޸� (�� PDF Ӳ�����Ķ����� v2026.05.25, ͬ 0x65 �޸� #1)��
							//   ԭ����ĩβ�� SwimControl_init() �������� UI, �� PC �� 0x43 �����·�ʱ,
							//   �ᵼ�±������� / ��Ȧ������ʾ�� init ·���б����ǻؿ���Ĭ�ϡ�
							//   ��Ϊֻ�ػ� TP/SB ״̬ͼ��, ���� All_Lap / LAll_Lap / RAll_Lap / RelayBit /
							//   Pool50mOr25mbit / SwimmingPool_Arrage ������ҵ���ֶΡ�
							case Set_PoolSingleOrDoubleTP:
								PoolSingleOrDoubleTPbit=(RXD_Data_Buffer[3]!=0)?1:0;
								OnWriteMatchData();		//�־û������� NAND Flash (2:/), �´ο�������Ч
								//2026-05-13 ͬ��ˢ�������� TP_Open_Close_State �������ػ�����/����̨/ä��ͼ��
								//2026-05-14 Fix #3: ���߰�װʱ, δ��װ�� �����Ǵ���û��, ��ͬ ����̨ ҲӦ���Ϊ "δ��װ"(=4)
								{
									u8 li;
									if(PoolSingleOrDoubleTPbit==1)
									{	//���߰�װ: δ��װ�˵Ĵ��� + ����̨ �����Ϊ "δ��װ"
										for(li=0;li<10;li++)
										{
											TP_Open_Close_State[li][1-FinalPlace]=4;	//δ��װ�� TP δװ
											TP_Open_Close_State[li][FinalPlace]=0;		//�յ�� TP ����
											Startbox_Open_Close_State[li][1-FinalPlace]=4;
											if(Startbox_Open_Close_State[li][FinalPlace]==4)
												Startbox_Open_Close_State[li][FinalPlace]=0;
										}
									}
									else
									{	//���˰�װ: ���߶�"�Ѱ�װ": TP+SB ͬ���ָ�����״̬(��/�������¼���ת)
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
									//���� �ֲ��ػ����� TP / SB ״̬ͼ�� (���� SwimControl_init) ����
									for(li=0;li<10;li++)
									{
										//��� TP
										if(TP_Open_Close_State[li][0]==4)        Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][0]==3)   Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][0]==0)   Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,Close_Color);
										else                                      Display_TP_State(TPsx[0],TPsy[0]+(li+1)*btnhy,8,btnh,Open_TP_Color);
										//�Ҷ� TP
										if(TP_Open_Close_State[li][1]==4)        Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,UnInstall_Color);
										else if(TP_Open_Close_State[li][1]==3)   Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,Bad_Color);
										else if(TP_Open_Close_State[li][1]==0)   Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,Close_Color);
										else                                      Display_TP_State(TPsx[1],TPsy[1]+(li+1)*btnhy,8,btnh,Open_TP_Color);
										//��� SB
										if(Startbox_Open_Close_State[li][0]==4)        Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,UnInstall_Color);
										else if(Startbox_Open_Close_State[li][0]==3)   Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][0]==0)   Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,Close_Color);
										else                                            Display_Startbox_State(Startboxsx[0],Startboxsy[0]+(li+1)*btnhy+8,24,24,Open_SB_Color);
										//�Ҷ� SB
										if(Startbox_Open_Close_State[li][1]==4)        Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,UnInstall_Color);
										else if(Startbox_Open_Close_State[li][1]==3)   Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,Bad_Color);
										else if(Startbox_Open_Close_State[li][1]==0)   Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,Close_Color);
										else                                            Display_Startbox_State(Startboxsx[1],Startboxsy[1]+(li+1)*btnhy+8,24,24,Open_SB_Color);
									}
								}
									//2026-05-26 (���� 2): �� idle ̬ռλ UI �ػ� (�� SwimControl_init ĩβ����)
								//   ԭ�ֲ��ػ���©�����ҳɼ�ռλ/����ռλ/����ʱ��Բ��, ���� PC ����������ʾ����λ
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
								OnWriteDeviceData();	//2026-05-26 ��/�����л��� TP+SB δ��װ���, ͬ���־û�
							break;

							default:

								break;
						}
				}
	}
}



void display_time(void)
{
	// 2026-06-03 always-open ģʽ����ʱ����ʾ (= PC ��ʾ�ɼ�)
	if (HardwareAlwaysOpenBit) return;
/*
	//��ʾ��1/1000��  2023-9-16
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"     %2d.%03d",second,msecond);//��LCD ID��ӡ��lcd_Dis���顣				 	
		else 	sprintf((char*)lcd_Dis,"  %2d:%02d.%03d",minute,second,msecond);//��LCD ID��ӡ��lcd_Dis���顣				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%03d",hour,minute,second,msecond);//��LCD ID��ӡ��lcd_Dis���顣				 	
*/
	//��ʾ��1/100��  2023-11-3
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"     %2d.%02d",second,msecond/10);//��LCD ID��ӡ��lcd_Dis���顣				 	
		else 	sprintf((char*)lcd_Dis,"  %2d:%02d.%02d",minute,second,msecond/10);//��LCD ID��ӡ��lcd_Dis���顣				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02d",hour,minute,second,msecond/10);//��LCD ID��ӡ��lcd_Dis���顣				 	
}

							
void display_closetime(void)
{
	sprintf((char*)lcd_Dis,"CT:%3ds",Close_Time/10);
	LCD_ShowString(connbtn_ux-500,2,200,btnh1,32,lcd_Dis);		//��ʾӾ��������ʱ��  2024-11-24
}


void 	Display_MB(void)			//��ʾ����ä���ɼ�		2023-11-7
{
	// 2026-06-03 always-open ģʽ���� MB ʱ����ʾ
	if (HardwareAlwaysOpenBit) return;
	//��ʾMB��1/100��  2023-11-7
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"    %2d.%02dM",second,msecond/10);			//��LCD ID��ӡ��lcd_Dis���顣				 	
		else 	sprintf((char*)lcd_Dis," %2d:%02d.%02dM",minute,second,msecond/10);				//��LCD ID��ӡ��lcd_Dis���顣				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02dM",hour,minute,second,msecond/10);	//��LCD ID��ӡ��lcd_Dis���顣				 	
}

void 	Display_SB(void)			//��ʾ��������̨�ɼ�		2024-12-3
{
	// 2026-06-03 always-open ģʽ���� SB ��Ӧʱ��ʾ
	if (HardwareAlwaysOpenBit) return;
	//��ʾSB��1/100��  2023-11-7
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"    %2d.%02dB",second,msecond/10);			//��LCD ID��ӡ��lcd_Dis���顣				 	
		else 	sprintf((char*)lcd_Dis," %2d:%02d.%02dB",minute,second,msecond/10);				//��LCD ID��ӡ��lcd_Dis���顣				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02dB",hour,minute,second,msecond/10);	//��LCD ID��ӡ��lcd_Dis���顣				 	
}



void 	Display_MB_Time(u16 hour,u16 minute,u16 second,u16 msecond)			//��ʾä���ɼ�		2023-11-7	
{
	// 2026-06-03 always-open ģʽ���� MB ʱ����ʾ
	if (HardwareAlwaysOpenBit) return;
	//��ʾMB��1/100��  2023-11-7
	if(hour==0)
	{
		if(minute==0) 	sprintf((char*)lcd_Dis,"    %2d.%02d*",second,msecond/10);			//��LCD ID��ӡ��lcd_Dis���顣				 	
		else 	sprintf((char*)lcd_Dis," %2d:%02d.%02d*",minute,second,msecond/10);				//��LCD ID��ӡ��lcd_Dis���顣				 	
	}
	else 	sprintf((char*)lcd_Dis,"%d:%02d:%02d.%02d*",hour,minute,second,msecond/10);	//��LCD ID��ӡ��lcd_Dis���顣				 	
}



//2026-05-27 ����Ӿ�����������ä���油��������ճɼ�
//  ���� (�� Left_MB_Num / Right_MB_Num Լ��ʵ��ä���� 1/2/3):
//   1 ��: ��Ψһһ��, ԭʼ�ɼ�, ����ǧ��λ
//   2 ��: ƽ��, ��ǧ��λ (ĩλ�� 0, ����������)
//   3 ��ȫ��: ˫��ͬ������ͬ; ȫ��ͬ����λ (����ֵ������м�ֵ)
//   3 ��� 2 �鹤�� (1 �黵): ƽ��, ��ǧ��λ
//  ����: lane (0-9), side (0=�� 1=��), result[4] ��� hour/min/sec/msec
//  ����: 1=����Ч�ɼ�, 0=�� (valid_count==0, ���÷�������)
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
		// �Ѱ��� (bitmap bit) + �ÿ�δ�� (MB_Open_Close_State != 3)
		if( ((bitmap>>k) & 1) && (MB_Open_Close_State[k][idx] != 3) ) {
			valid[k] = 1;
			ms[k] = (u32)MB_Result[idx][k][0]*3600000UL
			      + (u32)MB_Result[idx][k][1]*60000UL
			      + (u32)MB_Result[idx][k][2]*1000UL
			      + (u32)MB_Result[idx][k][3];
			valid_count++;
		}
	}

	if(valid_count == 0) return 0;     // һ�鶼û�� / ���� �� ����

	if(valid_count == 1) {
		// ����: ԭʼ, ����ǧ��λ
		for(k=0; k<mb_num; k++) if(valid[k]) { final_ms = ms[k]; break; }
	}
	else if(valid_count == 2) {
		// ƽ��, ��ǧ��λ (��λ ms �� 0)
		sum = 0;
		for(k=0; k<mb_num; k++) if(valid[k]) sum += ms[k];
		final_ms = sum / 2;
		final_ms = (final_ms / 10) * 10;
	}
	else {
		// valid_count==3, ��Ȼ mb_num==3
		m0 = ms[0]; m1 = ms[1]; m2 = ms[2];
		if(m0 == m1)      final_ms = m0;
		else if(m0 == m2) final_ms = m0;
		else if(m1 == m2) final_ms = m1;
		else {
			// ȫ��ͬ, ȡ��λ
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

void 	Process_TP_MB(void)				//����û�д���ɼ�ʱ��ä���ɼ�����  2023-11-4
{
	u8 i;
	for (i=0;i<10;i++)
	{																					
																					//=0����=1����
		if(Lane_TP_MB_State[i][1]==2)				//=1����		//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
		{
			Lane_TP_MB_Time_Difference[i]++;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
			if(Lane_TP_MB_Time_Difference[i]>MBdelay_Time)
			{
				Lane_TP_MB_Time_Difference[i]=0;
				Lane_TP_MB_State[i][1]=0;
				//2026-05-27 ��������������Ҳ�ä�����ճɼ� (�� Right_MB_Num 1/2/3 ��Լ��); _has=0 ������ʾ���ϱ�
				{
					u16 mb_final[4];
					u8 _has = CalculateMBFinalTime(i, 1, mb_final);
					if(_has) {
						// 2026-06-03 ֱͨģʽӲ�� LCD ����ʾ�ɼ� (= PC �ӹ���ʾ)
						if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
							Display_Laps_Place_Direct(i,1);
							Display_MB_Time(mb_final[0],mb_final[1],mb_final[2],mb_final[3]);
							Lane_Display_State[i][0]=0;
							Lane_Display_State[i][1]=1;
							Lane_Display_MSecond[i][1]=MBdelay_Time;
							LCD_ShowString(Middle_timer_posx,Middle_timer_posy+(i+1)*line_height1,180,32,32,lcd_Dis);
							Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Bad_Color);
						}
						//����ä���ɼ��� PC (��������������ճɼ�, D4=i+10 ��ʾ�Ҳ�)
						OnSendSWCommand_Data(Touchpad_Command+0x10,Pushbutton_Result,i+10,mb_final[1],mb_final[2],mb_final[3]/10,mb_final[0]*16+mb_final[3]%10,mb_final[0],0);
						Send_Bit=2+1;
					}
				}
			}
		}
		
		if(Lane_TP_MB_State[i][0]==2)				//=0����	//ÿ���˶�Ա����Ͳ��а�ä��״̬��=0���޶�����=1���˶�Ա���壻=2�����а�ä����=5�����廵��=6��ä����
		{
			Lane_TP_MB_Time_Difference[i]++;	//ÿ���˶�Ա����Ͳ��а�ä����ʱ���   2023-11-5
			if(Lane_TP_MB_Time_Difference[i] > MBdelay_Time)
			{
				Lane_TP_MB_Time_Difference[i]=0;
				Lane_TP_MB_State[i][0]=0;
				//2026-05-27 ����������������ä�����ճɼ� (�� Left_MB_Num 1/2/3 ��Լ��); _has=0 ������ʾ���ϱ�
				{
					u16 mb_final[4];
					u8 _has = CalculateMBFinalTime(i, 0, mb_final);
					if(_has) {
						// 2026-06-03 ֱͨģʽӲ�� LCD ����ʾ�ɼ� (= PC �ӹ���ʾ)
						if (!HardwareAlwaysOpenBit && CloseLaneState[i]==2) {
							Display_Laps_Place_Direct(i,0);
							Display_MB_Time(mb_final[0],mb_final[1],mb_final[2],mb_final[3]);
							Lane_Display_State[i][0]=1;
							Lane_Display_State[i][1]=0;
							Lane_Display_MSecond[i][0]=MBdelay_Time;
							LCD_ShowString(Final_timer_posx,Final_timer_posy+(i+1)*line_height1,180,32,32,lcd_Dis);
							Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Bad_Color);
						}
						//����ä���ɼ��� PC (��������������ճɼ�, D4=i ��ʾ���)
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


void	Process_TP_DelayClose(void)   //��������TP�ӳ�ʱ�� 2024-12-12
{
	u16 i,j;
	
	for(j=0;j<2;j++)
	{
		for(i=0;i<10;i++)
		{
			if((TP_Open_Close_State[i][j]==2))										//��ߵ�i������TP�ǳ��δ� 2024-12-12���ų���̨��Ч
			{
				TP_DelayClose_Time[i]++;															//���������˶�Ա����TP�źŹر��ӳ�ʱ��+1 2024-12-12
				if(TP_DelayClose_Time[i]>=TP_DelayCloseValue)   //�ӳ�ʱ�����Ԥ����ֵ 2024-11-25
				{
					TP_DelayClose_Time[i]=0;
					TP_Open_Close_State[i][j]=0;																			//����TP�ر�
					Display_TP_State(TPsx[j],TPsy[j]+(i+1)*btnhy,8,btnh,Close_Color);//��ߴ���TP��ʾ 2024-12-12 
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



void	Process_StartBox_DelayClose(void)   //��������̨�ӳ�ʱ�� 2025-11-25
{
	u16 i,j;
	u8 _sb_side;
//	u16	Relay_SB_DelayClose_Time[10];				//���������˶�Ա��̨�����źŹر��ӳ�ʱ�� 2024-11-25
//	u8	Relay_SB_DelayCloseBit[10];			//���������˶�Ա��̨�����źŹر��ӳ�ʱ��λ =1�����������˶�Ա������ʼ�ӳټ�ʱ   =0�������ӳټ�ʱ 2024-11-25
	
	for(i=0;i<10;i++)
	{
		// 2026-06-09 ������ɨ��: 4��50m StartPos=�� ��2 SB ���Ҳ� (1-Start_Dir), ԭֻɨ Start_Dir ��Զ����. �ĳ��Ĳ� ==2 �͸��Ĳ��ӳٹ�.
		_sb_side = 255;
		if(Startbox_Open_Close_State[i][0]==2) _sb_side = 0;
		else if(Startbox_Open_Close_State[i][1]==2) _sb_side = 1;
		if(_sb_side != 255)
		{
			Relay_SB_DelayClose_Time[i]++;							//���������˶�Ա��̨�����źŹر��ӳ�ʱ��+1 2024-11-25
			if(Relay_SB_DelayClose_Time[i]>=Relay_SB_DelayCloseValue)   //�ӳ�ʱ�����Ԥ����ֵ 2024-11-25
			{
				Relay_SB_DelayClose_Time[i]=0;
				Startbox_Open_Close_State[i][_sb_side]=0;																			//����̨�ر�
				j=i+1;
				Display_Startbox_State(Startboxsx[_sb_side],Startboxsy[_sb_side]+(i+1)*btnhy+8,24,24,Close_Color);// 2026-06-09 �� _sb_side ��
			}	
		}
	}
}

//2026-05-30 SB ״̬�仯ɨ��+�ϱ� (Ӳ����PC, cmd=0x1B Startbox_StateChange_Command+0x10)
//   ÿ��ѭ�� tick �Ƚ� Startbox_Open_Close_State[10][2] ���ϴο���, �б仯�����ϱ�
//   D4=Lane_NoTbl[i+side*10] (= ���� lane+�յ�/Զ��), D5=newState (0��/1��/2��ʱ/3��/4δװ)
void Process_StartboxStateChange(void)
{
	u8 i, j;
	// 2026-06-02 "Ӳ���豸һֱ��" ģʽ�²��ϱ�״̬�仯 cmd 0x50, PC ���Լ��Ŀ�������
	if (HardwareAlwaysOpenBit) return;
	for(i = 0; i < 10; i++) {
		for(j = 0; j < 2; j++) {
			if(Startbox_Open_Close_State[i][j] != prev_Startbox_State[i][j]) {
				//2026-05-30 D3=side(0/1) + D4=lane(0-9) ����, ������ Lane_NoTbl[i+side*10] ����
				u8 _newState = Startbox_Open_Close_State[i][j];
				OnSendSWCommand_Data(Startbox_StateChange_Command + 0x10, (u8)j,
					(u8)i, _newState, 0, 0, 0, 0, 0);
				Send_Bit = 2;
				prev_Startbox_State[i][j] = _newState;
			}
		}
	}
}

//2026-05-30 TP ״̬�仯ɨ��+�ϱ� (Ӳ����PC, cmd=0x1E)
//   ÿ��ѭ�� tick �Ƚ� TP_Open_Close_State[10][2] �� prev ����, �б仯�����ϱ�
void Process_TPStateChange(void)
{
	u8 i, j;
	// 2026-06-02 "Ӳ���豸һֱ��" ģʽ�²��ϱ�״̬�仯 cmd 0x51, PC ���Լ��Ŀ�������
	if (HardwareAlwaysOpenBit) return;
	for(i = 0; i < 10; i++) {
		for(j = 0; j < 2; j++) {
			if(TP_Open_Close_State[i][j] != prev_TP_State[i][j]) {
				//2026-05-30 D3=side + D4=lane ����
				u8 _newState = TP_Open_Close_State[i][j];
				OnSendSWCommand_Data(TP_StateChange_Command + 0x10, (u8)j,
					(u8)i, _newState, 0, 0, 0, 0, 0);
				Send_Bit = 2;
				prev_TP_State[i][j] = _newState;
			}
		}
	}
}

//2026-05-30 MB ״̬�仯ɨ��+�ϱ� (Ӳ����PC, cmd=0x1F, 3 ��ä�� �� 10 �� �� 2 ��)
//   MB_Open_Close_State[mb_idx][lane_side_pos], lane_side_pos = lane (0-9 �յ��) or lane+10 (10-19 Զ��)
void Process_MBStateChange(void)
{
	u8 m, p;
	// 2026-06-02 "Ӳ���豸һֱ��" ģʽ�²��ϱ�״̬�仯 cmd 0x52, PC ���Լ��Ŀ�������
	if (HardwareAlwaysOpenBit) return;
	for(m = 0; m < 3; m++) {
		for(p = 0; p < 20; p++) {
			if(MB_Open_Close_State[m][p] != prev_MB_State[m][p]) {
				//2026-05-30 D3=side + D4=lane ����; p = lane + side*10 ���: lane=p%10, side=p/10
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
					
//2026-05-14 Fix #2: ��"S"������ƫ�ƴ� +600 �� +300��
//   ԭ��"S"λ�� StartFinalPlace_x0(250)+600 = 850������¹���ʱ�䱳�� (655..935) ֮�ڣ�
//   �ᱻ���ǡ���Ϊ +300 �� ʵ�� X=550��λ�ڹ���״̬Բ��(627)֮�󣬰�ȫ��
//   ��"S"���� StartFinalPlace_x0=250 ���䡣
#define	StartFinalPlace_RightOffset	300
void Display_StartFinalPlace(u16 StartFinalPlace)  //��ʾ����λ�� 2024-6-17
{
	if(((StartFinalPlace&0x03)==0x01)||((StartFinalPlace&0x03)==0x02))
	{
						//�յ���Ӿ���ұ�
						gui_fill_circle(StartFinalPlace_x0,StartFinalPlace_y0+cr,1.3*cr,ControlArea_Color);
						gui_fill_circle(StartFinalPlace_x0+StartFinalPlace_RightOffset,StartFinalPlace_y0+cr,1.3*cr,YELLOW);
						LCD_ShowString(StartFinalPlace_x0+StartFinalPlace_RightOffset-0.5*cr,StartFinalPlace_y0-0*cr,32,32,32,"S");		//��ʾstr
						Start_Dir=1;							//��Ӿ��ͷָʾ�ķ��� =0��left<-right  =1��right->left�� 2024-6-9
	}
	else {
						//�յ���Ӿ�����
						gui_fill_circle(StartFinalPlace_x0,StartFinalPlace_y0+cr,1.3*cr,YELLOW);
						LCD_ShowString(StartFinalPlace_x0-0.5*cr,StartFinalPlace_y0-0*cr,32,32,32,"S");		//��ʾstr
						gui_fill_circle(StartFinalPlace_x0+StartFinalPlace_RightOffset,StartFinalPlace_y0+cr,1.3*cr,ControlArea_Color);
						Start_Dir=0;							//��Ӿ��ͷָʾ�ķ���  =1��right->left��=0��left<-right  2024-6-9
	}
}

void		SwimmingPool_ArrageSubject(u8 SwimmingPool_Arrage)  //Ӿ�� Ӿ��������˳��	2024-6-13
{
		u16 i;		
					
		for(i=0;i<10;i++)
		{
						cmdLbtn[i]=btn_creat(carea_x0,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 ��Ϊ��ɫ��ť
						//2024-6-8
						if(SwimmingPool_Arrage==0) 
						{
							cmdLbtn[i]->caption=Hcmd_Lbtncaption_tbl[i];			//����߰�ť ������ʾ����
							Lane_NoTbl[i]=i;
						}
						else 
						{
							cmdLbtn[i]->caption=Hcmd_Inv_Lbtncaption_tbl[i];			//����߰�ť ������ʾ����
							Lane_NoTbl[i]=9-i;
						}
						Setup_LaneBtn_LightYellow(cmdLbtn[i]);
						cmdLbtn[i]->font=btnfsize;
						btn_draw(cmdLbtn[i]);		//����߰�ť

						cmdRbtn[i]=btn_creat(btndsx,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 ��Ϊ��ɫ��ť	
						//2024-6-8
						if(SwimmingPool_Arrage==0) 	
						{
							cmdRbtn[i]->caption=Hcmd_btncaption_tbl[i];			//���ұ߰�ť ������ʾ����
							Lane_NoTbl[i+10]=10+i;
						}
						else 
						{
							cmdRbtn[i]->caption=Hcmd_Inv_btncaption_tbl[i];			//���ұ߰�ť ������ʾ����
							Lane_NoTbl[i+10]=10+9-i;
						}
						Setup_LaneBtn_LightYellow(cmdRbtn[i]);
						cmdRbtn[i]->font=btnfsize;
						btn_draw(cmdRbtn[i]);		//���ұ߰�ť
	}
}


void	SwimControl_init(void)			//��ʼ����Ӿ���ƽ���  2024-10-23
{
	u16 i;
/*	
	LCD_Clear(BLUE);//LGRAY);
	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//�·ֽ���	 
	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[22][gui_phy.language]);//��ʾ����  
	system_task_return=0;
*/
	POINT_COLOR=WHITE;//RED;	 
	LCD_Clear(BLUE);//GREEN);	
		
	BACK_COLOR=GRAYBLUE;//LIGHTBLUE;// DARKBLUE;//������ɫ  2024-12-2;

	//�������������򱳾�  2023-11-8
	gui_fill_rectangle(carea_x0-10,carea_y0,carea_x0-10+1045,carea_y0+595,ControlArea_Color);	

	calendar_get_date(&calendar);	//��������		
	sprintf((char*)lcd_id,"%4d-%02d-%02d",calendar.w_year,calendar.w_month,calendar.w_date);//��LCD ID��ӡ��lcd_Dis���顣	
	LCD_ShowString(lcddev.width-300,1,240,32*2,32,lcd_id);		//��ʾLCD ID	  2024-11-10    					 
	
	if(Startbtn&&Resetbtn)
	{
//			LCD_Clear(LGRAY);
	//	app_gui_tcbar(0,0,lcddev.width,gui_phy.tbheight,0x02);			//�·ֽ���	 
	//	gui_show_strmid(0,0,lcddev.width,gui_phy.tbheight,WHITE,gui_phy.tbfsize,(u8*)APP_MFUNS_CAPTION_TBL[23][gui_phy.language]);//��ʾ����  
 	
		//2026-05-12(2nd) 6�����ذ�ť������ɫ���� swim_play �б���һ��
		//���� ��ʼ��ʱ GREEN ����
		Startbtn->caption=Hds0_btncaption_tbl[0][gui_phy.language];
		Startbtn->font=btnfsize;
		Startbtn->bkctbl[0]=0X0420;
		Startbtn->bkctbl[1]=0X07E0;
		Startbtn->bkctbl[2]=0X07E0;
		Startbtn->bkctbl[3]=0X0500;
		Startbtn->bcfucolor=WHITE;	Startbtn->bcfdcolor=BLACK;

		//���� ��λ RED����"�˳�/�ػ�"ͬ�2026-05-12(3rd)������
		Resetbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		Resetbtn->font=btnfsize;
		Resetbtn->bkctbl[0]=0X9000;
		Resetbtn->bkctbl[1]=0XF800;
		Resetbtn->bkctbl[2]=0XF800;
		Resetbtn->bkctbl[3]=0X9000;
		Resetbtn->bcfucolor=WHITE;	Resetbtn->bcfdcolor=BLACK;

		btn_draw(Startbtn);		//����ť
		btn_draw(Resetbtn);		//����ť


		Startbtn->caption=Hds0_btncaption_tbl[1][gui_phy.language];


		//���� �������� CYAN��ǳɫ���� + ��ɫ���֣� ����
		Setupbtn->caption="��������";
		Setupbtn->font=btnfsize;
		Setupbtn->bkctbl[0]=0X041F;
		Setupbtn->bkctbl[1]=0X07FF;
		Setupbtn->bkctbl[2]=0X07FF;
		Setupbtn->bkctbl[3]=0X0410;
		Setupbtn->bcfucolor=BLACK;	Setupbtn->bcfdcolor=WHITE;
		btn_draw(Setupbtn);		//�����ͷ���ʱ�� ��ť  2024-10-23

		//���� �˳�/�ػ� RED ����
		if(ExitShutdownbtn)
		{
			ExitShutdownbtn->bkctbl[0]=0X9000;
			ExitShutdownbtn->bkctbl[1]=0XF800;
			ExitShutdownbtn->bkctbl[2]=0XF800;
			ExitShutdownbtn->bkctbl[3]=0X9000;
			ExitShutdownbtn->bcfucolor=WHITE;
			ExitShutdownbtn->bcfdcolor=BLACK;
			ExitShutdownbtn->caption="�˳�/�ػ�";
			ExitShutdownbtn->font=24;
			btn_draw(ExitShutdownbtn);
		}

		//2026-05-14 Fix #1: �Ӳ�������(net_test)���غ� SwimControl_init �ػ������棬
		//   ��ԭ��©���� NetConnbtn ���� ���°�ť"��ʧ"�����ﲹ�ϼ��ɡ�
		//   ��ɫ�� swim_play() ��ʼ��ʱ��ȫһ�£�caption ����ǰ connstatus ��̬�л���
		if(NetConnbtn)
		{
			//2026-05-18(5) "��������"��ť��ɫ��δ����=��ɫ��Ŀ��������(��ʾ"����Ͽ�")=�Һ�
			if(connstatus==1){	//�����ӣ��Һ�
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0X6800;
				NetConnbtn->bkctbl[2]=0X6800;	NetConnbtn->bkctbl[3]=0X4000;
			}else{	//δ���ӣ���Ŀ��
				NetConnbtn->bkctbl[0]=0X4000;	NetConnbtn->bkctbl[1]=0XF800;
				NetConnbtn->bkctbl[2]=0XF800;	NetConnbtn->bkctbl[3]=0X8000;
			}
			NetConnbtn->bcfucolor=WHITE;
			NetConnbtn->bcfdcolor=BLACK;
			NetConnbtn->caption=(connstatus==1)?"����Ͽ�":"��������";
			NetConnbtn->font=24;
			btn_draw(NetConnbtn);
		}

		//���� ����ʱ�� MAGENTA ����
		SendStartTimerbtn->caption=Hds1_btncaption_tbl[0][gui_phy.language];
		SendStartTimerbtn->caption="����ʱ��";	//"���ͷ���ʱ��";
		SendStartTimerbtn->font=btnfsize;
		SendStartTimerbtn->bkctbl[0]=0X9010;
		SendStartTimerbtn->bkctbl[1]=0XF81F;
		SendStartTimerbtn->bkctbl[2]=0XF81F;
		SendStartTimerbtn->bkctbl[3]=0XA014;
		SendStartTimerbtn->bcfucolor=WHITE;	SendStartTimerbtn->bcfdcolor=BLACK;
		btn_draw(SendStartTimerbtn);		//�����ͷ���ʱ�� ��ť  2024-9-1


	//��+1��ť
		Distance_Addbtn->caption="+1";
		Distance_Addbtn->font=btnfsize;
		btn_draw(Distance_Addbtn);		//��+1��ť

	//��-1��ť
		Distance_Decbtn->caption="-1";
		Distance_Decbtn->font=btnfsize;
		btn_draw(Distance_Decbtn);		//��-1��ť
		

//���԰�ť Test		
		Testbtn->caption=Test_btncaption_tbl[0][gui_phy.language];
		Testbtn->font=btnfsize;
		btn_draw(Testbtn);		//����ť

//������ť Relay	2024-11-21
		Relaybtn->caption=Relay_btncaption_tbl[RelayBit][gui_phy.language];
		Relaybtn->font=btnfsize;
		btn_draw(Relaybtn);		//����ť


/*  //2024-11-3
//���η���ť LaneInv		2024-6-8
		LaneInvbtn->caption=Lane_Inv_btncaption_tbl[0][gui_phy.language];
		LaneInvbtn->font=btnfsize;
		btn_draw(LaneInvbtn);		//����ť

//����λ�ð�ť StartFinalPlace		2024-6-8
		StartFinalPlacebtn->caption=StartFinalPlace_btncaption_tbl[0][gui_phy.language];
		StartFinalPlacebtn->font=btnfsize;
		btn_draw(StartFinalPlacebtn);		//����ť
*/


//		Testbtn->caption=Test_btncaption_tbl[1][gui_phy.language];
		
//׼��������ť Ready ���� YELLOW ������ǳɫ�������� BLACK �߶Ա�
		Readybtn->caption=Ready_btncaption_tbl[0][gui_phy.language];
		Readybtn->font=btnfsize;
		Readybtn->bkctbl[0]=0XA500;	//���Ʊ߿�
		Readybtn->bkctbl[1]=0XFFE0;	//���ƶ���
		Readybtn->bkctbl[2]=0XFFE0;	//�ϰ� ����
		Readybtn->bkctbl[3]=0XC600;	//�°� ����
		Readybtn->bcfucolor=BLACK;	Readybtn->bcfdcolor=WHITE;	//2026-05-12(2nd) �Ƶ׺���
		btn_draw(Readybtn);		//����ť
		
//		Readybtn->caption=Ready_btncaption_tbl[1][gui_phy.language];
	
	
	for(i=0;i<10;i++)
	{
		CloseLanebtn[i]=btn_creat(dir_posx,carea_y0+(i+1)*btnhy,CloseLanebtn_width,btnh,0,BTN_TYPE_ANG);

		CloseLanebtn[i]->bkctbl[0]=0X6BF6;	//�߿���ɫ
		CloseLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.��һ�е���ɫ				
		CloseLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,�ϰ벿����ɫ
		CloseLanebtn[i]->bkctbl[3]=0X2ADC;	//�°벿����ɫ	 
		CloseLanebtn[i]->bcfucolor=WHITE;	//�ɿ�ʱΪ��ɫ
		CloseLanebtn[i]->bcfdcolor=BLACK;	//����ʱΪ��ɫ 
//		CloseLanebtn[i]->caption=netplay_btncaption_tbl[4][gui_phy.language];
//		CloseLanebtn[i]->font=sbtnfsize;



		CloseLaneState[i]=2 ;					//�رյ���״̬=2���򿪣�=3���ر�
		CloseLanebtn[i]->caption="��";	//Hcmd_Lbtncaption_tbl[i];
		CloseLanebtn[i]->font=btnfsize;
		btn_draw(CloseLanebtn[i]);		//����/�رյ��ΰ�ť
	}	

	//ȡ�� ��Ҫ��ä���ɼ�  2024-10-15
/*
	for(i=0;i<10;i++)
	{
		RMBLanebtn[i]=btn_creat(RMBbtn_posx,RMBbtn_posy+(i+1)*btnhy,80,btnh,0,BTN_TYPE_ANG);

		RMBLanebtn[i]->bkctbl[0]=0X6BF6;	//�߿���ɫ
		RMBLanebtn[i]->bkctbl[1]=0X545E;	//0X8C3F.��һ�е���ɫ				
		RMBLanebtn[i]->bkctbl[2]=0X5C7E;	//0X545E,�ϰ벿����ɫ
		RMBLanebtn[i]->bkctbl[3]=0X2ADC;	//�°벿����ɫ	 
		RMBLanebtn[i]->bcfucolor=WHITE;	//�ɿ�ʱΪ��ɫ
		RMBLanebtn[i]->bcfdcolor=BLACK;	//����ʱΪ��ɫ 

		RMBLanebtn[i]->caption="��MB";	//Hcmd_Lbtncaption_tbl[i];
		RMBLanebtn[i]->font=btnfsize;
		btn_draw(RMBLanebtn[i]);		//����/�رյ��ΰ�ť
	}	
*/

	
	for(i=0;i<10;i++)
	{
		
		cmdLbtn[i]=btn_creat(carea_x0,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 ��Ϊ��ɫ��ť
		//2024-6-8
		if(SwimmingPool_Arrage==0) 
		{
			cmdLbtn[i]->caption=Hcmd_Lbtncaption_tbl[i];			//����߰�ť ������ʾ����
			Lane_NoTbl[i]=i;
		}
		else 
		{
			cmdLbtn[i]->caption=Hcmd_Inv_Lbtncaption_tbl[i];			//����߰�ť ������ʾ����
			Lane_NoTbl[i]=9-i;
		}
			
		Setup_LaneBtn_LightYellow(cmdLbtn[i]);
		cmdLbtn[i]->font=btnfsize;
		btn_draw(cmdLbtn[i]);		//����߰�ť

		sprintf((char*)lcd_Dis,"L%d",(i));
		//2026-05-17 MB ���ػ��� MB_Open_Close_State[0][i] ״̬ѡɫ
		{
			u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i];
			if(_mbs==4)      _mbc=UnInstall_Color;
			else if(_mbs==3) _mbc=Bad_Color;
			else if(_mbs==0) _mbc=Close_Color;
			else             _mbc=Open_MB_Color;
			Display_MB_StateGroup(0,i,_mbc,lcd_Dis);
		}

		//2026-05-14 Fix #3: �ػ�ʱ�� Startbox_/TP_Open_Close_State ������ɫ��
		//     ԭʼ�������� GREEN/YELLOW ��� "δ��װ(4)" / "��(3)" ״̬���ǵ���
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
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 ���=��
		else
			Display_TP_State(TPsx[0],TPsy[0]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 ��=Open_TP_Color

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
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Close_Color);	//2026-05-16 ���=��
		else
			Display_TP_State(TPsx[1],TPsy[1]+(i+1)*btnhy,8,btnh,Open_TP_Color);	//2026-05-16 ��=Open_TP_Color


			sprintf((char*)lcd_Dis,"R%d",(i));
			//2026-05-17 MB ���ػ��� MB_Open_Close_State[0][i+10] ״̬ѡɫ
			{
				u16 _mbc; u8 _mbs=MB_Open_Close_State[0][i+10];
				if(_mbs==4)      _mbc=UnInstall_Color;
				else if(_mbs==3) _mbc=Bad_Color;
				else if(_mbs==0) _mbc=Close_Color;
				else             _mbc=Open_MB_Color;
				Display_MB_StateGroup(1,i,_mbc,lcd_Dis);
			}

			cmdRbtn[i]=btn_creat(btndsx,carea_y0+(i+1)*btnhy,btnw,btnh,0,BTN_TYPE_ANG);	//2026-05-13 ��Ϊ��ɫ��ť

			//2024-6-8
			if(SwimmingPool_Arrage==0)
			{
				cmdRbtn[i]->caption=Hcmd_btncaption_tbl[i];			//���ұ߰�ť ������ʾ����
				Lane_NoTbl[i+10]=10+i;
			}
			else
			{
				cmdRbtn[i]->caption=Hcmd_Inv_btncaption_tbl[i];			//���ұ߰�ť ������ʾ����
				Lane_NoTbl[i+10]=10+9-i;
			}
			//2026-05-13 �ҵ��ΰ�ť��ɫ������ɫ���� + ����
			cmdRbtn[i]->bkctbl[0]=0XC600;	//���Ʊ߿�
			cmdRbtn[i]->bkctbl[1]=0XFFF8;	//���ƶ���
			cmdRbtn[i]->bkctbl[2]=0XFFF8;	//�ϰ뵭��
			cmdRbtn[i]->bkctbl[3]=0XEFE0;	//�°��԰�����
			cmdRbtn[i]->bcfucolor=BLACK;
			cmdRbtn[i]->bcfdcolor=WHITE;
/*	
	u8 type;						//��ť����
									//[7]:0,ģʽA,������һ��״̬,�ɿ���һ��״̬.
									//	  1,ģʽB,ÿ����һ��,״̬�ı�һ��.��һ�°���,�ٰ�һ�µ���.
									//[6:4]:����
									//[3:0]:0,��׼��ť;1,ͼƬ��ť;2,�߽ǰ�ť;3,���ְ�ť(����͸��),4,���ְ�ť(������һ)
	u8 sta;							//��ť״̬
									//[7]:����״̬ 0,�ɿ�.1,����.(������ʵ�ʵ�TP״̬)
									//[6]:0,�˴ΰ�����Ч;1,�˴ΰ�����Ч.(����ʵ�ʵ�TP״̬����)
									//[5:2]:����
									//[1:0]:0,�����(�ɿ�);1,����;2,δ�������
	u8 *caption;					//��ť����
	u8 font;						//caption��������
	u8 arcbtnr;						//Բ�ǰ�ťʱԲ�ǵİ뾶										
	u16 bcfucolor; 				  	//button caption font up color
	u16 bcfdcolor; 				  	//button caption font down color

	u16 *bkctbl;					//�������ְ�ť:
									//����ɫ��(��ťΪ���ְ�ť��ʱ��ʹ��)
									//a,��Ϊ���ְ�ť(����͸��ʱ),���ڴ洢����ɫ
									//b,��Ϊ���ְ�ť(������һ��),bkctbl[0]:����ɿ�ʱ�ı���ɫ;bkctbl[1]:��Ű���ʱ�ı���ɫ.
									//���ڱ߽ǰ�ť:
									//bkctbl[0]:Բ�ǰ�ť�߿����ɫ
									//bkctbl[1]:Բ�ǰ�ť��һ�е���ɫ
									//bkctbl[2]:Բ�ǰ�ť�ϰ벿�ֵ���ɫ
									//bkctbl[3]:Բ�ǰ�ť�°벿�ֵ���ɫ	

	u8 *picbtnpathu;				//ͼƬ��ť�ɿ�ʱ��ͼƬ·��
	u8 *picbtnpathd;		 		//ͼƬ��ť����ʱ��ͼƬ·��
*/

			Setup_LaneBtn_LightYellow(cmdRbtn[i]);
			cmdRbtn[i]->font=btnfsize;
			btn_draw(cmdRbtn[i]);		//���ұ߰�ť
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
		
										
	//2026-05-18(2) SwimControl_init ĩβ����"�� Open_State ǿ������ TP/SB/MB ����"��
	//   ԭʵ�ֻ�� PC ֮ǰ��ϸ�����õ�ĳ��״̬(��ص��� 5)���ǡ�
	//   PC ����·�� (case Set_LaneOpenClose / Set_PoolSingleOrDoubleTP / Set_ArmDelay_Time ��) ��ͬ�����飻
	//   net_test �� OpenCloseTPbtn / PoolSingleOrDoubleTPbtn �л�Ҳͬ�����顣
	//   �˴�ֻ��"��ǰ����״̬"�ػ�����ɫ��ʵ��ֵ 0/1/2/3/4 ������������ Open_State ���� col��
{
	u8 li, jj;
	u8 _mb_lbl[8];
	for (li = 0; li < 10; li++) {
		jj = li + 1;
		//TP ��
		if (TP_Open_Close_State[li][0] == 4)      Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, UnInstall_Color);
		else if (TP_Open_Close_State[li][0] == 3) Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, Bad_Color);
		else if (TP_Open_Close_State[li][0] == 0) Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, Close_Color);
		else                                       Display_TP_State(TPsx[0], TPsy[0]+jj*btnhy, 8, btnh, Open_TP_Color);
		//TP ��
		if (TP_Open_Close_State[li][1] == 4)      Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, UnInstall_Color);
		else if (TP_Open_Close_State[li][1] == 3) Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, Bad_Color);
		else if (TP_Open_Close_State[li][1] == 0) Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, Close_Color);
		else                                       Display_TP_State(TPsx[1], TPsy[1]+jj*btnhy, 8, btnh, Open_TP_Color);
		//SB ��
		if (Startbox_Open_Close_State[li][0] == 4)      Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, UnInstall_Color);
		else if (Startbox_Open_Close_State[li][0] == 3) Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, Bad_Color);
		else if (Startbox_Open_Close_State[li][0] == 0) Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, Close_Color);
		else                                             Display_Startbox_State(Startboxsx[0], Startboxsy[0]+jj*btnhy+8, 24, 24, Open_SB_Color);
		//SB ��
		if (Startbox_Open_Close_State[li][1] == 4)      Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, UnInstall_Color);
		else if (Startbox_Open_Close_State[li][1] == 3) Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, Bad_Color);
		else if (Startbox_Open_Close_State[li][1] == 0) Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, Close_Color);
		else                                             Display_Startbox_State(Startboxsx[1], Startboxsy[1]+jj*btnhy+8, 24, 24, Open_SB_Color);
		//MB ��/�� (���� 0 ��ä��ѡɫ)
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
					if((StartFinalPlace&0x03)==0x02)	//  50m, ������� �ұ� ���յ㣺��� 2024-11-27
					{
						LAll_Lap=1;			//2024-11-27
						RAll_Lap=0;			//2024-11-27
						Start_Dir=1;		//2024-12-1
					}
					if((StartFinalPlace&0x03)==0x03)	//  50m�� ���������� ���յ㣺�ұ� 2024-12-1
					{
						LAll_Lap=0;			//2024-11-27
						RAll_Lap=1;			//2024-11-27
						Start_Dir=0;		//2024-12-1
					}
				}

		if(Pool50mOr25mbit==0)	sprintf((char*)lcd_Dis,"  %4dm ",50*All_Lap);				//=0,��׼Ӿ��50m  2025-1-2
		else sprintf((char*)lcd_Dis,"  %4dm ",25*All_Lap);														//=1,�̳� 25m  		2025-1-2
		LCD_ShowString(Inf_area_x0+340,Inf_area_y0,150,btnh1,32,lcd_Dis);		//��ʾ��������  2026-05-12 ����140
			
		gui_fill_circle(RunningTime_x0-32,RunningTime_y0+cr,cr,Invalid_Color); 

	Exchange_StartFinalPlace();    //���������  2024-11-27	
	

		
		SwimmingPool_ArrageSubject(SwimmingPool_Arrage);   //2024-11-3

		gui_show_string("�������ӣ�",lcddev.width-630,(ip_height-ip_fsize)/2,lcddev.width,ip_fsize,ip_fsize,WHITE);//��ʾ���������� ��  2024-10-27

			if(connstatus==0)//���ӶϿ���,ǿ�ƶϿ�����?
						gui_fill_circle(cds0x,1+cr,cr,Close_Color); 			//��������ָʾ�� �죺����  �ң�������
			else 	gui_fill_circle(cds0x,1+cr,cr,Valid_Color); //������ ����ɫ 
			
		display_closetime();	//��ʾӾ������ر�ʱ��  2023-10-17
	
		LCD_ShowString(Voltage_x0,Voltage_y0,200,16,2*16,"BatVol:00.00V");//���ڹ̶�λ����ʾС����  	
								
		//2026-05-18(2) ȥ�� SwimControl_init ĩβ�� Reset_Timer �Զ����ã�
		//   ���Զ����û���ÿ�δ� net_test ����/PC ��ˢ������ʱ�� TP/SB/MB ��״̬���ã�
		//   ���û�'�øĵĸġ����øĵļ������'ԭ��Reset_Timer Ӧֻ���û������� Resetbtn ��
		//   PC �� Timer_Reset_Command ʱ��������Ӧ����ͨ�ػ�·�����Զ��ܡ�
		//if(timer_bit==0 && Ready_timer_bit==0) Reset_Timer();
		
		//2026-05-18(4) ����������/PC ˢ��ʱ��"�հ�ռλ��"����/�ҳɼ������Ρ�����ʱ�䡢����Բ�㡣
		//   ���� idle ̬�� (timer_bit==0 && Ready_timer_bit==0)�������в�����ʵʱ���ݡ�
		if(timer_bit==0 && Ready_timer_bit==0){
			u16 _jj, _jr;
			gui_fill_circle(RunningTime_x0-32, RunningTime_y0+cr, cr, Invalid_Color); //����״̬Բ��(δ����ɫ)
			display_rollingtime();        //����ʱ��(idle ̬��ʾ 0.0)
			for(_jj=0; _jj<10; _jj++){
				_jr = _jj+1;
				sprintf((char*)lcd_Dis,"          ");
				LCD_ShowString(Timer_posx[0], Timer_posy[0]+_jr*line_height1, 180, 32, 32, lcd_Dis); //���ɼ�ռλ
				LCD_ShowString(Timer_posx[1], Timer_posy[1]+_jr*line_height1, 180, 32, 32, lcd_Dis); //�Ҳ�ɼ�ռλ
				LCD_ShowString(Placex, Final_timer_posy+_jr*line_height1, 200, 32, 32, (u8*)"  "); //����ռλ
				LLaps_diaplay(_jj);  //2026-05-19 ���ʣ��Ȧ��ռλ (����ǰ laps[_jj][0] ֵ)
				RLaps_diaplay(_jj);  //2026-05-19 �Ҳ�ʣ��Ȧ��ռλ (����ǰ laps[_jj][1] ֵ)
			}
		}
}

void	Exchange_StartFinalPlace(void)    //���������  2024-11-27
{			
		//�յ���Ӿ�����
		Middle_MBsx=MBsx[1];							//Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ��
		Middle_Startboxsx=Startboxsx[1];
		Middle_TPsx=TPsx[1];
		Middle_timer_posx=Timer_posx[1];	
		Middle_lapsx=Lapsx[1];
		
		Final_MBsx=MBsx[0];							//Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��  2024-6-9
		Final_Startboxsx=Startboxsx[0];
		Final_TPsx=TPsx[0];
		Final_timer_posx=Timer_posx[0];	
		Final_lapsx=Lapsx[0];
/*
	if((FinalPlace==0x00)) //��50m������Ŀ  2024-11-27
	{
		//�յ���Ӿ�����
		Middle_MBsx=MBsx[1];							//Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ��
		Middle_Startboxsx=Startboxsx[1];
		Middle_TPsx=TPsx[1];
		Middle_timer_posx=Timer_posx[1];	
		Middle_lapsx=Lapsx[1];
		
		Final_MBsx=MBsx[0];							//Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��  2024-6-9
		Final_Startboxsx=Startboxsx[0];
		Final_TPsx=TPsx[0];
		Final_timer_posx=Timer_posx[0];	
		Final_lapsx=Lapsx[0];
	}
	else {
		//�յ���Ӿ���ұ�
		Middle_MBsx=MBsx[0];							//Ӿ�����MB,SB,TP,ʱ����ʾ��X�����λ��
		Middle_Startboxsx=Startboxsx[0];
		Middle_TPsx=TPsx[0];
		Middle_timer_posx=Timer_posx[0];	
		Middle_lapsx=Lapsx[0];

		Final_MBsx=MBsx[1];							//Ӿ���ұ�MB,SB,TP,ʱ����ʾ��X�����λ��  2024-6-9
		Final_Startboxsx=Startboxsx[1];
		Final_TPsx=TPsx[1];
		Final_timer_posx=Timer_posx[1];	
		Final_lapsx=Lapsx[1];
	}
	*/
	if((StartPlace==0x01)) //  50m������Ŀ 2024-11-27
	{
		if((FinalPlace==0x00)) //���յ���Ӿ�����  2024-11-27
		{
			//�������Ӿ���ұ�
//			Display_StartFinalPlace(StartFinalPlace);			//2024-6-17

			//Ӿ�����SB��ʱ����ʾ��X�����λ��
//			Middle_Startboxsx=Startboxsx[0];
//			Middle_timer_posx=Timer_posx[0];	

			//Ӿ���ұ�SB,ʱ����ʾ��X�����λ��  2024-6-9
			Final_Startboxsx=Startboxsx[1];
	//		Final_timer_posx=Timer_posx[1];	
		}
		else {
					//������Ӿ�����
//			Display_StartFinalPlace(StartFinalPlace);			//2024-11-27
//			gui_fill_circle(StartFinalPlace_x0,StartFinalPlace_y0+cr,1.3*cr,YELLOW); 					
//			LCD_ShowString(StartFinalPlace_x0-0.5*cr,StartFinalPlace_y0-0*cr,32,32,32,"S");		//��ʾstr	      					 

	//		Middle_Startboxsx=Startboxsx[1];
	//		Middle_timer_posx=Timer_posx[1];	

			//Ӿ�����SB,ʱ����ʾ��X�����λ��  2024-6-9
			Final_Startboxsx=Startboxsx[1];
	//		Final_timer_posx=Timer_posx[0];	
		}
	}
				
	Display_StartFinalPlace(StartFinalPlace);			//2024-11-27

}	

//#define SD_CARD 0 //SD��,����Ϊ0
//#define EX_FLASH 1 //�ⲿspi flash,����Ϊ 1
//#define EX_NAND 2 //�ⲿ nand flash,����Ϊ 2

void OnReadMatchData()   	//����������  2025-1-26
{
 	FIL* fp=0;		//�洢�ļ�	
	u8 res;
	u8 rval=0;
//	u8 *pname=0; 
	u8 *pdatabuf;
	u8* databuf;	
	
	const char *name="2:/swimtime.cfg";

  fp=(FIL *)gui_memin_malloc(sizeof(FIL));			//����FIL�ֽڵ��ڴ�����  
//	pname=gui_memin_malloc(120);							//����60���ֽ��ڴ�,����"0:RECORDER/REC20120321210633.wav" 

//	if(!fp||!pname) rval=1;
	if(!fp) rval=1;
 	else
	{
		res=f_open(fp,(const TCHAR*)name,FA_READ);//���ļ���  ���ļ�
		if(res==FR_OK)
		{
			databuf=(u8*)gui_memex_malloc(fp->obj.objsize);	//Ϊ���ݿ��ٻ����ַ
			if(databuf==0) 
			{
				res=f_open(fp,(const TCHAR*)name,FA_CREATE_ALWAYS|FA_WRITE);//���ļ��� д�ļ�
				
				//2026-05-26 (���� 17): ��������ֶ�, �ð��� NAND Flash (2:/, FatFs �� 2, ���� SD ��) Ҳ�����������/����/������־/�豸״̬�ȡ�
		//   �� 15 �ֶα���˳�򲻱�(�����ݾ� cfg), �����ֶ�׷����ĩβ:
		//   All_Lap / LAll_Lap / RAll_Lap = �������������� + ���Ҵ������ (����)
		//   RelayBit                       = ������Ŀ��־
		//   Open_State                     = ȫ��TP/SB/MB����״̬(0=���¼���ת, 1=ȫ��)
		//   Laps_No                        = ������������(������+1/-1��ť)
		sprintf((char*)databuf,"%d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d \n\0",
			StartFinalPlace,StartPlace,FinalPlace,Close_Time,SwimmingPool_Arrage,tport,
			Result_Display_Time,TP_DelayCloseValue,Relay_SB_DelayCloseValue,MBdelay_Time,
			Pool50mOr25mbit,PoolSingleOrDoubleTPbit,All_Close_Time,Left_MB_Num,Right_MB_Num,
			All_Lap,LAll_Lap,RAll_Lap,RelayBit,Open_State,Laps_No,StartBox_Edge_Bit,FalseStartThreshold);
				
				res=f_write(fp,databuf,strlen((char*)databuf),(UINT*)&bw);//д���ļ�
				
				if(res)
				{
					printf("write error:%d\r\n",res);
				}
				f_close(fp);

			}	
			else 
			{
				res=f_read(fp,databuf,fp->obj.objsize,(UINT*)&br);	//һ�ζ�ȡ�����ļ�
				//2026-05-26 (���� 17 ����): ��ȡ�����ֶ�, �����ݾ� cfg(15 �ֶ�) �� sscanf �������ֶα���ȫ��Ĭ��
				//2026-05-26 �� #181-D ���Ͳ�ƥ�� bug: sscanf %d ���� int*, ��ȫ�ֱ����� u8/u16,
				//   ֱ�Ӵ� &u16 ���� sscanf д 4 �ֽ�, �ƻ������ڴ档���� int ��ʱ�������, �� cast ���ء�
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
				//2026-05-26 �����Լ�� (�� cfg �쳣���ֶ�ȱʧʱ, �ú���Ĭ��ֵ����, ���⿪�����ֶ�Ϊ 0):
				if(Close_Time            == 0) Close_Time            = SW_50M_Close_Time;
				if(All_Close_Time        == 0) All_Close_Time        = 400;
				if(Result_Display_Time   == 0) Result_Display_Time   = Result_Display_Time_Value;
				if(TP_DelayCloseValue    == 0) TP_DelayCloseValue    = 40;
				if(Relay_SB_DelayCloseValue == 0) Relay_SB_DelayCloseValue = 30;
				if(MBdelay_Time          == 0) MBdelay_Time          = 50;
				if(Left_MB_Num           >  3) Left_MB_Num           = 2;
				if(Right_MB_Num          >  3) Right_MB_Num          = 1;
				if(tport                 == 0) tport                 = 8088;
				if(All_Lap               == 0) All_Lap               = laps_No_tbl[Laps_No];
				if(LAll_Lap + RAll_Lap == 0){ LAll_Lap = Llaps_No_tbl[Laps_No]; RAll_Lap = Rlaps_No_tbl[Laps_No]; }
				if(FalseStartThreshold   == 0) FalseStartThreshold   = 10;                        //������ֵĬ�� 0.1s
				f_close(fp);
			}
		}
	}
	//�ͷ��ڴ�
 	gui_memin_free(fp);
//	gui_memin_free(pname);  
	gui_memex_free(databuf);
//	databuf=0;				//����
	
}


void OnWriteMatchData()   	//�洢��������  2025-1-26
{
	FIL* fp=0;		//�洢�ļ�	

	u8 res;
	u8 rval=0;
//	u8 *pname=0; 
	u8 *pdatabuf;
	u8* databuf;	//
	const char *name="2:/swimtime.cfg";

  	
	fp=(FIL *)gui_memin_malloc(sizeof(FIL));			//����FIL�ֽڵ��ڴ�����  
//	pname=gui_memin_malloc(120);							//����120���ֽ��ڴ�,����"0:RECORDER/REC20120321210633.wav" 
	
//	if(!fp||!pname)rval=1;
	if(!fp)rval=1;
 	else
	{
		res=f_open(fp,(const TCHAR*)name,FA_CREATE_ALWAYS|FA_WRITE);//���ļ��� д�ļ�
					
		if(res)//�ļ�����ʧ��
		{
			rval=0XFE;//��ʾ�Ƿ����SD��
		}
		else 
		{
			databuf=(u8*)gui_memex_malloc(fp->obj.objsize);	//Ϊ���ݿ��ٻ����ַ
			//2026-05-26 (���� 17): ��������ֶ�, �ð��� NAND Flash (2:/, FatFs �� 2, ���� SD ��) Ҳ�����������/����/������־/�豸״̬�ȡ�
		//   �� 15 �ֶα���˳�򲻱�(�����ݾ� cfg), �����ֶ�׷����ĩβ:
		//   All_Lap / LAll_Lap / RAll_Lap = �������������� + ���Ҵ������ (����)
		//   RelayBit                       = ������Ŀ��־
		//   Open_State                     = ȫ��TP/SB/MB����״̬(0=���¼���ת, 1=ȫ��)
		//   Laps_No                        = ������������(������+1/-1��ť)
		sprintf((char*)databuf,"%d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d %d \n\0",
			StartFinalPlace,StartPlace,FinalPlace,Close_Time,SwimmingPool_Arrage,tport,
			Result_Display_Time,TP_DelayCloseValue,Relay_SB_DelayCloseValue,MBdelay_Time,
			Pool50mOr25mbit,PoolSingleOrDoubleTPbit,All_Close_Time,Left_MB_Num,Right_MB_Num,
			All_Lap,LAll_Lap,RAll_Lap,RelayBit,Open_State,Laps_No,StartBox_Edge_Bit,FalseStartThreshold);
				
 			res=f_write(fp,databuf,strlen((char*)databuf),(UINT*)&bw);//д���ļ�
				
			if(res)
			{
					printf("write error:%d\r\n",res);
			}
			f_close(fp);
		} 
	}
	//�ͷ��ڴ�
 	gui_memin_free(fp);
//	gui_memin_free(pname);  
	gui_memex_free(databuf);
//	databuf=0;				//����
}

//2026-05-26 �û�Ҫ��: �豸״̬���� (TP/SB/MB) �־û��������ļ� 2:/swimdev.cfg
//   ���� 100 �ֶ�: TP_Open_Close_State[10][2] + Startbox_Open_Close_State[10][2] + MB_Open_Close_State[3][20]
//   ״ֵ̬: 0=�ر�(����), 1=��(����), 3=��, 4=δ��װ��"ȫ��" = !=3 && !=4
//   ��ȡ: swim_play ����ʱ�� swimdev.cfg ������, �ֶα���ȫ��Ĭ�� (ͨ�� 0 = �ر�/���� = "ȫ��")
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
				databuf[fp->obj.objsize]=0;	//ȷ�� null ��β
				{
					char *_p = (char*)databuf;
					u8 _ti, _tj;
					//���� ���� TP_Open_Close_State[10][2] ����
					for(_ti=0; _ti<10; _ti++) for(_tj=0; _tj<2; _tj++){
						while(*_p == ' ' || *_p == '\t' || *_p == '\r' || *_p == '\n') _p++;
						if(!*_p) goto _eod;
						TP_Open_Close_State[_ti][_tj] = (u8)strtol(_p, &_p, 10);
					}
					//���� ���� Startbox_Open_Close_State[10][2] ����
					for(_ti=0; _ti<10; _ti++) for(_tj=0; _tj<2; _tj++){
						while(*_p == ' ' || *_p == '\t' || *_p == '\r' || *_p == '\n') _p++;
						if(!*_p) goto _eod;
						Startbox_Open_Close_State[_ti][_tj] = (u8)strtol(_p, &_p, 10);
					}
					//���� ���� MB_Open_Close_State[3][20] ����
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

//2026-05-26 �û�Ҫ��: �� TP/SB/MB �豸״̬����д�� 2:/swimdev.cfg
//   ������: case Set_TPSBMB_State (0x46) / case Set_PoolSingleOrDoubleTP (0x3A) ���޸��豸״̬�Ĵ���֮��
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
		//100 �ֶ� �� ~4 �ַ� �� 400 �ֽ�, ���� 1KB ����
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
