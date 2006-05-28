/*
 *  Copyright (c) 2005-2006 Association for Computing Machinery at the University of Illinois at Urbana-Champaign.
 *  All rights reserved.
 * 
 *  Developed by:     Matthew Loar
 *            ACM@UIUC
 *            http://www.acm.uiuc.edu
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
 *  documentation files (the "Software"), to deal with the Software without restriction, including without limitation 
 *  the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
 *  permit persons to whom the Software is furnished to do so, subject to the following conditions:
 *
 *  * Redistributions of source code must retain the above copyright notice, this list of conditions and the following 
 *    disclaimers.
 *  * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the 
 *    following disclaimers in the documentation and/or other materials provided with the distribution.
 *  * Neither the names of Matthew Loar, ACM@UIUC, nor the names of its contributors may be used to endorse or promote 
 *    products derived from this Software without specific prior written permission. 
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO 
 *  THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 *  CONTRIBUTORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
 *  CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS 
 *  WITH THE SOFTWARE.
 */
using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Xml;
using ACM.Sys.Windows.TerminalServices;
using Microsoft.Win32;

namespace ACM.rwho
{
  public class Daemon : System.ServiceProcess.ServiceBase
  {
    const long UNIX_ZERO_TICKS = 621355968000000000L;

    const string RUPTIME_USAGE = @"
Usage: ruptime [/alrut]
  /a  Include long-idle users in user counts
    
  /l  Sort by load averages
  /t  Sort by uptime
  /u  Sort by user count
    
  /r  Reverse sort order
  ";

    const string RWHO_USAGE = @"
Usage: rwho [/a]
  /a  Print entries from inactive hosts.

Usage: rwho install | uninstall
  Install or uninstall the rwhod service.

You can set a prefix to be trimmed from the outgoing hostname
 in HKEY_LOCAL_MACHINE\\Software\\ACM\\rwho\\Prefix.

You can set HKEY_LOCAL_MACHINE\\Software\\ACM\\rwho\\ForceCase
 (DWORD value) to 1 or 2 to force the outgoing hostname to lower
 or upper case, respectively.
    
You can set HKEY_LOCAL_MACHINE\\Software\\ACM\\rwho\\DisableSend
 (DWORD value) to 1 to run rwho in client-only mode.

You must restart the service after changing these settings for
 them to take effect.
";
    [StructLayout(LayoutKind.Sequential)]
    class outmp
    {
      public byte[] out_line = new byte[8];
      public byte[] out_name = new byte[8];
      public int out_time;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    class whod
    {
      public byte wd_vers;
      public byte wd_type;
      public byte[] wd_pad = new byte[2];
      public int wd_sendtime;
      public int wd_recvtime;
      public byte[] wd_hostname = new byte[32];
      public int[] wd_loadav = new int[3];
      public int wd_boottime;
      public class whoent
      {
        public outmp we_utmp = new outmp();
        public int we_idle;
      }
      public whoent[] wd_we = new whoent[64];
    }
    
    Socket sock;
    Timer timSend;
    byte[] buffer;

    Timer timPerf;
    PerformanceCounter pc;
    Queue perfData;

    ASCIIEncoding enc;
    
    XmlDocument cache;
    XmlTextWriter writer;
    
    bool initialized;
    bool exit;

    string prefix;
    int forcecase;
    bool disablesend;

