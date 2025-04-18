﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
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

#region 测试用,如果当前是调试器附加的,则当前服务器是从服务器

if (Debugger.IsAttached)
{
	settings.IsSlaveServer = true;
}

#endregion

var isSlaveServer = settings.IsSlaveServer;
var mainServerWhichKindOfConeRequestAndResponsePort = settings.MainServerWhichKindOfConeRequestAndResponsePort;
var mainServerWhichKindOfConeResponseSecondaryPort = settings.MainServerWhichKindOfConeResponseSecondaryPort;
var slaveServerWhichKindOfConeResponsePrimaryPort = settings.SlaveServerWhichKindOfConeResponsePrimaryPort;
var slaveServerWhichKindOfConeResponseSecondaryPort = settings.SlaveServerWhichKindOfConeResponseSecondaryPort;

var mainServerIsSymmetricRequestAndResponsePrimaryPort = settings.MainServerIsSymmetricRequestAndResponsePrimaryPort;
var mainServerIsSymmetricRequestAndResponseSecondaryPort = settings.MainServerIsSymmetricRequestAndResponseSecondaryPort;
var slaveServerIsSymmetricRequestAndResponsePrimaryPort = settings.SlaveServerIsSymmetricRequestAndResponsePrimaryPort;
var slaveServerIsSymmetricRequestAndResponseSecondaryPort = settings.SlaveServerIsSymmetricRequestAndResponseSecondaryPort;

var slaveServerReceiveMainServerBytesPort = settings.SlaveServerReceiveMainServerBytesPort;
// var slaveServerInternalIP = settings.SlaveServerInternalIP;


UdpClient? mainServerWhichKindOfConeServer = null;
UdpClient? mainServerWhichKindOfConeSecondarySender = null;
UdpClient? mainServerIsSymmetricPrimaryServer = null;
UdpClient? mainServerIsSymmetricSecondaryServer = null;
//用于主服务器发送给从服务器的透传消息的发送器,只有主服务器会初始化这个实例
UdpClient? mainStunToSlaveStunMainServerSideSender = null;

UdpClient? slaveServerWhichKindOfConeByPassReceiver = null;
UdpClient? slaveServerWhichKindOfConePrimarySender = null;
UdpClient? slaveServerWhichKindOfConeSecondarySender = null;
UdpClient? slaveServerIsSymmetricPrimaryServer = null;
UdpClient? slaveServerIsSymmetricSecondaryServer = null;
//初始化主服务器
if (!isSlaveServer)
{
	Console.WriteLine("当前工作在主服务器模式下");
	mainServerWhichKindOfConeServer = new UdpClient(new IPEndPoint(IPAddress.Any, mainServerWhichKindOfConeRequestAndResponsePort));
	mainServerIsSymmetricPrimaryServer = new UdpClient(new IPEndPoint(IPAddress.Any, mainServerIsSymmetricRequestAndResponsePrimaryPort));
	mainServerIsSymmetricSecondaryServer =
		new UdpClient(new IPEndPoint(IPAddress.Any, mainServerIsSymmetricRequestAndResponseSecondaryPort));
	mainServerWhichKindOfConeSecondarySender = new UdpClient();
	mainStunToSlaveStunMainServerSideSender = new UdpClient();
}
//初始化从服务器
else
{
	Console.WriteLine("当前工作在从服务器模式下");
	slaveServerWhichKindOfConeByPassReceiver = new UdpClient(new IPEndPoint(IPAddress.Any, slaveServerReceiveMainServerBytesPort));
	slaveServerIsSymmetricPrimaryServer =
		new UdpClient(new IPEndPoint(IPAddress.Any, slaveServerIsSymmetricRequestAndResponsePrimaryPort));
	slaveServerIsSymmetricSecondaryServer =
		new UdpClient(new IPEndPoint(IPAddress.Any, slaveServerIsSymmetricRequestAndResponseSecondaryPort));
	slaveServerWhichKindOfConePrimarySender = new UdpClient();
	slaveServerWhichKindOfConeSecondarySender = new UdpClient();
}

#endregion

#region 客户端字典

var clientDict = new ConcurrentDictionary<Guid, StunClient>();

#endregion

#region 添加定期清理超时客户端的定时器

