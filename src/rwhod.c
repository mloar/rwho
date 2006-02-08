/*
 *  Copyright (c) 2006 Association for Computing Machinery at the 
 *  University of Illinois at Urbana-Champaign.
 *  All rights reserved.
 * 
 *  Developed by: Matthew Loar
 *                ACM@UIUC
 *                http://www.acm.uiuc.edu
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a 
 *  copy of this software and associated documentation files (the "Software"),
 *  to deal with the Software without restriction, including without limitation
 *  the rights to use, copy, modify, merge, publish, distribute, sublicense,
 *  and/or sell copies of the Software, and to permit persons to whom the
 *  Software is furnished to do so, subject to the following conditions:
 *
 *  Redistributions of source code must retain the above copyright notice, this
 *  list of conditions and the following disclaimers.
 *  Redistributions in binary form must reproduce the above copyright notice,
 *  this list of conditions and the following disclaimers in the documentation
 *  and/or other materials provided with the distribution.
 *  Neither the names of SIGWin, ACM@UIUC, nor the names of its contributors
 *  may be used to endorse or promote products derived from this Software
 *  without specific prior written permission. 
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *  CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 *  FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 *  DEALINGS WITH THE SOFTWARE.
 */

#include <time.h>

#define UNIX_ZERO_TICKS 116444736000000000 

#define UNICODE
#define _WIN32_WINNT 0x0510
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <ntsecapi.h>
#include <winsock2.h>
#include <wtsapi32.h>

struct outmp
{
  char out_line[8];
  char out_name[8];
  int out_time;
};

struct whod
{
  char wd_vers;
  char wd_type;
  char wd_pad[2];
  int wd_sendtime;
  int wd_recvtime;
  char wd_hostname[32];
  int wd_loadav[3];
  int wd_boottime;
  struct whoent
  {
    struct outmp we_utmp;
    int we_idle;
  } wd_we[64];
};

SOCKET sock = INVALID_SOCKET;
SERVICE_STATUS_HANDLE servicehandle = NULL;
HANDLE eventlog = NULL;
HANDLE worker = NULL;
HANDLE timer = NULL;
HANDLE exitevent = NULL;
HANDLE readevent = NULL;

SERVICE_STATUS stat;

VOID Cleanup()
{
      stat.dwCurrentState = SERVICE_STOP_PENDING;
      SetServiceStatus(servicehandle, &stat);
      if(exitevent != NULL)
      {
        SetEvent(exitevent);
        if(worker != NULL)
        {
          /*if(WaitForSingleObject(worker, 2000) == WAIT_TIMEOUT)
          {
            TerminateThread(worker, -1);
          }*/
          //GetExitCodeThread(worker, &stat.dwWin32ExitCode);
          CloseHandle(worker);
          worker = NULL;
        }
        CloseHandle(exitevent);
        exitevent = NULL;
      }
      if(worker != NULL)
      {
        TerminateThread(worker, -1);
        CloseHandle(worker);
        worker = NULL;
      }
      if(eventlog != NULL)
      {
        DeregisterEventSource(eventlog);
        eventlog = NULL;
      }
      if(sock != INVALID_SOCKET)
      {
        closesocket(sock);
        sock = INVALID_SOCKET;
      }
      stat.dwCurrentState = SERVICE_STOPPED;
      SetServiceStatus(servicehandle, &stat);
      servicehandle = NULL;
}

VOID WINAPI rwhodHandler(DWORD fdwControl)
{
  switch(fdwControl)
  {
    case SERVICE_CONTROL_PAUSE:
      stat.dwCurrentState = SERVICE_PAUSED;
      SetServiceStatus(servicehandle, &stat);
      break;
    case SERVICE_CONTROL_CONTINUE:
      stat.dwCurrentState = SERVICE_RUNNING;
      SetServiceStatus(servicehandle, &stat);
      break;
    case SERVICE_CONTROL_STOP:
      Cleanup();
      break;
  }
}

VOID Error(int data)
{
  ReportEvent(eventlog, EVENTLOG_ERROR_TYPE, 0, 0, NULL, 0, 4, NULL, &data);
  Cleanup();
}

