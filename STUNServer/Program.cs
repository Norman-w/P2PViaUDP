﻿// See https://aka.ms/new-console-template for more information

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PViaUDP;
using P2PViaUDP.Model;
using P2PViaUDP.Model.STUN;
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

#region 如果自己的IP地址是域名,则解析为IP地址

if (IPAddress.TryParse(settings.STUNServerIP, out var ip))
{
	settings.STUNServerIP = ip.ToString();
}
else
{
	var ipAddresses = Dns.GetHostAddresses(settings.STUNServerIP);
	if (ipAddresses.Length > 0)
	{
		var realIP = ipAddresses[0];
		Console.WriteLine($"STUN服务器IP地址已由域名 {settings.STUNServerIP} 解析为 {realIP}");
		settings.STUNServerIP = realIP.ToString();
	}
	else
	{
		throw new Exception("无法解析STUN服务器IP地址");
	}
}

#endregion

//服务端(本程序)端口监听器的字典
var serverPortListenersDict = new Dictionary<ushort, UdpClient>();

#region 服务端端口监听器集合

//创建一个UDP服务器,绑定默认的初始化端口
var server = new UdpClient(settings.STUNServerPort);
serverPortListenersDict.Add(settings.STUNServerPort, server);

//额外的STUN服务器端口
var additionalServers = new Dictionary<ushort, UdpClient>();
foreach (var port in settings.STUNServerAdditionalPorts)
{
	var additionalServer = new UdpClient();
	additionalServer.Client.Bind(new IPEndPoint(IPAddress.Any, port));
	additionalServers.Add(port, additionalServer);
	serverPortListenersDict.Add(port, additionalServer);
	Console.WriteLine($"额外的STUN服务器端口已启动: {port}");
}

#endregion

#region 客户端字典
var clientDict = new ConcurrentDictionary<Guid, StunClient>();
#endregion

#region 添加服务器启动确认信息
// 添加服务器启动确认信息
Console.WriteLine(
	$"UDP服务器已启动，正在监听端口: {settings.STUNServerPort} 额外端口: {string.Join(",", settings.STUNServerAdditionalPorts)}");
#endregion