var cleanupTimer = new Timer(CleanupInactiveClients, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

#endregion

#region 绑定所有服务器端点的接收回调

switch (isSlaveServer)
{
	case true when 
		(slaveServerWhichKindOfConeByPassReceiver == null
		 || slaveServerIsSymmetricPrimaryServer == null
		 || slaveServerIsSymmetricSecondaryServer == null):
		Console.WriteLine("从服务器的UDP客户端未初始化,无法绑定接收回调");
		return;
	case false when
		(mainServerWhichKindOfConeServer == null
		 || mainServerIsSymmetricPrimaryServer == null
		 || mainServerIsSymmetricSecondaryServer == null):
		Console.WriteLine("主服务器的UDP客户端未初始化,无法绑定接收回调");
		return;
	case true:
		slaveServerWhichKindOfConeByPassReceiver.BeginReceive(ReceiveByPassWhichKindOfConeRequestFromMainStunServerCallback, slaveServerWhichKindOfConeByPassReceiver);
		slaveServerIsSymmetricPrimaryServer.BeginReceive(ReceiveCallback,
			(slaveServerIsSymmetricRequestAndResponsePrimaryPort, slaveServerIsSymmetricPrimaryServer));
		slaveServerIsSymmetricSecondaryServer.BeginReceive(ReceiveCallback,
			(slaveServerIsSymmetricRequestAndResponseSecondaryPort, slaveServerIsSymmetricSecondaryServer));
		break;
	case false:
		mainServerWhichKindOfConeServer.BeginReceive(ReceiveCallback, 
			(mainServerWhichKindOfConeRequestAndResponsePort, mainServerWhichKindOfConeServer));
		mainServerIsSymmetricPrimaryServer.BeginReceive(ReceiveCallback, 
			(mainServerIsSymmetricRequestAndResponsePrimaryPort, mainServerIsSymmetricPrimaryServer));
		mainServerIsSymmetricSecondaryServer.BeginReceive(ReceiveCallback,
			(mainServerIsSymmetricRequestAndResponseSecondaryPort, mainServerIsSymmetricSecondaryServer));
		break;
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
		//定义一个空的远程终端点,用于接收的时候确定数据的来源
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
			Console.WriteLine($"已重新注册回调,端口: {serverPort}, 服务器: {serverUdpClient.Client.LocalEndPoint}");
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

void Release()
{
	mainServerWhichKindOfConeServer?.Close();
	mainServerIsSymmetricPrimaryServer?.Close();
	mainServerIsSymmetricSecondaryServer?.Close();
	mainServerWhichKindOfConeSecondarySender?.Close();
	mainStunToSlaveStunMainServerSideSender?.Close();

	
	slaveServerWhichKindOfConeByPassReceiver?.Close();
	slaveServerIsSymmetricPrimaryServer?.Close();
	slaveServerIsSymmetricSecondaryServer?.Close();
	slaveServerWhichKindOfConePrimarySender?.Close();
	slaveServerWhichKindOfConeSecondarySender?.Close();
}

Console.CancelKeyPress += (_, e) =>
{
	e.Cancel = true; // 防止程序立即退出
	Console.WriteLine("正在关闭服务器...");
	Release();
	
	Console.WriteLine("服务器已关闭");
	Environment.Exit(0);
};

Console.ReadLine();
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
	UdpClient updPortServer,
	bool isFromMainStunServer
)
{
	Console.WriteLine(
		$"{(isFromMainStunServer ? "主STUN服务器" : "从STUN服务器")} 的端口{serverPort} 收到了来自客户端公网{remoteEndPoint} 的NAT类型检测请求,请求类型: {message.SubCheckingType}");
	var isIsSymmetricCheckingRequest =
		message.SubCheckingType == StunNATTypeCheckingRequest.SubCheckingTypeEnum.IsSymmetric;
	var isWhichKindOfConeCheckingRequest =
		message.SubCheckingType == StunNATTypeCheckingRequest.SubCheckingTypeEnum.WhichKindOfCone;
	if (isIsSymmetricCheckingRequest)
	{
		ProcessIsSymmetricCheckingRequest(serverPort, message, remoteEndPoint, updPortServer, isFromMainStunServer);
	}

	if (isWhichKindOfConeCheckingRequest)
	{
		if (!isSlaveServer)
		{
			MainStunServerProcessWhichKindOfConeCheckingRequest(serverPort, message, remoteEndPoint, updPortServer,
				isFromMainStunServer);
		}
	}
}

void ProcessIsSymmetricCheckingRequest(ushort serverPort, StunNATTypeCheckingRequest request, IPEndPoint remoteEndPoint, UdpClient udpPortServer, bool isFromMainStunServer)
{
	var response = new StunNATTypeCheckingResponse(
		request.RequestId,
		isFromMainStunServer,
		!isFromMainStunServer,
		new IPEndPoint(IPAddress.Any, serverPort),
		remoteEndPoint,
		DateTime.UtcNow
	);
	//尝试从clientDict取出客户端信息,如果没有,添加一个
	if (clientDict.TryGetValue(request.ClientId, out var stunClient))
	{
		stunClient.LastActivity = DateTime.UtcNow;
		stunClient.LastToServerTime = DateTime.UtcNow;
		stunClient.LastToClientTime = DateTime.UtcNow;
	}
	else
	{
		stunClient = new StunClient(request.ClientId, new IPEndPoint(IPAddress.Any, serverPort), remoteEndPoint);
		clientDict.TryAdd(request.ClientId, stunClient);
	}
	//返回响应给客户端
	var responseBytes = response.ToBytes();
	udpPortServer.Send(responseBytes, responseBytes.Length, remoteEndPoint);
	Console.WriteLine($"{(isFromMainStunServer?"主STUN服务器":"从STUN服务器")} 的端口{serverPort} 向客户端公网{remoteEndPoint} 发送了NAT类型检测响应");
}
void MainStunServerProcessWhichKindOfConeCheckingRequest(
	ushort serverPort, 
	StunNATTypeCheckingRequest request, 
	IPEndPoint remoteEndPoint, 
	UdpClient udpPortServer, 
	bool isFromClientToMainStunServer
	)
{
	//如果是主服务器收到的,需要转发给从服务器,然后主服务器的两个端口和从服务器的两个端口都需要往客户端回复,客户端看能收到不,根据从哪里收到了消息检测自己的NAT类型.
	if (isFromClientToMainStunServer)
	{
		if (mainStunToSlaveStunMainServerSideSender == null)
		{ 
			Console.WriteLine("主STUN服务器的透传消息发送器未初始化,无法透传消息给从服务器");
			return;
		}
		var responseFromMainStunPrimaryPort = new StunNATTypeCheckingResponse(
			request.RequestId,
			true,
			false,
			new IPEndPoint(IPAddress.Any, mainServerWhichKindOfConeRequestAndResponsePort),
			remoteEndPoint,
			DateTime.UtcNow
		);
		var responseBytes = responseFromMainStunPrimaryPort.ToBytes();
		//主服务器主端口返回
		mainServerWhichKindOfConeServer!.Send(responseBytes, responseBytes.Length, remoteEndPoint);
		Console.WriteLine($"主STUN服务器 的端口{serverPort} 向客户端公网{remoteEndPoint} 发送了NAT类型检测响应");
		//主服务器从端口返回
		var responseFromMainStunSecondaryPort = new StunNATTypeCheckingResponse(
			request.RequestId,
			true,
			!false,
			new IPEndPoint(IPAddress.Any, mainServerWhichKindOfConeResponseSecondaryPort),
			remoteEndPoint,
			DateTime.UtcNow
		);
		var responseBytesSecondaryPort = responseFromMainStunSecondaryPort.ToBytes();

		#region region 两个response之间有个延迟
		const int delayBetweenToPackage = 200;
		Thread.Sleep(delayBetweenToPackage);
		Console.WriteLine($"①主STUN服务器 的端口{mainServerWhichKindOfConeServer.Client.LocalEndPoint} 向客户端公网{remoteEndPoint} 发送了NAT类型检测响应,等待{delayBetweenToPackage}ms后再发送第二条消息");
		#endregion
		
		mainServerWhichKindOfConeSecondarySender!.Send(responseBytesSecondaryPort, responseBytesSecondaryPort.Length, remoteEndPoint);
		Console.WriteLine(
			$"②主STUN服务器 的端口{mainServerWhichKindOfConeSecondarySender.Client.LocalEndPoint} 向客户端公网{remoteEndPoint} 发送了NAT类型检测响应,等待{delayBetweenToPackage}ms后再发送透传消息");
		//转发给从服务器,从服务器收到以后还会在发出去两条消息到客户端
		//直接创建一个链接就行
		var mainToSlaveByPassResponse = new StunNATTypeCheckingResponse(
			request.RequestId,
			true,
			false,
			new IPEndPoint(IPAddress.Any, mainServerWhichKindOfConeRequestAndResponsePort),
			remoteEndPoint,
			DateTime.UtcNow
		);
		var mainToSlaveByPassResponseBytes = mainToSlaveByPassResponse.ToBytes();
		mainStunToSlaveStunMainServerSideSender.Send(mainToSlaveByPassResponseBytes, mainToSlaveByPassResponseBytes.Length, new IPEndPoint(IPAddress.Parse(settings.SlaveServerInternalIP), settings.SlaveServerReceiveMainServerBytesPort));
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine($"主STUN服务器 向从STUN服务器的端口{settings.SlaveServerReceiveMainServerBytesPort} 发送了透传消息");
		Console.ResetColor();
	}
	else
	{
		var response = new StunNATTypeCheckingResponse(
			request.RequestId,
			isFromClientToMainStunServer,
			!isFromClientToMainStunServer,
			new IPEndPoint(IPAddress.Any, serverPort),
			remoteEndPoint,
			DateTime.UtcNow
		);
		var responseBytes = response.ToBytes();
		udpPortServer.Send(responseBytes, responseBytes.Length, remoteEndPoint);
		Console.WriteLine($"从STUN服务器 的端口{serverPort} 向客户端公网{remoteEndPoint} 发送了NAT类型检测响应");
	}
}
//只有从STUN服务器会触发调用这个端口,当主服务器给从服务器发送了具体哪种锥形检测的消息包时,从服务器会接收到这个消息包,然后处理(修改远端信息)后转发给客户端
void ReceiveByPassWhichKindOfConeRequestFromMainStunServerCallback(IAsyncResult ar)
{
	Console.ForegroundColor = ConsoleColor.Cyan;
	Console.WriteLine("从STUN服务器收到了主STUN服务器的透传消息");
	Console.ResetColor();
	try
	{
		if (ar.AsyncState == null)
		{
			Console.WriteLine("在ReceiveByPassWhichKindOfConeRequestFromMainStunServerCallback中无法获取服务器实例");
			return;
		}

		var udpClient = (UdpClient)ar.AsyncState;
		var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
		var receivedBytes = udpClient.EndReceive(ar, ref remoteEndPoint);
		if (remoteEndPoint == null)
		{
			throw new Exception($"从从服务器接收主服务器的具体哪种锥形检测的消息包时,检测到所谓的主服务端端点为空,接收的内容是:{receivedBytes}");
		}

		#region 进行检查,如果是从其他服务器过来的而不是主服务器的内网地址,则说明可能被攻击

		if (remoteEndPoint.Address.ToString().Equals(settings.MainServerInternalIP) == false)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(
				$"透传端口{settings.SlaveServerReceiveMainServerBytesPort}接收到了来自非主STUN服务器的消息,可能被攻击,来自: {remoteEndPoint}");
			Console.ResetColor();
			return;
		}

		#endregion

		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType == MessageType.StunNATTypeCheckingResponse)
		{
			var originalResponse = StunNATTypeCheckingResponse.FromBytes(receivedBytes);
			//分别从自己的主端口和从端口返回回去Response(要重新构建response)
			var mainPortResponse = new StunNATTypeCheckingResponse(
				originalResponse.RequestId,
				false,
				true,
				new IPEndPoint(IPAddress.Any, slaveServerWhichKindOfConeResponsePrimaryPort),
				originalResponse.DetectedClientNATEndPoint,
				DateTime.UtcNow
			);
			var slavePortResponse = new StunNATTypeCheckingResponse(
				originalResponse.RequestId,
				false,
				true,
				new IPEndPoint(IPAddress.Any, slaveServerWhichKindOfConeResponseSecondaryPort),
				originalResponse.DetectedClientNATEndPoint,
				DateTime.UtcNow
			);
			var mainPortResponseBytes = mainPortResponse.ToBytes();
			var slavePortResponseBytes = slavePortResponse.ToBytes();
			//尝试从主端口给客户端发回去(当前是从服务器)
			slaveServerWhichKindOfConePrimarySender!.Send(mainPortResponseBytes, mainPortResponseBytes.Length,
				originalResponse.DetectedClientNATEndPoint);
			//尝试从次端口给客户端发回去(当前是从服务器)
			slaveServerWhichKindOfConeSecondarySender!.Send(slavePortResponseBytes, slavePortResponseBytes.Length,
				originalResponse.DetectedClientNATEndPoint);
			Console.WriteLine(
				$"从属STUN服务器收到了主服务器的透传信息,已将消息透传给客户端{originalResponse.DetectedClientNATEndPoint} 以便客户端确认自己的NAT类型(哪种锥形)");
		}
		else
		{
			Console.WriteLine($"从主服务器那边接受过来的消息不是预期的具体哪种类型的锥形的检测消息 是不是发错了?消息类型{messageType}");
		}
	}
	catch (Exception e)
	{
		Console.WriteLine($"从STUN服务器处理主STUN服务器透传消息时发生错误: {e.Message}");
		throw;
	}
	finally
	{
		try
		{
			var udpClient = (UdpClient)ar.AsyncState!;
			udpClient.BeginReceive(ReceiveByPassWhichKindOfConeRequestFromMainStunServerCallback, udpClient);
		}
		catch (ObjectDisposedException)
		{
			// UDP客户端已释放，忽略
		}
		catch (Exception ex)
		{
			Console.WriteLine($"重新注册回调时发生错误: {ex.Message}");
		}
	}
}