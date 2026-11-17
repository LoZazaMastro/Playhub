param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;using System.Text;
public static class PrivateStoreReview {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SI{public uint cb;public string reserved,desktop,title;public uint x,y,xs,ys,xc,yc,fill,flags;public ushort show,cb2;public IntPtr reserved2,input,output,error;}
 [StructLayout(LayoutKind.Sequential)]struct PI{public IntPtr process,thread;public uint pid,tid;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,IntPtr sa);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr d);
 [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr h,int index,StringBuilder s,int bytes,out int needed);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CreateProcess(string app,StringBuilder cmd,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr environment,string cwd,ref SI startup,out PI process);
 [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr h,uint ms);
 [DllImport("kernel32.dll")]static extern bool TerminateProcess(IntPtr h,uint code);
 [DllImport("kernel32.dll")]static extern bool GetExitCodeProcess(IntPtr h,out uint code);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
 static string Input(){var h=OpenInputDesktop(0,false,1);if(h==IntPtr.Zero)return "unknown";try{var s=new StringBuilder(256);int n;return GetUserObjectInformation(h,2,s,512,out n)?s.ToString():"unknown";}finally{CloseDesktop(h);}}
 public static string Run(string exe,string output){string before=Input(),name="PlayhubStoreReview_"+Guid.NewGuid().ToString("N");var d=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x1ff,IntPtr.Zero);if(d==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();PI p=new PI();try{var si=new SI{cb=(uint)Marshal.SizeOf<SI>(),desktop="winsta0\\"+name};if(!CreateProcess(exe,new StringBuilder("\""+exe+"\" \""+output+"\""),IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,System.IO.Path.GetDirectoryName(exe),ref si,out p))throw new System.ComponentModel.Win32Exception();uint wait=WaitForSingleObject(p.process,45000);if(wait!=0){TerminateProcess(p.process,74);WaitForSingleObject(p.process,1000);}uint code;GetExitCodeProcess(p.process,out code);return "desktop="+name+" inputBefore="+before+" inputAfter="+Input()+" childExit="+code+" timeout="+(wait!=0);}finally{if(p.thread!=IntPtr.Zero)CloseHandle(p.thread);if(p.process!=IntPtr.Zero)CloseHandle(p.process);CloseDesktop(d);}}
}
'@
[PrivateStoreReview]::Run([IO.Path]::GetFullPath($Executable),[IO.Path]::GetFullPath($Output))
