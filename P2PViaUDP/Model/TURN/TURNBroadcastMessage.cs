using System.Net;

namespace P2PViaUDP.Model.TURN;

public class TURNBroadcastMessage
{
	private static MessageType MessageType { get; } = MessageType.TURNBroadcast;

	private static uint DefaultMessageLength =>
		4 + // MessageType
		16 + // Guid
		4 + 4 + // EndPoint
		1 + // IsNeedPrepareAcceptIncomingConnectionForThisClient
		1 + // IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient
		16 + // GroupGuid
		1 + // IsNeedHolePunchingToThisClient
		1 + // IsFullConeDetected
		1; // 需要打洞的端口数量
	//= 49 最少是这个数,而且是当IsNeedHolePunchingToThisClient为false时的最小值
	
	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; init; }
	/// <summary>
	/// 客户端在TURN服务器中的公网信息
	/// 当广播提及的客户端是一个全锥形的NAT并且广播说收到广播的人需要打洞,那么就用这个EndPoint来进行打洞
	/// </summary>
	public required IPEndPoint ClientSideEndPointToTURN { get; init; }
	/// <summary>
	/// 是否需要像对方抛橄榄枝,如果是则向对方发送一个到对方65535端口的消息,也就是在自己的外网开一个口子,报告可以接收对方的链接
	/// IP受限和端口受限的一方因为需要先连接对方的IP,但是不一定要真正到达对方的端点,只是让NAT设备知道能接受目标IP过来的请求.
	/// </summary>
	/// <returns></returns>
	public bool IsNeedPrepareAcceptIncomingConnectionForThisClient { get; init; }
	/// <summary>
	/// 是否需要等待对方为自己准备接收连接,如果是,则需要等待对方发来一个消息(自己并不会收到,因为会发到自己的65535端口)
	/// 如果这个值是true,则需要延迟一定时间再连接对方.默认是延迟1秒
	/// </summary>
	public bool IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient { get; init; }
	/// <summary>
	/// 要加入的组Guid
	/// </summary>
	public Guid GroupGuid { get; init; }
	
	/// <summary>
	/// 这个客户端是否检测到了自己的NAT类型是Full Cone(全锥),如果是,其他客户端可以使用他的这个公网信息直接进行P2P连接
	/// </summary>
	public bool IsFullConeDetected { get; init; }
	/// <summary>
	/// 收到这个广播消息的客户端是否需要对这个客户端进行打洞,这个值由TURN服务器来决定
	/// 一旦需要跟这个客户端进行打洞,就需要在后续保持打洞任务的进行和监测,直到打洞成功
	/// </summary>
	public bool IsNeedHolePunchingToThisClient { get; init; }
	/// <summary>
	/// TURN猜测的这个客户端被打洞时可能用到的端口号信息集合,收到这个消息的客户端需要依次尝试往这些端口发送消息
	/// </summary>
	public List<IPEndPoint> EndPointsInferByTURNServerNeedHolePunchingTo { get; init; } = new();

	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(Guid.ToByteArray());
		bytesList.AddRange(ClientSideEndPointToTURN.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(ClientSideEndPointToTURN.Port));
		bytesList.Add(IsNeedPrepareAcceptIncomingConnectionForThisClient ? (byte)1 : (byte)0);
		bytesList.Add(IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient ? (byte)1 : (byte)0);
		bytesList.AddRange(GroupGuid.ToByteArray());
		bytesList.Add(IsNeedHolePunchingToThisClient ? (byte)1 : (byte)0);
		bytesList.Add(IsFullConeDetected ? (byte)1 : (byte)0);
		// TURN服务器推算出来的要打洞的端口号信息
		bytesList.Add((byte)EndPointsInferByTURNServerNeedHolePunchingTo.Count);
		foreach (var endPoint in EndPointsInferByTURNServerNeedHolePunchingTo)
		{
			bytesList.AddRange(endPoint.Address.GetAddressBytes());
			bytesList.AddRange(BitConverter.GetBytes(endPoint.Port));
		}
		
		var bytes = bytesList.ToArray();
		return bytes;
	}
	public static TURNBroadcastMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length < DefaultMessageLength)
		{
			throw new ArgumentException("接收到的新人加入的广播字节数组长度不正确");
		}
		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType != MessageType.TURNBroadcast)
		{
			throw new ArgumentException("接收到的新人加入的广播消息类型不正确");
		}
		var guid = new Guid(receivedBytes.Skip(4).Take(16).ToArray());
		var address = new IPAddress(receivedBytes.Skip(20).Take(4).ToArray());
		var port = BitConverter.ToInt32(receivedBytes, 24);
		var endPoint = new IPEndPoint(address, port);
		var isNeedPrepareAcceptIncomingConnectionForThisClient = Convert.ToBoolean(receivedBytes[28]);
		var isNeedWaitForPrepareAcceptIncomingConnectionForThisClient = Convert.ToBoolean(receivedBytes[29]);
		var groupGuid = new Guid(receivedBytes.Skip(30).Take(16).ToArray());
		var isNeedHolePunchingToThisClient = Convert.ToBoolean(receivedBytes[46]);
		var isFullConeDetected = Convert.ToBoolean(receivedBytes[47]);
		var endPointCount = receivedBytes[48];
		var endPoints = new List<IPEndPoint>();
		for (var i = 0; i < endPointCount; i++)
		{
			var startIndex = 49 + i * 8;
			var endPointAddress = new IPAddress(receivedBytes.Skip(startIndex).Take(4).ToArray());
			var endPointPort = BitConverter.ToInt32(receivedBytes, startIndex + 4);
			endPoints.Add(new IPEndPoint(endPointAddress, endPointPort));
		}
		
		return new TURNBroadcastMessage
		{
			Guid = guid,
			ClientSideEndPointToTURN = endPoint,
			IsNeedPrepareAcceptIncomingConnectionForThisClient = isNeedPrepareAcceptIncomingConnectionForThisClient,
			IsNeedWaitForPrepareAcceptIncomingConnectionForThisClient = isNeedWaitForPrepareAcceptIncomingConnectionForThisClient,
			GroupGuid = groupGuid,
			IsNeedHolePunchingToThisClient = isNeedHolePunchingToThisClient,
			IsFullConeDetected = isFullConeDetected,
			EndPointsInferByTURNServerNeedHolePunchingTo = endPoints
		};
	}
}