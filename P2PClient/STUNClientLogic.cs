using System.Net;
using System.Net.Sockets;
using System.Text;
using P2PViaUDP.Model;
using P2PViaUDP.Model.STUN;
using TURNServer;

namespace P2PClient;

public class STUNClient
{
	public STUNClient(P2PClientConfig settings, UdpClient udpClient)
	{
		_settings = settings;
		_udpClient = udpClient;
	}
	#region 私有字段

	private IPEndPoint? _myEndPointFromMainStunMainPortReply;
	public IPEndPoint? MyEndPointFromMainStunSecondaryPortReply;
	private IPEndPoint? _myEndPointFromSlaveStunMainPortReply;
	private IPEndPoint? _myEndPointFromSlaveStunSecondaryPortReply;

	private P2PClientConfig _settings;
	private UdpClient _udpClient;
	private Guid _clientId = Guid.NewGuid();
	private uint _isSymmetricNATTypeCheckingRequestRetriedTimes;
	private const uint MaxIsSymmetricNATTypeCheckingRequestRetryTimes = 3;
	private readonly Queue<StunNATTypeCheckingResponse> _whichKindOfConeResponseQueue = new();
	private readonly Queue<StunNATTypeCheckingResponse> _isSymmetricResponseQueue = new();

	public NATTypeEnum MyNATType;

	public enum WholeProcessStatusEnum
	{
		等待是哪种锥形的测试结果中,
		等待是否为对称型NAT测试结果中,
	}
	private WholeProcessStatusEnum _wholeProcessStatus = WholeProcessStatusEnum.等待是哪种锥形的测试结果中;

	#endregion
	#region STUN 流程控制
	public async Task RequestStunServerAsync()
	{
		#region 如果IP设置的不是IP的格式(域名)要解析成IP
		var domain = _settings.STUNMainServerIP;
		if (!IPAddress.TryParse(domain, out var _))
		{
			var ip = await Dns.GetHostAddressesAsync(domain);
			_settings.STUNMainServerIP = ip[0].ToString();
		}
		#endregion
		
		var serverEndPoint = new IPEndPoint(
			IPAddress.Parse(_settings.STUNMainServerIP),
			_settings.STUNMainAndSlaveServerPrimaryPort
		);


		await ConductWhichKindOfConeNATCheckAsync(serverEndPoint);
		if (MyNATType == NATTypeEnum.FullCone)
		{
			return;
		}
		
		_wholeProcessStatus = WholeProcessStatusEnum.等待是否为对称型NAT测试结果中;

		Console.ForegroundColor = ConsoleColor.Gray;
		Console.WriteLine("**************************[哪种锥形]检测完毕,进入[是否对称型NAT]测试**************************");
		Console.ResetColor();
		
		await ConductSymmetricNATCheckAsync(serverEndPoint);
	}

	private async Task ConductWhichKindOfConeNATCheckAsync(IPEndPoint serverEndPoint)
	{
		
		#region 第一轮测试,先测试是什么锥形,只给主服务器的主端口发送一条消息,看看能从哪些路径回来.

		var whichKindOfConeNATTypeCheckingRequest = new StunNATTypeCheckingRequest(
			Guid.NewGuid(),
			StunNATTypeCheckingRequest.SubCheckingTypeEnum.WhichKindOfCone,
			_clientId,
			serverEndPoint,
			DateTime.Now
		);
		var whichKindOfConeNATTypeCheckingRequestBytes = whichKindOfConeNATTypeCheckingRequest.ToBytes();
		//只需要发送给主服务器的主要端口.主服务器接收到消息以后会转发到从服务器,然后主服务器的两个端口尝试返回,从服务器的两个端口尝试返回.
		await _udpClient.SendAsync(whichKindOfConeNATTypeCheckingRequestBytes,
			whichKindOfConeNATTypeCheckingRequestBytes.Length, serverEndPoint);
		// return;
		var whichKindOfConeCheckingResult = await ReceiveWhichKindOfConeCheckingRequestStunResponses(2000);
		// var whichKindOfConeCheckingResult = NATTypeEnum.PortRestrictedCone;//Remove this fake result
		// var whichKindOfConeCheckingResult = NATTypeEnum.Unknown;
		MyNATType = whichKindOfConeCheckingResult;

		#endregion

		if (whichKindOfConeCheckingResult == NATTypeEnum.Unknown)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("经过 [是哪种锥形]的测试,无法确定NAT类型,需要进入下一轮测试");
			Console.ResetColor();
		}