    // The main entry point for the process
    [STAThread]
    static void Main(string[] args)
    {
      Daemon rd = new Daemon();
      if(rd.GetType().Assembly.Location.EndsWith("ruptime.exe"))
      {
        if( args.Length > 0 && (args[0] == "/?" || args[0] == "--help"))
        {
          Console.Write(RUPTIME_USAGE);
          return;
        }
        
        bool all = false;
        string sort = "f";
        foreach(string arg in args)
        {
          string sortstart = sort;
          if(arg.IndexOf("a") > -1)
            all = true;
          if(arg.IndexOf("r") > -1)
            sort = sort.Replace("f","r");
          if(arg.IndexOf("l") > -1)
            sort += "l";
          else if(arg.IndexOf("u") > -1)
            sort += "u";
          else if(arg.IndexOf("t") > -1)
            sort += "t";
          if((sort == sortstart && all == false) || sort.Length > 2)
          {
            Console.WriteLine("Invalid option - " + arg);
            Console.WriteLine("");
            Console.Write(RUPTIME_USAGE);
            return;
          }
        }
        if(sort.Length < 2)
          sort += "h";

        int timestamp = (int)((DateTime.UtcNow.Ticks - UNIX_ZERO_TICKS)/TimeSpan.TicksPerSecond);

        try
        {
          XmlDocument cache = new XmlDocument();
          string path = System.Environment.SystemDirectory;
          path = path.Remove(path.LastIndexOf('\\'),path.Length-path.LastIndexOf('\\'));
          System.IO.FileStream st = new System.IO.FileStream(path + "\\TEMP\\rwhocache.xml",System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite);
          XmlTextReader reader = new XmlTextReader(st);
        
          cache.Load(reader);

          int i = 0;
          string[] lines = new string[cache.FirstChild.ChildNodes.Count];
          string[] keys = new string[cache.FirstChild.ChildNodes.Count];
          foreach(XmlElement node in cache.FirstChild.ChildNodes)
          {
            int downtime = timestamp - int.Parse(node.GetAttribute("recvtime"));
            if(downtime < 11 * 60)
            {
              int uptime = int.Parse(node.GetAttribute("uptime"));
              TimeSpan ts = new TimeSpan(((long)(timestamp - uptime)) * TimeSpan.TicksPerSecond);

              lines[i] = node.GetAttribute("hostname").PadRight(12) + "  up" + string.Format("{0}+{1:00}:{2:00}",ts.Days,ts.Hours,ts.Minutes).PadLeft(12);

              int j = 0;
              int users = 0;
              foreach(XmlElement user in node.ChildNodes)
              {

                TimeSpan idle = new TimeSpan(Int64.Parse(user.GetAttribute("idle")) * 10000000);
                if(all || (idle.Ticks < 36000000000))
                  users++;
                j++;
              }
              lines[i] += ",\t" + (users.ToString() + " users, ").PadRight(10);
              lines[i] +=  string.Format("load {0:0.00}, {1:0.00}, {2:0.00}",double.Parse(node.GetAttribute("loadav0"))/100,double.Parse(node.GetAttribute("loadav1"))/100,double.Parse(node.GetAttribute("loadav2"))/100);

              if(sort[1] == 'l')
                keys[i] = string.Format("load {0:0.00}, {1:0.00}, {2:0.00}",double.Parse(node.GetAttribute("loadav0"))/100,double.Parse(node.GetAttribute("loadav1"))/100,double.Parse(node.GetAttribute("loadav2"))/100);
              else if(sort[1] == 'u')
                keys[i] = users.ToString();
              else if(sort[1] == 't')
                keys[i] = string.Format("{0}+{1:00}:{2:00}",ts.Days,ts.Hours,ts.Minutes).PadLeft(12);
              else
                keys[i] = node.GetAttribute("hostname");

              i++;
            }
            else if(downtime < 4 * 24 * 60 * 60)
            {
              TimeSpan ts = new TimeSpan((timestamp - int.Parse(node.GetAttribute("recvtime"))) * TimeSpan.TicksPerSecond);
              lines[i] = node.GetAttribute("hostname").PadRight(12) + "down" + string.Format("{0}+{1:00}:{2:00}",ts.Days,ts.Hours,ts.Minutes).PadLeft(12);

              if(sort[1] == 'l')
                keys[i] = string.Format("load {0:0.00}, {1:0.00}, {2:0.00}",-1L,-1L,-1L);
              else if(sort[1] == 'u')
                keys[i] = string.Format("{0}",-1L);
              else if(sort[1] == 't')
                keys[i] = string.Format("{0}+{1:00}:{2:00}",-1L,-1L,-1L).PadLeft(12);
              else
                keys[i] = node.GetAttribute("hostname");

              i++;
            }
          }
          string[] alllines = new string[i];
          string[] allkeys = new string[i];
          Array.Copy(lines,alllines,i);
          Array.Copy(keys,allkeys,i);
          Array.Sort(allkeys,alllines);
          if(sort[0] != 'r' && sort != "fh" || sort == "rh")
            Array.Reverse(alllines);
          Console.Write(string.Join("\r\n",alllines).Trim() + "\r\n");
        }
        catch(System.IO.FileNotFoundException)
        {
          Console.WriteLine("Data cache missing.  Is the rwho service installed/started?");
          Console.WriteLine("Try running 'rwho install'.");
        }
        catch(System.Xml.XmlException)
        {
          Console.WriteLine("Corrupt data cache.");
        }
        catch(System.Security.SecurityException)
        {
          Console.WriteLine("You do not have read access to the rwho data cache.");
        }
        catch(System.Exception e)
        {
          Console.WriteLine("Could not access the data cache: " + e.Message);
        }

        return;
      }                
      if(args.Length > 0 && (args[0] == "service" || args[0] == "install" || args[0] == "uninstall" || args[0] == "isdrivingcar"))
      {
        string loc = rd.GetType().Assembly.Location;
        string maxver = "";
        
        foreach(string dir in System.IO.Directory.GetDirectories(System.Environment.SystemDirectory.Replace("\\system32","") + "\\Microsoft.NET\\Framework","v?.?.?????"))
        {
          if(maxver.Length == 0 || (maxver.CompareTo(dir) < 0))
            maxver = dir;
        }
        if(args[0] == "service")
        {
          System.ServiceProcess.ServiceBase[] ServicesToRun;
          ServicesToRun = new System.ServiceProcess.ServiceBase[] { new Daemon() };
          System.ServiceProcess.ServiceBase.Run(ServicesToRun);
        }
        else if(args[0] == "install")
        {
          Process proc = Process.Start(maxver + "\\installutil.exe", "/LogFile=\"\" \"" + loc + "\"");
          if(proc != null)
          {
            proc.WaitForExit();
            if(proc.ExitCode != 0)
            {
              Console.WriteLine(string.Format("InstallUtil returned an error code:{0}  The installation may have failed.",proc.ExitCode));
              Console.WriteLine("Do you have administrative privileges?");
            }
            proc.Close();
          }
        }
        else if(args[0] == "uninstall")
        {
          Process proc = Process.Start(maxver + "\\installutil.exe","/u /LogFile=\"\" " + "\"" + loc + "\"");
          if(proc != null)
          {
            proc.WaitForExit();
            if(proc.ExitCode != 0)
            {
              Console.WriteLine(string.Format("InstallUtil returned an error code:{0}  The uninstallation may have failed.",proc.ExitCode));
              Console.WriteLine("Do you have administrative privileges?");
            }
            proc.Close();
          }
        }
        else
        {
          Console.WriteLine("rwho for Windows v" + rd.GetType().Assembly.GetName().Version.ToString());
          Console.WriteLine("Copyright (C) 2005-2006 ACM@UIUC.  Copying permitted under the terms of");
          Console.WriteLine("the University of Illinois-NCSA Open Source License.");
          Console.WriteLine("");
          Console.WriteLine("Written by Matthew Loar, the awesomest C# hacker EVAR!");
        }
      }
      else
      {
        bool all = false;
        if(args.Length > 0)
        {
          if(args[0] == "-a")
            all = true;
          else if(args[0] == "/a")
            all = true;
          else
          {
            Console.WriteLine("Invalid option - " + args[0]);
            Console.WriteLine("");
            Console.Write(RWHO_USAGE);
            return;
          }
        }

        
        int timestamp = (int)((DateTime.UtcNow.Ticks - UNIX_ZERO_TICKS)/TimeSpan.TicksPerSecond);

        try
        {
          XmlDocument cache = new XmlDocument();
          string path = System.Environment.SystemDirectory;
          path = path.Remove(path.LastIndexOf('\\'),path.Length-path.LastIndexOf('\\'));
          System.IO.FileStream st = new System.IO.FileStream(path + "\\TEMP\\rwhocache.xml",System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.ReadWrite);
          XmlTextReader reader = new XmlTextReader(st);
          cache.Load(reader);

          string[] alllines = new string[0];
          foreach(XmlElement node in cache.DocumentElement.ChildNodes)
          {
            // Drop records older than 11 minutes
            if(timestamp - int.Parse(node.GetAttribute("recvtime")) < 660)
            {
              int i = 0;
              string[] lines = new string[node.ChildNodes.Count];
              foreach(XmlElement user in node.ChildNodes)
              {
                TimeSpan idle = new TimeSpan(Int64.Parse(user.GetAttribute("idle")) * 10000000);
                if(all || (idle.Ticks > 0 && idle.Ticks < 36000000000))
                  lines[i] = String.Format("{0}\t{1}\t{2}\t{3}",user.GetAttribute("name").PadRight(8),(node.GetAttribute("hostname") + ":"+ user.GetAttribute("line")).PadRight(16),new DateTime(Int64.Parse(user.GetAttribute("time")) * 10000000 + UNIX_ZERO_TICKS).ToLocalTime().ToString("MMM dd HH:mm"), (idle.Ticks>0)?String.Format("{0:00}:{1:00}",Math.Floor(idle.TotalHours),idle.Minutes):"");
                else if(Int64.Parse(user.GetAttribute("idle")) == 0)
                  lines[i] = String.Format("{0}\t{1}\t{2}",user.GetAttribute("name").PadRight(8),(node.GetAttribute("hostname") + ":"+ user.GetAttribute("line")).PadRight(16),new DateTime(Int64.Parse(user.GetAttribute("time")) * 10000000 + UNIX_ZERO_TICKS).ToLocalTime().ToString("MMM dd HH:mm"));
                i++;
              }
              
              string[] temp = new string[lines.Length + alllines.Length];
              Array.Copy(alllines,0,temp,0,alllines.Length);
              Array.Copy(lines,0,temp,alllines.Length,lines.Length);
              alllines = temp;
            }
          }
          Array.Sort(alllines);
          foreach(string line in alllines)
          {
            if(line != null && line.Length != 0)
              Console.WriteLine(line);
          }
        }
        catch(System.IO.FileNotFoundException)
        {
          Console.WriteLine("Data cache missing.  Is the rwho service installed/started?");
          Console.WriteLine("Try running 'rwho install'.");
        }
        catch(System.Xml.XmlException)
        {
          Console.WriteLine("Corrupt data cache.");
        }
        catch(System.Security.SecurityException)
        {
          Console.WriteLine("You do not have read access to the rwho data cache.");
        }
        catch(System.Exception e)
        {
          Console.WriteLine("Could not access data cache: " + e.Message);
        }
      }
    }

