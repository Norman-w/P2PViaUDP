Console.WriteLine("STUN客户端启动...");
var client = new P2PClient();
await client.StartAsync();
Console.WriteLine("STUN客户端已退出");