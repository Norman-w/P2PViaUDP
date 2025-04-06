/*


 如果还不知道自己的NAT类型,不能去注册到TURN服务器,因为注册到TURN服务器需要知道自己的NAT类型.
   如果不是全锥形的NAT,访问STUN A服务器的第一个端口,用A服务器的第二个端口回复,如果能回复,但是用别的IP不能回复,就是限制型的
    如果还是访问STUN A服务器的第一个端口,用A服务器的第二个端口都无法回复,则就是端口限制型的
	    虽然对称的和端口限制型的都是只能从原端口(发起方请求过的端口)返回,但是对称型的每次创建连接的端口


*/

using System.Collections.Concurrent;
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
	private readonly ConcurrentDictionary<Guid, PeerClient> _peerClients = new();

	private readonly object _peerClientsLock = new object();

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
			await LogMyNetWorkInfoAsync();
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

			while (_isRunning)
			{
				//等待用户退出
				Console.WriteLine("输入stop或Ctrl+C退出");
				var input = Console.ReadLine();
				if (input != null && input.Equals("stop", StringComparison.OrdinalIgnoreCase))
				{
					Stop();
					Console.WriteLine("停止监听UDP消息...");
				}
				else
				{
					Console.WriteLine("输入错误,请重新输入");
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"发生错误: {ex.Message}");
		}
	}

	private async Task LogMyNetWorkInfoAsync()
	{
		//获取自己的所有IP地址 IPv4
		var host = await Dns.GetHostEntryAsync(Dns.GetHostName());
		foreach (var ip in host.AddressList)
		{
			if (ip.AddressFamily == AddressFamily.InterNetwork)
			{
				Console.WriteLine($"本机IPv4地址: {ip} , 地址类型: {ip.AddressFamily}, 是否环回: {ip.IsIPv4MappedToIPv6}");
			}
		}

		//获取本机所有的IP地址 IPv6
		foreach (var ip in host.AddressList)
		{
			if (ip.AddressFamily == AddressFamily.InterNetworkV6)
			{
				Console.WriteLine(
					$"本机IPv6地址: {ip} 类型: {ip.AddressFamily}环回?:{ip.IsIPv6LinkLocal}本地?: {ip.IsIPv6SiteLocal},临时?: {ip.IsIPv6Teredo},本地回环?: {ip.IsIPv6Multicast} ");
			}
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
				var messageType = (MessageType)BitConverter.ToInt32(result.Buffer, 0);
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

	private async Task ProcessReceivedMessageAsync(byte[] data, IPEndPoint messageSenderEndPoint)
	{
		var messageType = (MessageType)BitConverter.ToInt32(data, 0);
		Console.WriteLine($"消息类型: {messageType}");
		switch (messageType)
		{
			case MessageType.TURNBroadcast:
				await ProcessBroadcastMessageAsync(data);
				break;
			case MessageType.P2PHolePunchingRequest:
				await ProcessP2PHolePunchingRequestMessageAsync(data, messageSenderEndPoint);
				break;
			case MessageType.P2PHeartbeat:
				await ProcessP2PHeartbeatMessageAsync(data, messageSenderEndPoint);
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
			case MessageType.TURNCheckNATConsistencyRequest:
			case MessageType.TURNCheckNATConsistencyResponse:
				TURNClientLogic.ProcessNATConsistencyResponseMessageAsync(data, _myEndPointFromMainStunSecondPortReply);
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

	private Task ProcessP2PHeartbeatMessageAsync(byte[] data, IPEndPoint messageSenderIPEndPoint)
	{
		try
		{
			// 从字节数组中解析P2P心跳消息
			var heartbeatMessage = P2PHeartbeatMessage.FromBytes(data);
			var senderIdShort = heartbeatMessage.SenderId.ToString()[..8];
			Console.WriteLine($"收到P2P心跳消息，来自: {senderIdShort} 的NAT:{messageSenderIPEndPoint}");

			// 线程安全地更新对方的心跳时间
			lock (_peerClientsLock)
			{
				if (_peerClients.TryGetValue(heartbeatMessage.SenderId, out var peer))
				{
					#region 如果他已经给我发送心跳包了但是我还没给他发过,则需要我再一次打洞,通常这说明我先打洞的,然后他能给我回应,但是由于我第一次打洞的消息实际上他没收到,只是在我这边的NAT上捅出一个窟窿,我们还需要再打一次

					if (peer.LastHeartbeatToHim == null)
					{
						Console.ForegroundColor = ConsoleColor.Red;
						Console.WriteLine($"对方({heartbeatMessage.SenderId})已经给我发送心跳包了,但是我还没给他发过,需要再打一次洞");
						Console.ResetColor();
						ContinuousSendP2PHeartbeatMessagesAsync(
							heartbeatMessage.SenderId,
							messageSenderIPEndPoint);
					}

					#endregion
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

	private Task ProcessP2PHolePunchingRequestMessageAsync(byte[] data, IPEndPoint messageSenderEndPoint)
	{
		try
		{
			// 从字节数组中解析P2P打洞消息
			var holePunchingMessageFromOtherClient = Client2ClientP2PHolePunchingRequestMessage.FromBytes(data);
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			// 消息ID前几位
			var messageId = holePunchingMessageFromOtherClient.RequestId.ToString()[..8];
			Console.WriteLine(
				$"收到P2P打洞消息{messageId}，来自TURN服务器中地址标记为{holePunchingMessageFromOtherClient.SourceEndPoint}的 实际端口为: {messageSenderEndPoint}的客户端");
			Console.ResetColor();

			// 线程安全地处理客户端信息
			PeerClient peer;
			lock (_peerClientsLock)
			{
				// 他要跟我打洞,我看我这边记录没有记录他的信息,如果没记录则记录一下,如果记录了则更新他的端点的相关信息
				var peerId = holePunchingMessageFromOtherClient.SourceClientId;
				if (!_peerClients.TryGetValue(peerId, out peer!))
				{
					var newPeerClient = new PeerClient(messageSenderEndPoint)
					{
						Guid = peerId,
						ReceivedHolePunchMessageFromHimTime = DateTime.Now,
					};
					_peerClients.TryAdd(peerId, newPeerClient);
					peer = newPeerClient;
					Console.WriteLine($"新的PeerClient已加入: {peerId}");
				}
				else
				{
					peer.EndPoint = messageSenderEndPoint;
					peer.ReceivedHolePunchMessageFromHimTime = DateTime.Now;
					Console.WriteLine($"更新PeerClient: {peerId}");
				}

				#region 如果他是对称型的,他过来的时候不一定是什么端口,他自己也不知道,我得告诉他

				if (holePunchingMessageFromOtherClient.SourceNatType == NATTypeEnum.Symmetric)
				{
					Console.WriteLine($"打洞请求的来源是对称型NAT,需要告诉他他自己是什么端口: {messageSenderEndPoint}");
					peer.EndPoint = messageSenderEndPoint;
				}

				#endregion
			}

			#region 给他发送P2P打洞响应消息

			var holePunchingResponseMessage = new Client2ClientP2PHolePunchingResponseMessage
			{
				RequestSenderEndPoint = messageSenderEndPoint,
				RequestReceiverEndPoint = holePunchingMessageFromOtherClient.DestinationEndPoint,
				RequestSenderClientId = holePunchingMessageFromOtherClient.SourceClientId,
				RequestReceiverClientId = holePunchingMessageFromOtherClient.DestinationClientId,
				//把我的NAT类型告诉他,不告诉他的话,只有TURN服务器知道.
				RequestReceiverNATTye = _myNATType,
				GroupId = holePunchingMessageFromOtherClient.GroupId,
				SendTime = DateTime.Now
			};

			var responseBytes = holePunchingResponseMessage.ToBytes();
			_udpClient.SendAsync(responseBytes, responseBytes.Length, messageSenderEndPoint);

			#endregion

			// 然后我开启一个新的线程去给他发送我的心跳包给他
			ContinuousSendP2PHeartbeatMessagesAsync(
				holePunchingMessageFromOtherClient.SourceClientId,
				messageSenderEndPoint);

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

			// 线程安全地检查连接状态
			bool connectionEstablished;
			lock (_peerClientsLock)
			{
				// 如果跟我打洞的这个客户端我们已经有peer的有效连接了 则忽略这个打洞响应即可
				connectionEstablished = _peerClients.Any(
					x => x.Key == holePunchingResponseMessage.RequestReceiverClientId
					     && x.Value.EndPoint.Equals(holePunchingResponseMessage.RequestReceiverEndPoint)
					     && x.Value.IsP2PHasBeenEstablished);
			}

			if (connectionEstablished)
			{
				Console.WriteLine($"对方({holePunchingResponseMessage.RequestReceiverClientId})已经跟我创建连接了,不需要再发送打洞响应消息了");
				return Task.CompletedTask;
			}
			
			#region 必要时更新我的出网信息
			/*
			 如果我是对称型NAT,我刚才发的请求到对方(全锥形或IP受限型)的链接中,对方会告诉我本次出网时的端口,我需要把这个端口更新到我自己的记录当中.
			 注意⚠️:如果我是对称型NAT,但是对方是端口受限型,我是通常(端口变化无规律)是无法和对方打洞的,原因是:
				若端口受限的主动发起连接,不知道对称NAT的的实际出网使用端口,无法打洞
				若对称型的,知道对方(端口受限)的IP+端口,但是端口受限那一端需要[端口受限型之前请求过的端口]才能回应,所以需要端口受限型的先为对称型的开口子,但又不知道应该给对称型开到什么地方,所以陷入僵局无法打洞(除非有规律可预测)
			 */

			if (_myNATType == NATTypeEnum.Symmetric
			    && holePunchingResponseMessage.RequestReceiverNATTye
				    is NATTypeEnum.FullCone
				    or NATTypeEnum.RestrictedCone
			   )
			{
				var oldEndPoint = _myEndPointFromMainStunSecondPortReply;
				// 如果对方是全锥形或IP受限型的,则我可以更新我的出网端口
				_myEndPointFromMainStunSecondPortReply = holePunchingResponseMessage.RequestSenderEndPoint;
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine(
					$"我是对称型NAT🛡, 已更新我的出网端信息从 {oldEndPoint} 到 {holePunchingResponseMessage.RequestSenderEndPoint}");
				Console.ResetColor();
			}
			else
			{
				Console.WriteLine($"不需要更新我的出网端口,当前NAT类型: {_myNATType}");
			}
			#endregion

			
			// 然后我开启一个新的线程去给她发送我的心跳包给他
			ContinuousSendP2PHeartbeatMessagesAsync(
				holePunchingResponseMessage.RequestSenderClientId,
				holePunchingResponseMessage.RequestSenderEndPoint);
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
				$"从自己在TURN服务器上暴露的外网端口: {_myEndPointFromMainStunSecondPortReply} 收到消息: {broadcastMessage}");
			if (broadcastMessage.Guid == _clientId)
			{
				Console.WriteLine("收到自己的广播消息，忽略");
				return;
			}

			await HolePunchingToClientAsync(broadcastMessage);

			// 打洞后检查NAT一致性
			if (_myEndPointFromMainStunSecondPortReply != null)
			{
				try
				{
					await TURNClientLogic.SendCheckNATConsistencyRequestAsync(
						_settings,
						_clientId,
						_udpClient
					);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"打洞后NAT一致性检查失败: {ex.Message}");
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理广播消息时出错: {ex.Message}");
			throw;
		}
	}

	private async Task HolePunchingToClientAsync(TURNBroadcastMessage broadcastMessage)
	{
		bool connectionEstablished;

		// 线程安全地检查连接状态
		lock (_peerClientsLock)
		{
			// 如果跟我打洞的这个客户端我们已经有peer的有效连接了 则忽略这个打洞请求即可
			connectionEstablished = _peerClients.Any(
				x => x.Key == broadcastMessage.Guid
				     && x.Value.EndPoint.Equals(broadcastMessage.ClientSideEndPointToTURN)
				     && x.Value.IsP2PHasBeenEstablished);
		}

		if (connectionEstablished)
		{
			Console.WriteLine($"对方({broadcastMessage.Guid})已经跟我创建连接了,不需要再发送打洞请求了");
			return;
		}

		var holePunchingMessage = new Client2ClientP2PHolePunchingRequestMessage(
			broadcastMessage.GroupGuid,
			broadcastMessage.ClientSideEndPointToTURN,
			broadcastMessage.Guid,
			_myNATType,
			_clientId,
			_myEndPointFromMainStunSecondPortReply
		)
		{
			RequestId = Guid.NewGuid(),
		};

		//线程安全地加入到PeerClient集合中
		lock (_peerClientsLock)
		{
			if (!_peerClients.TryGetValue(broadcastMessage.Guid, out var peer))
			{
				_peerClients.TryAdd(broadcastMessage.Guid, new PeerClient(holePunchingMessage.DestinationEndPoint)
				{
					SendHolePunchMessageToHimTime = DateTime.Now,
					Guid = broadcastMessage.Guid
				});
				Console.WriteLine($"新的PeerClient已加入: {broadcastMessage.Guid}");
			}
			else
			{
				peer.EndPoint = holePunchingMessage.DestinationEndPoint;
			}
		}

		// 处理P2P打洞
		await SendHolePunchingMessageAsync(holePunchingMessage);
	}

	#endregion

	#endregion

	#endregion

	#region Out 发送消息

	#region 持续发送P2P心跳包

	private void ContinuousSendP2PHeartbeatMessagesAsync(Guid heartbeatReceiverClientId,
		IPEndPoint sendHeartbeatMessageTo)
	{
		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine("正在启动心跳进程,请稍等...");
		Console.ResetColor();

		// 使用锁来保证线程安全地检查和更新心跳状态
		var shouldStartHeartbeat = false;
		lock (_peerClientsLock)
		{
			// 查找对应的peer
			if (!_peerClients.TryGetValue(heartbeatReceiverClientId, out var peerEntry))
			{
				Console.WriteLine($"未找到对应的PeerClient: {heartbeatReceiverClientId}");
				return;
			}

			// 检查是否已经启动了心跳
			if (peerEntry.LastHeartbeatToHim == null)
			{
				shouldStartHeartbeat = true;
				peerEntry.LastHeartbeatToHim = DateTime.Now;
			}
		}

		if (!shouldStartHeartbeat)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"已经为终结点 {sendHeartbeatMessageTo} 启动了心跳进程，不需要再次启动");
			Console.ResetColor();
			return;
		}

		// 启动心跳任务
		Task.Run(async () =>
		{
			try
			{
				Console.WriteLine($"开始向 {sendHeartbeatMessageTo} 发送心跳包");
				var sentTimes = 0;
				while (_isRunning)
				{
					sentTimes++;
					if (sentTimes > 2000)
					{
						Console.WriteLine("已发送心跳包超过2000次，停止发送");
						break;
					}

					var heartbeatMessage = new P2PHeartbeatMessage(_clientId, $"NORMAN P2P HEARTBEAT {sentTimes}");
					//发送
					var heartbeatBytes = heartbeatMessage.ToBytes();
					await _udpClient.SendAsync(heartbeatBytes, heartbeatBytes.Length, sendHeartbeatMessageTo);
					Console.WriteLine($"已发送心跳包到: {sendHeartbeatMessageTo}, 第{sentTimes}次");

					// 线程安全地更新心跳时间
					lock (_peerClientsLock)
					{
						if (_peerClients.TryGetValue(heartbeatReceiverClientId, out var currentPeer))
						{
							currentPeer.LastHeartbeatToHim = DateTime.Now;
						}
					}

					//延迟2秒继续发
					await Task.Delay(2000);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"心跳发送异常: {ex.Message}");
			}
		});
	}

	#endregion

	#region 发送P2P打洞消息

	private async Task SendHolePunchingMessageAsync(Client2ClientP2PHolePunchingRequestMessage message)
	{
		const int maxRetries = 1;
		const int retryDelay = 300;

		for (var i = 0; i < maxRetries; i++)
		{
			try
			{
				// 线程安全地检查连接状态
				bool connectionEstablished;
				lock (_peerClientsLock)
				{
					//检查一下这个客户端是不是已经跟我创建连接了.如果创建了,则退出
					connectionEstablished = _peerClients.Any(
						x => x.Key == message.DestinationClientId
						     && x.Value.EndPoint.Equals(message.DestinationEndPoint)
						     && x.Value.IsP2PHasBeenEstablished);
				}

				if (connectionEstablished)
				{
					Console.WriteLine($"对方({message.DestinationClientId})已经跟我创建连接了,不需要再发送打洞消息了");
					break;
				}

				var messageBytes = message.ToBytes();
				await _udpClient.SendAsync(messageBytes, messageBytes.Length, message.DestinationEndPoint);
				var messageId = message.RequestId.ToString()[..8];
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine(
					$"我发出的P2P打洞消息 {messageId} 已经由{message.SourceEndPoint}发送到{message.DestinationEndPoint}");
				Console.ResetColor();
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