    /// <summary>
    /// Set things in motion so your service can do its work.
    /// </summary>
    protected override void OnStart(string[] args)
    {
      try
      {
        initialized = false;
        exit = false;

        sock = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
        sock.Bind(new IPEndPoint(IPAddress.Any,513));
        sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
        
        buffer = new byte[1024];
        sock.BeginReceive(buffer,0,buffer.Length,SocketFlags.None,new AsyncCallback(RecvData),null);
        
        Thread t = new Thread(new ThreadStart(Initialize));
        t.Start();
      }
      catch(Exception ex)
      {
        EventLog.WriteEntry("rwhod",ex.Message + ex.StackTrace,EventLogEntryType.Error);
      }
    }
 
    /// <summary>
    /// Stop this service.
    /// </summary>
    protected override void OnStop()
    {
      exit = true;
      if(timSend != null)
      {
        timSend.Dispose();
        timSend = null;
      }
      if(timPerf != null)
      {
        timPerf.Dispose();
        timPerf = null;
      }
    }

    void Initialize()
    {
      try
      {
        cache = new XmlDocument();
        XmlElement root = cache.CreateElement("cache");
        cache.AppendChild(root);

        string path = System.Environment.SystemDirectory;
        path = path.Remove(path.LastIndexOf('\\'),path.Length-path.LastIndexOf('\\'));
        System.IO.FileStream st = new System.IO.FileStream(path + "\\TEMP\\rwhocache.xml",System.IO.FileMode.OpenOrCreate,System.IO.FileAccess.Write,System.IO.FileShare.Read);
        writer = new XmlTextWriter(st,Encoding.ASCII);
          
        RegistryKey rk = Registry.LocalMachine.OpenSubKey("SOFTWARE\\ACM\\rwho");
        if(rk != null)
        {
          object obj;
          obj = rk.GetValue("Prefix","");
          if(obj.GetType().ToString() != "System.String")
          {
            EventLog.WriteEntry("rwhod","Registry value 'Prefix' is not REG_SZ - ignored.",EventLogEntryType.Warning);
            prefix = "";
          }
          else
          {
            prefix = (string)obj;
          }

          obj = rk.GetValue("ForceCase",0);
          if(obj.GetType().ToString() != "System.Int32")
          {
            EventLog.WriteEntry("rwhod","Registry value 'ForceCase' is not REG_DWORD - ignored.",EventLogEntryType.Warning);
            forcecase = 0;
          }
          else
          {
            forcecase = (int)obj;
          }

          obj = rk.GetValue("DisableSend",0);
          if(obj.GetType().ToString() != "System.Int32")
          {
            EventLog.WriteEntry("rwhod","Registry value 'DisableSend' is not REG_DWORD - ignored.",EventLogEntryType.Warning);
            disablesend = false;
          }
          else
          {
            disablesend = (int)obj != 0;
          }

          rk.Close();
          
        }
        else
        {
          prefix = "";
          forcecase = 0;
          disablesend = false;
        }
      

        pc = new PerformanceCounter("Processor","% Processor Time","_Total");
        perfData = Queue.Synchronized(new Queue());

        enc = new ASCIIEncoding();
      
        whod d = new whod();
        if(!disablesend)
        {
          timSend = new Timer(new TimerCallback(SendData),d,0,180000);
          timPerf = new Timer(new TimerCallback(GetPerfData),null,1000,15000);
        }

        initialized = true;
      }
      catch(Exception e)
      {
        EventLog.WriteEntry("rwhod",e.Message + e.StackTrace, EventLogEntryType.Error);
      }
    }

