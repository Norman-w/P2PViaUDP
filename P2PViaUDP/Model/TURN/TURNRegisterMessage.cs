using System.Net;
using TURNServer;

namespace P2PViaUDP.Model.TURN;

/// <summary>
/// 客户端向TURN服务器注册的消息
/// TODO 当前我们就是使用简单形式来保存,占用的字节还是比较多的,后续我们可以把多个要表达的信息进行合并以减少字节数,比如IsFullConeDetected可以与运算到MessageType中等等的策略
/// </summary>
public class TURNRegisterMessage
{
	private static MessageType MessageType => MessageType.TURNRegister;

	private static uint DefaultMessageLength =>
		4 + // MessageType
		16 + // Guid
		4 + // Address
		4 + // Port
		16 + // GroupGuid
		4; // NATType
	//= 44


	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; init; }

	/// <summary>
	/// 客户端的公网信息
	/// </summary>
	public required IPEndPoint EndPoint { get; init; }

	/// <summary>
	/// 要加入的组Guid
	/// </summary>
	public Guid GroupGuid { get; init; }
	
	// /// <summary>
	// /// 是否检测到了这个客户端未Full Cone(全锥)的NAT类型
	// /// 如果是,则P2P的另外一端可以直接使用这个客户端的公网信息进行P2P连接快速的完成打洞
	// /// 也不需要这个客户端像另外的客户端开洞(不需要为另外客户端准备一个入口,这已经是入口)
	// /// 另外一端的客户端只需要和这个客户端发送过一次(打洞)消息,这个客户端知道了以后保存到自己的列表中即可
	// /// </summary>
	// public bool IsFullConeDetected { get; init; }
	
	/// <summary>
	/// 检测到的NAT类型,未检测到则为null
	/// 通过多台STUN服务器可以确定客户端是否为Full Cone(全锥)类型
	/// TODO 其他类型的NAT类型检测尚在研究中
	/// </summary>
	public NATTypeEnum? DetectedNATType { get; set; }

	public static TURNRegisterMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length != DefaultMessageLength)
		{
			throw new ArgumentException($"接收到的字节数组长度不正确，应为{DefaultMessageLength}，实际为{receivedBytes.Length}");
		}

		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType != MessageType)
		{
			throw new ArgumentException("读取的消息类型不匹配");
		}

		var guid = new Guid(receivedBytes.Skip(4).Take(16).ToArray());
		var address = new IPAddress(receivedBytes.Skip(20).Take(4).ToArray());
		var port = BitConverter.ToInt32(receivedBytes, 24);
		var endPoint = new IPEndPoint(address, port);
		var groupGuid = new Guid(receivedBytes.Skip(28).Take(16).ToArray());
		var natType = (NATTypeEnum)BitConverter.ToInt32(receivedBytes, 44);

		return new TURNRegisterMessage
		{
			Guid = guid,
			EndPoint = endPoint,
			GroupGuid = groupGuid,
			DetectedNATType = natType
		};
	}

	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(Guid.ToByteArray());
		bytesList.AddRange(EndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(EndPoint.Port));
		bytesList.AddRange(GroupGuid.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes((int)(DetectedNATType ?? NATTypeEnum.Unknown)));
		var bytes = bytesList.ToArray();
		return bytes;
	}
}