using System.Net;
using System.Runtime.InteropServices;
using TURNServer;

namespace P2PViaUDP.Model.Client;

/// <summary>
/// 客户端到客户端的P2P打洞消息
/// </summary>
public class Client2ClientP2PHolePunchingRequestMessage
{
	public Client2ClientP2PHolePunchingRequestMessage(
		Guid groupId,
		IPEndPoint destinationEndPoint, 
		Guid destinationClientId, 
		NATTypeEnum sourceNatType, 
		Guid sourceClientId, 
		IPEndPoint? sourceEndPoint = null
		)
	{
		DestinationEndPoint = destinationEndPoint;
		SourceNatType = sourceNatType;
		SourceClientId = sourceClientId;
		DestinationClientId = destinationClientId;
		GroupId = groupId;
		SourceEndPoint = sourceEndPoint;
		SendTime = DateTime.Now;
	}

	private static MessageType MessageType => MessageType.P2PHolePunchingRequest;
	private static uint DefaultMessageLength => 
	4 + // MessageType
	16 + // RequestId
	4 + // SourceEndPoint.Address
	4 + // SourceEndPoint.Port
	4 + // DestinationEndPoint.Address
	4 + // DestinationEndPoint.Port
	4 + // SourceNatType
	16 + // SourceClientId
	16 + // DestinationClientId
	16 + // GroupId
	8; // SendTime
	//= 96;
	
	public Guid RequestId { get; init; }
	
	/// <summary>
	/// 谁发起的打洞,并不是本条消息是谁发送的,这个不一定是有值的,因为对称型NAT在开始打洞的时候并不知道自己的端口信息
	/// 但是会知道自己的类型
	/// </summary>
	public IPEndPoint? SourceEndPoint { get; private init; }
	/// <summary>
	/// 谁接收打洞请求,并不是本条消息是谁接收的
	/// </summary>
	public IPEndPoint DestinationEndPoint { get; init; }
	/// <summary>
	/// 源NAT类型,如果是对称型的,则包到达目的地以后才知道源的出网时候的真正端口号.这种request需要修正了信息以后再response返回去
	/// </summary>
	public NATTypeEnum SourceNatType { get; init; }
	/// <summary>
	/// 源客户端ID,发起打洞的客户端ID
	/// </summary>
	public Guid SourceClientId { get; init; }
	/// <summary>
	/// 目标客户端ID,接收打洞请求的客户端ID
	/// </summary>
	public Guid DestinationClientId { get; init; }
	/// <summary>
	/// 组ID
	/// </summary>
	public Guid GroupId { get; init; }
	/// <summary>
	/// 发送时间
	/// </summary>
	public DateTime SendTime { get; init; }
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(RequestId.ToByteArray());
		var sourceEndPoint = SourceEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
		bytesList.AddRange(sourceEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(sourceEndPoint.Port));
		bytesList.AddRange(DestinationEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(DestinationEndPoint.Port));
		bytesList.AddRange(BitConverter.GetBytes((int)SourceNatType));
		bytesList.AddRange(SourceClientId.ToByteArray());
		bytesList.AddRange(DestinationClientId.ToByteArray());
		bytesList.AddRange(GroupId.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		var bytes = bytesList.ToArray();
		return bytes;
	}

	public static Client2ClientP2PHolePunchingRequestMessage FromBytes(byte[] bytes)
	{
		if (bytes.Length < DefaultMessageLength)
		{
			throw new ArgumentException("字节数组长度不足");
		}
		var messageType = (MessageType)BitConverter.ToInt32(bytes, 0);
		if (messageType != MessageType.P2PHolePunchingRequest)
		{
			throw new ArgumentException("消息类型不匹配");
		}
		var requestId = new Guid(bytes.Skip(4).Take(16).ToArray());
		var sourceEndPointAddress = new IPAddress(bytes.Skip(20).Take(4).ToArray());
		var sourceEndPointPort = BitConverter.ToInt32(bytes, 24);
		var sourceEndPoint = new IPEndPoint(sourceEndPointAddress, sourceEndPointPort);
		var destinationEndPointAddress = new IPAddress(bytes.Skip(28).Take(4).ToArray());
		var destinationEndPointPort = BitConverter.ToInt32(bytes, 32);
		var destinationEndPoint = new IPEndPoint(destinationEndPointAddress, destinationEndPointPort);
		var sourceNatType = (NATTypeEnum)BitConverter.ToInt32(bytes, 36);
		var sourceClientId = new Guid(bytes.Skip(40).Take(16).ToArray());
		var destinationClientId = new Guid(bytes.Skip(56).Take(16).ToArray());
		var groupId = new Guid(bytes.Skip(72).Take(16).ToArray());
		var sendTimeTicks = BitConverter.ToInt64(bytes, 88);
		var sendTime = new DateTime(sendTimeTicks);
		// 这里的时间是发送的时间,不是接收的时间
		return new Client2ClientP2PHolePunchingRequestMessage(
			groupId,
			destinationEndPoint, 
			destinationClientId,
			sourceNatType, 
			sourceClientId,
			sourceEndPoint
			)
		{
			RequestId = requestId,
			SendTime = sendTime
		};
	}
}