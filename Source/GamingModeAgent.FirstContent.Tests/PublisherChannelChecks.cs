using System.Buffers.Binary;
using GamingMode.Services;
internal static class PublisherChannelChecks{
 internal static int Run(){
  int count=0;void Check(bool b){if(!b)throw new Exception("publisher table "+count);++count;}
  byte[] Row(uint pid=70){var b=new byte[28];BinaryPrimitives.WriteUInt32LittleEndian(b,1);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4),5);
   new byte[]{127,0,0,1}.CopyTo(b,8);new byte[]{127,0,0,1}.CopyTo(b,16);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(12),50000);
   BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20),47991);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24),pid);return b;}
  var good=Row();Check(LaunchCurtainPublisherChannel.ParseOwner(good,50000,47991)==70);
  Check(LaunchCurtainPublisherChannel.ParseOwner(good,50001,47991)==0);Check(LaunchCurtainPublisherChannel.ParseOwner(good,50000,47992)==0);
  Check(LaunchCurtainPublisherChannel.ParseOwner(good,0,47991)==0);Check(LaunchCurtainPublisherChannel.ParseOwner(good.AsSpan(0,27),50000,47991)==0);
  foreach(int at in new[]{4,8,16}){var bad=Row();bad[at]=8;Check(LaunchCurtainPublisherChannel.ParseOwner(bad,50000,47991)==0);}
  Check(LaunchCurtainPublisherChannel.ParseOwner(Row(0),50000,47991)==0);
  var two=new byte[52];good.CopyTo(two,0);good.AsSpan(4).CopyTo(two.AsSpan(28));BinaryPrimitives.WriteUInt32LittleEndian(two,2);
  Check(LaunchCurtainPublisherChannel.ParseOwner(two,50000,47991)==0);
  return count;
 }
}