#region 添加定期清理超时客户端的定时器
var cleanupTimer = new Timer(CleanupInactiveClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
#endregion

#region 绑定所有服务器断点的接收回调

server.BeginReceive(ReceiveCallback, (settings.STUNServerPort, server));
for (var i = 0; i < additionalServers.Count; i++)
{
	var param = (settings.STUNServerAdditionalPorts[i], additionalServers.ElementAt(i).Value);
	Console.WriteLine($"额外的STUN服务器端口 {param.Item1} 已绑定,准备接收数据");
	additionalServers.ElementAt(i).Value.BeginReceive(ReceiveCallback, param);
}

#endregion

#region 接收回调方法
void ReceiveCallback(IAsyncResult ar)
{
	#region 参数验证和提取,提取服务器实例

	if (ar.AsyncState == null)
	{
		Console.WriteLine("在ReceiveCallback中无法获取服务器实例");
		return;
	}
	var (serverPort, serverUdpClient) = ((ushort serverPort, UdpClient serverUdpClient))ar.AsyncState;

	#endregion

	#region 接收回调已触发日志输出
	Console.ForegroundColor = ConsoleColor.Magenta;
	Console.WriteLine($"接收回调已触发,端口: {serverPort}");
	Console.ResetColor();
	#endregion

	try
	{
		var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

		Console.WriteLine($"收到数据！来自: {serverUdpClient.Client.LocalEndPoint}");
		var receivedBytes = serverUdpClient.EndReceive(ar, ref remoteEndPoint);
		if (remoteEndPoint == null)
		{
			throw new Exception("远程终端点为空");
		}
		Console.WriteLine(!remoteEndPoint.Address.Equals(IPAddress.Any)
			? $"收到来自 {remoteEndPoint.Address}:{remoteEndPoint.Port} 的连接"
			: "收到来自未知地址的连接");

		var message = StunMessage.FromBytes(receivedBytes);
		message.ClientEndPoint = remoteEndPoint;
		switch (message.MessageType)
		{
			case MessageType.StunRequest:
			{
				var serverEndPoint = new IPEndPoint(IPAddress.Parse(settings.STUNServerIP), serverPort);
				var client = new StunClient(message.ClientId, serverEndPoint, remoteEndPoint);

				#region 如果客户端信息不存在于字典中则添加

				// 使用线程安全的字典操作
				if (clientDict.TryAdd(client.Id, client))
				{
					Console.WriteLine($"新客户端已连接: {client.Id} - {remoteEndPoint}");
				}

				#endregion

				#region 如果客户端信息存在于字典中则更新客户端信息

				if (!clientDict.TryGetValue(message.ClientId, out var clientInDict)) return;
				// 更新客户端最后活动时间
				clientInDict.LastActivity = DateTime.UtcNow;
				//将他的新的公网端点信息存储到列表里面
				// if (!clientInDict.AdditionalClientEndPoints.Contains(remoteEndPoint))
				lock (clientInDict.AdditionalClientEndPoints)
				{
					if (clientInDict.AdditionalClientEndPoints.All(p => p.serverEndPoint.Port != remoteEndPoint.Port))
					{
						clientInDict.AdditionalClientEndPoints.Add((serverEndPoint, remoteEndPoint));
						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine($"客户端 {clientInDict.Id} 的端口 {remoteEndPoint} 已添加");
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine($"客户端 {clientInDict.Id} 的端口 {remoteEndPoint} 已存在");
						Console.ResetColor();
					}
				}

				#endregion

				#region 发送STUN响应
				
				var responseMessage = new StunMessage(
					MessageType.StunResponse,
					MessageSource.Server,
					client.Id,
					new IPEndPoint(
						IPAddress.Parse(settings.STUNServerIP),
						serverPort
					))
				{
					ClientEndPoint = remoteEndPoint
				};

				var sendingBytes = responseMessage.ToBytes();
				var sendingBytesLength = sendingBytes.Length;

				try
				{
					serverUdpClient.Send(sendingBytes, sendingBytesLength, remoteEndPoint);
					Console.WriteLine($"已发送响应到客户端: {client.Id},通过端口: {remoteEndPoint}");
				}
				catch (SocketException ex)
				{
					Console.WriteLine($"发送响应失败: {ex.Message}");
				}

				#endregion

				#region 统计并输出客户端的公网IP和端口,并去重和排序, 英文方法名: CountAndOutputClientIPAndPort

				CountAndOutputClientIPAndPort(clientInDict);

				#endregion

				break;
			}
			case MessageType.StunResponse:
			case MessageType.StunResponseError:
			case MessageType.TURNBroadcast:
			case MessageType.TURNRegister:
			case MessageType.TURNServer2ClientHeartbeat:
			case MessageType.TURNClient2ServerHeartbeat:
			case MessageType.P2PHolePunchingRequest:
			case MessageType.P2PHolePunchingResponse:
			case MessageType.P2PHeartbeat:
			default:
				var errorMessage = $"未实现的消息类型: {message.MessageType}";
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine(errorMessage);
				Console.ResetColor();
				break;
		}
	}
	#region 错误处理
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
	#endregion

	#region 确保服务器继续监听，除非已经被销毁

	finally
	{
		// 确保服务器继续监听，除非已经被销毁
		try
		{
			serverUdpClient.BeginReceive(ReceiveCallback, (serverPort, serverUdpClient));
		}
		catch (ObjectDisposedException)
		{
			// 服务器已关闭，忽略
		}
	}

	#endregion
}
#endregion

#region 优雅关闭服务器
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
return;
#endregion

#region 定时清理超时客户端的方法
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
#endregion

#region 统计并输出客户端的公网IP和端口,并去重和排序, 英文方法名: CountAndOutputClientIPAndPort
void CountAndOutputClientIPAndPort(StunClient? stunClient)
{
	if (stunClient == null) return;
	Console.ForegroundColor = ConsoleColor.DarkCyan;
	var ipAndPortsDict = new Dictionary<string, ConcurrentBag<(int serverPort, int clientPort)>>
	{
		{
			stunClient.InitialClientEndPoint.Address.ToString(), 
			// new ConcurrentBag<(int serverPort, int clientPort)> { (stunClient.ServerEndPoint.Port, stunClient.InitialClientEndPoint.Port) }
			new ConcurrentBag<(int serverPort, int clientPort)> { (stunClient.InitialServerEndPoint.Port, stunClient.InitialClientEndPoint.Port) }
		}
	};
	lock (stunClient.AdditionalClientEndPoints)
	{
		foreach (var ipAndPort in stunClient.AdditionalClientEndPoints)
		{
			if (ipAndPortsDict.TryGetValue(ipAndPort.clientEndPoint.Address.ToString(), out var value))
			{
				value.Add((ipAndPort.serverEndPoint.Port, ipAndPort.clientEndPoint.Port));
			}
			else
			{
				ipAndPortsDict.Add(ipAndPort.clientEndPoint.Address.ToString(), new ConcurrentBag<(int serverPort, int clientPort)> { (ipAndPort.serverEndPoint.Port, ipAndPort.clientEndPoint.Port) });
			}
		}
	}
	
	//将相同IP的端口进行去重和排序
	var keys = ipAndPortsDict.Keys.ToList();
	foreach (var key in keys)
	{
		ipAndPortsDict[key] = new ConcurrentBag<(int serverPort, int clientPort)>(ipAndPortsDict[key].Distinct().OrderBy(p => p.clientPort));
	}

	foreach (var ipAndPort in ipAndPortsDict)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"客户端 {stunClient.Id} 的IP地址: {ipAndPort.Key} 绑定端口:");
		var clientPorts = ipAndPort.Value
			.Select(p => p.clientPort) // 选取客户端端口
			.Distinct() // 去重操作
			.OrderBy(p => p) // 按顺序排序
			.ToList(); // 转为列表
		foreach (var port in ipAndPort.Value)
		{
			sb.AppendLine($"{port.clientPort} -> {port.serverPort}");
		}

		sb.AppendLine($"共计 {clientPorts.Count} 个");
		Console.WriteLine(sb.ToString());
		if (clientPorts.Count != 1) continue;
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine("恭喜,看起来客户端是全锥形或者端口受限锥形网络(这两种是我测试过的,使用同一个客户端实例连接到同一个服务器的不同端口时,客户端公网使用相同端口)");
	}
			
	Console.ResetColor();
}
#endregion