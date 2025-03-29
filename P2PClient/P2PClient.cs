/*


 如果还不知道自己的NAT类型,不能去注册到TURN服务器,因为注册到TURN服务器需要知道自己的NAT类型.
   如果不是全锥形的NAT,访问STUN A服务器的第一个端口,用A服务器的第二个端口回复,如果能回复,但是用别的IP不能回复,就是限制型的
    如果还是访问STUN A服务器的第一个端口,用A服务器的第二个端口都无法回复,则就是端口限制型的
	    虽然对称的和端口限制型的都是只能从原端口(发起方请求过的端口)返回,但是对称型的每次创建连接的端口


*/

using System.Net;
using System.Net.Sockets;
using P2PViaUDP.Model;
using P2PViaUDP.Model.Client;
using P2PViaUDP.Model.TURN;
using TURNServer;

namespace P2PClient;

public class P2PClient
{
	#region 私有字段

	/// <summary>
	/// 跟我打洞的客户端集合,key是对方的Guid,value是对方的信息以及和我的相关交互信息
	/// </summary>
	private readonly Dictionary<Guid, PeerClient> _peerClients = new();

	private readonly UdpClient _udpClient = new(new IPEndPoint(IPAddress.Any, 0));
	private readonly P2PClientConfig _settings = P2PClientConfig.Default;

	private IPEndPoint? _myEndPointFromMainStunSecondPortReply;

	private NATTypeEnum _myNATType = NATTypeEnum.Unknown;
	private readonly Guid _clientId = Guid.NewGuid();
	private bool _isRunning;
	private STUNClient _stunClient;

	public P2PClient()
	{
		_stunClient = new STUNClient(_settings, _udpClient);
	}

	#endregion

	#region 启动和停止

