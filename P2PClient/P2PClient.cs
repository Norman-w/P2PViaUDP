/*


 如果还不知道自己的NAT类型,不能去注册到TURN服务器,因为注册到TURN服务器需要知道自己的NAT类型.
   如果不是全锥形的NAT,访问STUN A服务器的第一个端口,用A服务器的第二个端口回复,如果能回复,但是用别的IP不能回复,就是限制型的
    如果还是访问STUN A服务器的第一个端口,用A服务器的第二个端口都无法回复,则就是端口限制型的
	    虽然对称的和端口限制型的都是只能从原端口(发起方请求过的端口)返回,但是对称型的每次创建连接的端口


*/

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PViaUDP;
using P2PViaUDP.Model;
using P2PViaUDP.Model.Client;
using P2PViaUDP.Model.STUN;
using P2PViaUDP.Model.TURN;
using TURNServer;

namespace P2PClient;

public class P2PClient
{
	#region 私有字段

	/// <summary>
	/// 跟我打洞的客户端集合,key是对方的Guid,value是对方的信息以及和我的相关交互信息
	/// </summary>
	private Dictionary<Guid, PeerClient> _peerClients = new();

	private readonly UdpClient _udpClient = new();
	private readonly P2PClientConfig _settings = P2PClientConfig.Default;
	/// <summary>
	/// 从主STUN服务器的主端口响应中获取到的我的公网IP和端口
	/// </summary>
	private IPEndPoint? _myEndPointFromMainStunMainPortReply;
	/// <summary>
	/// 什么时间确定的我是全锥形的NAT,如果我并不是全锥形的NAT,那么这个值就是null
	/// </summary>
	private DateTime? _determinedFullConeTime;
	private NATTypeEnum _myNATType = NATTypeEnum.Unknown;
	private readonly Guid _clientId = Guid.NewGuid();
	private bool _isRunning;

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
			// STUN 阶段
			await RequestStunServerAsync(true);
			await RequestAnOtherStunServerAsync(false);

			// TURN 阶段
			await RegisterToTurnServerAsync();

			// 持续监听
			await StartListeningAsync();
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

	#region STUN 流程控制

	#region 请求STUN服务器

	/// <summary>
	/// 是否使用相同的端口想不同的服务器端口请求数据
	/// 如果为true, 只会使用最初创建的udpClient进行对外不同端口的请求,比如都是从一个随机端口55555 请求到 3478, 3479, 3480 ... 3497
	/// 如果为false, 则会使用不同的新创建的udp client进行对外不同端口的请求,比如从55555->3478, 54321->3479, 54545->3480 ...
	///		如果使用同一个UDP客户端udpClient对象请求同一个服务器的不同的端口,在服务器收到的都是来自于客户端公网IP的只有一个端口的连接, 那么很可能就是全锥形的NAT
	///			但是如果用多个客户端udpClient对象请求同一个服务器的不同的端口的话,仍然还是机会在服务端看到来自多个公网端口的连接, 但通常全锥形的端口号是有序递增且是偶数号的
	///		否则如果使用同一个UDP客户端udpClient或者是使用不同的udpClient来请求,在服务端都收到随机的端口,且怎样都会出现多个连接的话,那么基本就是对称型的.
	/// </summary>
	/// <param name="useSameUdpClientToRequestDiffServerPorts"></param>
	private async Task RequestStunServerAsync(bool useSameUdpClientToRequestDiffServerPorts)
	{
		#region 输出日志

		Console.ForegroundColor = useSameUdpClientToRequestDiffServerPorts
			? ConsoleColor.DarkGreen
			: Console.ForegroundColor = ConsoleColor.DarkYellow;
		var stringBuilder = new StringBuilder("执行STUN请求测试,当前正在使用");
		stringBuilder.Append(useSameUdpClientToRequestDiffServerPorts ? "同一个出网客户端连接实例" : "多个不同的出网客户端连接实例");
		stringBuilder.Append("向服务器的不同端口请求实例");
		stringBuilder.Append(useSameUdpClientToRequestDiffServerPorts ? "" : "***但配置中的第一个服务端端口仍然会以初始客户端连接实例进行请求***");
		Console.WriteLine(stringBuilder.ToString());
		Console.ResetColor();

		#endregion

		#region 如果IP设置的不是IP的格式(域名)要解析成IP

		var domain = _settings.STUNMainServerIP;
		if (!IPAddress.TryParse(domain, out var _))
		{
			var ip = await Dns.GetHostAddressesAsync(domain);
			_settings.STUNMainServerIP = ip[0].ToString();
		}

		var serverEndPoint = new IPEndPoint(
			IPAddress.Parse(_settings.STUNMainServerIP),
			_settings.STUNMainServerSecondaryPort
		);

		#endregion

		#region 构建STUN请求消息

		var stunRequest = new StunMessage(
			MessageType.StunRequest,
			MessageSource.Client,
			_clientId,
			serverEndPoint
		);

		#endregion

		#region 发送STUN请求消息

		var requestBytes = stunRequest.ToBytes();
		await _udpClient.SendAsync(requestBytes, requestBytes.Length, serverEndPoint);

		#endregion

		#region 接收STUN响应消息

		var receiveResult = await _udpClient.ReceiveAsync();
		var response = StunMessage.FromBytes(receiveResult.Buffer);

		#endregion

		#region 处理STUN响应消息(获取到的公网IP和端口)

		if (response.MessageType == MessageType.StunResponse)
		{
			_myEndPointFromMainStunMainPortReply = response.ClientEndPoint;
			Console.WriteLine($"STUN 响应: 公网终端点 {_myEndPointFromMainStunMainPortReply}");
		}

		#endregion
	}

