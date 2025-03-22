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
	/// 来自从服务器的STUN服务器的主端口响应中获取到的我的公网IP和端口
	/// </summary>
	private IPEndPoint? _myEndPointFromSlaveStunMainPortReply;

	/// <summary>
	/// 来自主STUN服务器的次要端口响应中获取到的我的公网IP和端口
	/// </summary>
	private IPEndPoint? _myEndPointFromMainStunSecondaryPortReply;

	/// <summary>
	/// 来自从STUN服务器的次要端口响应中获取到的我的公网IP和端口
	/// </summary>
	private IPEndPoint? _myEndPointFromSlaveStunSecondaryPortReply;

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
			await RequestStunServerAsync();
			
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

	private async Task RequestStunServerAsync()
	{
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

		#region 构建STUN请求消息, 先发送是否对称型的检测包

		var isSymmetricNATTypeCheckingRequest = new StunNATTypeCheckingRequest(
			Guid.NewGuid(),
			StunNATTypeCheckingRequest.SubCheckingTypeEnum.IsSymmetric,
			_clientId,
			serverEndPoint,
			DateTime.Now
		);

		#endregion

		#region 发送STUN请求消息

		var isSymmetricNATTypeCheckingRequestBytes = isSymmetricNATTypeCheckingRequest.ToBytes();
		// await _udpClient.SendAsync(isSymmetricNATTypeCheckingRequestBytes, isSymmetricNATTypeCheckingRequestBytes.Length, serverEndPoint);

		// 创建所有发送任务,分别发送到4个服务器端点
		var sendTasks = new[]
		{
			// 主服务器主端口
			_udpClient.SendAsync(
				isSymmetricNATTypeCheckingRequestBytes,
				isSymmetricNATTypeCheckingRequestBytes.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNMainServerIP),
					_settings.STUNMainAndSlaveServerPrimaryPort)
			),

			// 主服务器次端口
			_udpClient.SendAsync(
				isSymmetricNATTypeCheckingRequestBytes,
				isSymmetricNATTypeCheckingRequestBytes.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNMainServerIP),
					_settings.STUNMainServerSecondaryPort)
			),

			// 从服务器主端口
			_udpClient.SendAsync(
				isSymmetricNATTypeCheckingRequestBytes,
				isSymmetricNATTypeCheckingRequestBytes.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNSlaveServerIP),
					_settings.STUNMainAndSlaveServerPrimaryPort)
			),

			// 从服务器次端口
			_udpClient.SendAsync(
				isSymmetricNATTypeCheckingRequestBytes,
				isSymmetricNATTypeCheckingRequestBytes.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNSlaveServerIP),
					_settings.STUNSlaveServerSecondaryPort)
			)
		};

		// 并行执行所有发送任务,只要有一个发送成功就进入到接收状态防止漏掉消息.
		await Task.WhenAll(sendTasks);
		Console.WriteLine("所有的STUN请求消息已发送");
		#endregion

		var isSymmetricCheckingResult = await ReceiveIsSymmetricCheckingRequestStunResponses(2000);
		if (isSymmetricCheckingResult == NATTypeEnum.Symmetric)
		{
			_myNATType = NATTypeEnum.Symmetric;
			Console.WriteLine("检测到对称型NAT,不需要测试了");
			return;
		}
		if (isSymmetricCheckingResult == NATTypeEnum.Unknown)
		{
			Console.WriteLine("经过第一轮测试,无法确定NAT类型,需要进入下一轮测试");
		}

		#region 经过第一轮测试没有确定下来是对称型的NAT的话,继续进行其他三类的测试
		
		//先清空第一轮所有的已经检测到的我的公网IP和端口记录,进行第二轮
		_myEndPointFromMainStunMainPortReply = null;
		_myEndPointFromMainStunSecondaryPortReply = null;
		_myEndPointFromSlaveStunMainPortReply = null;
		_myEndPointFromSlaveStunSecondaryPortReply = null;

		var whichKindOfConeNATTypeCheckingRequest = new StunNATTypeCheckingRequest(
			Guid.NewGuid(),
			StunNATTypeCheckingRequest.SubCheckingTypeEnum.WhichKindOfCone,
			_clientId,
			serverEndPoint,
			DateTime.Now
		);
		var whichKindOfConeNATTypeCheckingRequestBytes = whichKindOfConeNATTypeCheckingRequest.ToBytes();
		//只需要发送给主服务器的主要端口.主服务器接收到消息以后会转发到从服务器,然后主服务器的两个端口尝试返回,从服务器的两个端口尝试返回.
		await _udpClient.SendAsync(whichKindOfConeNATTypeCheckingRequestBytes, whichKindOfConeNATTypeCheckingRequestBytes.Length, serverEndPoint);
		var whichKindOfConeCheckingResult = await ReceiveWhichKindOfConeCheckingRequestStunResponses(2000);
		_myNATType = whichKindOfConeCheckingResult;
		#endregion
	}

	private async Task<NATTypeEnum> ReceiveWhichKindOfConeCheckingRequestStunResponses(int timeoutMs)
	{
		var responses = new List<StunNATTypeCheckingResponse>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs)); // 2秒超时

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				try
				{
					// 设置接收超时
					var receiveTask = _udpClient.ReceiveAsync();
					var completedTask = await Task.WhenAny(receiveTask, Task.Delay(2000, cts.Token));

					if (completedTask != receiveTask)
					{
						break; // 超时退出
					}

					var result = await receiveTask;
					var messageType = (MessageType)result.Buffer[0];

					if (messageType == MessageType.StunNATTypeCheckingResponse)
					{
						var response = StunNATTypeCheckingResponse.FromBytes(result.Buffer);
						ProcessIsSymmetricStunNATTypeCheckingResponse(response);
						responses.Add(response);

						// 如果收到了所有4个预期的响应，提前结束
						if (responses.Count >= 4)
						{
							break;
						}
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
		finally
		{
			cts.Cancel();
		}
		
		return AnalyzeWhichKindOfConeCheckingResponses(responses);
	}

	private NATTypeEnum AnalyzeWhichKindOfConeCheckingResponses(List<StunNATTypeCheckingResponse> responses)
	{
		/*
		 如果只有一个回信,是从主服务器的主端口返回的,那么就是IP限制+端口限制型的
		 如果只有主服务器的2个端口返回的,那就就是IP限制型的
		 如果有4个回信是从主服务器的主端口从端口以及从服务器的主端口从端口的,那就是全锥形的 啥都可以访问的
		*/
		var fromMainServerPrimaryPort = responses.FirstOrDefault(r =>
			r.IsFromMainSTUNServer && r.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort);
		var fromMainServerSecondaryPort = responses.FirstOrDefault(r =>
			r.IsFromMainSTUNServer && r.StunServerEndPoint.Port == _settings.STUNMainServerSecondaryPort);
		var fromSlaveServerPrimaryPort = responses.FirstOrDefault(r =>
			r.IsFromSlaveSTUNServer && r.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort);
		var fromSlaveServerSecondaryPort = responses.FirstOrDefault(r =>
			r.IsFromSlaveSTUNServer && r.StunServerEndPoint.Port == _settings.STUNSlaveServerSecondaryPort);
		if (fromMainServerPrimaryPort != null 
		    && fromMainServerSecondaryPort == null 
		    && fromSlaveServerPrimaryPort == null 
		    && fromSlaveServerSecondaryPort == null)
		{
			Console.WriteLine("只有一个回信,是从主服务器的主端口返回的,那么就是IP限制+端口限制型的");
			return NATTypeEnum.PortRestrictedCone;
		}

		if (fromMainServerPrimaryPort != null 
		    && fromMainServerSecondaryPort != null 
		    && fromSlaveServerPrimaryPort == null 
		    && fromSlaveServerSecondaryPort == null)
		{
			Console.WriteLine("只有主服务器的2个端口返回的,那就就是IP限制型的");
			return NATTypeEnum.RestrictedCone;
		}
		if (fromMainServerPrimaryPort != null 
		    && fromMainServerSecondaryPort != null 
		    && fromSlaveServerPrimaryPort != null 
		    && fromSlaveServerSecondaryPort != null)
		{
			Console.WriteLine("有4个回信是从主服务器的主端口从端口以及从服务器的主端口从端口的,那就是全锥形的 啥都可以访问的");
			return NATTypeEnum.FullCone;
		}

		Console.WriteLine("第二轮检测中无法确定NAT类型");
		return NATTypeEnum.Unknown;
	}

	public async Task<NATTypeEnum> ReceiveIsSymmetricCheckingRequestStunResponses(int timeoutMs)
	{
		var responses = new List<StunNATTypeCheckingResponse>();
		var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs)); // 2秒超时

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				try
				{
					// 设置接收超时
					var receiveTask = _udpClient.ReceiveAsync();
					var completedTask = await Task.WhenAny(receiveTask, Task.Delay(2000, cts.Token));

					if (completedTask != receiveTask)
					{
						Console.WriteLine("接收STUN响应超时");
						break; // 超时退出
					}

					var result = await receiveTask;
					var messageType = (MessageType)result.Buffer[0];

					if (messageType == MessageType.StunNATTypeCheckingResponse)
					{
						var response = StunNATTypeCheckingResponse.FromBytes(result.Buffer);
						//输出响应中的客户端外网端点信息:
						Console.WriteLine($"收到STUN响应: {result.RemoteEndPoint}, 报告的我的外网信息: {response.DetectedClientNATEndPoint}");
						ProcessWhichKindOfConeStunNATTypeCheckingResponse(response);
						responses.Add(response);

						// 如果收到了所有4个预期的响应，提前结束
						if (responses.Count >= 4)
						{
							break;
						}
					}
				}
				catch (OperationCanceledException)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine("接收STUN响应超时");
					Console.ResetColor();
					break;
				}
			}
		}
		finally
		{
			cts.Cancel();
		}

		return AnalyzeIsSymmetricCheckingResponses(responses);
	}

	private void ProcessWhichKindOfConeStunNATTypeCheckingResponse(StunNATTypeCheckingResponse response)
	{
		if (response.IsFromMainSTUNServer)
		{
			if (response.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort)
			{
				_myEndPointFromMainStunMainPortReply = response.DetectedClientNATEndPoint;
			}
			if (response.StunServerEndPoint.Port == _settings.STUNMainServerSecondaryPort)
			{
				_myEndPointFromMainStunSecondaryPortReply = response.DetectedClientNATEndPoint;
			}
		}
		else if (response.IsFromSlaveSTUNServer)
		{
			if (response.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort)
			{
				_myEndPointFromSlaveStunMainPortReply = response.DetectedClientNATEndPoint;
			}
			else if (response.StunServerEndPoint.Port == _settings.STUNSlaveServerSecondaryPort)
			{
				_myEndPointFromSlaveStunSecondaryPortReply = response.DetectedClientNATEndPoint;
			}
		}
		else
		{
			Console.WriteLine(
				$"未知来源的STUN服务器和内容,来源: {response.StunServerEndPoint}, 我的NAT公网信息: {response.DetectedClientNATEndPoint}");
		}
	}

	private void ProcessIsSymmetricStunNATTypeCheckingResponse(StunNATTypeCheckingResponse response)
	{
		if (response.IsFromMainSTUNServer)
		{
			if (response.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort)
			{
				_myEndPointFromMainStunMainPortReply = response.DetectedClientNATEndPoint;
			}
			else if (response.StunServerEndPoint.Port == _settings.STUNMainServerSecondaryPort)
			{
				_myEndPointFromMainStunSecondaryPortReply = response.DetectedClientNATEndPoint;
			}
		}
		else if (response.IsFromSlaveSTUNServer)
		{
			if (response.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort)
			{
				_myEndPointFromSlaveStunMainPortReply = response.DetectedClientNATEndPoint;
			}
			else if (response.StunServerEndPoint.Port == _settings.STUNSlaveServerSecondaryPort)
			{
				_myEndPointFromSlaveStunSecondaryPortReply = response.DetectedClientNATEndPoint;
			}
		}
		else
		{
			Console.WriteLine(
				$"未知来源的STUN服务器和内容,来源: {response.StunServerEndPoint}, 我的NAT公网信息: {response.DetectedClientNATEndPoint}");
		}
	}

	private NATTypeEnum AnalyzeIsSymmetricCheckingResponses(List<StunNATTypeCheckingResponse> responses)
	{
		if (responses.Count != 4)
		{
			Console.WriteLine($"收到的STUN响应数量不正确,应为4,实际为{responses.Count}");
			#region 根据收到的ip数量判断,如果是只有一个IP收到了,报告一下是主服务器掉故障了还是从服务器.
			var isMainServerError = !responses.Any(r => r.IsFromMainSTUNServer);
			var isSlaveServerError = !responses.Any(r => r.IsFromSlaveSTUNServer);
			if (isMainServerError)
			{
				Console.ForegroundColor = ConsoleColor.DarkRed;
				Console.WriteLine("应该是主STUN服务器故障了");
			}
			if (isSlaveServerError)
			{
				Console.ForegroundColor = ConsoleColor.DarkRed;
				Console.WriteLine("应该是从STUN服务器故障了");
			}
			Console.ResetColor();
			#endregion
			return NATTypeEnum.Unknown;
		}
		else
		{
			#region 缺一不可

			if (_myEndPointFromMainStunMainPortReply == null)
			{
				Console.WriteLine("没有收到主STUN服务器主端口的响应,无法确定NAT类型");
				return NATTypeEnum.Unknown;
			}
			if (_myEndPointFromMainStunSecondaryPortReply == null)
			{
				Console.WriteLine("没有收到主STUN服务器次要端口的响应,无法确定NAT类型");
				return NATTypeEnum.Unknown;
			}
			if (_myEndPointFromSlaveStunMainPortReply == null)
			{
				Console.WriteLine("没有收到从STUN服务器主端口的响应,无法确定NAT类型");
				return NATTypeEnum.Unknown;
			}
			if (_myEndPointFromSlaveStunSecondaryPortReply == null)
			{
				Console.WriteLine("没有收到从STUN服务器次要端口的响应,无法确定NAT类型");
				return NATTypeEnum.Unknown;
			}

			#endregion

			var outgoingIpList = new List<string>();
			var portsToMainServer = new List<int>();
			var portsToSlaveServer = new List<int>();
			foreach (var rsp in responses)
			{
				var ip = rsp.DetectedClientNATEndPoint.Address.ToString();
				var port = rsp.DetectedClientNATEndPoint.Port;
				if (!outgoingIpList.Contains(ip))
				{
					outgoingIpList.Add(ip);
				}
				if (rsp.IsFromMainSTUNServer)
				{
					if (!portsToMainServer.Contains(port))
					{
						portsToMainServer.Add(port);
					}
				}
				else if (rsp.IsFromSlaveSTUNServer)
				{
					if (!portsToSlaveServer.Contains(port))
					{
						portsToSlaveServer.Add(port);
					}
				}
			}

			#region 如果从多个ip出去的 那没法弄了

			if (outgoingIpList.Count > 1)
			{
				Console.WriteLine("从多个IP出去,无法确定NAT类型");
				return NATTypeEnum.Unknown;
			}

			#endregion
			
			#region 如果出网端口是从4个出去的就是对称型NAT

			if (portsToMainServer.Count + portsToSlaveServer.Count == 4)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("太遗憾了,你出网到4个不同的端点时,使用了不同的外网地址,你是对称型NAT,打洞成功率会低很多哦.不过不要灰心!");
				Console.ResetColor();
				return NATTypeEnum.Symmetric;
			}

			#endregion
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("虽然经过第一轮测试,无法确定NAT类型,需要进入下一轮测试,但是恭喜,这样的打洞成功率会高一些哦");
			Console.ResetColor();

			//需要进入下一轮测试了,让消息从主服务器的主端口出去,然后看回来的路径.
			return NATTypeEnum.Unknown;
		}
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
		if (_myEndPointFromMainStunMainPortReply == null)
		{
			throw new Exception("STUN响应为空, 无法处理广播消息");
		}

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

			var holePunchingMessage = new Client2ClientP2PHolePunchingRequestMessage
			{
				SourceEndPoint = _myEndPointFromMainStunMainPortReply,
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