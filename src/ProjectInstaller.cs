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
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

namespace ACM.rwho
{
	/// <summary>
	/// Summary description for ProjectInstaller.
	/// </summary>
	[RunInstaller(true)]
	public class Installer : System.Configuration.Install.Installer
	{
		private System.ServiceProcess.ServiceProcessInstaller serviceProcessInstaller1;
		private System.ServiceProcess.ServiceInstaller serviceInstaller1;
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.Container components = null;

		public Installer()
		{
			// This call is required by the Designer.
			InitializeComponent();

			// TODO: Add any initialization after the InitializeComponent call
		}

		/// <summary> 
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if(components != null)
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		public override void Install(IDictionary stateSaver)
		{
			base.Install (stateSaver);

			Daemon rd = new Daemon();
			
			string loc = rd.GetType().Assembly.Location;
			
			/*System.IO.Stream rs = rd.GetType().Assembly.GetManifestResourceStream("ACM.WTS.dll");
			System.IO.Stream st = System.IO.File.OpenWrite(System.Environment.SystemDirectory + "\\WTS.dll");
			while(rs.Position < rs.Length)
			{
				byte[] buffer = new byte[1024];
				int bytes = rs.Read(buffer, 0, 1024);
				st.Write(buffer, 0, bytes);
			}
			rs.Close();
			st.Close();*/
					
			System.IO.File.Copy(loc,System.Environment.SystemDirectory + "\\ruptime.exe",true);

			Process proc = Process.Start(System.Environment.SystemDirectory + "\\sc.exe", "config rwhod binPath= \"" + loc + " service\"");
			if(proc != null)
			{
				proc.WaitForExit();
				proc.Close();
			}
			try
			{
				ServiceController sc = new ServiceController("rwhod");
				sc.Start();
				sc.WaitForStatus(ServiceControllerStatus.Running);
			}
			catch(System.Exception){}
		}

		public override void Rollback(IDictionary savedState)
		{
			base.Rollback (savedState);

			/*try
			{
				if(System.IO.File.Exists(System.Environment.SystemDirectory + "\\WTS.dll"))
					System.IO.File.Delete(System.Environment.SystemDirectory + "\\WTS.dll");
			}
			catch(System.Exception){}*/
			try
			{
				if(System.IO.File.Exists(System.Environment.SystemDirectory + "\\ruptime.exe"))
					System.IO.File.Delete(System.Environment.SystemDirectory + "\\ruptime.exe");
			}
			catch(System.Exception){}
			
		}

		public override void Uninstall(IDictionary savedState)
		{
			base.Uninstall (savedState);

			Thread.Sleep(2000);

			try
			{
				string path = System.Environment.SystemDirectory;
				path = path.Remove(path.LastIndexOf('\\'),path.Length-path.LastIndexOf('\\'));
				if(System.IO.File.Exists(path + "\\TEMP\\rwhocache.xml"))
					System.IO.File.Delete(path + "\\TEMP\\rwhocache.xml");
			}
			catch(System.Exception){}
			try
			{
				if(System.IO.File.Exists(System.Environment.SystemDirectory + "\\WTS.dll"))
					System.IO.File.Delete(System.Environment.SystemDirectory + "\\WTS.dll");
			}
			catch(System.Exception){}
			try
			{
				if(System.IO.File.Exists(System.Environment.SystemDirectory + "\\ruptime.exe"))
					System.IO.File.Delete(System.Environment.SystemDirectory + "\\ruptime.exe");
			}
			catch(System.Exception){}
		}


		#region Component Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.serviceProcessInstaller1 = new System.ServiceProcess.ServiceProcessInstaller();
			this.serviceInstaller1 = new System.ServiceProcess.ServiceInstaller();
			// 
			// serviceProcessInstaller1
			// 
			this.serviceProcessInstaller1.Account = System.ServiceProcess.ServiceAccount.LocalSystem;
			this.serviceProcessInstaller1.Password = null;
			this.serviceProcessInstaller1.Username = null;
			// 
			// serviceInstaller1
			// 
			this.serviceInstaller1.DisplayName = "RWHO Daemon";
			this.serviceInstaller1.ServiceName = "rwhod";
			this.serviceInstaller1.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
			// 
			// ProjectInstaller
			// 
			this.Installers.AddRange(new System.Configuration.Install.Installer[] {
																					  this.serviceProcessInstaller1,
																					  this.serviceInstaller1});

		}
		#endregion
	}
}