	/// <summary>
	/// 像另外一个STUN服务器发送请求,我们可以利用这个方法来侦测相同一个客户端或新开udp客户端以后的端口变化,便于我们进行真正出网请求的猜测.
	/// 如果是全锥形网络则这个步骤是不需要的,因为我们可以从任何一个地址来访问客户端对外打出的口子
	/// </summary>
	/// <param name="useNewUdpClient"></param>
	private async Task RequestAnOtherStunServerAsync(bool useNewUdpClient)
	{
		//TODO 移除这个测试的代码
		const string theOtherStunServerIp = "121.22.36.190";
		const int theOtherStunServerPort = 3478;
		var realUsingUdpClient = useNewUdpClient ? new UdpClient() : _udpClient;
		var serverEndPoint = new IPEndPoint(IPAddress.Parse(theOtherStunServerIp), theOtherStunServerPort);
		var stunMessage = new StunMessage(MessageType.StunRequest, MessageSource.Client, _clientId, serverEndPoint);
		var bytes = stunMessage.ToBytes();
		await realUsingUdpClient.SendAsync(bytes, bytes.Length, serverEndPoint);
		const int waitAnOtherStunServerResponseDelayMs = 2000;
		var timeoutTask = Task.Delay(waitAnOtherStunServerResponseDelayMs);
		var waitReceiveResponseTask = realUsingUdpClient.ReceiveAsync();
		var completedTask = await Task.WhenAny(timeoutTask, waitReceiveResponseTask);
		if (completedTask == waitReceiveResponseTask)
		{
			var stunResponseMessage = StunMessage.FromBytes(waitReceiveResponseTask.Result.Buffer);
			var natEndPointToThisOtherServer = stunResponseMessage.ClientEndPoint;
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine($"客户端到另外一个STUN服务器{serverEndPoint}的NAT外网信息为:{natEndPointToThisOtherServer}");

			#region 如果发现到另外一台STUN服务器的NAT外网信息和之前的一样,则说明是全锥形网络
			if (_myEndPointFromMainStunMainPortReply != null && natEndPointToThisOtherServer != null &&
			    _myEndPointFromMainStunMainPortReply.Address.Equals(natEndPointToThisOtherServer.Address) &&
			    _myEndPointFromMainStunMainPortReply.Port == natEndPointToThisOtherServer.Port)
			{
				_myNATType = NATTypeEnum.FullCone;
				_determinedFullConeTime = DateTime.Now;
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("🎉🎉🎉恭喜!到另外一台STUN服务器的NAT外网信息和之前的一样,说明是全锥形网络🎉🎉🎉");
				Console.WriteLine($"你应该可以通过任何一个公网IP和端口访问到这个客户端地址: {_myEndPointFromMainStunMainPortReply}");
				Console.ResetColor();
			}
			#endregion
		}
		else
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"客户端到另外一个STUN服务器{serverEndPoint}的请求失败了,超过{waitAnOtherStunServerResponseDelayMs}ms没有收到服务器结果");
		}
		Console.ResetColor();
	}

	#endregion

	#endregion

	#region TURN 流程控制

	#region 注册到TURN服务器
	private async Task RegisterToTurnServerAsync()
	{
		try
		{
			//如果配置的TURN服务器IP不是IP格式的话要解析成IP
			var domain = _settings.TURNServerIP;
			if (!IPAddress.TryParse(domain, out var _))
			{
				var ip = await Dns.GetHostAddressesAsync(domain);
				_settings.TURNServerIP = ip[0].ToString();
			}

			if (_myEndPointFromMainStunMainPortReply == null)
			{
				throw new Exception("STUN响应为空");
			}

			var registerMessage = new TURNRegisterMessage
			{
				EndPoint = _myEndPointFromMainStunMainPortReply,
				Guid = _clientId,
				GroupGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
				DetectedNATType = _myNATType
			};

			var turnServerEndPoint = new IPEndPoint(
				IPAddress.Parse(_settings.TURNServerIP),
				_settings.TURNServerPrimaryPort
			);

			Console.WriteLine($"正在向TURN服务器注册: {turnServerEndPoint}");
			Console.WriteLine($"本地终端点: {_myEndPointFromMainStunMainPortReply}");

			var registerBytes = registerMessage.ToBytes();
			Console.WriteLine($"发送数据大小: {registerBytes.Length}");

			await _udpClient.SendAsync(registerBytes, registerBytes.Length, turnServerEndPoint);
			Console.WriteLine("TURN注册消息已发送");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"TURN注册失败: {ex}");
			throw;
		}
	}

	#endregion

	#endregion

	#region 开始监听自己的端口

	private async Task StartListeningAsync()
	{
		while (_isRunning)
		{
			try
			{
				var receiveResult = await _udpClient.ReceiveAsync();
				var receiverRemoteEndPoint = receiveResult.RemoteEndPoint;
				await ProcessReceivedMessageAsync(receiveResult.Buffer, receiverRemoteEndPoint);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"接收消息错误: {ex.Message}");
			}
		}
	}

	#endregion

	#region In 处理消息

	#region 入口(消息类型路由)

	#region 处理接收到的消息总入口

	private async Task ProcessReceivedMessageAsync(byte[] data, IPEndPoint receiverRemoteEndPoint)
	{
		Console.WriteLine($"收到来自: {receiverRemoteEndPoint} 的消息，大小: {data.Length}, 内容: {BitConverter.ToString(data)}");
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
			case MessageType.TURNRegister:
			case MessageType.TURNServer2ClientHeartbeat:
			case MessageType.TURNClient2ServerHeartbeat:
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
					Console.WriteLine($"首次收到对方({heartbeatMessage.SenderId})的心跳时间: {peer.LastHeartbeatFromHim}, 开始给他发送心跳包");
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
			Thread.Sleep(1000);
			// 然后我开启一个新的线程去给他发送我的心跳包给他
			ContinuousSendP2PHeartbeatMessagesAsync(receiverRemoteEndPoint);

			#endregion

			if (_myEndPointFromMainStunMainPortReply == null)
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
			Console.WriteLine($"收到P2P打洞响应消息: {holePunchingResponseMessage}, 我实际打洞后跟他通讯的地址是: {holePunchingResponseMessage.ActiveClientEndPoint}, 他实际打洞后跟我通讯的地址是: {holePunchingResponseMessage.PassiveClientEndPoint}");

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
		if (_myEndPointFromMainStunMainPortReply == null)
		{
			throw new Exception("STUN响应为空, 无法处理广播消息");
		}

		try
		{
			// 从字节数组中解析广播消息
			var broadcastMessage = TURNBroadcastMessage.FromBytes(data);
			Console.WriteLine($"从自己在TURN服务器上暴露的外网端口: {broadcastMessage.ClientSideEndPointToTURN} 收到消息: {broadcastMessage}");
			if (broadcastMessage.Guid == _clientId)
			{
				Console.WriteLine("收到自己的广播消息，忽略");
				return;
			}

			var holePunchingMessage = new Client2ClientP2PHolePunchingRequestMessage
			{
				SourceEndPoint = _myEndPointFromMainStunMainPortReply,
				DestinationEndPoint = broadcastMessage.ClientSideEndPointToTURN, DestinationClientId = broadcastMessage.Guid,
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
		if (_myEndPointFromMainStunMainPortReply == null)
		{
			throw new Exception("STUN响应为空, 无法发送P2P打洞消息");
		}

		const int maxRetries = 2;
		const int retryDelay = 1000;

		for (var i = 0; i < maxRetries; i++)
		{
			try
			{
				var messageBytes = message.ToBytes();
				await _udpClient.SendAsync(messageBytes, messageBytes.Length, message.DestinationEndPoint);
				Console.WriteLine($"P2P打洞消息已经由{message.SourceEndPoint}发送到{message.DestinationEndPoint}");
				return;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"发送失败 ({i + 1}/{maxRetries}): {ex.Message}");
				if (i < maxRetries - 1)
					await Task.Delay(retryDelay);
			}
		}
	}

	#endregion

	#endregion
}