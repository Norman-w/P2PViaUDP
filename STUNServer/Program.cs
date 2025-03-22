using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PViaUDP.Model;
using P2PViaUDP.Model.STUN;
using STUNServer;

Console.WriteLine("Hello, STUNServer!");

/*


 启动UDP服务端,监听Settings里面的端口
 等待客户端连接,得到他的公网IP和端口,存储为一个StunClient到列表里面
 同时把他自己的端口和公网IP发送给他自己
 如果客户端发来消息不需要响应


*/

#region 服务端端口监听器

var settings = STUNServerConfig.Default;

var isSlaveServer = settings.IsSlaveServer;
var primaryPort = settings.MainServerAndSlaveServerPrimaryPort;
var secondaryPort = isSlaveServer ? settings.SlaveServerSecondaryPort : settings.MainServerSecondaryPort;

//创建一个UDP服务器,绑定默认的初始化端口
var primaryPortServer = new UdpClient(new IPEndPoint(IPAddress.Any, primaryPort));
Console.WriteLine($"{(isSlaveServer ? "从服务器" : "主服务器")} 的主要端口服务已启动,监听端口: {primaryPort}");

//额外的STUN服务器端口
var secondaryPortServer = new UdpClient(new IPEndPoint(IPAddress.Any, secondaryPort));

Console.WriteLine($"{(isSlaveServer ? "从服务器" : "主服务器")} 的次要端口服务已启动,监听端口: {secondaryPort}");

#endregion

#region 客户端字典

var clientDict = new ConcurrentDictionary<Guid, StunClient>();

#endregion

#region 添加定期清理超时客户端的定时器

var cleanupTimer = new Timer(CleanupInactiveClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

#endregion

#region 绑定所有服务器端点的接收回调

primaryPortServer.BeginReceive(ReceiveCallback, (primaryPort, primaryPortServer));
secondaryPortServer.BeginReceive(ReceiveCallback, (secondaryPort, secondaryPortServer));

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
		
		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType == MessageType.StunNATTypeCheckingRequest)
		{
			var stunNATTypeCheckingRequestMessage = StunNATTypeCheckingRequest.FromBytes(receivedBytes);
			ProcessStunNATTypeCheckingRequestMessage(
				serverPort, 
				stunNATTypeCheckingRequestMessage, 
				remoteEndPoint, 
				clientDict, 
				serverUdpClient,
				!isSlaveServer
				);
			return;
		}

		var message = StunMessage.FromBytes(receivedBytes);
		message.ClientEndPoint = remoteEndPoint;
		switch (message.MessageType)
		{
			case MessageType.StunRequest:
			{
				ProcessSTUNRequestMessage(serverPort, message, remoteEndPoint, clientDict, serverUdpClient);
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
	primaryPortServer.Close();
	secondaryPortServer.Close();

	Console.WriteLine("服务器已关闭");
	Environment.Exit(0);
};

Console.ReadLine();
primaryPortServer.Close();
secondaryPortServer.Close();

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

void ProcessSTUNRequestMessage(ushort serverPort, StunMessage stunMessage, IPEndPoint ipEndPoint,
	ConcurrentDictionary<Guid, StunClient> concurrentDictionary, UdpClient udpClient)
{
	var serverEndPoint = new IPEndPoint(IPAddress.Any, serverPort);
	var stunClient = new StunClient(stunMessage.ClientId, serverEndPoint, ipEndPoint);

	#region 如果客户端信息不存在于字典中则添加

	// 使用线程安全的字典操作
	if (concurrentDictionary.TryAdd(stunClient.Id, stunClient))
	{
		Console.WriteLine($"新客户端已连接: {stunClient.Id} - {ipEndPoint}");
	}

	#endregion

	#region 如果客户端信息存在于字典中则更新客户端信息

	if (concurrentDictionary.TryGetValue(stunMessage.ClientId, out var clientInDict))
	{
		// 更新客户端最后活动时间
		clientInDict.LastActivity = DateTime.UtcNow;
	}

	#endregion

	#region 发送STUN响应

	var responseMessage = new StunMessage(
		MessageType.StunResponse,
		MessageSource.Server,
		stunClient.Id,
		new IPEndPoint(
			IPAddress.Any,
			serverPort
		))
	{
		ClientEndPoint = ipEndPoint
	};

	var sendingBytes = responseMessage.ToBytes();
	var sendingBytesLength = sendingBytes.Length;

	try
	{
		udpClient.Send(sendingBytes, sendingBytesLength, ipEndPoint);
		Console.WriteLine($"已发送响应到客户端: {stunClient.Id},通过端口: {ipEndPoint}");
	}
	catch (SocketException ex)
	{
		Console.WriteLine($"发送响应失败: {ex.Message}");
	}

	#endregion

	#region 如果当前是主STUN服务器,还要看客户端发来的STUNRequest的类型是什么然后进行后续的处理
	
	/*
	 如果是需要1发多回(当前设计是1发4回),用于检测是否全锥形,是否IP受限锥,是否端口受限锥,是否对称形
	 */

	#endregion
}

void ProcessStunNATTypeCheckingRequestMessage(
	ushort serverPort, 
	StunNATTypeCheckingRequest message, 
	IPEndPoint remoteEndPoint, 
	ConcurrentDictionary<Guid, StunClient> clientDict, 
	UdpClient updPortServer,
	bool isFromMainStunServer
	)
{
	Console.WriteLine($"{(isFromMainStunServer?"主STUN服务器":"从STUN服务器")} 的端口{serverPort} 收到了来自客户端公网{remoteEndPoint} 的NAT类型检测请求");
	var response = new StunNATTypeCheckingResponse(
		message.RequestId,
		isFromMainStunServer,
		!isFromMainStunServer,
		new IPEndPoint(IPAddress.Any, serverPort),
		remoteEndPoint,
		DateTime.UtcNow
	);
	//尝试从clientDict取出客户端信息,如果没有,添加一个
	if (clientDict.TryGetValue(message.ClientId, out var stunClient))
	{
		stunClient.LastActivity = DateTime.UtcNow;
		stunClient.LastToServerTime = DateTime.UtcNow;
		stunClient.LastToClientTime = DateTime.UtcNow;
	}
	else
	{
		stunClient = new StunClient(message.ClientId, new IPEndPoint(IPAddress.Any, serverPort), remoteEndPoint);
		clientDict.TryAdd(message.ClientId, stunClient);
	}
	//返回响应给客户端
	var responseBytes = response.ToBytes();
	updPortServer.Send(responseBytes, responseBytes.Length, remoteEndPoint);
	Console.WriteLine($"{(isFromMainStunServer?"主STUN服务器":"从STUN服务器")} 的端口{serverPort} 向客户端公网{remoteEndPoint} 发送了NAT类型检测响应");
}