void SendData()
{
  struct whod block;
  struct sockaddr_in addr;
  time_t tim;
  PWTS_SESSION_INFO sessions;
  DWORD users = 0L;
  DWORD k = 0L;
  int i = 0L;
  DWORD console = 0L;

  time(&tim);

  block.wd_vers = 1;
  block.wd_type = 1;
  block.wd_sendtime = htonl(tim);
  block.wd_boottime = htonl(tim - GetTickCount()/1000);
  gethostname(block.wd_hostname, 32);

  console = WTSGetActiveConsoleSessionId();
  if(WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, &sessions, &users))
  {
    DWORD logoncount = 0;
    PLUID logons;
    
    k = users;
    
    LsaEnumerateLogonSessions(&logoncount, &logons);
    for(i = 0; (i < users) && (i < 64); i++)
    {
      LPTSTR username;
      DWORD userlen;
      WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE,
        sessions[i].SessionId, WTSUserName, &username, &userlen);
      if(sessions[i].State != WTSActive)
      {
        k--;
      }
      else
      {
        int j = 0;
        LARGE_INTEGER logtime = {0, 0};
        if(sessions[i].SessionId == console)
        {
          _snprintf(block.wd_we[i].we_utmp.out_line, 8, "console");
        }
        else
        {
          _snprintf(block.wd_we[i].we_utmp.out_line, 8, "rdp/%d",
            sessions[i].SessionId);
        }
        
        for(j = 0; j < logoncount; j++)
        {
          SECURITY_LOGON_SESSION_DATA* secdat;
          LsaGetLogonSessionData(&logons[j], &secdat);
          if(secdat->Session == sessions[i].SessionId
              && (secdat->LogonType == Interactive
                || secdat->LogonType == RemoteInteractive)
              && secdat->LogonTime.QuadPart > logtime.QuadPart)
          {
            logtime.QuadPart = secdat->LogonTime.QuadPart;
          }
          LsaFreeReturnBuffer(secdat);
        }
        _snprintf(block.wd_we[i].we_utmp.out_name, 8, "%S", username);
        block.wd_we[i].we_utmp.out_time = htonl(
          (logtime.QuadPart - UNIX_ZERO_TICKS) / 10000000L);
      }
      WTSFreeMemory(username);
    }
    LsaFreeReturnBuffer(logons);
    WTSFreeMemory(sessions);
  }

  addr.sin_family = AF_INET;
  addr.sin_port = htons(513);
  addr.sin_addr.s_addr = INADDR_BROADCAST;
  if(sendto(sock, (char*)&block, 60L + k * 24L, 0, (SOCKADDR*) &addr,
    sizeof(struct sockaddr_in)) == SOCKET_ERROR)
  {
    Error(5);
  }
}

DWORD WINAPI rwhodThread(LPVOID notused)
{
  HANDLE handles[3];
  LARGE_INTEGER tim = {0L, 0L};
  char readbuffer[1024];
  BOOL keepgoing = TRUE;

  if((timer = CreateWaitableTimer(NULL, FALSE, NULL)) == NULL)
  {
    Error(0x11);
    return 1;
  }
  if(SetWaitableTimer(timer, &tim, 180000, NULL, NULL, FALSE) == 0)
  {
    Error(0x12);
    return 2;
  }
  if((exitevent = CreateEvent(NULL, TRUE, FALSE, NULL)) == NULL)
  {
    Error(0x13);
    return 3;
  }
  if((readevent = CreateEvent(NULL, FALSE, FALSE, NULL)) == NULL)
  {
    Error(0x14);
    return 4;
  }
  if(WSAEventSelect(sock, (WSAEVENT)readevent, FD_READ) != 0)
  {
    Error(0x15);
    return 5;
  }

  handles[0] = timer;
  handles[1] = readevent;
  handles[2] = exitevent;

  while(keepgoing)
  {
    DWORD signal = WaitForMultipleObjects(3, handles, FALSE, INFINITE) 
      - WAIT_OBJECT_0;
    switch(signal)
    {
      case 0:
        SendData();
        break;
      case 1:
        recv(sock, readbuffer, 1024, 0);
        break;
      case 2:
        keepgoing = FALSE;
        break;
      default:
        keepgoing = FALSE;
        Error(0x16);
        break;
    }
  }
  return 0;
}

VOID WINAPI rwhodMain(DWORD dwArgc, LPTSTR* lpsvArgv)
{
  BOOL opt = TRUE;
  WSADATA wsaData;
  struct sockaddr_in addr;

  servicehandle = RegisterServiceCtrlHandler(L"rwhod", &rwhodHandler);
  stat.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
  stat.dwControlsAccepted = SERVICE_ACCEPT_PAUSE_CONTINUE | SERVICE_ACCEPT_STOP;
  stat.dwWin32ExitCode = NO_ERROR;
  stat.dwCheckPoint = 0;
  stat.dwWaitHint = 0;
  
  eventlog = RegisterEventSource(NULL, L"rwhod");

  if(WSAStartup(MAKEWORD(1,1), &wsaData) != 0)
  {
    Error(0);
    return;
  }

  sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
  if(sock == INVALID_SOCKET)
  {
    Error(1);
    return;
  }

  if(setsockopt(sock, SOL_SOCKET, SO_BROADCAST, (char*) &opt, sizeof(BOOL)))
  {
    Error(2);
    return;
  }
  
  addr.sin_family = AF_INET;
  addr.sin_port = htons(513);
  addr.sin_addr.s_addr = INADDR_ANY;
  if(bind(sock, (SOCKADDR*) &addr, sizeof(addr)) != 0)
  {
    Error(3);
    return;
  }

  if((worker = CreateThread(NULL, 0, &rwhodThread, NULL, 0, NULL)) == NULL)
  {
    Error(4);
    return;
  }
  stat.dwCurrentState = SERVICE_RUNNING;
  SetServiceStatus(servicehandle, &stat);
}

SERVICE_TABLE_ENTRY Services[2]={L"rwhod",&rwhodMain,NULL,NULL};
int main(int argc, char* argv[])
{
  StartServiceCtrlDispatcher(Services);
  return 0;
}
