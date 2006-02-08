/*
 *	Copyright (c) 2005 Association for Computing Machinery at the University of Illinois at Urbana-Champaign.
 *  All rights reserved.
 * 
 *	Developed by: 		Matthew Loar
 *						ACM@UIUC
 *						http://www.acm.uiuc.edu
 *
 *	Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal with the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 *
 *	* Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimers.
 *	* Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimers in the documentation and/or other materials provided with the distribution.
 *	* Neither the names of Matthew Loar, ACM@UIUC, nor the names of its contributors may be used to endorse or promote products derived from this Software without specific prior written permission. 
 *
 *	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS WITH THE SOFTWARE.
 */

using System;
using System.Runtime.InteropServices;

namespace ACM.Sys.Windows.TerminalServices
{
	public struct SessionInfo
	{
		public int SessionID;
		public string WinStationName;
		public int State;
	}

	/// <summary>
	/// Summary description for WTS.
	/// </summary>
	public unsafe class SessionManager
	{
		[DllImport("wtsapi32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int WTSEnumerateSessionsW(
			IntPtr hServer,
			Int32 Reserved,
			Int32 Version,
			out IntPtr SessionInfo,
			Int32* Count
			);

		[DllImport("wtsapi32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int WTSFreeMemory(
			IntPtr pMemory
			);

		[DllImport("wtsapi32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int WTSQuerySessionInformationW(
			IntPtr hServer,
			Int32 SessionId,
			Int32 WTSInfoClass,
			out byte* ppBuffer,
			Int32* pBytesReturned
			);

		[DllImport("kernel32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int WTSGetActiveConsoleSessionId();

		[DllImport("secur32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int LsaEnumerateLogonSessions(
			Int32* LogonSessionCount,
			out IntPtr LogonSessionList
			);

		[DllImport("secur32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int LsaGetLogonSessionData(
			IntPtr LogonId,
			out IntPtr ppLogonSessionData
			);

		[DllImport("secur32.dll", CallingConvention=CallingConvention.StdCall)]
		private static extern int LsaFreeReturnBuffer(
			IntPtr pBuffer
			);
		
		public static SessionInfo[] EnumerateSessions()
		{
			IntPtr sessions;
			Int32 length;

			WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out sessions, &length);

			SessionInfo[] ret = new SessionInfo[length];
			for(int i = 0; i < length; i++)
			{
				IntPtr session = (IntPtr)((Int32)sessions + i * 12);
				ret[i].SessionID = *(Int32*)((Int32)sessions + i * 12);
				ret[i].WinStationName = Marshal.PtrToStringUni((IntPtr)((Int32)session+4));
				ret[i].State = *(Int32*)((Int32)session+8);
			}

			WTSFreeMemory((IntPtr)sessions);

			return ret;
		}

		public static string GetSessionUserName(int SessionID)
		{
			byte* user;
			Int32 userlen;
			WTSQuerySessionInformationW(IntPtr.Zero, SessionID, 5 /*WTSUserName*/, out user, &userlen);

			string ret = Marshal.PtrToStringUni((IntPtr)user);

			WTSFreeMemory((IntPtr)user);

			return ret;
		}

		public static string GetSessionClientName(int SessionID)
		{
			byte* user;
			Int32 userlen;
			WTSQuerySessionInformationW(IntPtr.Zero, SessionID, 10 /*WTSClientName*/, out user, &userlen);

			string ret = Marshal.PtrToStringUni((IntPtr)user);

			WTSFreeMemory((IntPtr)user);

			return ret;
		}

		public static bool IsSessionConnected(int SessionID)
		{
			byte* user;
			Int32 userlen;
			WTSQuerySessionInformationW(IntPtr.Zero, SessionID, 8 /*WTSConnectState*/, out user, &userlen);


			bool ret;
			if(*user == 0 || *user == 1)
				ret = true;
			else
				ret = false;

			WTSFreeMemory((IntPtr)user);

			return ret;
		}

		public static int GetActiveConsoleSessionID()
		{
			return WTSGetActiveConsoleSessionId();
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct session_data
		{
			Int32 size;
			Int64 luid;
			Int64 username;
			Int64 logondomain;
			Int64 authentication_package;
			public Int32 logontype;
			public Int32 session;
			Int32 sid;
			public Int64 LogonTime;
			Int64 LogonServer;
			Int64 DnsDomainName;
			Int64 upn;
		}


		public static Int64 GetSessionLogonTime(int SessionId)
		{
			IntPtr sessions;
			Int32 count;
			Int64 ret;
			
			if(LsaEnumerateLogonSessions(&count,out sessions) != 0x00000000L)
			{
				return -1;
			}

			// Process the array of session LUIDs...
			ret = 0;
			for (long i = count - 1;i >= 0; i--) 
			{
				IntPtr pData;
				if(LsaGetLogonSessionData((IntPtr)((Int32)sessions + i * 8), out pData) == 0x00000000L)
				{
					Int32 LogonType = *(Int32*)((Int32)pData + 36);
					Int32 Session = *(Int32*)((Int32)pData + 40);
					Int64 LogonTime = *(Int64*)((Int32)pData + 48);
					if((LogonType == 2 || LogonType == 10) && Session == SessionId && LogonTime > ret)
					{
						ret = LogonTime;
					}
					LsaFreeReturnBuffer(pData);
				}
			}

			// Free the array of session LUIDs allocated by the LSA.
			LsaFreeReturnBuffer(sessions);

			return ret;
		}

	}
}
