using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PViaUDP;
using P2PViaUDP.Model;
using P2PViaUDP.Model.Client;
using P2PViaUDP.Model.STUN;
using P2PViaUDP.Model.TURN;

namespace P2PClient;

public class P2PClient
{
	#region 私有字段

	/// <summary>
	/// 跟我打洞的客户端集合,key是对方的Guid,value是对方的信息以及和我的相关交互信息
	/// </summary>
	private Dictionary<Guid, PeerClient> _peerClients = new();

	private readonly UdpClient _udpClient = new();
	private readonly Settings _settings = new();
	private IPEndPoint? _myEndPointFromStunReply;
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

		Console.WriteLine($"STUN服务器IP: {_settings.STUNServerIP}");

		#endregion

		try
		{
			// STUN 阶段
			await RequestStunServerAsync(true);
			await RequestAnOtherStunServerAsync(true);

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

		var domain = _settings.STUNServerIP;
		if (!IPAddress.TryParse(domain, out var _))
		{
			var ip = await Dns.GetHostAddressesAsync(domain);
			_settings.STUNServerIP = ip[0].ToString();
		}

		var serverEndPoint = new IPEndPoint(
			IPAddress.Parse(_settings.STUNServerIP),
			_settings.STUNServerPort
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
			_myEndPointFromStunReply = response.ClientEndPoint;
			Console.WriteLine($"STUN 响应: 公网终端点 {_myEndPointFromStunReply}");
		}

		#endregion

		#region 每隔50MS(暂定)向额外STUN端口请求进行连接以供STUN能抓到本机的公网IP和端口变化规律

		var allReceivedTasks = new List<Task>();
		var allStunResponseMessages = new ConcurrentBag<StunMessage>();

		//注意IP可能确实是不同的,因为我的ID不变但是出网可能因为双线光纤之类的自动切换
		foreach (var additionalPort in _settings.STUNServerAdditionalPorts)
		{
			var additionalServerEndPoint = new IPEndPoint(
				IPAddress.Parse(_settings.STUNServerIP),
				additionalPort
			);

			var additionalStunRequest = new StunMessage(
				MessageType.StunRequest,
				MessageSource.Client,
				_clientId,
				additionalServerEndPoint
			);

			var additionalRequestBytes = additionalStunRequest.ToBytes();
			var realUsingOutGoingUdpClient = useSameUdpClientToRequestDiffServerPorts
				? _udpClient //使用同一个客户端发送给不同的端口
				: new UdpClient(); //使用不同的新建的客户端发送给不同的端口

			await realUsingOutGoingUdpClient.SendAsync(additionalRequestBytes, additionalRequestBytes.Length,
				additionalServerEndPoint);

			// 发送后转接收,等待5秒后关闭,使用等待2秒的task和等待接收消息的task,同时执行谁wait完毕了以后就整体退出
			var delayCloseTask = Task.Delay(2000);
			var receiveTask = realUsingOutGoingUdpClient.ReceiveAsync();
			allReceivedTasks.Add(receiveTask);
			_ = Task.Run(async () =>
			{
				var completedTask = await Task.WhenAny(delayCloseTask, receiveTask);
				var stunResponse = StunMessage.FromBytes(receiveTask.Result.Buffer);
				allStunResponseMessages.Add(stunResponse);
				Console.WriteLine(completedTask == receiveTask
					? $"来自{additionalServerEndPoint}的响应:{stunResponse}"
					: $"请求到等待响应超时: {additionalServerEndPoint}");
			});
			const int delayMs = 50;
			Console.WriteLine($"已发送额外STUN请求到: {additionalServerEndPoint}, 休息{delayMs}毫秒后将继续");
			
			Thread.Sleep(delayMs);
		}

		#endregion

		#region 等待所有的超时机和所有的接收任务结束,或者是如果总用时超过了5秒的话,结束等待,反馈结果

		const int allTaskShouldBeCompletedWithinMs = 5000;
		var timeoutTask = Task.Delay(allTaskShouldBeCompletedWithinMs);
		var allTasks = Task.WhenAll(allReceivedTasks);
		var firstCompletedTask = await Task.WhenAny(timeoutTask, allTasks);

		if (firstCompletedTask == timeoutTask)
		{
			Console.WriteLine($"等待时间到,已等待{allTaskShouldBeCompletedWithinMs}MS,并非所有任务完成,这通常应该是个bug");
		}
		else
		{
			var ports = new List<int>();
			foreach (var message in allStunResponseMessages)
			{
				if (message.ClientEndPoint == null)
					continue;
				if (ports.Contains(message.ClientEndPoint.Port))
				{
					continue;
				}
				ports.Add(message.ClientEndPoint.Port);
			}

			//输出反馈结果
			if (ports.Count == 1)
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine("恭喜,你访问多个STUN服务器只使用了一个端口,这意味着你并[不是对称型NAT]");
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine(
					$"祝你好运!你出网访问的时候,你的外网给你分配的NAT端口有{ports.Count}个" +
					$"看起来不是Full Cone(全锥形)并不很好打洞" +
					$"分别是:{string.Join(", ", ports)}");
			}

			Console.ResetColor();
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

			if (_myEndPointFromStunReply == null)
			{
				throw new Exception("STUN响应为空");
			}

			var registerMessage = new TURNRegisterMessage
			{
				EndPoint = _myEndPointFromStunReply,
				Guid = _clientId,
				GroupGuid = Guid.Parse("00000000-0000-0000-0000-000000000001")
			};

			var turnServerEndPoint = new IPEndPoint(
				IPAddress.Parse(_settings.TURNServerIP),
				_settings.TURNServerPort
			);

			Console.WriteLine($"正在向TURN服务器注册: {turnServerEndPoint}");
			Console.WriteLine($"本地终端点: {_myEndPointFromStunReply}");

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
				await ProcessReceivedMessageAsync(receiveResult.Buffer);
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

	private async Task ProcessReceivedMessageAsync(byte[] data)
	{
		Console.WriteLine($"收到消息，大小: {data.Length}, 内容: {BitConverter.ToString(data)}");
		var messageType = (MessageType)data[0];
		switch (messageType)
		{
			case MessageType.TURNBroadcast:
				await ProcessBroadcastMessageAsync(data);
				break;
			case MessageType.P2PHolePunchingRequest:
				await ProcessP2PHolePunchingMessageAsync(data);
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
				peer.LastHeartbeatFromHim = DateTime.Now;
				Console.WriteLine($"已更新对方的心跳时间: {heartbeatMessage.SenderId}");
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

	private Task ProcessP2PHolePunchingMessageAsync(byte[] data)
	{
		try
		{
			// 从字节数组中解析P2P打洞消息
			var holePunchingMessageFromOtherClient = Client2ClientP2PHolePunchingRequestMessage.FromBytes(data);
			Console.WriteLine($"收到P2P打洞消息，来自: {holePunchingMessageFromOtherClient.SourceEndPoint}");
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

			if (_myEndPointFromStunReply == null)
			{
				throw new Exception("STUN响应为空, 无法处理P2P打洞消息");
			}

			// 然后我开启一个新的线程去给她发送我的心跳包给他
			ContinuousSendP2PHeartbeatMessagesAsync(holePunchingMessageFromOtherClient);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"处理P2P打洞消息时出错: {ex.Message}");
			throw;
		}

		return Task.CompletedTask;
	}

	#endregion

	#region 处理接收到的TURN广播消息

	private async Task ProcessBroadcastMessageAsync(byte[] data)
	{
		if (_myEndPointFromStunReply == null)
		{
			throw new Exception("STUN响应为空, 无法处理广播消息");
		}

		try
		{
			// 从字节数组中解析广播消息
			var broadcastMessage = TURNBroadcastMessage.FromBytes(data);
			Console.WriteLine($"收到广播消息，来自: {broadcastMessage.EndPoint}");
			if (broadcastMessage.Guid == _clientId)
			{
				Console.WriteLine("收到自己的广播消息，忽略");
				return;
			}

			var holePunchingMessage = new Client2ClientP2PHolePunchingRequestMessage
			{
				SourceEndPoint = _myEndPointFromStunReply,
				DestinationEndPoint = broadcastMessage.EndPoint, DestinationClientId = broadcastMessage.Guid,
				SourceClientId = _clientId, GroupId = broadcastMessage.GroupGuid, SendTime = DateTime.Now
			};

			//加入到对方的PeerClient集合
			if (!_peerClients.TryGetValue(broadcastMessage.Guid, out var peer))
			{
				_peerClients.Add(broadcastMessage.Guid, new PeerClient(broadcastMessage.EndPoint)
				{
					Guid = broadcastMessage.Guid
				});
				Console.WriteLine($"新的PeerClient已加入: {broadcastMessage.Guid}");
			}
			else
			{
				peer.EndPoint = broadcastMessage.EndPoint;
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

	private void ContinuousSendP2PHeartbeatMessagesAsync(
		Client2ClientP2PHolePunchingRequestMessage holePunchingMessageFromOtherClient)
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
					holePunchingMessageFromOtherClient.SourceEndPoint);
				Console.WriteLine($"已发送心跳包到: {holePunchingMessageFromOtherClient.SourceEndPoint}, 第{sentTimes}次");
				//延迟2秒继续发
				await Task.Delay(2000);
			}
		});
	}

	#endregion

	#region 发送P2P打洞消息

	private async Task SendHolePunchingMessageAsync(Client2ClientP2PHolePunchingRequestMessage message)
	{
		if (_myEndPointFromStunReply == null)
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
				Console.WriteLine("P2P打洞消息已发送");
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