		#region 如果是检测到了漩口受限型的,还不能完全确定就是端口受限,有可能是对称型的.其他的就可以直接结束测试了

		if (whichKindOfConeCheckingResult == NATTypeEnum.FullCone)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("🎉🎉🎉🎉检测到全锥形NAT,不需要测试了🎉🎉🎉🎉");
			Console.ResetColor();
			return;
		}

		if (whichKindOfConeCheckingResult == NATTypeEnum.RestrictedCone)
		{
			Console.ForegroundColor = ConsoleColor.DarkBlue;
			Console.WriteLine("🌏🌏🌏检测到IP限制型NAT(相同IP端口不受限),不需要测试了🌏🌏🌏");
			Console.ResetColor();
			return;
		}

		if (whichKindOfConeCheckingResult == NATTypeEnum.PortRestrictedCone)
		{
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine("🤯🤯🤯检测到端口限制型NAT(相同IP端口受限,有可能还是对称型的<出网端口都不一样>),需要进入下一轮测试🤯🤯🤯");
			Console.ResetColor();
		}

		#endregion
	}

	private async Task ConductSymmetricNATCheckAsync(IPEndPoint serverEndPoint)
	{
		_isSymmetricNATTypeCheckingRequestRetriedTimes = 0;
		while (true)
		{
			_isSymmetricResponseQueue.Clear();
			_isSymmetricNATTypeCheckingRequestRetriedTimes++;
			if (_isSymmetricNATTypeCheckingRequestRetriedTimes > MaxIsSymmetricNATTypeCheckingRequestRetryTimes)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"🛡🛡🛡对称型NAT检测已经重试了{MaxIsSymmetricNATTypeCheckingRequestRetryTimes}次,无法确定NAT类型,请检查网络环境🛡🛡🛡");
				Console.ResetColor();
				return;
			}

			#region 构建STUN请求消息, 先发送是否对称型的检测包

			var isSymmetricNATTypeCheckingRequest = new StunNATTypeCheckingRequest(Guid.NewGuid(), StunNATTypeCheckingRequest.SubCheckingTypeEnum.IsSymmetric, _clientId, serverEndPoint, DateTime.Now);

			#endregion

			#region 进行是否是对称型NAT的一轮测试

			//先清空上一轮所有的已经检测到的我的公网IP和端口记录,进行下面的测试
			_myEndPointFromMainStunMainPortReply = null;
			MyEndPointFromMainStunSecondaryPortReply = null;
			_myEndPointFromSlaveStunMainPortReply = null;
			_myEndPointFromSlaveStunSecondaryPortReply = null;

			var isSymmetricNATTypeCheckingRequestBytes = isSymmetricNATTypeCheckingRequest.ToBytes();
			// 发送请求
			Console.WriteLine("开始向所有STUN服务器端口发送请求...");
			await SendIsSymmetricNATTypeCheckingRequestToAllPortsAsync(isSymmetricNATTypeCheckingRequestBytes);
			
			var receivedCount = 0;

			//等异步5秒,每100毫秒检测一次是否已经接收到了足够的响应,如果没有就重试,如果重试次数超过了最大重试次数,就结束测试
			var maxWaitTime = 5000;
			var startTime = DateTime.Now;
			while (DateTime.Now - startTime < TimeSpan.FromMilliseconds(maxWaitTime) && _isSymmetricResponseQueue.Count < 4)
			{
				await Task.Delay(100);
			}

			// 检查结果
			Console.WriteLine($"最终收到 {receivedCount} 个响应，继续分析NAT类型...");

			#endregion

			MyNATType = AnalyzeIsSymmetricCheckingResponses(_isSymmetricResponseQueue.ToList(), out var needRetry);
			Console.ForegroundColor = MyNATType == NATTypeEnum.Symmetric ? ConsoleColor.DarkRed : ConsoleColor.DarkYellow;
			var natTypeString = MyNATType == NATTypeEnum.Symmetric ? "🛡🛡🛡对称型🛡🛡🛡" : "🤯🤯🤯端口受限型🤯🤯🤯";
			Console.WriteLine($"**************************[是否对称型NAT]检测完成,最终确定NAT类型为: {natTypeString}**************************");
			Console.ResetColor();

			if (needRetry)
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine($"执行是否对称型NAT检测的第{_isSymmetricNATTypeCheckingRequestRetriedTimes}次重试,仍然无法确定NAT类型,继续重试...");
				Console.ResetColor();
				continue;
			}
			break;
		}
	}

	private async Task<NATTypeEnum> ReceiveWhichKindOfConeCheckingRequestStunResponses(int timeoutMs)
	{
		_whichKindOfConeResponseQueue.Clear();
		var startTime = DateTime.Now;
		while (DateTime.Now - startTime < TimeSpan.FromMilliseconds(timeoutMs))
		{
			if (_whichKindOfConeResponseQueue.Count == 4)
			{
				break;
			}

			await Task.Delay(100);
		}
		return AnalyzeWhichKindOfConeCheckingResponses(_whichKindOfConeResponseQueue.ToList());
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
		Console.WriteLine("以下是 [哪种锥形] 检测从的服务端回访来源信息:");
		Console.ForegroundColor = ConsoleColor.DarkYellow;
		if (fromMainServerPrimaryPort != null)
		{
			Console.WriteLine($"主服务器主端口: {fromMainServerPrimaryPort.StunServerEndPoint}");
		}

		if (fromMainServerSecondaryPort != null)
		{
			Console.WriteLine($"主服务器次要端口: {fromMainServerSecondaryPort.StunServerEndPoint}");
		}

		if (fromSlaveServerPrimaryPort != null)
		{
			Console.WriteLine($"从服务器主端口: {fromSlaveServerPrimaryPort.StunServerEndPoint}");
		}

		if (fromSlaveServerSecondaryPort != null)
		{
			Console.WriteLine($"从服务器次要端口: {fromSlaveServerSecondaryPort.StunServerEndPoint}");
		}

		Console.ResetColor();

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
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("🎉🎉🎉🎉有4个回信是从主服务器的主端口从端口以及从服务器的主端口从端口的,那就是全锥形的 啥都可以访问的🎉🎉🎉🎉");
			Console.ResetColor();
			return NATTypeEnum.FullCone;
		}

		Console.WriteLine("[哪种锥形] 检测中无法确定NAT类型");
		return NATTypeEnum.Unknown;
	}
	private void ProcessWhichKindOfConeStunNATTypeCheckingResponse(StunNATTypeCheckingResponse response)
	{
		_whichKindOfConeResponseQueue.Enqueue(response);
		Console.WriteLine(
			$"检测(哪种锥形)收到了来自 {(response.IsFromMainSTUNServer ? "主" : "从")} STUN服务器的{response.StunServerEndPoint.Port} 端口的响应,我的NAT公网信息: {response.DetectedClientNATEndPoint}");
		if (response.IsFromMainSTUNServer)
		{
			if (response.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort)
			{
				_myEndPointFromMainStunMainPortReply = response.DetectedClientNATEndPoint;
			}

			if (response.StunServerEndPoint.Port == _settings.STUNMainServerSecondaryPort)
			{
				MyEndPointFromMainStunSecondaryPortReply = response.DetectedClientNATEndPoint;
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
		_isSymmetricResponseQueue.Enqueue(response);
		if (response.IsFromMainSTUNServer)
		{
			if (response.StunServerEndPoint.Port == _settings.STUNMainAndSlaveServerPrimaryPort)
			{
				_myEndPointFromMainStunMainPortReply = response.DetectedClientNATEndPoint;
			}
			else if (response.StunServerEndPoint.Port == _settings.STUNMainServerSecondaryPort)
			{
				MyEndPointFromMainStunSecondaryPortReply = response.DetectedClientNATEndPoint;
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

	private NATTypeEnum AnalyzeIsSymmetricCheckingResponses(List<StunNATTypeCheckingResponse> responses, out bool needRetry)
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
				throw new Exception("主STUN服务器故障了");
			}

			if (isSlaveServerError)
			{
				Console.ForegroundColor = ConsoleColor.DarkRed;
				Console.WriteLine("应该是从STUN服务器故障了");
				throw new Exception("从STUN服务器故障了");
			}

			Console.ResetColor();

			#endregion

			needRetry = true;
			return NATTypeEnum.Unknown;
		}
		else
		{
			#region 缺一不可

			if (_myEndPointFromMainStunMainPortReply == null)
			{
				Console.WriteLine("没有收到主STUN服务器主端口的响应,无法确定NAT类型");
				needRetry = true;
				return NATTypeEnum.Unknown;
			}

			if (MyEndPointFromMainStunSecondaryPortReply == null)
			{
				Console.WriteLine("没有收到主STUN服务器次要端口的响应,无法确定NAT类型");
				needRetry = true;
				return NATTypeEnum.Unknown;
			}

			if (_myEndPointFromSlaveStunMainPortReply == null)
			{
				Console.WriteLine("没有收到从STUN服务器主端口的响应,无法确定NAT类型");
				needRetry = true;
				return NATTypeEnum.Unknown;
			}

			if (_myEndPointFromSlaveStunSecondaryPortReply == null)
			{
				Console.WriteLine("没有收到从STUN服务器次要端口的响应,无法确定NAT类型");
				needRetry = true;
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
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("从多个IP出去,无法确定NAT类型,可能是双线宽带之类的情况,这种就不要再尝试重连了");
				Console.ResetColor();
				needRetry = false;
				return NATTypeEnum.Unknown;
			}

			#endregion
			
			var allPorts = portsToMainServer.Concat(portsToSlaveServer).Distinct().ToList();

			#region 如果只有1个,因为之前已经检查完了是什么类型的锥形,认为可能是端口受限型的才到这里的,而出网只有一个的话,那就是端口首先行不是对称型了.

			if (allPorts.Count == 1)
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine("出网端口只有1个,不是对称型NAT,由于之前已经检查完了是什么类型的锥形,认为可能是端口受限型的");
				Console.ResetColor();
				needRetry = false;
				return NATTypeEnum.PortRestrictedCone;
			}

			#endregion

			#region 如果是2个,发送到主服务器的次要端口,从服务器的主端口和次要端口的三个出网NAT端口都一致的话,也可以确定不是对称型的

			//原因是我们第一轮测试中会向主服务器的主要端口已经发过一条消息(主服务器大喇叭到从服务器和自己的一共4个端口返回那次),所以已经算是建立起来连接了,就会复用之前的端口
			//而且因为发送完了以后进入到ReceiveAsync状态以后,NAT设备(或者本机)认为这个会话就结束了,所以再使用_udpClient换目的地端口出去的消息,客户端NAT公网端口也有了变化
			//udpClient->主3478 1发4回消息 建立公网11111
			//udpClient->主3478,主3479,从3478,从3479 4发4回消息, 第一条复用了11111(NAT复用机制),第2~4条会有新的公网端口(之前进入到ReceiveAsync以为是断开了要创建新映射)
			if (MyEndPointFromMainStunSecondaryPortReply.Port == _myEndPointFromSlaveStunMainPortReply.Port
			    && MyEndPointFromMainStunSecondaryPortReply.Port == _myEndPointFromSlaveStunSecondaryPortReply.Port)
			{
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				var sb = new StringBuilder();
				sb.AppendLine("出网端口只有2个,不是对称型NAT,因为发送到主服务器的次要端口,从服务器的主端口和次要端口的三个出网NAT端口都一致的,确定为端口受限型的");
				sb.AppendLine($"曾会话过的主服务器的主端口:{_myEndPointFromMainStunMainPortReply},");
				sb.AppendLine($"主服务器的次要端口:{MyEndPointFromMainStunSecondaryPortReply},");
				sb.AppendLine($"从服务器的主端口:{_myEndPointFromSlaveStunMainPortReply},");
				sb.AppendLine($"从服务器的次要端口:{_myEndPointFromSlaveStunSecondaryPortReply}");
				Console.WriteLine(sb.ToString());
				Console.ResetColor();
				needRetry = false;
				return NATTypeEnum.PortRestrictedCone;
			}

			#endregion

			#region 如果出网端口是从4个出去的就是对称型NAT
			

			if (allPorts.Count == 4)
			{
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("太遗憾了,你出网到4个不同的端点时,使用了不同的外网地址,你是对称型NAT,打洞成功率会低很多哦.不过不要灰心!");
				Console.ResetColor();
				needRetry = false;
				return NATTypeEnum.Symmetric;
			}

			#endregion

			#region 如果出去不是1个ip+端口也不是4个,那就可能是网络不稳定需要重新测试一次
			
			if (allPorts.Count != 4)
			{
				Console.ForegroundColor = ConsoleColor.Magenta;
				var endPointsString = string.Join(Environment.NewLine, responses
					.Select(
						r=>
						$"从{(r.IsFromMainSTUNServer ? "主" : "从")}STUN服务器的{r.StunServerEndPoint.Port}端口到{r.DetectedClientNATEndPoint}"
					));
				Console.WriteLine($"出网端口不是4个(对称型),也不是1或2个(某种锥形),而是 {allPorts.Count} 个,可能是网络不稳定,需要重新测试一次,出网端口:{Environment.NewLine} {endPointsString}");
				Console.ResetColor();
				needRetry = true;
				return NATTypeEnum.Unknown;
			}

			#endregion

			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine($"其他未知的代码没有处理的情况,需要完善逻辑");
			Console.ResetColor();
			needRetry = false;
			return NATTypeEnum.Unknown;
		}
	}

	#endregion
	private async Task SendIsSymmetricNATTypeCheckingRequestToAllPortsAsync(byte[] data)
	{
		try
		{
			// 主服务器主端口
			await _udpClient.SendAsync(data, data.Length, 
				new IPEndPoint(IPAddress.Parse(_settings.STUNMainServerIP), _settings.STUNMainAndSlaveServerPrimaryPort));
			// Thread.Sleep(2000);
			
			// 主服务器次端口
			await _udpClient.SendAsync(data, data.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNMainServerIP), _settings.STUNMainServerSecondaryPort));
			// Thread.Sleep(2000);

			// 从服务器主端口
			await _udpClient.SendAsync(data, data.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNSlaveServerIP), _settings.STUNMainAndSlaveServerPrimaryPort));
			// Thread.Sleep(2000);

			// 从服务器次端口
			await _udpClient.SendAsync(data, data.Length,
				new IPEndPoint(IPAddress.Parse(_settings.STUNSlaveServerIP), _settings.STUNSlaveServerSecondaryPort));
			// Thread.Sleep(2000);

			Console.WriteLine("已发送 [是否对称型NAT] 检测请求到所有端口");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"发送消息时发生错误: {ex.Message}");
		}
	}

	public void ProcessReceivedMessage(byte[] data)
	{
		// 确认消息类型
		var messageType = (MessageType)data[0];
		switch (messageType)
		{
			case MessageType.StunNATTypeCheckingResponse:
				var response = StunNATTypeCheckingResponse.FromBytes(data);
				if (_wholeProcessStatus == WholeProcessStatusEnum.等待是哪种锥形的测试结果中)
				{
					ProcessWhichKindOfConeStunNATTypeCheckingResponse(response);
				}
				else
				{
					ProcessIsSymmetricStunNATTypeCheckingResponse(response);
				}
				break;
			default:
				Console.WriteLine($"未知的消息类型: {messageType}");
				break;
		}
	}
}