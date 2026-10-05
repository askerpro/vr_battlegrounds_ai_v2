param([uint32]$ThreadId = 60476)
# Только чтение Windows wait-chain; не debugger attach, без памяти процесса и Unity API.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ArsenalWaitChain {
 [DllImport("advapi32.dll", SetLastError=true)] static extern IntPtr OpenThreadWaitChainSession(uint flags, IntPtr callback);
 [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetThreadWaitChain(IntPtr session, IntPtr context, uint flags, uint threadId, ref uint count, IntPtr nodes, out bool cycle);
 [DllImport("advapi32.dll")] static extern void CloseThreadWaitChainSession(IntPtr session);
 public static string Read(uint threadId) {
  IntPtr session=OpenThreadWaitChainSession(0,IntPtr.Zero);
  if(session==IntPtr.Zero)return "Open error="+Marshal.GetLastWin32Error();
  // WAITCHAIN_NODE_INFO: object type/status (8), union max LockObject (280): sizeof=288.
  IntPtr nodes=Marshal.AllocHGlobal(288*16);
  try {
   uint count=16; bool cycle;
   if(!GetThreadWaitChain(session,IntPtr.Zero,7,threadId,ref count,nodes,out cycle))return "Read error="+Marshal.GetLastWin32Error();
   var result="cycle="+cycle+" nodes="+count;
   for(int i=0;i<count;i++) {
    IntPtr p=IntPtr.Add(nodes,i*288); int type=Marshal.ReadInt32(p), status=Marshal.ReadInt32(p,4);
    result+="\nnode="+i+" type="+type+" status="+status;
    if(type==8)result+=" pid="+Marshal.ReadInt32(p,8)+" tid="+Marshal.ReadInt32(p,12)+" wait="+Marshal.ReadInt32(p,16)+" switches="+Marshal.ReadInt32(p,20);
    else result+=" name="+Marshal.PtrToStringUni(IntPtr.Add(p,8));
   }
   return result;
  }finally {Marshal.FreeHGlobal(nodes);CloseThreadWaitChainSession(session);}
 }
}
'@
[ArsenalWaitChain]::Read($ThreadId)