    void RecvData(IAsyncResult ar)
    {
      if(initialized)
      {
        try
        {
          int timestamp = (int)((DateTime.UtcNow.Ticks - UNIX_ZERO_TICKS)/TimeSpan.TicksPerSecond);
          int bytes = sock.EndReceive(ar);

          if(bytes < 60)
            EventLog.WriteEntry("rwhod","Malformed packet received.",EventLogEntryType.Warning);
          else
          {
            byte[] hostname = new byte[32];
            Array.Copy(buffer,12,hostname,0,32);

            XmlElement node = (XmlElement)cache.FirstChild.SelectSingleNode("descendant::host[attribute::hostname='" + enc.GetString(hostname).Split('\0')[0] + "']");
            if(node == null)
            {
              node = cache.CreateElement("host");
              cache.FirstChild.AppendChild(node);
            }
            node.RemoveAll();

            node.SetAttribute("hostname",enc.GetString(hostname).Split('\0')[0]);
            node.SetAttribute("uptime",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,56))));
            node.SetAttribute("sendtime",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,4))));
            node.SetAttribute("recvtime",string.Format("{0}",(int)timestamp));
            node.SetAttribute("loadav0",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,44))));
            node.SetAttribute("loadav1",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,48))));
            node.SetAttribute("loadav2",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,52))));

            // Declare these up here to avoid allocating memory in every loop iteration
            byte[] line = new byte[8];
            byte[] name = new byte[8];

            if(bytes >= 84)
            {
              for(int i = 60; i < bytes; i+=24)
              {
                XmlElement usernode = cache.CreateElement("user");
                node.AppendChild(usernode);

                Array.Copy(buffer,i,line,0,8);
                Array.Copy(buffer,i+8,name,0,8);

                usernode.SetAttribute("name",enc.GetString(name).Split('\0')[0]);
                usernode.SetAttribute("line",enc.GetString(line).Split('\0')[0]);
                usernode.SetAttribute("time",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,i+16))));
                usernode.SetAttribute("idle",string.Format("{0}",(int)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(buffer,i+20))));
              }
            }

            writer.BaseStream.Seek(0,System.IO.SeekOrigin.Begin);
            writer.BaseStream.SetLength(0);
            cache.WriteTo(writer);
            writer.Flush();
            Console.Write("\r\n");
          }
        }
        catch(Exception e)
        {
          EventLog.WriteEntry("rwhod",e.Message + e.StackTrace,EventLogEntryType.Error);
        }
        if(exit)
        {
          sock.Close();
          writer.Close();
          writer = null;
          sock = null;
          buffer = null;
          return;
        }
      }
      sock.BeginReceive(buffer,0,buffer.Length,SocketFlags.None,new AsyncCallback(RecvData),ar.AsyncState);
    }
    void SendData(object state)
    {
      if(!initialized)
        return;
      try
      {     
        int timestamp = (int)((DateTime.UtcNow.Ticks - UNIX_ZERO_TICKS)/TimeSpan.TicksPerSecond);

        whod d = (whod)state;
        /*
         * The trinary operator in the next line is to correct MS stupidity
         * According to the docs, the tick count is stored as a signed 32-bit integer
         * and thus is reset to 0 every 24.9 days.  It explicitly states
         * that the minimum value is 0.  However, on 10/3/05, bobrife.acm.uiuc.edu,
         * up 26 days and running version v1.1 of the .NET Framework, returned a negative number.
         * Stupid, isn't it?  I wasted so much time looking for overflows in my code,
         * and it turns out not to be my code at all.  Lousy MS.  They're why I don't get any sleep.
         */
        d.wd_boottime = (int)((uint)timestamp - (System.Environment.TickCount>0?(uint)System.Environment.TickCount:(uint)int.MaxValue+(uint)(System.Environment.TickCount-int.MinValue)) / 1000);

        d.wd_hostname = new byte[32];
        string hostname = System.Environment.MachineName;
        if(hostname.StartsWith(prefix))
          hostname = hostname.Remove(0,prefix.Length);
        if(forcecase == 1)
          hostname = hostname.ToLower();
        else if(forcecase == 2)
          hostname = hostname.ToUpper();

        Array.Copy(enc.GetBytes(hostname),0,d.wd_hostname,0,enc.GetByteCount(hostname));
        
        d.wd_sendtime = (int)timestamp;
        d.wd_vers = 1;
        d.wd_type = 1;

        object[] perfArray = perfData.ToArray();
        if(perfArray.Length >= 20)
        {
          float sum5 = 0f;
          for(int z=perfArray.Length-20;z<perfArray.Length;z++)
          {
            sum5 += (float)perfArray[z];
          }
          d.wd_loadav[0] = (int)(sum5/20f);
          if(perfArray.Length >= 40)
          {
            float sum10 = sum5;
            for(int z=perfArray.Length-40;z<perfArray.Length-20;z++)
            {
              sum10 += (float)perfArray[z];
            }
            d.wd_loadav[1] = (int)(sum10/40f);
            if(perfArray.Length >= 60)
            {
              float sum15 = sum10;
              for(int z=0;z<perfArray.Length-40;z++)
              {
                sum15 += (float)perfArray[z];
              }
              d.wd_loadav[2] = (int)(sum15/60f);
            }
          }
        }
        
        int k=0; // User number in buffer
        
        SessionInfo[] sessions = SessionManager.EnumerateSessions();
        if(sessions.Length > 0)
        { 
          for(int x = 0;x < sessions.Length;x++)
          {
            string use = SessionManager.GetSessionUserName(sessions[x].SessionID);
            if(use.Length > 8)
              use = use.Substring(0,8);
            if(sessions[x].SessionID != 65536 && use.Length != 0) // Get rid of listener
            {
              d.wd_we[k] = new whod.whoent();
                            
              string cli;
              double tim=0;
              if(!SessionManager.IsSessionConnected(sessions[x].SessionID))
              {
                cli = "not on";
              }
              else
              {             
                if(sessions[x].SessionID == SessionManager.GetActiveConsoleSessionID())
                  cli = "console";
                else
                  cli = string.Format("rdp/{0}",sessions[x].SessionID);
              }
              DateTime logtime = DateTime.FromFileTimeUtc(SessionManager.GetSessionLogonTime(sessions[x].SessionID));
              tim = (logtime.Ticks - UNIX_ZERO_TICKS)/TimeSpan.TicksPerSecond;

              Array.Copy(enc.GetBytes(use),0,d.wd_we[k].we_utmp.out_name,0,enc.GetByteCount(use));
              Array.Copy(enc.GetBytes(cli),0,d.wd_we[k].we_utmp.out_line,0,enc.GetByteCount(cli));
              d.wd_we[k].we_utmp.out_time = (int)tim;
              k++;
            }
          }
        }

        byte[] buffy = new byte[60 + k * 24];
              
        buffy[0] = d.wd_vers;
        buffy[1] = d.wd_type;
        
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_sendtime)),0,buffy,4,4);
        Array.Copy(d.wd_hostname,0,buffy,12,32);
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_boottime)),0,buffy,56,4);
        // These next three ought to be cast to long (I think)
        // But it doesn't work if they are
        // I really ought to figure out what exactly is going on
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_loadav[0])),0,buffy,44,4);
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_loadav[1])),0,buffy,48,4);
        Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_loadav[2])),0,buffy,52,4);
        if(sessions.Length > 0)
        {
          int i = 60;
          for(int j = 0; j < k; j++)
          {
            Array.Copy(d.wd_we[j].we_utmp.out_line,0,buffy,i,8);
            Array.Copy(d.wd_we[j].we_utmp.out_name,0,buffy,i+8,8);

            Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_we[j].we_utmp.out_time)),0,buffy,i+16,4);
            Array.Copy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)d.wd_we[j].we_idle)),0,buffy,i+20,4);
            
            i+=24;
          }
        }

        sock.SendTo(buffy,new IPEndPoint(IPAddress.Broadcast,513));

        GC.Collect();
      }
      catch(SocketException e)
      {
        EventLog.WriteEntry("rwhod", string.Format("Encountered socket error %d %s", e.ErrorCode, e.StackTrace), 
              EventLogEntryType.Error);
      }
      catch(Exception e)
      {
        EventLog.WriteEntry("rwhod",e.Message + e.StackTrace,EventLogEntryType.Error);
      }

    }

    void GetPerfData(object state)
    {
      if(!initialized)
        return;
      if(perfData.Count >= 60)
        perfData.Dequeue();
      perfData.Enqueue(pc.NextValue());
    }
  }
}
