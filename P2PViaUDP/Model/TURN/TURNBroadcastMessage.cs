using System.Net;

namespace P2PViaUDP.Model.TURN;

public class TURNBroadcastMessage
{
	private static MessageType MessageType { get; } = MessageType.TURNBroadcast;

	private static uint DefaultMessageLength =>
		4 + // MessageType
		16 + // Guid
		4 + 4 + // EndPoint
		16 + // GroupGuid
		1 + // IsNeedHolePunchingToThisClient
		1 + // IsFullConeDetected
		1; // 需要打洞的端口数量
	//= 47 最少是这个数,而且是当IsNeedHolePunchingToThisClient为false时的最小值
	
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
		var groupGuid = new Guid(receivedBytes.Skip(28).Take(16).ToArray());
		var isNeedHolePunchingToThisClient = Convert.ToBoolean(receivedBytes[44]);
		var isFullConeDetected = Convert.ToBoolean(receivedBytes[45]);
		var endPointCount = receivedBytes[46];
		var endPoints = new List<IPEndPoint>();
		for (var i = 0; i < endPointCount; i++)
		{
			var startIndex = 47 + i * 6;
			var endPointAddress = new IPAddress(receivedBytes.Skip(startIndex).Take(4).ToArray());
			var endPointPort = BitConverter.ToInt32(receivedBytes, startIndex + 4);
			endPoints.Add(new IPEndPoint(endPointAddress, endPointPort));
		}
		
		return new TURNBroadcastMessage
		{
			Guid = guid,
			ClientSideEndPointToTURN = endPoint,
			GroupGuid = groupGuid,
			IsNeedHolePunchingToThisClient = isNeedHolePunchingToThisClient,
			IsFullConeDetected = isFullConeDetected,
			EndPointsInferByTURNServerNeedHolePunchingTo = endPoints
		};
	}
}