	public async Task StartAsync()
	{
		_isRunning = true;

		#region 如果是编译器附加的时候,则设置STUNServerIP为本地IP

		// if (Debugger.IsAttached)
		// {
		//     Console.WriteLine("调试模式已启用,将STUN服务器IP设置为本地IP");
		//     _settings.STUNServerIP = "127.0.0.1";
		//     Console.WriteLine($"我的ID: {_clientId}");
		// }

		Console.WriteLine($"STUN服务器IP: {_settings.STUNMainServerIP}");

		#endregion

		try
		{
			//发送给Localhost:65535一条消息,为了让udpClient进入到bind状态
			// await _udpClient.SendAsync(new byte[] { 0 }, 1, new IPEndPoint(IPAddress.Any, 0));
			// 持续监听
			_ = Task.Run(StartListeningAsync);
			// STUN 阶段
			_stunClient = new STUNClient(_settings, _udpClient);
			await _stunClient.RequestStunServerAsync();
			_myNATType = _stunClient.MyNATType;
			//TODO 使用第二个口的信息,因为第一个口的信息总是和第二个的不一样
			_myEndPointFromMainStunSecondPortReply = _stunClient.MyEndPointFromMainStunSecondaryPortReply;

			// TURN 阶段
			if (_myEndPointFromMainStunSecondPortReply != null)
				await TURNClientLogic.RegisterToTurnServerAsync(_settings, _myEndPointFromMainStunSecondPortReply,
					_clientId, _myNATType, _udpClient);
			else
			{
				Console.WriteLine("STUN响应为空, 无法注册到TURN服务器");
				return;
			}

			//等待用户退出
			Console.WriteLine("按任意键退出...");
			Console.ReadKey();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"发生错误: {ex.Message}");
		}
	}

	public void Stop()
	{
		_isRunning = false;
		_udpClient.Close();
	}

	#endregion

	#region 开始监听自己的端口

	private async Task StartListeningAsync()
	{
		Console.WriteLine("开始监听UDP消息...");
		while (_isRunning)
		{
			try
			{
				var result = await _udpClient.ReceiveAsync();
				var messageType = (MessageType)result.Buffer[0];
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"从: {result.RemoteEndPoint} 收到消息, 消息类型: {messageType}");
				Console.ResetColor();
				_ = Task.Run(() => ProcessReceivedMessageAsync(result.Buffer, result.RemoteEndPoint));
			}
			catch (Exception ex)
			{
				if (_isRunning)
				{
					Console.WriteLine($"接收消息时发生错误: {ex.Message}");
				}
			}
		}
	}

	#endregion

	#region In 处理消息

	#region 入口(消息类型路由)

	#region 处理接收到的消息总入口

	private async Task ProcessReceivedMessageAsync(byte[] data, IPEndPoint receiverRemoteEndPoint)
	{
		var messageType = (MessageType)data[0];
		Console.WriteLine($"消息类型: {messageType}");
		switch (messageType)
		{
			case MessageType.TURNBroadcast:
				await ProcessBroadcastMessageAsync(data);
				break;
			case MessageType.P2PHolePunchingRequest:
				await ProcessP2PHolePunchingRequestMessageAsync(data, receiverRemoteEndPoint);
				break;
			case MessageType.P2PHeartbeat:
				await ProcessP2PHeartbeatMessageAsync(data);
				break;
			case MessageType.StunRequest:
			case MessageType.StunResponse:
			case MessageType.StunResponseError:
			case MessageType.StunNATTypeCheckingRequest:
			case MessageType.StunNATTypeCheckingResponse:
				_stunClient.ProcessReceivedMessage(data);
				break;
			case MessageType.TURNRegister:
			case MessageType.TURNServer2ClientHeartbeat:
			case MessageType.TURNClient2ServerHeartbeat:
				TURNClientLogic.ProcessReceivedMessage(data);
				break;
			case MessageType.P2PHolePunchingResponse:
				await ProcessP2PHolePunchingResponseMessageAsync(data);
				break;
			default:
				Console.WriteLine($"未知消息类型: {messageType}");
				break;
		}
	}

	#endregion

	#endregion

	#region 处理具体类型的消息

	#region 处理接收到的心跳消息

	private Task ProcessP2PHeartbeatMessageAsync(byte[] data)
	{
		try
		{
			// 从字节数组中解析P2P心跳消息
			var heartbeatMessage = P2PHeartbeatMessage.FromBytes(data);
			Console.WriteLine($"收到P2P心跳消息，来自: {heartbeatMessage.SenderId}");
			// 更新对方的心跳时间
			if (_peerClients.TryGetValue(heartbeatMessage.SenderId, out var peer))
			{
				if (peer.LastHeartbeatFromHim == DateTime.MinValue || peer.LastHeartbeatFromHim == null)
				{
					peer.LastHeartbeatFromHim = DateTime.Now;
					Console.WriteLine(
						$"首次收到对方({heartbeatMessage.SenderId})的心跳时间: {peer.LastHeartbeatFromHim}, 开始给他发送心跳包");
				}
				else
				{
					peer.LastHeartbeatFromHim = DateTime.Now;
					Console.WriteLine($"已更新对方({heartbeatMessage.SenderId})的心跳时间: {peer.LastHeartbeatFromHim}");
				}
			}
			else
			{
				Console.WriteLine($"未找到对方的信息: {heartbeatMessage.SenderId}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理P2P心跳消息时出错: {ex.Message}");
			throw;
		}

		return Task.CompletedTask;
	}

	#endregion

	#region 处理接收到的P2P打洞消息

	private Task ProcessP2PHolePunchingRequestMessageAsync(byte[] data, IPEndPoint receiverRemoteEndPoint)
	{
		try
		{
			// 从字节数组中解析P2P打洞消息
			var holePunchingMessageFromOtherClient = Client2ClientP2PHolePunchingRequestMessage.FromBytes(data);
			Console.WriteLine(
				$"收到P2P打洞消息，来自TURN服务器中地址标记为{holePunchingMessageFromOtherClient.SourceEndPoint}的 实际端口为: {receiverRemoteEndPoint}的客户端");
			Console.WriteLine($"更新他的实际通讯地址为: {receiverRemoteEndPoint}");
			holePunchingMessageFromOtherClient.SourceEndPoint = receiverRemoteEndPoint;
			// 他要跟我打洞,我看我这边记录没有记录他的信息,如果没记录则记录一下,如果记录了则更新他的端点的相关信息
			var peerId = holePunchingMessageFromOtherClient.SourceClientId;
			if (!_peerClients.TryGetValue(peerId, out var peer))
			{
				_peerClients.Add(peerId, new PeerClient(holePunchingMessageFromOtherClient.SourceEndPoint)
				{
					Guid = peerId
				});
				Console.WriteLine($"新的PeerClient已加入: {peerId}");
			}
			else
			{
				peer.EndPoint = holePunchingMessageFromOtherClient.SourceEndPoint;
			}

			#region 如果他是对称型的,他过来的时候不一定是什么端口,他自己也不知道,我得告诉他

			if (holePunchingMessageFromOtherClient.SourceNatType == NATTypeEnum.Symmetric)
			{
				Console.WriteLine($"打洞请求的来源是对称型NAT,需要告诉他他自己是什么端口: {receiverRemoteEndPoint}");
				if (peer != null) peer.EndPoint = receiverRemoteEndPoint;
			}

			#endregion

			#region 给他发送P2P打洞响应消息

			var holePunchingResponseMessage = new Client2ClientP2PHolePunchingResponseMessage
			{
				ActiveClientEndPoint = receiverRemoteEndPoint,
				PassiveClientEndPoint = holePunchingMessageFromOtherClient.DestinationEndPoint,
				ActiveClientId = holePunchingMessageFromOtherClient.SourceClientId,
				PassiveClientId = holePunchingMessageFromOtherClient.DestinationClientId,
				//把我的NAT类型告诉他,不告诉他的话,只有TURN服务器知道.
				PassiveClientNATTye = _myNATType,
				GroupId = holePunchingMessageFromOtherClient.GroupId,
				SendTime = DateTime.Now
			};

			var responseBytes = holePunchingResponseMessage.ToBytes();
			_udpClient.SendAsync(responseBytes, responseBytes.Length, receiverRemoteEndPoint);

			#endregion

			#region 因为我已经收到他的打洞消息请求了,所以他就是能发消息给我,我只需要按照他原来的路径给它开一个线程持续发送心跳就行保活就可以了

			Console.WriteLine($"我是打洞的被动方,我已经给他发送了打洞响应消息: {holePunchingResponseMessage},下面开始给他发送心跳包");
			Thread.Sleep(200);
			// 然后我开启一个新的线程去给他发送我的心跳包给他
			ContinuousSendP2PHeartbeatMessagesAsync(receiverRemoteEndPoint);

			#endregion

			if (_myEndPointFromMainStunSecondPortReply == null)
			{
				throw new Exception("STUN响应为空, 无法处理P2P打洞消息");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理P2P打洞消息时出错: {ex.Message}");
			throw;
		}

		return Task.CompletedTask;
	}

	private Task ProcessP2PHolePunchingResponseMessageAsync(byte[] data)
	{
		try
		{
			// 从字节数组中解析P2P打洞响应消息
			var holePunchingResponseMessage = Client2ClientP2PHolePunchingResponseMessage.FromBytes(data);
			// 我是主动方,所以我发出去了打洞消息,才有响应消息
			Console.WriteLine(
				$"收到P2P打洞响应消息: {holePunchingResponseMessage}, 我实际打洞后跟他通讯的地址是: {holePunchingResponseMessage.ActiveClientEndPoint}, 他实际打洞后跟我通讯的地址是: {holePunchingResponseMessage.PassiveClientEndPoint}");

			Console.WriteLine($"我是主动方,我之前已经发送过打洞请求,这是他给我的回应,所以我们已经打通了,下面开始给他发送心跳包");
			// 然后我开启一个新的线程去给她发送我的心跳包给他
			ContinuousSendP2PHeartbeatMessagesAsync(holePunchingResponseMessage.PassiveClientEndPoint);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理P2P打洞响应消息时出错: {ex.Message}");
			throw;
		}

		return Task.CompletedTask;
	}

	#endregion

	#region 处理接收到的TURN广播消息

	private async Task ProcessBroadcastMessageAsync(byte[] data)
	{
		try
		{
			// 从字节数组中解析广播消息
			var broadcastMessage = TURNBroadcastMessage.FromBytes(data);
			Console.WriteLine(
				$"从自己在TURN服务器上暴露的外网端口: {broadcastMessage.ClientSideEndPointToTURN} 收到消息: {broadcastMessage}");
			if (broadcastMessage.Guid == _clientId)
			{
				Console.WriteLine("收到自己的广播消息，忽略");
				return;
			}

			#region 如果广播说我需要给他先抛橄榄枝,那我就先抛橄榄枝

			var needPrepareAcceptIncomingConnectionForThisClient =
				broadcastMessage.IsNeedPrepareAcceptIncomingConnectionForThisClient;
			if (needPrepareAcceptIncomingConnectionForThisClient)
			{
				Console.WriteLine($"收到广播消息,需要我先抛橄榄枝给对方地址: {broadcastMessage.ClientSideEndPointToTURN}");
				for (var i = 1; i <= 10; i++)
				{
					var oliveBranchMessage = new P2PHeartbeatMessage(_clientId, $"NORMAN P2P OLIVE BRANCH {i}");
					var oliveBranchBytes = oliveBranchMessage.ToBytes();
					await _udpClient.SendAsync(oliveBranchBytes, oliveBranchBytes.Length,
						broadcastMessage.ClientSideEndPointToTURN);
					Thread.Sleep(300);
					Console.WriteLine($"已发送第{i}次橄榄枝消息到: {broadcastMessage.ClientSideEndPointToTURN}");
				}

				return;
			}

			#endregion

			#region 如果广播说我要先等他抛橄榄枝,那我需要等待一段时间再打洞

			var needWaitForPrepareAcceptIncomingConnectionForThisClient =
				broadcastMessage.IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient;
			if (needWaitForPrepareAcceptIncomingConnectionForThisClient)
			{
				Console.WriteLine($"收到广播消息,需要我等待对方抛橄榄枝: {broadcastMessage.ClientSideEndPointToTURN}");
				await Task.Delay(500);
				Console.WriteLine($"等待对方抛橄榄枝结束,开始打洞到对方: {broadcastMessage.ClientSideEndPointToTURN}");
			}

			#endregion

			var holePunchingMessage = new Client2ClientP2PHolePunchingRequestMessage
			{
				SourceEndPoint = _myEndPointFromMainStunSecondPortReply,
				DestinationEndPoint = broadcastMessage.ClientSideEndPointToTURN,
				DestinationClientId = broadcastMessage.Guid,
				SourceClientId = _clientId, GroupId = broadcastMessage.GroupGuid, SendTime = DateTime.Now
			};

			//加入到对方的PeerClient集合
			if (!_peerClients.TryGetValue(broadcastMessage.Guid, out var peer))
			{
				_peerClients.Add(broadcastMessage.Guid, new PeerClient(broadcastMessage.ClientSideEndPointToTURN)
				{
					Guid = broadcastMessage.Guid
				});
				Console.WriteLine($"新的PeerClient已加入: {broadcastMessage.Guid}");
			}
			else
			{
				peer.EndPoint = broadcastMessage.ClientSideEndPointToTURN;
			}

			// 处理P2P打洞
			await SendHolePunchingMessageAsync(holePunchingMessage);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理广播消息时出错: {ex.Message}");
			throw;
		}
	}

	#endregion

	#endregion

	#endregion

	#region Out 发送消息

	#region 持续发送P2P心跳包

	private void ContinuousSendP2PHeartbeatMessagesAsync(IPEndPoint sendHeartbeatMessageTo)
	{
		Task.Run(async () =>
		{
			Console.WriteLine("开始发送P2P打洞消息");
			var sentTimes = 0;
			while (_isRunning)
			{
				sentTimes++;
				if (sentTimes > 2000)
				{
					Console.WriteLine("已发送3次心跳包，停止发送");
					break;
				}

				var heartbeatMessage = new P2PHeartbeatMessage(_clientId, $"NORMAN P2P HEARTBEAT {sentTimes}");
				//发送
				var heartbeatBytes = heartbeatMessage.ToBytes();
				await _udpClient.SendAsync(heartbeatBytes, heartbeatBytes.Length,
					sendHeartbeatMessageTo);
				Console.WriteLine($"已发送心跳包到: {sendHeartbeatMessageTo}, 第{sentTimes}次");
				//延迟2秒继续发
				await Task.Delay(2000);
			}
		});
	}

	#endregion

	#region 发送P2P打洞消息

	private async Task SendHolePunchingMessageAsync(Client2ClientP2PHolePunchingRequestMessage message)
	{
		const int maxRetries = 5;
		const int retryDelay = 300;

		for (var i = 0; i < maxRetries; i++)
		{
			try
			{
				var messageBytes = message.ToBytes();
				await _udpClient.SendAsync(messageBytes, messageBytes.Length, message.DestinationEndPoint);
				Console.WriteLine($"P2P打洞消息已经由{message.SourceEndPoint}发送到{message.DestinationEndPoint}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"发送失败 ({i + 1}/{maxRetries}): {ex.Message}");
			}

			if (i < maxRetries - 1)
				await Task.Delay(retryDelay);
		}
	}

	#endregion

	#endregion
}