// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using P2PViaUDP;
using P2PViaUDP.Model;
using STUNServer;

Console.WriteLine("Hello, World!");



/*


 启动UDP服务端,监听Settings里面的端口
 等待客户端连接,得到他的公网IP和端口,存储为一个StunClient到列表里面
 同时把他自己的端口和公网IP发送给他自己
 如果客户端发来消息不需要响应


*/
var settings = new Settings();


#region 如果是编译器附加,则设置STUNServerIP为本地IP
if (Debugger.IsAttached)
{
	Console.WriteLine("调试模式已启用,将STUN服务器IP设置为本地IP");
	settings.STUNServerIP = "127.0.0.1";
}
#endregion
//创建一个UDP服务器,绑定默认的初始化端口
var server = new UdpClient(settings.STUNServerPort);
//额外的STUN服务器端口
var additionalServers = new Dictionary<ushort, UdpClient>();
foreach (var port in settings.STUNServerAdditionalPorts)
{
	var additionalServer = new UdpClient();
	additionalServer.Client.Bind(new IPEndPoint(IPAddress.Any, port));
	additionalServers.Add(port, additionalServer);
	Console.WriteLine($"额外的STUN服务器端口已启动: {port}");
}
var clientDict = new ConcurrentDictionary<Guid, StunClient>();

// 添加服务器启动确认信息
Console.WriteLine($"UDP服务器已启动，正在监听端口: {settings.STUNServerPort} 额外端口: {string.Join(",", settings.STUNServerAdditionalPorts)}");


// 添加定期清理超时客户端的定时器
var cleanupTimer = new Timer(CleanupInactiveClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

#region 绑定所有服务器的接收回调

server.BeginReceive(ReceiveCallback, server);
// foreach (var additionalServer in additionalServers)
// {
// 	additionalServer.Value.BeginReceive(ReceiveCallback, additionalServer);
// }
for(var i = 0; i < additionalServers.Count; i++)
{
	additionalServers.ElementAt(i).Value.BeginReceive(ReceiveCallback, additionalServers.ElementAt(i).Value);
}

#endregion

void ReceiveCallback(IAsyncResult ar)
{
	UdpClient? serverUdpClient;
	try { serverUdpClient = (UdpClient?)ar.AsyncState; }catch { serverUdpClient = null; }
	if (serverUdpClient == null)
	{
		// throw new Exception("在ReceiveCallback无法获取服务器实例");
		Console.WriteLine($"在ReceiveCallback无法获取服务器实例,可能是额外的服务器");
		return;
	}
	try
	{
		var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
		
		Console.WriteLine($"收到数据！来自: {serverUdpClient.Client.LocalEndPoint}");  // 添加这行
		var receivedBytes = serverUdpClient.EndReceive(ar, ref remoteEndPoint);
        
		// Console.WriteLine($"收到数据！来自: {remoteEndPoint}");  // 添加这行
		// Console.WriteLine($"收到原始数据长度: {receivedBytes.Length}");
		// Console.WriteLine($"原始数据: {BitConverter.ToString(receivedBytes)}");
		// Console.WriteLine($"尝试转换为文本: {System.Text.Encoding.UTF8.GetString(receivedBytes)}");// 验证远程终端点
		if(remoteEndPoint == null)
		{
			throw new Exception("远程终端点为空");
		}
		if (!remoteEndPoint.Address.Equals(IPAddress.Any))
		{
			Console.WriteLine($"收到来自 {remoteEndPoint.Address}:{remoteEndPoint.Port} 的连接");
		}
		else if (remoteEndPoint.Address.Equals(IPAddress.Any))
		{
			Console.WriteLine($"回环连接");
		}

		var message = StunMessage.FromBytes(receivedBytes);
		message.ClientEndPoint = remoteEndPoint;
		if (message.MessageType == MessageType.StunRequest)
		{
			var client = new StunClient(message.ClientId, remoteEndPoint);
			// 使用线程安全的字典操作
			if (clientDict.TryAdd(client.Id, client))
			{
				Console.WriteLine($"新客户端已连接: {client.Id} - {remoteEndPoint}");

				var responseMessage = new StunMessage(
					MessageType.StunResponse,
					MessageSource.Server,
					client.Id,
					new IPEndPoint(
						IPAddress.Parse(settings.STUNServerIP),
						settings.STUNServerPort
					));
				responseMessage.ClientEndPoint = remoteEndPoint;

				var sendingBytes = responseMessage.ToBytes();
				var sendingBytesLength = sendingBytes.Length;

				try
				{
					serverUdpClient.Send(sendingBytes, sendingBytesLength, remoteEndPoint);
					Console.WriteLine($"已发送响应到客户端: {client.Id}");
				}
				catch (SocketException ex)
				{
					Console.WriteLine($"发送响应失败: {ex.Message}");
				}
			}
			if (!clientDict.TryGetValue(message.ClientId, out var clientInDict)) return;
			// 更新客户端最后活动时间
			clientInDict.LastActivity = DateTime.UtcNow;
			//将他的新的公网端点信息存储到列表里面
			if (!clientInDict.AdditionalClientEndPoints.Contains(remoteEndPoint))
			{
				clientInDict.AdditionalClientEndPoints.Add(remoteEndPoint);
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"客户端 {clientInDict.Id} 的端口 {remoteEndPoint} 已添加");
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"客户端 {clientInDict.Id} 的端口 {remoteEndPoint} 已存在");
			}
			Console.ForegroundColor = ConsoleColor.DarkCyan;
			Console.WriteLine($"客户端 {clientInDict.Id} 的额外端口: {string.Join(",", clientInDict.AdditionalClientEndPoints)}");
			Console.ResetColor();
		}
		else
		{
			
		}
	}
	catch (ObjectDisposedException)
	{
		// 服务器已关闭，不需要继续处理
		//return;
	}
	catch (SocketException ex)
	{
		Console.WriteLine($"网络错误: {ex.Message}");
	}
	catch (Exception ex)
	{
		Console.WriteLine($"处理消息时发生错误: {ex.Message}, {ex.StackTrace}");
	}
	finally
	{
		// 确保服务器继续监听，除非已经被销毁
		try
		{
			serverUdpClient.BeginReceive(ReceiveCallback, null);
		}
		catch (ObjectDisposedException)
		{
			// 服务器已关闭，忽略
		}
	}
}

void CleanupInactiveClients(object? state)
{
	var timeoutThreshold = DateTime.UtcNow.AddMinutes(-10); // 10分钟超时
	var inactiveClients = clientDict.Where(kvp => kvp.Value.LastActivity < timeoutThreshold).ToList();

	foreach (var client in inactiveClients)
	{
		if (clientDict.TryRemove(client.Key, out _))
		{
			Console.WriteLine($"已移除超时客户端: {client.Key}");
		}
	}
}

// 优雅关闭服务器
Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true; // 防止程序立即退出
	Console.WriteLine("正在关闭服务器...");
	// cleanupTimer.Dispose();
	server.Close();
	foreach (var additionalServer in additionalServers)
	{
		additionalServer.Value.Close();
	}
	Console.WriteLine("服务器已关闭");
	Environment.Exit(0);
};

Console.ReadLine();
server.Close();
foreach (var additionalServer in additionalServers)
{
	additionalServer.Value.Close();
}
cleanupTimer.Dispose();