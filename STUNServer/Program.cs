// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
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

// 修改为只监听端口
var server = new UdpClient(settings.STUNServerPort);
var clientDict = new ConcurrentDictionary<Guid, StunClient>();

// 添加服务器启动确认信息
Console.WriteLine($"UDP服务器已启动，正在监听端口: {settings.STUNServerPort}");

// 添加定期清理超时客户端的定时器
var cleanupTimer = new Timer(CleanupInactiveClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

server.BeginReceive(ReceiveCallback, null);

void ReceiveCallback(IAsyncResult ar)
{
	try
	{
		var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
		Console.WriteLine("等待接收数据...");  // 添加这行
		var receivedBytes = server.EndReceive(ar, ref remoteEndPoint);
        
		Console.WriteLine($"收到数据！来自: {remoteEndPoint}");  // 添加这行
		Console.WriteLine($"收到原始数据长度: {receivedBytes.Length}");
		Console.WriteLine($"原始数据: {BitConverter.ToString(receivedBytes)}");
		Console.WriteLine($"尝试转换为文本: {System.Text.Encoding.UTF8.GetString(receivedBytes)}");// 验证远程终端点
		if(remoteEndPoint == null)
		{
			throw new Exception("远程终端点为空");
		}
		if (!remoteEndPoint.Address.Equals(IPAddress.Any))
		{
			Console.WriteLine($"收到来自 {remoteEndPoint.Address}:{remoteEndPoint.Port} 的连接");
		}

		var message = StunMessage.FromBytes(receivedBytes);
		if (message.MessageType == MessageType.StunRequest)
		{
			var client = new StunClient(remoteEndPoint);
			// 使用线程安全的字典操作
			if (clientDict.TryAdd(client.Id, client))
			{
				Console.WriteLine($"新客户端已连接: {client.Id} - {remoteEndPoint}");

				var responseMessage = new StunMessage(
					MessageType.StunResponse,
					MessageSource.Server,
					client.Id,
					client.InitialClientEndPoint,
					new IPEndPoint(
						IPAddress.Parse(settings.STUNServerIP),
						settings.STUNServerPort
					));

				var sendingBytes = responseMessage.ToBytes();
				var sendingBytesLength = sendingBytes.Length;

				try
				{
					server.Send(sendingBytes, sendingBytesLength, remoteEndPoint);
					Console.WriteLine($"已发送响应到客户端: {client.Id}");
				}
				catch (SocketException ex)
				{
					Console.WriteLine($"发送响应失败: {ex.Message}");
				}
			}
		}
		else
		{
			if (clientDict.TryGetValue(message.ClientId, out var client))
			{
				// 更新客户端最后活动时间
				client.LastActivity = DateTime.UtcNow;
				Console.WriteLine($"收到来自客户端的消息 {client.InitialClientEndPoint} - " +
				                  $"消息来源: {message.MessageSource}, " +
				                  $"消息类型: {message.MessageType}, " +
				                  $"客户端ID: {message.ClientId}, " +
				                  $"客户端终端点: {message.ClientEndPoint}, " +
				                  $"服务器终端点: {message.ServerEndPoint}");
			}
			else
			{
				Console.WriteLine($"收到来自未知客户端的消息: {message.ClientId}");
			}
		}
	}
	catch (ObjectDisposedException)
	{
		// 服务器已关闭，不需要继续处理
		return;
	}
	catch (SocketException ex)
	{
		Console.WriteLine($"网络错误: {ex.Message}");
	}
	catch (Exception ex)
	{
		Console.WriteLine($"处理消息时发生错误: {ex.Message}");
	}
	finally
	{
		// 确保服务器继续监听，除非已经被销毁
		try
		{
			server.BeginReceive(ReceiveCallback, null);
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
Console.CancelKeyPress += (sender, e) =>
{
	e.Cancel = true; // 防止程序立即退出
	Console.WriteLine("正在关闭服务器...");
	// cleanupTimer.Dispose();
	server.Close();
	Console.WriteLine("服务器已关闭");
	Environment.Exit(0);
};

Console.ReadLine();
server.Close();
cleanupTimer.Dispose();