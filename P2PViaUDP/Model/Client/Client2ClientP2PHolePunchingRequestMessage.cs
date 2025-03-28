using System.Net;
using System.Runtime.InteropServices;
using TURNServer;

namespace P2PViaUDP.Model.Client;

/// <summary>
/// 客户端到客户端的P2P打洞消息
/// </summary>
public class Client2ClientP2PHolePunchingRequestMessage
{
	private static MessageType MessageType => MessageType.P2PHolePunchingRequest;
	private static uint DefaultMessageLength => 
	4 + // MessageType
	4 + // SourceEndPoint.Address
	4 + // SourceEndPoint.Port
	4 + // DestinationEndPoint.Address
	4 + // DestinationEndPoint.Port
	4 + // SourceNatType
	16 + // SourceClientId
	16 + // DestinationClientId
	16 + // GroupId
	8; // SendTime
	//= 80
	
	/// <summary>
	/// 谁发起的打洞,并不是本条消息是谁发送的
	/// </summary>
	public IPEndPoint SourceEndPoint { get; set; }
	/// <summary>
	/// 谁接收打洞请求,并不是本条消息是谁接收的
	/// </summary>
	public IPEndPoint DestinationEndPoint { get; set; }
	/// <summary>
	/// 源NAT类型,如果是对称型的,则包到达目的地以后才知道源的出网时候的真正端口号.这种request需要修正了信息以后再response返回去
	/// </summary>
	public NATTypeEnum SourceNatType { get; set; }
	/// <summary>
	/// 源客户端ID,发起打洞的客户端ID
	/// </summary>
	public Guid SourceClientId { get; set; }
	/// <summary>
	/// 目标客户端ID,接收打洞请求的客户端ID
	/// </summary>
	public Guid DestinationClientId { get; set; }
	/// <summary>
	/// 组ID
	/// </summary>
	public Guid GroupId { get; set; }
	/// <summary>
	/// 发送时间
	/// </summary>
	public DateTime SendTime { get; set; } = DateTime.Now;
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(SourceEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(SourceEndPoint.Port));
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
		var sourceEndPoint =
			new IPEndPoint(new IPAddress(bytes.Skip(4).Take(4).ToArray()), BitConverter.ToInt32(bytes, 8));
		var destinationEndPoint =
			new IPEndPoint(new IPAddress(bytes.Skip(12).Take(4).ToArray()), BitConverter.ToInt32(bytes, 16));
		var sourceNatType = (NATTypeEnum)BitConverter.ToInt32(bytes, 20);
		var sourceClientId = new Guid(bytes.Skip(24).Take(16).ToArray());
		var destinationClientId = new Guid(bytes.Skip(40).Take(16).ToArray());
		var groupId = new Guid(bytes.Skip(56).Take(16).ToArray());
		var sendTime = new DateTime(BitConverter.ToInt64(bytes, 72));
		return new Client2ClientP2PHolePunchingRequestMessage
		{
			SourceEndPoint = sourceEndPoint,
			DestinationEndPoint = destinationEndPoint,
			SourceClientId = sourceClientId,
			DestinationClientId = destinationClientId,
			SourceNatType = sourceNatType,
			GroupId = groupId,
			SendTime = sendTime
		};
	}
}